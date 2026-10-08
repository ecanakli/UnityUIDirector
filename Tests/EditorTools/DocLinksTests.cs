using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.UIDirector.EditorTools.Tests
{
    /// <summary>Doc links built from the installed version, and the Troubleshooting page against DiagnosticIds in both directions.</summary>
    [TestFixture]
    public sealed class DocLinksTests
    {
        private const string PackageFolder = "Packages/com.ecanakli.uidirector";
        private static readonly Regex LevelTwoIdHeading = new Regex(@"^##\s+(UIDIRECTOR\d+)\s*$");
        private static readonly Regex AnyLevelIdHeading = new Regex(@"^#{1,6}\s+(UIDIRECTOR\d+)\b");

        // DocLinks.For

        [TestCase("UIDIRECTOR101", "uidirector101")]
        [TestCase("UIDIRECTOR110", "uidirector110")]
        [TestCase("UIDIRECTOR124", "uidirector124")]
        [TestCase("UIDIRECTOR125", "uidirector125")]
        public void For_DiagnosticId_EndsWithTheTroubleshootingPageAndTheLowerCaseAnchor(string id, string anchor)
        {
            string link = DocLinks.For(id);

            Assert.That(link, Does.EndWith("Documentation~/Troubleshooting.md#" + anchor));
        }

        [Test]
        public void For_DiagnosticId_IsAnAbsoluteWebLink()
        {
            string link = DocLinks.For("UIDIRECTOR104");

            Assert.That(link, Does.StartWith("https://"));
        }

        [Test]
        public void For_IdAlreadyInLowerCase_GivesTheSameLinkAsTheUpperCaseId()
        {
            Assert.That(DocLinks.For("uidirector104"), Is.EqualTo(DocLinks.For("UIDIRECTOR104")));
        }

        [Test]
        public void For_NullId_ThrowsArgumentNullException()
        {
            Assert.That(() => { DocLinks.For(null); }, Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void For_EveryId_GivesADistinctLink()
        {
            IReadOnlyList<string> ids = DocLinks.AllDiagnosticIds();
            var links = new List<string>();
            for (var i = 0; i < ids.Count; i++)
                links.Add(DocLinks.For(ids[i]));

            Assert.That(links, Is.Unique);
        }

        // DocLinks root from the installed version

        [Test]
        public void RootFor_KnownVersion_PointsAtTheVersionTag()
        {
            Assert.That(
                DocLinks.RootFor("0.2.1"),
                Is.EqualTo("https://github.com/ecanakli/UnityUIDirector/blob/v0.2.1/Documentation~/"));
        }

        [Test]
        public void RootFor_PreReleaseVersion_KeepsTheWholeVersionInTheTag()
        {
            Assert.That(DocLinks.RootFor("1.0.0-preview.2"), Does.Contain("/blob/v1.0.0-preview.2/Documentation~/"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void RootFor_UnknownVersion_FallsBackToMain(string version)
        {
            Assert.That(
                DocLinks.RootFor(version),
                Is.EqualTo("https://github.com/ecanakli/UnityUIDirector/blob/main/Documentation~/"));
        }

        [Test]
        public void DocumentationRoot_UsesTheVersionOfTheInstalledPackage()
        {
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DocLinks).Assembly);

            Assert.That(DocLinks.DocumentationRoot, Is.EqualTo(DocLinks.RootFor(package?.version)));
            Assert.That(DocLinks.DocumentationRoot, Does.EndWith("/Documentation~/"));
        }

        [Test]
        public void For_DiagnosticId_StartsWithTheDocumentationRoot()
        {
            Assert.That(DocLinks.For("UIDIRECTOR104"), Does.StartWith(DocLinks.DocumentationRoot));
        }

        // DocLinks.AllDiagnosticIds

        [Test]
        public void AllDiagnosticIds_ListsEveryIdFrom101To126()
        {
            IReadOnlyList<string> ids = DocLinks.AllDiagnosticIds();

            Assert.That(ids, Is.EquivalentTo(ExpectedIds()));
        }

        [Test]
        public void AllDiagnosticIds_ListsEachIdOnce()
        {
            Assert.That(DocLinks.AllDiagnosticIds(), Is.Unique);
        }

        [Test]
        public void AllDiagnosticIds_IsSortedById()
        {
            Assert.That(DocLinks.AllDiagnosticIds(), Is.Ordered);
        }

        [TestCase("UIDIRECTOR101", nameof(DiagnosticIds.UnknownViewId))]
        [TestCase("UIDIRECTOR120", nameof(DiagnosticIds.NoProviderForAssetKey))]
        [TestCase("UIDIRECTOR121", nameof(DiagnosticIds.RowWithoutSource))]
        [TestCase("UIDIRECTOR124", nameof(DiagnosticIds.AssetKeyNotAddressable))]
        [TestCase("UIDIRECTOR125", nameof(DiagnosticIds.RowWithEmptyId))]
        public void AllDiagnosticIds_Constant_IsListed(string value, string constantName)
        {
            Assert.That(DocLinks.AllDiagnosticIds(), Does.Contain(value), constantName);
        }

        [Test]
        public void AllDiagnosticIds_EveryDiagnosticIdsConstant_IsListed()
        {
            var constants = new List<string>
            {
                DiagnosticIds.UnknownViewId,
                DiagnosticIds.ViewTypeMismatch,
                DiagnosticIds.MissingViewHost,
                DiagnosticIds.LoadFailed,
                DiagnosticIds.LoadTimedOut,
                DiagnosticIds.TransitionTimedOut,
                DiagnosticIds.ViewLostOutsidePackage,
                DiagnosticIds.DuplicateViewId,
                DiagnosticIds.UnknownLayer,
                DiagnosticIds.InputHoldTooLong,
                DiagnosticIds.QueueHoldTooLong,
                DiagnosticIds.WaitingCapReached,
                DiagnosticIds.OwnerCancelledOffThread,
                DiagnosticIds.CameraRootWithoutCamera,
                DiagnosticIds.SortBandOverflow,
                DiagnosticIds.NoEventSystem,
                DiagnosticIds.OnOpenThrew,
                DiagnosticIds.CompleteInsideOnOpen,
                DiagnosticIds.GateThrew,
                DiagnosticIds.NoProviderForAssetKey,
                DiagnosticIds.QueueLayerMismatch,
                DiagnosticIds.RowWithoutSource,
                DiagnosticIds.BackCloseOnLayerWithoutBack,
                DiagnosticIds.DropdownInView,
                DiagnosticIds.AssetKeyNotAddressable,
                DiagnosticIds.RowWithEmptyId
            };

            Assert.That(DocLinks.AllDiagnosticIds(), Is.EquivalentTo(constants));
        }

        [Test]
        public void AllDiagnosticIds_EveryPublicConstantOfDiagnosticIds_IsListed()
        {
            var constants = new List<string>();
            FieldInfo[] fields = typeof(DiagnosticIds).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (FieldInfo field in fields)
            {
                if (field.IsLiteral && field.FieldType == typeof(string))
                    constants.Add((string)field.GetRawConstantValue());
            }

            Assert.That(DocLinks.AllDiagnosticIds(), Is.EquivalentTo(constants));
        }

        // Troubleshooting page against DiagnosticIds

        [Test]
        public void TroubleshootingPage_Exists()
        {
            Assert.That(TroubleshootingPath(), Does.Exist.IgnoreDirectories);
        }

        [TestCaseSource(nameof(ExpectedIds))]
        public void TroubleshootingPage_DiagnosticId_HasExactlyOneHeading(string id)
        {
            List<string> headings = IdHeadings(LevelTwoIdHeading);

            Assert.That(headings, Has.Exactly(1).EqualTo(id));
        }

        [Test]
        public void TroubleshootingPage_EveryIdHeading_IsAnIdThatExists()
        {
            List<string> headings = IdHeadings(AnyLevelIdHeading);
            var ids = new List<string>(DocLinks.AllDiagnosticIds());

            Assert.That(headings, Has.All.Matches<string>(heading => ids.Contains(heading)));
        }

        [Test]
        public void TroubleshootingPage_IdHeadings_AreExactlyTheDiagnosticIds()
        {
            List<string> headings = IdHeadings(LevelTwoIdHeading);

            Assert.That(headings, Is.EquivalentTo(DocLinks.AllDiagnosticIds()));
        }

        [Test]
        public void TroubleshootingPage_IdHeadings_AreAllAtLevelTwo()
        {
            List<string> anyLevel = IdHeadings(AnyLevelIdHeading);
            List<string> levelTwo = IdHeadings(LevelTwoIdHeading);

            Assert.That(levelTwo, Is.EqualTo(anyLevel));
        }

        // Troubleshooting page: what each section says

        [TestCaseSource(nameof(ExpectedIds))]
        public void TroubleshootingPage_Section_HasEachLabelExactlyOnceInOrder(string id)
        {
            List<string> section = SectionOf(id);

            var positions = new List<int>();
            foreach (string label in RequiredLabels)
            {
                List<int> found = LabelLines(section, label);
                Assert.That(found.Count, Is.EqualTo(1), id + " must carry " + label + " exactly once");
                positions.Add(found[0]);
            }

            Assert.That(positions, Is.Ordered, id + " must give What happened, then Why, then Fix");
        }

        [TestCaseSource(nameof(ExpectedIds))]
        public void TroubleshootingPage_Section_GivesTheMeaningCauseAndFixSomeText(string id)
        {
            List<string> section = SectionOf(id);

            foreach (string label in RequiredLabels)
            {
                string text = TextAfterLabel(section, label);
                Assert.That(text.Length, Is.GreaterThanOrEqualTo(MinimumLabelTextLength), id + " " + label + " is empty or too short");
            }
        }

        // Helpers

        private const string FixLabel = "**Fix.**";
        private const int MinimumLabelTextLength = 20;

        private static readonly string[] RequiredLabels = { "**What happened.**", "**Why.**", FixLabel };

        // Every label a section may carry, so the text of one label stops at the next.
        private static readonly string[] KnownLabels = { "**What happened.**", "**Why.**", FixLabel, "**Severity.**" };

        private static readonly Regex LevelTwoHeading = new Regex(@"^##\s");

        private static IEnumerable<string> ExpectedIds()
        {
            for (var number = 101; number <= 126; number++)
                yield return "UIDIRECTOR" + number;
        }

        private static string TroubleshootingPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, PackageFolder, "Documentation~", "Troubleshooting.md");
        }

        // The lines under the id's level-two heading, up to the next level-two heading.
        private static List<string> SectionOf(string id)
        {
            string path = TroubleshootingPath();
            Assert.That(path, Does.Exist.IgnoreDirectories, "The Troubleshooting page is missing.");

            var section = new List<string>();
            var inSection = false;
            var found = false;
            foreach (string line in File.ReadAllLines(path))
            {
                if (LevelTwoHeading.IsMatch(line))
                {
                    if (inSection)
                        break;

                    Match match = LevelTwoIdHeading.Match(line);
                    inSection = match.Success && match.Groups[1].Value == id;
                    found |= inSection;
                    continue;
                }

                if (inSection)
                    section.Add(line);
            }

            Assert.That(found, Is.True, "No level-two heading for " + id);
            return section;
        }

        // Indexes of the lines that start with the label.
        private static List<int> LabelLines(List<string> section, string label)
        {
            var indexes = new List<int>();
            for (var i = 0; i < section.Count; i++)
            {
                if (section[i].TrimStart().StartsWith(label, StringComparison.Ordinal))
                    indexes.Add(i);
            }

            return indexes;
        }

        // The text after the label on its line plus the lines below it, up to the next known label; trimmed.
        private static string TextAfterLabel(List<string> section, string label)
        {
            List<int> found = LabelLines(section, label);
            Assert.That(found.Count, Is.GreaterThan(0), "The section has no " + label);

            var text = new System.Text.StringBuilder();
            text.Append(section[found[0]].TrimStart().Substring(label.Length));
            for (var i = found[0] + 1; i < section.Count && !StartsWithKnownLabel(section[i]); i++)
                text.Append('\n').Append(section[i]);

            return text.ToString().Trim();
        }

        private static bool StartsWithKnownLabel(string line)
        {
            string trimmed = line.TrimStart();
            foreach (string known in KnownLabels)
            {
                if (trimmed.StartsWith(known, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        // The ids of the page's headings that match the pattern, in page order.
        private static List<string> IdHeadings(Regex pattern)
        {
            string path = TroubleshootingPath();
            Assert.That(path, Does.Exist.IgnoreDirectories, "The Troubleshooting page is missing.");

            var ids = new List<string>();
            foreach (string line in File.ReadAllLines(path))
            {
                Match match = pattern.Match(line);
                if (match.Success)
                    ids.Add(match.Groups[1].Value);
            }

            return ids;
        }
    }
}
