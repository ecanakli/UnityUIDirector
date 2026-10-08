using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ecanakli.UIDirector.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.EditorTools.Tests
{
    /// <summary>The validator rules 101 to 103, 108, 109 and 120 to 125, each in isolation, on in-memory catalogs; project discovery and the menu on temporary assets.</summary>
    [TestFixture]
    public sealed class CatalogValidationTests
    {
        private const string TmpDropdownTypeName = "TMPro.TMP_Dropdown";
        private const string NoSuchAddress = "validation/no-such-address";
        private const string AddressablesSettingsTypeName = "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor";

        private readonly List<Object> _created = new List<Object>();
        private string _folder;

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();

            // Assets are deleted by path, never destroyed through _created.
            if (!string.IsNullOrEmpty(_folder))
            {
                AssetDatabase.DeleteAsset(_folder);
                _folder = null;
            }
        }

        // ---- Clean input ----

        [Test]
        public void Validate_CleanCatalogsAndKeys_HasNoFindings()
        {
            var addresses = new Dictionary<string, GameObject> { { "addr/ok", HostPrefab("addressed") } };
            ViewCatalog catalog = Catalog(
                "Clean",
                Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("plain")),
                Row(ValidationIds.Args, HostPrefab<ValidationArgsView>("args"), ViewLayers.Popups.Id, back: BackPolicy.Close),
                Row(ValidationIds.Result, HostPrefab<ValidationResultView>("result"), ViewLayers.System.Id, back: BackPolicy.Ignore),
                Row("validation.addressed", null, assetKey: "addr/ok"));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog },
                new[] { typeof(ValidationKeysPlain), typeof(ValidationKeysArgs), typeof(ValidationKeysResult) },
                AddressSettings(addresses));

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [Test]
        public void Validate_NoCatalogsAndNoKeys_HasNoFindings()
        {
            List<CatalogIssue> issues = CatalogValidation.Validate(new ViewCatalog[0], new Type[0], Settings());

            Assert.That(issues, Is.Empty);
        }

        // ---- 108 duplicate id ----

        [Test]
        public void Validate_SameIdInTwoCatalogs_ReportsTheSecondRow108()
        {
            ViewCatalog first = Catalog("First", Row("shared", HostPrefab("a")));
            ViewCatalog second = Catalog("Second", Row("other", HostPrefab("b")), Row("shared", HostPrefab("c")));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { first, second }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.DuplicateViewId, CatalogIssueSeverity.Error, second, 1);
        }

        [Test]
        public void Validate_SameIdTwiceInOneCatalog_ReportsTheSecondRow108()
        {
            ViewCatalog catalog = Catalog(
                "Single",
                Row("once", HostPrefab("a")),
                Row("twice", HostPrefab("b")),
                Row("twice", HostPrefab("c")));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.DuplicateViewId, CatalogIssueSeverity.Error, catalog, 2);
        }

        [Test]
        public void Validate_DifferentIdsInTwoCatalogs_HasNoFindings()
        {
            ViewCatalog first = Catalog("First", Row("one", HostPrefab("a")));
            ViewCatalog second = Catalog("Second", Row("two", HostPrefab("b")));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { first, second }, new Type[0], Settings());

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        // ---- 109 unknown layer ----

        [Test]
        public void Validate_UnknownLayer_Reports109()
        {
            ViewCatalog catalog = Catalog("Layers", Row("a", HostPrefab("a")), Row("b", HostPrefab("b"), "hud"));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.UnknownLayer, CatalogIssueSeverity.Error, catalog, 1);
        }

        [Test]
        public void Validate_ExtraLayerInKnownLayers_Passes()
        {
            ViewCatalog catalog = Catalog("Layers", Row("a", HostPrefab("a"), ValidationTestLayers.Hud.Id));
            CatalogValidationSettings settings = Settings();
            List<ViewLayer> known = CatalogValidation.DefaultLayers();
            known.Add(ValidationTestLayers.Hud);
            settings.KnownLayers = known;

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], settings);

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [Test]
        public void Validate_ExtraLayerWithoutKnownLayers_Reports109()
        {
            ViewCatalog catalog = Catalog("Layers", Row("a", HostPrefab("a"), ValidationTestLayers.Hud.Id));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.UnknownLayer, CatalogIssueSeverity.Error, catalog, 0);
        }

        [TestCase("screens")]
        [TestCase("popups")]
        [TestCase("system")]
        public void Validate_DefaultLayerIds_AreKnown(string layerId)
        {
            ViewCatalog catalog = Catalog("Layers", Row("a", HostPrefab("a"), layerId));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        // ---- 109 before 108 ----

        [Test]
        public void Validate_UnknownLayerRowThenTheSameIdOnAKnownLayer_ReportsOnly109OnTheFirstRow()
        {
            ViewCatalog catalog = Catalog(
                "Shop",
                Row("shop", HostPrefab("refused"), "popup"),
                Row("shop", HostPrefab("registered"), ViewLayers.Popups.Id));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.UnknownLayer, CatalogIssueSeverity.Error, catalog, 0);
        }

        [Test]
        public void Validate_UnknownLayerRowThenTheSameIdOnAKnownLayer_ChecksTheKeyAgainstTheRegisteredRow()
        {
            ViewCatalog catalog = Catalog(
                "Shop",
                Row(ValidationIds.Plain, HostPrefab<ValidationResultView>("refused"), "nowhere"),
                Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("registered"), ViewLayers.Screens.Id));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysPlain) }, Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.UnknownLayer, CatalogIssueSeverity.Error, catalog, 0);
        }

        [Test]
        public void Validate_UnknownLayerRowThenTheSameIdOnAKnownLayer_Reports102AgainstTheRegisteredRow()
        {
            ViewCatalog catalog = Catalog(
                "Shop",
                Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("refused"), "nowhere"),
                Row(ValidationIds.Plain, HostPrefab<ValidationResultView>("registered"), ViewLayers.Screens.Id));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysPlain) }, Settings());

            Assert.That(issues, Has.Count.EqualTo(2), Describe(issues));
            Assert.That(issues[0].Id, Is.EqualTo(DiagnosticIds.UnknownLayer));
            Assert.That(issues[0].Catalog, Is.SameAs(catalog));
            Assert.That(issues[0].RowIndex, Is.EqualTo(0));
            Assert.That(issues[1].Id, Is.EqualTo(DiagnosticIds.ViewTypeMismatch));
            Assert.That(issues[1].KeyType, Is.EqualTo(typeof(ValidationKeysPlain)));
            Assert.That(issues[1].KeyField, Is.EqualTo("Open"));
            Assert.That(issues[1].Catalog, Is.SameAs(catalog));
            Assert.That(issues[1].RowIndex, Is.EqualTo(1), "the key is checked against the registered row, not the refused one");
        }

        [Test]
        public void Validate_KnownLayerRowThenTheSameIdOnAnUnknownLayer_ReportsOnly109OnTheSecondRow()
        {
            ViewCatalog catalog = Catalog(
                "Shop",
                Row("shop", HostPrefab("registered"), ViewLayers.Popups.Id),
                Row("shop", HostPrefab("refused"), "popup"));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.UnknownLayer, CatalogIssueSeverity.Error, catalog, 1);
        }

        [Test]
        public void Validate_KnownLayerRowThenTheSameIdOnAnUnknownLayer_ChecksTheKeyAgainstTheFirstRowOnly()
        {
            ViewCatalog catalog = Catalog(
                "Shop",
                Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("registered"), ViewLayers.Screens.Id),
                Row(ValidationIds.Plain, HostPrefab<ValidationResultView>("refused"), "nowhere"));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysPlain) }, Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.UnknownLayer, CatalogIssueSeverity.Error, catalog, 1);
        }

        [TestCase("")]
        [TestCase("  ")]
        public void Validate_RowWithAnEmptyLayerId_Reports109(string layerId)
        {
            ViewCatalog catalog = Catalog("Layers", Row("a", HostPrefab("a")), Row("b", HostPrefab("b"), layerId));

            List<CatalogIssue> issues = null;

            Assert.DoesNotThrow(() => issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings()));

            AssertSingleRowIssue(issues, DiagnosticIds.UnknownLayer, CatalogIssueSeverity.Error, catalog, 1);
        }

        [Test]
        public void Validate_KeyWhoseOnlyRowHasAnUnknownLayer_Reports101NamingTheRefusedRow()
        {
            ViewCatalog catalog = Catalog("Refused", Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("plain"), "nowhere"));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysPlain) }, Settings());

            Assert.That(issues, Has.Count.EqualTo(2), Describe(issues));
            Assert.That(issues[0].Id, Is.EqualTo(DiagnosticIds.UnknownLayer));
            Assert.That(issues[0].RowIndex, Is.EqualTo(0));
            Assert.That(issues[1].Id, Is.EqualTo(DiagnosticIds.UnknownViewId));
            Assert.That(issues[1].Severity, Is.EqualTo(CatalogIssueSeverity.Error));
            Assert.That(issues[1].KeyType, Is.EqualTo(typeof(ValidationKeysPlain)));
            Assert.That(issues[1].KeyField, Is.EqualTo("Open"));
            Assert.That(issues[1].Catalog, Is.SameAs(catalog));
            Assert.That(issues[1].RowIndex, Is.EqualTo(0));
            Assert.That(issues[1].Message, Does.Contain("Row 0 ('" + ValidationIds.Plain + "') in catalog 'Refused'"));
            Assert.That(issues[1].Message, Does.Contain("unknown layer"));
        }

        [Test]
        public void Validate_KeyWhoseRowsAllHaveUnknownLayers_Reports101NamingTheFirstRefusedRow()
        {
            ViewCatalog catalog = Catalog(
                "Refused",
                Row("first", HostPrefab("first")),
                Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("one"), "nowhere"),
                Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("two"), "elsewhere"));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysPlain) }, Settings());

            CatalogIssue key = issues.Single(issue => issue.Id == DiagnosticIds.UnknownViewId);
            Assert.That(key.KeyType, Is.EqualTo(typeof(ValidationKeysPlain)));
            Assert.That(key.Catalog, Is.SameAs(catalog));
            Assert.That(key.RowIndex, Is.EqualTo(1), "the first refused row is named");
        }

        [Test]
        public void Validate_TwoRowsOnUnknownLayersWithTheSameId_Report109EachAndNo108()
        {
            ViewCatalog catalog = Catalog(
                "Layers",
                Row("shop", HostPrefab("a"), "nowhere"),
                Row("shop", HostPrefab("b"), "elsewhere"));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            Assert.That(issues.Select(Format).ToArray(), Is.EqualTo(new[] { "UIDIRECTOR109|Layers|0", "UIDIRECTOR109|Layers|1" }));
        }

        [Test]
        public void Validate_UnknownLayerRowWithAHostlessPrefab_Reports109And103()
        {
            ViewCatalog catalog = Catalog("Layers", Row("a", Track(new GameObject("hostless")), "nowhere"));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            Assert.That(issues.Select(Format).ToArray(), Is.EqualTo(new[] { "UIDIRECTOR109|Layers|0", "UIDIRECTOR103|Layers|0" }));
        }

        // ---- 121 neither prefab nor key ----

        [TestCase("")]
        [TestCase(null)]
        public void Validate_RowWithNeitherPrefabNorAssetKey_Reports121(string assetKey)
        {
            ViewCatalog catalog = Catalog("Sources", Row("a", HostPrefab("a")), Row("b", null, assetKey: assetKey));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.RowWithoutSource, CatalogIssueSeverity.Error, catalog, 1);
        }

        // ---- 120 asset key without Addressables ----

        [Test]
        public void Validate_AssetKeyWhileAddressablesNotInstalled_Reports120AsWarning()
        {
            ViewCatalog catalog = Catalog("Keys", Row("a", null, assetKey: "addr/a"));
            CatalogValidationSettings settings = Settings();
            settings.AddressablesInstalled = false;
            settings.LookupAddress = key => default;

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], settings);

            AssertSingleRowIssue(issues, DiagnosticIds.NoProviderForAssetKey, CatalogIssueSeverity.Warning, catalog, 0);
        }

        // ---- 124 asset key without an Addressables entry ----

        [Test]
        public void Validate_AssetKeyWithoutAddressablesEntry_Reports124AsWarning()
        {
            ViewCatalog catalog = Catalog("Keys", Row("a", null, assetKey: "addr/missing"));
            var asked = new List<string>();

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new Type[0], AddressSettings(new Dictionary<string, GameObject>(), asked));

            AssertSingleRowIssue(issues, DiagnosticIds.AssetKeyNotAddressable, CatalogIssueSeverity.Warning, catalog, 0);
            Assert.That(asked, Is.EqualTo(new[] { "addr/missing" }));
        }

        [Test]
        public void Validate_AssetKeyWithoutAddressablesEntry_MessageNamesAddressGuidLabelAndAnotherProvider()
        {
            ViewCatalog catalog = Catalog("Keys", Row("a", null, assetKey: "addr/missing"));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new Type[0], AddressSettings(new Dictionary<string, GameObject>()));

            Assert.That(issues, Has.Count.EqualTo(1), Describe(issues));
            Assert.That(issues[0].Message, Does.Contain("address, GUID or label"));
            Assert.That(issues[0].Message, Does.Contain("another asset provider"));
        }

        [Test]
        public void Validate_AssetKeyThatIsAGuid_HasNoFindings()
        {
            const string guid = "0123456789abcdef0123456789abcdef";
            ViewCatalog catalog = Catalog("Keys", Row("a", null, assetKey: guid));
            var asked = new List<string>();
            var settings = new CatalogValidationSettings
            {
                AddressablesInstalled = true,
                LookupAddress = key =>
                {
                    asked.Add(key);
                    return key == guid ? new AddressLookupResult(true, HostPrefab("addressed")) : default;
                }
            };

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], settings);

            Assert.That(issues, Is.Empty, Describe(issues));
            Assert.That(asked, Is.EqualTo(new[] { guid }));
        }

        [Test]
        public void Validate_AssetKeyThatIsALabel_HasNoFindings()
        {
            ViewCatalog catalog = Catalog("Keys", Row("a", null, assetKey: "popups"));
            var settings = new CatalogValidationSettings
            {
                AddressablesInstalled = true,
                // A label names a group of assets, so the lookup has no single main asset.
                LookupAddress = key => key == "popups" ? new AddressLookupResult(true, null) : default
            };

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], settings);

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [Test]
        public void Validate_AssetKeyThatIsAGuidOfAHostlessAsset_Reports103()
        {
            const string guid = "fedcba9876543210fedcba9876543210";
            ViewCatalog catalog = Catalog("Keys", Row("a", null, assetKey: guid));
            var addresses = new Dictionary<string, GameObject> { { guid, Track(new GameObject("hostless")) } };

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], AddressSettings(addresses));

            AssertSingleRowIssue(issues, DiagnosticIds.MissingViewHost, CatalogIssueSeverity.Error, catalog, 0);
        }

        // ---- 125 row with an empty id ----

        [TestCase("")]
        [TestCase(null)]
        [TestCase(" ")]
        [TestCase("\t \n")]
        public void Validate_RowWithAnEmptyId_Reports125(string id)
        {
            ViewCatalog catalog = Catalog("Ids", Row("a", HostPrefab("a")), Row(id, HostPrefab("b")));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.RowWithEmptyId, CatalogIssueSeverity.Error, catalog, 1);
        }

        [Test]
        public void Validate_RowWithAnEmptyIdAndOtherProblems_ReportsOnly125()
        {
            ViewCatalog catalog = Catalog(
                "Ids",
                Row("", Track(new GameObject("hostless")), "nowhere", back: BackPolicy.Close),
                Row(" ", null, ViewLayers.System.Id, back: BackPolicy.Close));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            Assert.That(issues.Select(Format).ToArray(), Is.EqualTo(new[] { "UIDIRECTOR125|Ids|0", "UIDIRECTOR125|Ids|1" }));
        }

        [Test]
        public void Validate_TwoRowsWithEmptyIds_Report125EachAndNo108()
        {
            ViewCatalog catalog = Catalog("Ids", Row("", HostPrefab("a")), Row("", HostPrefab("b")));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            Assert.That(issues.Select(issue => issue.Id).ToArray(), Is.EqualTo(new[] { DiagnosticIds.RowWithEmptyId, DiagnosticIds.RowWithEmptyId }));
        }

        [Test]
        public void Validate_RowWithAnEmptyId_DoesNotClaimAnIdForKeys()
        {
            ViewCatalog catalog = Catalog("Ids", Row("", HostPrefab<ValidationPlainView>("plain")));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysPlain) }, Settings());

            Assert.That(issues.Select(issue => issue.Id).ToArray(), Is.EqualTo(new[] { DiagnosticIds.RowWithEmptyId, DiagnosticIds.UnknownViewId }));
        }

        [Test]
        public void Validate_AssetKeyWithEntry_HasNoFindings()
        {
            ViewCatalog catalog = Catalog("Keys", Row("a", null, assetKey: "addr/a"));
            var addresses = new Dictionary<string, GameObject> { { "addr/a", HostPrefab("addressed") } };

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], AddressSettings(addresses));

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [Test]
        public void Validate_AssetKeyWhoseMainAssetHasNoViewHost_Reports103()
        {
            ViewCatalog catalog = Catalog("Keys", Row("a", null, assetKey: "addr/a"));
            var addresses = new Dictionary<string, GameObject> { { "addr/a", Track(new GameObject("hostless")) } };

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], AddressSettings(addresses));

            AssertSingleRowIssue(issues, DiagnosticIds.MissingViewHost, CatalogIssueSeverity.Error, catalog, 0);
        }

        [Test]
        public void Validate_AssetKeyWhoseMainAssetHasWrongViewType_Reports102()
        {
            ViewCatalog catalog = Catalog("Keys", Row(ValidationIds.ArgsMismatch, null, assetKey: "addr/a"));
            var addresses = new Dictionary<string, GameObject> { { "addr/a", HostPrefab<ValidationPlainView>("addressed") } };

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysArgsMismatch) }, AddressSettings(addresses));

            AssertSingleKeyIssue(issues, DiagnosticIds.ViewTypeMismatch, typeof(ValidationKeysArgsMismatch), "Open", catalog, 0);
        }

        // ---- 103 prefab without ViewHost ----

        [Test]
        public void Validate_PrefabWithoutViewHost_Reports103()
        {
            ViewCatalog catalog = Catalog("Hosts", Row("a", HostPrefab("a")), Row("b", Track(new GameObject("hostless"))));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.MissingViewHost, CatalogIssueSeverity.Error, catalog, 1);
        }

        [Test]
        public void Validate_ViewHostOnlyOnAChild_Reports103()
        {
            GameObject root = Track(new GameObject("root"));
            root.SetActive(false);
            GameObject child = Child(root, "child");
            child.AddComponent<ViewHost>();
            ViewCatalog catalog = Catalog("Hosts", Row("a", root));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.MissingViewHost, CatalogIssueSeverity.Error, catalog, 0);
        }

        // ---- 101 key with no row ----

        [Test]
        public void Validate_KeyWithoutRow_Reports101OnTheKeyField()
        {
            ViewCatalog catalog = Catalog("Rows", Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("plain")));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysOrphan) }, Settings());

            AssertSingleKeyIssue(issues, DiagnosticIds.UnknownViewId, typeof(ValidationKeysOrphan), "Open", null, -1);
        }

        [Test]
        public void Validate_KeyWithRowInAnotherCatalog_HasNoFindings()
        {
            ViewCatalog unrelated = Catalog("Unrelated", Row("other", HostPrefab("other")));
            ViewCatalog owner = Catalog("Owner", Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("plain")));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { unrelated, owner }, new[] { typeof(ValidationKeysPlain) }, Settings());

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [Test]
        public void Validate_KeyTypesWithoutKeyFields_HasNoFindings()
        {
            List<CatalogIssue> issues = CatalogValidation.Validate(new ViewCatalog[0], new[] { typeof(ValidationIds) }, Settings());

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        // ---- 102 key and view types ----

        [Test]
        public void Validate_ViewWithoutArgumentsForAKeyWithArguments_Reports102()
        {
            ViewCatalog catalog = Catalog(
                "Types",
                Row("first", HostPrefab("first")),
                Row(ValidationIds.ArgsMismatch, HostPrefab<ValidationPlainView>("plain")));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysArgsMismatch) }, Settings());

            AssertSingleKeyIssue(issues, DiagnosticIds.ViewTypeMismatch, typeof(ValidationKeysArgsMismatch), "Open", catalog, 1);
        }

        [Test]
        public void Validate_ViewWithADifferentResultType_Reports102()
        {
            ViewCatalog catalog = Catalog("Types", Row(ValidationIds.ResultMismatch, HostPrefab<ValidationResultView>("result")));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysResultMismatch) }, Settings());

            AssertSingleKeyIssue(issues, DiagnosticIds.ViewTypeMismatch, typeof(ValidationKeysResultMismatch), "Open", catalog, 0);
        }

        [Test]
        public void Validate_ViewComponentOnlyOnAChild_Reports102()
        {
            GameObject root = HostPrefab("root");
            GameObject child = Child(root, "child");
            child.AddComponent<ValidationPlainView>();
            ViewCatalog catalog = Catalog("Types", Row(ValidationIds.Plain, root));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysPlain) }, Settings());

            AssertSingleKeyIssue(issues, DiagnosticIds.ViewTypeMismatch, typeof(ValidationKeysPlain), "Open", catalog, 0);
        }

        [Test]
        public void Validate_MatchingViewForAKeyWithoutArgumentsAndResult_Passes()
        {
            ViewCatalog catalog = Catalog("Types", Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("plain")));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysPlain) }, Settings());

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [Test]
        public void Validate_MatchingViewForAKeyWithArgumentsOnly_Passes()
        {
            ViewCatalog catalog = Catalog("Types", Row(ValidationIds.Args, HostPrefab<ValidationArgsView>("args")));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysArgs) }, Settings());

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [Test]
        public void Validate_MatchingViewForAKeyWithArgumentsAndResult_Passes()
        {
            ViewCatalog catalog = Catalog("Types", Row(ValidationIds.Result, HostPrefab<ValidationResultView>("result")));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysResult) }, Settings());

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        // ---- 122 Close on a layer without back ----

        [Test]
        public void Validate_CloseOnTheSystemLayer_Reports122AsWarning()
        {
            ViewCatalog catalog = Catalog(
                "Back",
                Row("a", HostPrefab("a"), ViewLayers.Screens.Id, back: BackPolicy.Close),
                Row("b", HostPrefab("b"), ViewLayers.System.Id, back: BackPolicy.Close));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.BackCloseOnLayerWithoutBack, CatalogIssueSeverity.Warning, catalog, 1);
        }

        [TestCase("screens")]
        [TestCase("popups")]
        public void Validate_CloseOnALayerThatTakesBack_Passes(string layerId)
        {
            ViewCatalog catalog = Catalog("Back", Row("a", HostPrefab("a"), layerId, back: BackPolicy.Close));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [TestCase(BackPolicy.LayerDefault)]
        [TestCase(BackPolicy.Ignore)]
        [TestCase(BackPolicy.HandledByView)]
        public void Validate_OtherBackPoliciesOnTheSystemLayer_Pass(BackPolicy back)
        {
            ViewCatalog catalog = Catalog("Back", Row("a", HostPrefab("a"), ViewLayers.System.Id, back: back));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [Test]
        public void Validate_CloseOnAKnownCustomLayerWithoutTakesBack_Reports122()
        {
            ViewCatalog catalog = Catalog("Back", Row("a", HostPrefab("a"), ValidationTestLayers.Hud.Id, back: BackPolicy.Close));
            CatalogValidationSettings settings = Settings();
            List<ViewLayer> known = CatalogValidation.DefaultLayers();
            known.Add(ValidationTestLayers.Hud);
            settings.KnownLayers = known;

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], settings);

            AssertSingleRowIssue(issues, DiagnosticIds.BackCloseOnLayerWithoutBack, CatalogIssueSeverity.Warning, catalog, 0);
        }

        [Test]
        public void Validate_CloseOnAnUnknownLayer_ReportsOnly109()
        {
            ViewCatalog catalog = Catalog("Back", Row("a", HostPrefab("a"), "hud", back: BackPolicy.Close));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.UnknownLayer, CatalogIssueSeverity.Error, catalog, 0);
        }

        // ---- 123 dropdown in a view ----

        [Test]
        public void Validate_PrefabWithADropdownInAGrandchild_Reports123AsWarning()
        {
            GameObject root = HostPrefab("root");
            GameObject panel = Child(root, "panel");
            GameObject dropdown = Child(panel, "dropdown");
            dropdown.AddComponent<Dropdown>();
            ViewCatalog catalog = Catalog("Dropdowns", Row("a", HostPrefab("clean")), Row("b", root));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.DropdownInView, CatalogIssueSeverity.Warning, catalog, 1);
        }

        [Test]
        public void Validate_PrefabWithADropdownInAnInactiveChild_Reports123()
        {
            GameObject root = HostPrefab("root");
            GameObject dropdown = Child(root, "dropdown");
            dropdown.AddComponent<Dropdown>();
            dropdown.SetActive(false);
            ViewCatalog catalog = Catalog("Dropdowns", Row("a", root));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.DropdownInView, CatalogIssueSeverity.Warning, catalog, 0);
        }

        [Test]
        public void Validate_PrefabWithADropdownOnTheRoot_Reports123()
        {
            GameObject root = HostPrefab("root");
            root.AddComponent<Dropdown>();
            ViewCatalog catalog = Catalog("Dropdowns", Row("a", root));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.DropdownInView, CatalogIssueSeverity.Warning, catalog, 0);
        }

        // ---- Order ----

        [Test]
        public void Validate_MixedFindings_AreOrderedByCatalogThenRowThenKeyTypeThenField()
        {
            ViewCatalog second = Catalog(
                "Second",
                Row("b0", HostPrefab("b0"), "nowhere"),
                Row("b1", null));
            ViewCatalog first = Catalog("First", Row("a0", HostPrefab("a0"), "nowhere"));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { second, first },
                new[] { typeof(ValidationKeysOrderB), typeof(ValidationKeysOrderA) },
                Settings());

            Assert.That(
                issues.Select(Format).ToArray(),
                Is.EqualTo(new[]
                {
                    "UIDIRECTOR109|Second|0",
                    "UIDIRECTOR121|Second|1",
                    "UIDIRECTOR109|First|0",
                    "UIDIRECTOR101|" + typeof(ValidationKeysOrderA).FullName + ".Alpha",
                    "UIDIRECTOR101|" + typeof(ValidationKeysOrderA).FullName + ".Zeta",
                    "UIDIRECTOR101|" + typeof(ValidationKeysOrderB).FullName + ".Alpha",
                    "UIDIRECTOR101|" + typeof(ValidationKeysOrderB).FullName + ".Zeta"
                }));
        }

        [Test]
        public void Validate_SameInputTwice_GivesTheSameFindingsInTheSameOrder()
        {
            ViewCatalog catalog = Catalog(
                "Repeat",
                Row("dup", HostPrefab("a"), "nowhere"),
                Row("dup", null),
                Row("system", HostPrefab("b"), ViewLayers.System.Id, back: BackPolicy.Close));
            Type[] keys = { typeof(ValidationKeysOrderA), typeof(ValidationKeysOrphan) };

            string[] first = CatalogValidation.Validate(new[] { catalog }, keys, Settings()).Select(Format).ToArray();
            string[] second = CatalogValidation.Validate(new[] { catalog }, keys, Settings()).Select(Format).ToArray();

            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void Validate_FindingsOfDifferentRules_AreReportedTogether()
        {
            ViewCatalog catalog = Catalog(
                "Mixed",
                Row("hostless", Track(new GameObject("hostless"))),
                Row("nothing", null, "nowhere"));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            Assert.That(
                issues.Select(Format).ToArray(),
                Is.EqualTo(new[]
                {
                    "UIDIRECTOR103|Mixed|0",
                    "UIDIRECTOR109|Mixed|1",
                    "UIDIRECTOR121|Mixed|1"
                }));
        }

        // ---- Defaults and the project ----

        [Test]
        public void DefaultSettings_KnownLayers_HoldTheThreeDefaultLayers()
        {
            CatalogValidationSettings settings = CatalogValidation.DefaultSettings();

            string[] ids = settings.KnownLayers.Select(layer => layer.Id).ToArray();
            Assert.That(ids, Does.Contain(ViewLayers.Screens.Id));
            Assert.That(ids, Does.Contain(ViewLayers.Popups.Id));
            Assert.That(ids, Does.Contain(ViewLayers.System.Id));
            Assert.That(ids, Is.Unique);
        }

        [Test]
        public void DefaultSettings_KnownLayers_IncludeAStaticLayerDeclaredInAnotherAssembly()
        {
            CatalogValidationSettings settings = CatalogValidation.DefaultSettings();

            Assert.That(settings.KnownLayers.Select(layer => layer.Id), Does.Contain(ValidationTestLayers.Hud.Id));
        }

        [Test]
        public void DefaultSettings_RowOnALayerDeclaredInAnotherAssembly_IsNotUnknown()
        {
            ViewCatalog catalog = Catalog("Layers", Row("a", HostPrefab("a"), ValidationTestLayers.Hud.Id));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], CatalogValidation.DefaultSettings());

            Assert.That(issues.Where(issue => issue.Id == DiagnosticIds.UnknownLayer), Is.Empty);
        }

        [Test]
        public void ValidateProject_InThisHost_DoesNotThrow()
        {
            List<CatalogIssue> issues = null;

            Assert.DoesNotThrow(() => issues = CatalogValidation.ValidateProject());

            Assert.That(issues, Is.Not.Null);
            Assert.That(issues.All(issue => !string.IsNullOrEmpty(issue.Id) && !string.IsNullOrEmpty(issue.Message)), Is.True);
        }

        // ---- Stable ids ----

        [Test]
        public void DiagnosticIds_ValidatorRules_KeepTheirStableNumbers()
        {
            Assert.That(DiagnosticIds.UnknownViewId, Is.EqualTo("UIDIRECTOR101"));
            Assert.That(DiagnosticIds.ViewTypeMismatch, Is.EqualTo("UIDIRECTOR102"));
            Assert.That(DiagnosticIds.MissingViewHost, Is.EqualTo("UIDIRECTOR103"));
            Assert.That(DiagnosticIds.DuplicateViewId, Is.EqualTo("UIDIRECTOR108"));
            Assert.That(DiagnosticIds.UnknownLayer, Is.EqualTo("UIDIRECTOR109"));
            Assert.That(DiagnosticIds.NoProviderForAssetKey, Is.EqualTo("UIDIRECTOR120"));
            Assert.That(DiagnosticIds.RowWithoutSource, Is.EqualTo("UIDIRECTOR121"));
            Assert.That(DiagnosticIds.BackCloseOnLayerWithoutBack, Is.EqualTo("UIDIRECTOR122"));
            Assert.That(DiagnosticIds.DropdownInView, Is.EqualTo("UIDIRECTOR123"));
            Assert.That(DiagnosticIds.AssetKeyNotAddressable, Is.EqualTo("UIDIRECTOR124"));
            Assert.That(DiagnosticIds.RowWithEmptyId, Is.EqualTo("UIDIRECTOR125"));
        }

        // ---- Row with a prefab and an asset key ----

        [Test]
        public void Validate_RowWithPrefabAndAssetKeyWhileAddressablesNotInstalled_HasNoFindings()
        {
            ViewCatalog catalog = Catalog("Both", Row("a", HostPrefab("a"), assetKey: "addr/missing"));
            CatalogValidationSettings settings = Settings();
            settings.AddressablesInstalled = false;

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], settings);

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [Test]
        public void Validate_RowWithPrefabAndAnUnknownAssetKey_HasNoFindingsAndAsksNoAddress()
        {
            ViewCatalog catalog = Catalog("Both", Row("a", HostPrefab("a"), assetKey: "addr/missing"));
            var asked = new List<string>();

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new Type[0], AddressSettings(new Dictionary<string, GameObject>(), asked));

            Assert.That(issues, Is.Empty, Describe(issues));
            Assert.That(asked, Is.Empty);
        }

        // ---- 102 through an address ----

        [Test]
        public void Validate_AssetKeyWhoseMainAssetMatchesTheKey_HasNoFindings()
        {
            ViewCatalog catalog = Catalog("Keys", Row(ValidationIds.Plain, null, assetKey: "addr/plain"));
            var addresses = new Dictionary<string, GameObject> { { "addr/plain", HostPrefab<ValidationPlainView>("addressed") } };

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysPlain) }, AddressSettings(addresses));

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        [Test]
        public void Validate_AssetKeyWhoseMainAssetMatchesAKeyWithArgumentsAndResult_HasNoFindings()
        {
            ViewCatalog catalog = Catalog("Keys", Row(ValidationIds.Result, null, assetKey: "addr/result"));
            var addresses = new Dictionary<string, GameObject> { { "addr/result", HostPrefab<ValidationResultView>("addressed") } };

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { catalog }, new[] { typeof(ValidationKeysResult) }, AddressSettings(addresses));

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        // ---- Duplicate ids and keys ----

        [Test]
        public void Validate_DuplicateIdWhereTheFirstRowMatchesTheKey_ReportsOnly108OnTheSecondRow()
        {
            ViewCatalog first = Catalog("A", Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("plain")));
            ViewCatalog second = Catalog("B", Row(ValidationIds.Plain, HostPrefab<ValidationResultView>("result")));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { first, second }, new[] { typeof(ValidationKeysPlain) }, Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.DuplicateViewId, CatalogIssueSeverity.Error, second, 0);
        }

        [Test]
        public void Validate_DuplicateIdWhereTheFirstRowHasTheWrongView_Reports108And102AgainstTheFirstRow()
        {
            ViewCatalog first = Catalog("A", Row(ValidationIds.Plain, HostPrefab<ValidationResultView>("result")));
            ViewCatalog second = Catalog("B", Row(ValidationIds.Plain, HostPrefab<ValidationPlainView>("plain")));

            List<CatalogIssue> issues = CatalogValidation.Validate(
                new[] { first, second }, new[] { typeof(ValidationKeysPlain) }, Settings());

            Assert.That(issues, Has.Count.EqualTo(2), Describe(issues));
            Assert.That(issues[0].Id, Is.EqualTo(DiagnosticIds.DuplicateViewId));
            Assert.That(issues[0].Catalog, Is.SameAs(second));
            Assert.That(issues[0].RowIndex, Is.EqualTo(0));
            Assert.That(issues[1].Id, Is.EqualTo(DiagnosticIds.ViewTypeMismatch));
            Assert.That(issues[1].KeyType, Is.EqualTo(typeof(ValidationKeysPlain)));
            Assert.That(issues[1].KeyField, Is.EqualTo("Open"));
            Assert.That(issues[1].Catalog, Is.SameAs(first));
            Assert.That(issues[1].RowIndex, Is.EqualTo(0));
        }

        // ---- 101 on a non-public key field ----

        [Test]
        public void Validate_NonPublicKeyFieldWithoutRow_Reports101()
        {
            // Key classes are often internal in a game, so an internal field must be checked like a public one.
            List<CatalogIssue> issues = CatalogValidation.Validate(
                new ViewCatalog[0], new[] { typeof(ValidationKeysInternal) }, Settings());

            AssertSingleKeyIssue(issues, DiagnosticIds.UnknownViewId, typeof(ValidationKeysInternal), "Open", null, -1);
        }

        // ---- 123 more cases ----

        [Test]
        public void Validate_PrefabWithATmpDropdown_Reports123AsWarning()
        {
            Type tmpDropdown = FindLoadedType(TmpDropdownTypeName);
            Assert.That(tmpDropdown, Is.Not.Null, "TMP_Dropdown ships with com.unity.ugui, which this host lists.");

            GameObject root = HostPrefab("root");
            GameObject dropdown = ChildUI(root, "dropdown");
            dropdown.AddComponent(tmpDropdown);
            ViewCatalog catalog = Catalog("Dropdowns", Row("a", root));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            AssertSingleRowIssue(issues, DiagnosticIds.DropdownInView, CatalogIssueSeverity.Warning, catalog, 0);
        }

        [Test]
        public void Validate_PrefabWithOtherUiControlsButNoDropdown_HasNoFindings()
        {
            GameObject root = HostPrefab("root");
            ChildUI(root, "button").AddComponent<Button>();
            ChildUI(root, "toggle").AddComponent<Toggle>();
            ChildUI(root, "scroll").AddComponent<ScrollRect>();
            ChildUI(root, "input").AddComponent<InputField>();
            ViewCatalog catalog = Catalog("Controls", Row("a", root));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], Settings());

            Assert.That(issues, Is.Empty, Describe(issues));
        }

        // ---- Key and layer discovery ----

        [Test]
        public void FindKeyTypes_IncludingTestAssemblies_ContainsAMarkedClass()
        {
            Assert.That(CatalogValidation.FindKeyTypes(true), Does.Contain(typeof(ValidationKeysMarked)));
        }

        [Test]
        public void FindKeyTypes_MarkedClassInATestAssembly_IsSkipped()
        {
            Assert.That(CatalogValidation.FindKeyTypes(), Has.No.Member(typeof(ValidationKeysMarked)));
            Assert.That(CatalogValidation.FindKeyTypes(), Has.No.Member(typeof(MarkedOrphanKeys)));
        }

        [Test]
        public void ValidateProject_MarkedKeyClassInATestAssembly_ReportsNothingForIt()
        {
            // The fixture is a real finding when handed over directly, so the skip is what keeps the project run clean.
            Assert.That(CatalogValidation.FindKeyTypes(true), Does.Contain(typeof(MarkedOrphanKeys)));
            List<CatalogIssue> direct = CatalogValidation.Validate(new ViewCatalog[0], new[] { typeof(MarkedOrphanKeys) }, Settings());
            AssertSingleKeyIssue(direct, DiagnosticIds.UnknownViewId, typeof(MarkedOrphanKeys), "Open", null, -1);

            List<CatalogIssue> issues = CatalogValidation.ValidateProject();

            Assert.That(issues.Where(issue => issue.KeyType != null && issue.KeyType.Namespace == typeof(ValidationIds).Namespace), Is.Empty, Describe(issues));
        }

        [Test]
        public void FindKeyTypes_SkipsAClassWithKeysThatIsNotMarked()
        {
            Assert.That(CatalogValidation.FindKeyTypes(), Has.No.Member(typeof(ValidationKeysUnmarked)));
        }

        [Test]
        public void ValidateProject_ClassWithKeysThatIsNotMarked_HasNoFinding()
        {
            List<CatalogIssue> issues = CatalogValidation.ValidateProject();

            Assert.That(issues.Where(issue => issue.KeyType == typeof(ValidationKeysUnmarked)), Is.Empty);
        }

        [Test]
        public void DefaultSettings_KnownLayers_IncludeANonPublicStaticLayer()
        {
            CatalogValidationSettings settings = CatalogValidation.DefaultSettings();

            Assert.That(settings.KnownLayers.Select(layer => layer.Id), Does.Contain(ValidationInternalLayers.Overlay.Id));
        }

        [Test]
        public void DefaultSettings_RowOnANonPublicStaticLayer_IsNotUnknown()
        {
            ViewCatalog catalog = Catalog("Layers", Row("a", HostPrefab("a"), ValidationInternalLayers.Overlay.Id));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], CatalogValidation.DefaultSettings());

            Assert.That(issues.Where(issue => issue.Id == DiagnosticIds.UnknownLayer), Is.Empty);
        }

        // ---- Addressables wiring of DefaultSettings ----

