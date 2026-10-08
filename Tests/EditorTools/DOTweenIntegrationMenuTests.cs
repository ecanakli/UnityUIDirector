using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Ecanakli.UIDirector.EditorTools.Tests
{
    /// <summary>
    /// Toggle logic of the DOTween integration menu. The decision tests are pure; the write and check-mark
    /// tests change Player Settings defines under a reload lock and restore every build target in teardown.
    /// </summary>
    [TestFixture]
    public sealed class DOTweenIntegrationMenuTests
    {
        private const string DOTween = "DOTWEEN";
        private const string Integration = "ECANAKLI_UIDIRECTOR_DOTWEEN";
        private const string MenuPath = "Tools/UIDirector/DOTween Integration";

        private DefinesSandbox _sandbox;

        [TearDown]
        public void TearDown()
        {
            _sandbox?.Dispose();
            _sandbox = null;
        }

        // Holds the assembly reload lock, remembers the defines of every build target and restores them on Dispose.
        private sealed class DefinesSandbox : IDisposable
        {
            private readonly List<NamedBuildTarget> _targets = new List<NamedBuildTarget>();
            private readonly List<string> _originals = new List<string>();

            internal DefinesSandbox()
            {
                // The lock is taken first and released last, so no compile starts while the defines are changed or restored.
                EditorApplication.LockReloadAssemblies();
                try
                {
                    foreach (NamedBuildTarget target in EnumerateTargets())
                    {
                        string original;
                        try
                        {
                            original = PlayerSettings.GetScriptingDefineSymbols(target);
                            PlayerSettings.SetScriptingDefineSymbols(target, original);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        _targets.Add(target);
                        _originals.Add(original);
                    }
                }
                catch (Exception)
                {
                    Dispose();
                    throw;
                }
            }

            internal IReadOnlyList<NamedBuildTarget> Targets => _targets;

            internal void Seed(Func<NamedBuildTarget, string[]> symbolsFor)
            {
                foreach (NamedBuildTarget target in _targets)
                {
                    PlayerSettings.SetScriptingDefineSymbols(target, ScriptingDefineSymbols.Join(symbolsFor(target)));
                }
            }

            internal string[] Read(NamedBuildTarget target)
            {
                return ScriptingDefineSymbols.Parse(PlayerSettings.GetScriptingDefineSymbols(target));
            }

            public void Dispose()
            {
                try
                {
                    for (int i = 0; i < _targets.Count; i++)
                    {
                        PlayerSettings.SetScriptingDefineSymbols(_targets[i], _originals[i]);
                    }
                }
                finally
                {
                    _targets.Clear();
                    _originals.Clear();
                    EditorApplication.UnlockReloadAssemblies();
                }
            }

            // Every non-obsolete BuildTargetGroup plus the Server target, without duplicates.
            private static IEnumerable<NamedBuildTarget> EnumerateTargets()
            {
                var seen = new HashSet<string>();
                foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
                {
                    if (group == BuildTargetGroup.Unknown)
                    {
                        continue;
                    }

                    FieldInfo field = typeof(BuildTargetGroup).GetField(group.ToString());
                    if (field != null && Attribute.IsDefined(field, typeof(ObsoleteAttribute)))
                    {
                        continue;
                    }

                    NamedBuildTarget target;
                    try
                    {
                        target = NamedBuildTarget.FromBuildTargetGroup(group);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (seen.Add(target.TargetName))
                    {
                        yield return target;
                    }
                }

                if (seen.Add(NamedBuildTarget.Server.TargetName))
                {
                    yield return NamedBuildTarget.Server;
                }
            }
        }

        private static NamedBuildTarget ActiveTarget()
        {
            return NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
        }

        private static MethodInfo FindMenuMethod(bool validate)
        {
            FieldInfo pathField = typeof(MenuItem).GetField("menuItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo validateField = typeof(MenuItem).GetField("validate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(pathField, Is.Not.Null);
            Assert.That(validateField, Is.Not.Null);

            MethodInfo method = typeof(DOTweenIntegrationMenu)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.GetCustomAttributes<MenuItem>()
                    .Any(attribute => (string)pathField.GetValue(attribute) == MenuPath && (bool)validateField.GetValue(attribute) == validate));

            Assert.That(method, Is.Not.Null, "a " + (validate ? "validate" : "menu") + " method at " + MenuPath);
            return method;
        }

        private static bool HasDOTweenAssembly()
        {
            MethodInfo method = typeof(DOTweenIntegrationMenu).GetMethod("HasDOTweenAssembly", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "the menu detects the DOTween assembly");
            return (bool)method.Invoke(null, null);
        }

        private static bool HostHasDOTweenPackage()
        {
            MethodInfo method = typeof(DOTweenIntegrationMenu).GetMethod("IsDOTweenPackageInstalled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "the menu detects the DOTween package");
            return (bool)method.Invoke(null, null);
        }

        // The real menu method writes defines only when Decide says Enable or Disable, so the test seeds a state that does.
        private static void RequireDOTweenAssembly()
        {
            if (!HasDOTweenAssembly())
            {
                Assert.Ignore("No DOTween assembly in this project; the menu would only show its no-assembly dialog.");
            }

            if (HostHasDOTweenPackage())
            {
                Assert.Ignore("A DOTween package is installed; the menu would only show its already-active dialog.");
            }
        }

        [Serializable]
        private sealed class AsmdefVersionDefine
        {
            public string name = "";
            public string expression = "";
            public string define = "";
        }

        [Serializable]
        private sealed class AsmdefFile
        {
            public List<AsmdefVersionDefine> versionDefines = new List<AsmdefVersionDefine>();
        }

        private static string[] Others(NamedBuildTarget target, bool withIntegration)
        {
            // A per-target symbol proves each target keeps its own symbols; the order is checked too.
            string targetName = new string(target.TargetName.Where(char.IsLetterOrDigit).ToArray());
            var symbols = new List<string> { "FOO", "T_" + targetName };
            if (withIntegration)
            {
                symbols.Add(Integration);
            }

            symbols.Add("BAR");
            return symbols.ToArray();
        }

        private static string[] Symbols(bool dotween, bool integration)
        {
            var symbols = new List<string> { "FOO", "BAR" };
            if (dotween)
            {
                symbols.Add(DOTween);
            }

            if (integration)
            {
                symbols.Add(Integration);
            }

            return symbols.ToArray();
        }

        // dotween define, package installed, integration define, DOTween assembly present, expected action
        private static IEnumerable<TestCaseData> DecisionCases()
        {
            const DOTweenIntegrationMenu.ToggleAction already = DOTweenIntegrationMenu.ToggleAction.AlreadyActiveViaDOTween;
            const DOTweenIntegrationMenu.ToggleAction viaPackage = DOTweenIntegrationMenu.ToggleAction.AlreadyActiveViaPackage;
            const DOTweenIntegrationMenu.ToggleAction disable = DOTweenIntegrationMenu.ToggleAction.Disable;
            const DOTweenIntegrationMenu.ToggleAction enable = DOTweenIntegrationMenu.ToggleAction.Enable;
            const DOTweenIntegrationMenu.ToggleAction none = DOTweenIntegrationMenu.ToggleAction.NoDOTweenAssembly;

            yield return new TestCaseData(false, false, false, false, none).SetName("Decide_NoDefines_NoAssembly_NoDOTweenAssembly");
            yield return new TestCaseData(false, false, false, true, enable).SetName("Decide_NoDefines_AssemblyPresent_Enable");
            yield return new TestCaseData(false, false, true, false, disable).SetName("Decide_IntegrationDefine_NoAssembly_Disable");
            yield return new TestCaseData(false, false, true, true, disable).SetName("Decide_IntegrationDefine_AssemblyPresent_Disable");
            yield return new TestCaseData(true, false, false, false, already).SetName("Decide_DOTweenDefine_NoAssembly_AlreadyActiveViaDOTween");
            yield return new TestCaseData(true, false, false, true, already).SetName("Decide_DOTweenDefine_AssemblyPresent_AlreadyActiveViaDOTween");
            yield return new TestCaseData(true, false, true, false, already).SetName("Decide_BothDefines_NoAssembly_AlreadyActiveViaDOTween");
            yield return new TestCaseData(true, false, true, true, already).SetName("Decide_BothDefines_AssemblyPresent_AlreadyActiveViaDOTween");

            yield return new TestCaseData(false, true, false, false, viaPackage).SetName("Decide_Package_NoDefines_NoAssembly_AlreadyActiveViaPackage");
            yield return new TestCaseData(false, true, false, true, viaPackage).SetName("Decide_Package_NoDefines_AssemblyPresent_AlreadyActiveViaPackage");
            yield return new TestCaseData(false, true, true, false, viaPackage).SetName("Decide_Package_IntegrationDefine_NoAssembly_AlreadyActiveViaPackage");
            yield return new TestCaseData(false, true, true, true, viaPackage).SetName("Decide_Package_IntegrationDefine_AssemblyPresent_AlreadyActiveViaPackage");
            yield return new TestCaseData(true, true, false, false, already).SetName("Decide_Package_DOTweenDefine_NoAssembly_AlreadyActiveViaDOTween");
            yield return new TestCaseData(true, true, false, true, already).SetName("Decide_Package_DOTweenDefine_AssemblyPresent_AlreadyActiveViaDOTween");
            yield return new TestCaseData(true, true, true, false, already).SetName("Decide_Package_BothDefines_NoAssembly_AlreadyActiveViaDOTween");
            yield return new TestCaseData(true, true, true, true, already).SetName("Decide_Package_BothDefines_AssemblyPresent_AlreadyActiveViaDOTween");
        }

        [TestCaseSource(nameof(DecisionCases))]
        public void Decide_EveryInputCombination(bool dotween, bool package, bool integration, bool hasAssembly, object expected)
        {
            DOTweenIntegrationMenu.ToggleAction action = DOTweenIntegrationMenu.Decide(Symbols(dotween, integration), package, hasAssembly);

            Assert.That(action, Is.EqualTo((DOTweenIntegrationMenu.ToggleAction)expected));
        }

        [Test]
        public void Decide_PackageInstalledWithNullOrEmptySymbols_IsAlreadyActiveViaPackage()
        {
            Assert.That(DOTweenIntegrationMenu.Decide(null, true, true), Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.AlreadyActiveViaPackage));
            Assert.That(DOTweenIntegrationMenu.Decide(Array.Empty<string>(), true, false), Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.AlreadyActiveViaPackage));
        }

        [Test]
        public void Decide_NullSymbols_AreTreatedAsEmpty()
        {
            Assert.That(DOTweenIntegrationMenu.Decide(null, false, true), Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.Enable));
            Assert.That(DOTweenIntegrationMenu.Decide(null, false, false), Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.NoDOTweenAssembly));
        }

        [Test]
        public void Decide_EmptySymbols_AreTreatedAsNoDefines()
        {
            Assert.That(DOTweenIntegrationMenu.Decide(Array.Empty<string>(), false, true), Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.Enable));
            Assert.That(DOTweenIntegrationMenu.Decide(Array.Empty<string>(), false, false), Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.NoDOTweenAssembly));
        }

        [Test]
        public void Decide_LookAlikesPlusIntegrationDefine_DOTweenIsMatchedExactly()
        {
            // DOTWEEN_PRO and the integration symbol both contain "DOTWEEN" but are not it.
            string[] lookAlikes = { "DOTWEEN_PRO", "MY_DOTWEEN", Integration };

            DOTweenIntegrationMenu.ToggleAction action = DOTweenIntegrationMenu.Decide(lookAlikes, false, true);

            Assert.That(action, Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.Disable), "only the integration define counts here");
        }

        [Test]
        public void Decide_OnlyLookAlikeSymbols_BehavesAsNoDefines()
        {
            string[] lookAlikes = { "DOTWEEN_PRO", "MY_DOTWEEN", "ECANAKLI_UIDIRECTOR_DOTWEEN_X" };

            Assert.That(DOTweenIntegrationMenu.Decide(lookAlikes, false, true), Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.Enable));
            Assert.That(DOTweenIntegrationMenu.Decide(lookAlikes, false, false), Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.NoDOTweenAssembly));
        }

        [Test]
        public void Decide_SymbolOrderAndOtherSymbols_DoNotMatter()
        {
            string[] symbols = { Integration, "A", "B", "C" };
            string[] reversed = symbols.Reverse().ToArray();

            Assert.That(DOTweenIntegrationMenu.Decide(symbols, false, false), Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.Disable));
            Assert.That(DOTweenIntegrationMenu.Decide(reversed, false, false), Is.EqualTo(DOTweenIntegrationMenu.ToggleAction.Disable));
        }

        // ---- IsIntegrationOn ----

        [Test]
        public void IsIntegrationOn_NoDefines_IsFalse()
        {
            Assert.That(DOTweenIntegrationMenu.IsIntegrationOn(Symbols(false, false), false), Is.False);
        }

        [Test]
        public void IsIntegrationOn_DOTweenDefine_IsTrue()
        {
            Assert.That(DOTweenIntegrationMenu.IsIntegrationOn(Symbols(true, false), false), Is.True);
        }

        [Test]
        public void IsIntegrationOn_IntegrationDefine_IsTrue()
        {
            Assert.That(DOTweenIntegrationMenu.IsIntegrationOn(Symbols(false, true), false), Is.True);
        }

        [Test]
        public void IsIntegrationOn_BothDefines_IsTrue()
        {
            Assert.That(DOTweenIntegrationMenu.IsIntegrationOn(Symbols(true, true), false), Is.True);
        }

        [Test]
        public void IsIntegrationOn_NullOrEmpty_IsFalse()
        {
            Assert.That(DOTweenIntegrationMenu.IsIntegrationOn(null, false), Is.False);
            Assert.That(DOTweenIntegrationMenu.IsIntegrationOn(Array.Empty<string>(), false), Is.False);
        }

        [Test]
        public void IsIntegrationOn_LookAlikeSymbols_IsFalse()
        {
            Assert.That(DOTweenIntegrationMenu.IsIntegrationOn(new[] { "DOTWEEN_PRO", "MY_DOTWEEN", "ECANAKLI_UIDIRECTOR_DOTWEEN_X" }, false), Is.False);
        }

        [Test]
        public void IsIntegrationOn_PackageInstalledWithoutDefines_IsTrue()
        {
            Assert.That(DOTweenIntegrationMenu.IsIntegrationOn(Symbols(false, false), true), Is.True);
        }

        [Test]
        public void IsIntegrationOn_PackageInstalledWithNullOrEmptySymbols_IsTrue()
        {
            Assert.That(DOTweenIntegrationMenu.IsIntegrationOn(null, true), Is.True);
            Assert.That(DOTweenIntegrationMenu.IsIntegrationOn(Array.Empty<string>(), true), Is.True);
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void IsIntegrationOn_AgreesWithDecide_WhenTheAssemblyIsPresent(bool dotween, bool package, bool integration)
        {
            string[] symbols = Symbols(dotween, integration);

            bool on = DOTweenIntegrationMenu.IsIntegrationOn(symbols, package);
            DOTweenIntegrationMenu.ToggleAction action = DOTweenIntegrationMenu.Decide(symbols, package, true);

            // On exactly when the menu would not offer Enable.
            Assert.That(on, Is.EqualTo(action != DOTweenIntegrationMenu.ToggleAction.Enable));
        }

        // ---- Package name ----

        [Test]
        public void IsDOTweenPackageName_TheVersionDefinePackage_IsTrue()
        {
            Assert.That(DOTweenIntegrationMenu.IsDOTweenPackageName("com.demigiant.dotween"), Is.True);
        }

        [Test]
        public void IsDOTweenPackageName_TheAsmdefVersionDefinePackage_IsTrue()
        {
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DOTweenIntegrationMenu).Assembly);
            Assert.That(package, Is.Not.Null, "the editor assembly belongs to a package");
            string path = Path.Combine(package.resolvedPath, "DOTween", "Ecanakli.UIDirector.DOTween.asmdef");
            Assert.That(File.Exists(path), Is.True, "the integration asmdef at " + path);

            AsmdefFile asmdef = JsonUtility.FromJson<AsmdefFile>(File.ReadAllText(path));
            AsmdefVersionDefine versionDefine = asmdef.versionDefines.FirstOrDefault(define => define.define == Integration);

            Assert.That(versionDefine, Is.Not.Null, "a versionDefines entry for " + Integration);
            Assert.That(versionDefine.name, Is.Not.Empty);
            Assert.That(versionDefine.expression, Is.Empty, "any version of the package turns the integration on");
            Assert.That(DOTweenIntegrationMenu.IsDOTweenPackageName(versionDefine.name), Is.True, "the menu names the package the asmdef watches");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("com.demigiant.dotween.pro")]
        [TestCase("com.other.dotween")]
        [TestCase("COM.DEMIGIANT.DOTWEEN")]
        public void IsDOTweenPackageName_LookAlikeOrNull_IsFalse(string name)
        {
            Assert.That(DOTweenIntegrationMenu.IsDOTweenPackageName(name), Is.False);
        }

        // ---- Menu entries ----

        [Test]
        public void MenuItem_IsRegisteredAtTheDocumentedPathWithAValidateFunction()
        {
            FieldInfo pathField = typeof(MenuItem).GetField("menuItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo validateField = typeof(MenuItem).GetField("validate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(pathField, Is.Not.Null);
            Assert.That(validateField, Is.Not.Null);

            var entries = typeof(DOTweenIntegrationMenu)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(method => method.GetCustomAttributes<MenuItem>())
                .Select(attribute => (Path: (string)pathField.GetValue(attribute), Validate: (bool)validateField.GetValue(attribute)))
                .Where(entry => entry.Path == "Tools/UIDirector/DOTween Integration")
                .ToArray();

            Assert.That(entries.Any(entry => !entry.Validate), Is.True, "the menu item");
            Assert.That(entries.Any(entry => entry.Validate), Is.True, "the validate function that draws the check mark");
        }

        // ---- Assembly detection ----

        [Test]
        public void HasDOTweenAssembly_DevProjectShipsDOTween_IsTrue()
        {
            // The dev project ships Assets/Plugins/Demigiant/DOTween/DOTween.dll, so the menu never reaches NoDOTweenAssembly here.
            if (!File.Exists(Path.Combine(Application.dataPath, "Plugins/Demigiant/DOTween/DOTween.dll")))
                Assert.Ignore("This project does not ship DOTween under Assets/Plugins/Demigiant.");

            Assert.That(HasDOTweenAssembly(), Is.True);
        }

        // ---- Check mark ----

        [Test]
        public void Validate_ReturnsTrueAndChecksTheItemFromTheActiveTargetDefines()
        {
            bool expected = DOTweenIntegrationMenu.IsIntegrationOn(
                ScriptingDefineSymbols.Parse(PlayerSettings.GetScriptingDefineSymbols(ActiveTarget())), HostHasDOTweenPackage());

            object result = FindMenuMethod(true).Invoke(null, null);

            Assert.That(result, Is.EqualTo(true), "the item is always enabled");
            Assert.That(Menu.GetChecked(MenuPath), Is.EqualTo(expected));
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void Validate_FollowsTheActiveTargetsDefines(bool dotween, bool integration, bool expectedChecked)
        {
            _sandbox = new DefinesSandbox();
            NamedBuildTarget active = ActiveTarget();
            _sandbox.Seed(target => target.TargetName == active.TargetName ? Symbols(dotween, integration) : Symbols(false, false));

            object result = FindMenuMethod(true).Invoke(null, null);

            Assert.That(result, Is.EqualTo(true));
            Assert.That(Menu.GetChecked(MenuPath), Is.EqualTo(expectedChecked || HostHasDOTweenPackage()));
        }

        [Test]
        public void Validate_ReadsTheActiveTargetNotAnotherOne()
        {
            _sandbox = new DefinesSandbox();
            NamedBuildTarget active = ActiveTarget();
            _sandbox.Seed(target => target.TargetName == active.TargetName ? Symbols(false, false) : Symbols(true, true));

            FindMenuMethod(true).Invoke(null, null);

            Assert.That(Menu.GetChecked(MenuPath), Is.EqualTo(HostHasDOTweenPackage()), "only the other targets define DOTWEEN, so only an installed package can check the item");
        }

        // ---- Writing the defines ----

        [Test]
        public void MenuMethod_Enable_AddsTheIntegrationDefineToEveryBuildTargetKeepingTheOthers()
        {
            RequireDOTweenAssembly();
            _sandbox = new DefinesSandbox();
            _sandbox.Seed(target => Others(target, false));
            Assert.That(_sandbox.Targets.Count, Is.GreaterThan(1));
            Assert.That(_sandbox.Targets.Any(target => target.TargetName == NamedBuildTarget.Server.TargetName), Is.True, "the Server target is covered");

            FindMenuMethod(false).Invoke(null, null);

            foreach (NamedBuildTarget target in _sandbox.Targets)
            {
                string[] expected = Others(target, false).Concat(new[] { Integration }).ToArray();
                Assert.That(_sandbox.Read(target), Is.EqualTo(expected), target.TargetName);
            }
        }

        [Test]
        public void MenuMethod_Disable_RemovesTheIntegrationDefineFromEveryBuildTargetKeepingTheOthers()
        {
            RequireDOTweenAssembly();
            _sandbox = new DefinesSandbox();
            _sandbox.Seed(target => Others(target, true));

            FindMenuMethod(false).Invoke(null, null);

            foreach (NamedBuildTarget target in _sandbox.Targets)
            {
                Assert.That(_sandbox.Read(target), Is.EqualTo(Others(target, false)), target.TargetName);
            }
        }

        [Test]
        public void MenuMethod_Enable_TargetsThatAlreadyHaveIt_GetNoDuplicate()
        {
            RequireDOTweenAssembly();
            _sandbox = new DefinesSandbox();
            string activeName = ActiveTarget().TargetName;
            int index = 0;
            var alreadyOn = new HashSet<string>();
            foreach (NamedBuildTarget target in _sandbox.Targets)
            {
                // The active target stays off, so the menu decides Enable.
                if (target.TargetName != activeName && index++ % 2 == 0)
                {
                    alreadyOn.Add(target.TargetName);
                }
            }

            _sandbox.Seed(target => Others(target, alreadyOn.Contains(target.TargetName)));

            FindMenuMethod(false).Invoke(null, null);

            foreach (NamedBuildTarget target in _sandbox.Targets)
            {
                string[] symbols = _sandbox.Read(target);
                Assert.That(symbols.Count(symbol => symbol == Integration), Is.EqualTo(1), target.TargetName);
            }
        }
    }
}