#if ECANAKLI_UIDIRECTOR_ADDRESSABLES
        [Test]
        public void DefaultSettings_WithAddressablesEditor_MarksItInstalledAndLooksUpAddresses()
        {
            Assume.That(AddressablesEditorPresent(), Is.True, "The Addressables editor is not installed in this host.");

            CatalogValidationSettings settings = CatalogValidation.DefaultSettings();

            Assert.That(settings.AddressablesInstalled, Is.True);
            Assert.That(settings.LookupAddress, Is.Not.Null);
        }

        [Test]
        public void DefaultSettings_WithAddressablesEditor_ReportsAnUnknownAddressAsMissing()
        {
            Assume.That(AddressablesEditorPresent(), Is.True, "The Addressables editor is not installed in this host.");
            CatalogValidationSettings settings = CatalogValidation.DefaultSettings();
            AddressLookupResult result = default;

            Assert.DoesNotThrow(() => result = settings.LookupAddress(NoSuchAddress));

            Assert.That(result.Exists, Is.False);
        }

        [Test]
        public void DefaultSettings_WithAddressablesEditor_ReportsAnUnknownAssetKeyAs124Not120()
        {
            Assume.That(AddressablesEditorPresent(), Is.True, "The Addressables editor is not installed in this host.");
            ViewCatalog catalog = Catalog("Keys", Row("a", null, assetKey: NoSuchAddress));

            List<CatalogIssue> issues = CatalogValidation.Validate(new[] { catalog }, new Type[0], CatalogValidation.DefaultSettings());

            AssertSingleRowIssue(issues, DiagnosticIds.AssetKeyNotAddressable, CatalogIssueSeverity.Warning, catalog, 0);
        }

#else
        [Test]
        public void DefaultSettings_WithoutAddressablesEditor_MarksItNotInstalledAndHasNoLookup()
        {
            Assume.That(AddressablesEditorPresent(), Is.False, "The Addressables editor is installed in this host.");

            CatalogValidationSettings settings = CatalogValidation.DefaultSettings();

            Assert.That(settings.AddressablesInstalled, Is.False);
            Assert.That(settings.LookupAddress, Is.Null);
        }

#endif

        // ---- Project catalogs (temporary assets) ----

        [Test]
        public void ValidateProject_CatalogAssetWithAnEmptyRow_IsFoundAndReports121OnIt()
        {
            ViewCatalog saved = SaveCatalog("Empty.asset", Row("validation.project.empty", null));

            Assert.That(CatalogValidation.FindProjectCatalogs().Contains(saved), Is.True, "FindProjectCatalogs misses the saved catalog.");

            List<CatalogIssue> issues = CatalogValidation.ValidateProject();

            CatalogIssue[] ours = issues.Where(issue => issue.Catalog == saved && issue.Id == DiagnosticIds.RowWithoutSource).ToArray();
            Assert.That(ours, Has.Length.EqualTo(1), Describe(issues));
            Assert.That(ours[0].RowIndex, Is.EqualTo(0));
            Assert.That(ours[0].Severity, Is.EqualTo(CatalogIssueSeverity.Error));
        }

        [Test]
        public void ValidateProject_TwoCatalogAssetsSharingAnId_Reports108OnTheLaterPath()
        {
            // B is created first, so creation order and path order disagree.
            ViewCatalog b = SaveCatalog("B.asset", Row("validation.project.shared", null));
            ViewCatalog a = SaveCatalog("A.asset", Row("validation.project.shared", null));

            List<ViewCatalog> found = CatalogValidation.FindProjectCatalogs();
            Assert.That(found.IndexOf(a), Is.GreaterThanOrEqualTo(0));
            Assert.That(found.IndexOf(b), Is.GreaterThan(found.IndexOf(a)), "Project catalogs are ordered by asset path.");

            List<CatalogIssue> issues = CatalogValidation.ValidateProject();

            CatalogIssue[] duplicates = issues
                .Where(issue => issue.Id == DiagnosticIds.DuplicateViewId && (issue.Catalog == a || issue.Catalog == b))
                .ToArray();
            Assert.That(duplicates, Has.Length.EqualTo(1), Describe(issues));
            Assert.That(duplicates[0].Catalog == b, Is.True, "The 108 belongs on the catalog with the later path.");
            Assert.That(duplicates[0].RowIndex, Is.EqualTo(0));
        }

        // ---- Menu log format (temporary asset) ----

        [Test]
        public void ValidateCatalogsMenu_LogsAnIdFirstLinePerIssueAndAFailedSummary()
        {
            SaveCatalog(
                "Menu.asset",
                Row("validation.menu.empty", null),
                Row("validation.menu.system", null, ViewLayers.System.Id, back: BackPolicy.Close));
            LogAssert.ignoreFailingMessages = true;

            LogAssert.Expect(LogType.Error, new Regex("^UIDIRECTOR121: Row 0 "));
            LogAssert.Expect(LogType.Error, new Regex("^UIDIRECTOR121: Row 1 "));
            LogAssert.Expect(LogType.Warning, new Regex("^UIDIRECTOR122: Row 1 "));
            LogAssert.Expect(LogType.Log, new Regex("^UIDirector catalog validation FAILED: "));

            Assert.That(EditorApplication.ExecuteMenuItem("Tools/UIDirector/Validate Catalogs"), Is.True);
        }

        // ---- Helpers ----

        // A marked class holding a key with no row; it lives in this test assembly, so project runs must skip it.
        [ViewKeys]
        internal static class MarkedOrphanKeys
        {
            public static readonly ViewKey<NoArgs, NoResult> Open = ViewKey.Create("validation.marked.orphan");
        }

        private T Track<T>(T instance) where T : Object
        {
            _created.Add(instance);
            return instance;
        }

        // A catalog saved as an asset in the temporary folder; rows can hold no scene prefab.
        private ViewCatalog SaveCatalog(string fileName, params ViewEntry[] rows)
        {
            if (string.IsNullOrEmpty(_folder))
            {
                string name = "UIDirectorValidationTests_" + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", name);
                _folder = "Assets/" + name;
            }

            string path = _folder + "/" + fileName;
            AssetDatabase.CreateAsset(ViewCatalog.Create(rows), path);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);

            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(path);
            Assert.That(catalog != null, Is.True, "The catalog asset at " + path + " did not load.");
            return catalog;
        }

        private static bool AddressablesEditorPresent()
        {
            return Type.GetType(AddressablesSettingsTypeName) != null;
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        // A child with a RectTransform, as UI controls have.
        private static GameObject ChildUI(GameObject parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        // A prefab stand-in: a plain object with ViewHost on its root.
        private GameObject HostPrefab(string name)
        {
            var root = Track(new GameObject(name));
            root.SetActive(false);
            root.AddComponent<ViewHost>();
            return root;
        }

        private GameObject HostPrefab<TView>(string name) where TView : Component
        {
            GameObject root = HostPrefab(name);
            root.AddComponent<TView>();
            return root;
        }

        private static ViewEntry Row(
            string id,
            GameObject prefab,
            string layerId = null,
            string assetKey = "",
            BackPolicy back = BackPolicy.LayerDefault)
        {
            return new ViewEntry(
                id,
                prefab,
                assetKey,
                layerId ?? ViewLayers.Screens.Id,
                ViewRetention.ReleaseOnClose,
                back,
                false,
                CoverPolicy.LayerDefault,
                CoveredMode.DisableCanvas,
                false);
        }

        private ViewCatalog Catalog(string name, params ViewEntry[] rows)
        {
            ViewCatalog catalog = Track(ViewCatalog.Create(rows));
            catalog.name = name;
            return catalog;
        }

        // Defaults for every rule except the address checks: Addressables present, no lookup.
        private static CatalogValidationSettings Settings()
        {
            return new CatalogValidationSettings { AddressablesInstalled = true };
        }

        private static CatalogValidationSettings AddressSettings(Dictionary<string, GameObject> addresses, List<string> asked = null)
        {
            return new CatalogValidationSettings
            {
                AddressablesInstalled = true,
                LookupAddress = key =>
                {
                    if (asked != null)
                        asked.Add(key);

                    return addresses.TryGetValue(key, out GameObject asset) ? new AddressLookupResult(true, asset) : default;
                }
            };
        }

        private static void AssertSingleRowIssue(
            List<CatalogIssue> issues,
            string id,
            CatalogIssueSeverity severity,
            ViewCatalog catalog,
            int rowIndex)
        {
            Assert.That(issues, Has.Count.EqualTo(1), Describe(issues));
            CatalogIssue issue = issues[0];
            Assert.That(issue.Id, Is.EqualTo(id));
            Assert.That(issue.Severity, Is.EqualTo(severity));
            Assert.That(issue.Catalog, Is.SameAs(catalog));
            Assert.That(issue.RowIndex, Is.EqualTo(rowIndex));
            Assert.That(issue.Message, Is.Not.Empty);
        }

        private static void AssertSingleKeyIssue(
            List<CatalogIssue> issues,
            string id,
            Type keyType,
            string keyField,
            ViewCatalog catalog,
            int rowIndex)
        {
            Assert.That(issues, Has.Count.EqualTo(1), Describe(issues));
            CatalogIssue issue = issues[0];
            Assert.That(issue.Id, Is.EqualTo(id));
            Assert.That(issue.Severity, Is.EqualTo(CatalogIssueSeverity.Error));
            Assert.That(issue.KeyType, Is.EqualTo(keyType));
            Assert.That(issue.KeyField, Is.EqualTo(keyField));
            Assert.That(issue.Catalog, Is.SameAs(catalog));
            Assert.That(issue.RowIndex, Is.EqualTo(rowIndex));
            Assert.That(issue.Message, Is.Not.Empty);
        }

        // Row findings read "id|catalog|row", key findings "id|Type.Field".
        private static string Format(CatalogIssue issue)
        {
            return issue.KeyType != null && issue.Catalog == null
                ? issue.Id + "|" + issue.KeyType.FullName + "." + issue.KeyField
                : issue.Id + "|" + issue.Catalog.name + "|" + issue.RowIndex;
        }

        private static string Describe(List<CatalogIssue> issues)
        {
            return issues.Count == 0
                ? "no findings"
                : "findings: " + string.Join("; ", issues.Select(issue => issue.Id + " " + issue.Message));
        }
    }
}
