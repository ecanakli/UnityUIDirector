using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;

namespace Ecanakli.UIDirector.EditorTools
{
    // Checkable toggle for ECANAKLI_UIDIRECTOR_DOTWEEN across every build target; mirrors the Zenject toggle.
    internal static class DOTweenIntegrationMenu
    {
        private const string MenuPath = "Tools/UIDirector/DOTween Integration";
        private const string IntegrationSymbol = "ECANAKLI_UIDIRECTOR_DOTWEEN";
        private const string DOTweenSymbol = "DOTWEEN";
        private const string DOTweenAssemblyName = "DOTween";

        // Must match the versionDefines entry in DOTween/Ecanakli.UIDirector.DOTween.asmdef (expression "" = any version).
        private const string DOTweenUpmPackageId = "com.demigiant.dotween";

        // What the menu does for a given project state.
        internal enum ToggleAction
        {
            Enable,
            Disable,
            AlreadyActiveViaDOTween,
            AlreadyActiveViaPackage,
            NoDOTweenAssembly
        }

        // True for the UPM package whose version define turns the integration on.
        internal static bool IsDOTweenPackageName(string name)
        {
            return string.Equals(name, DOTweenUpmPackageId, StringComparison.Ordinal);
        }

        // Pure decision: symbols are those of the active build target.
        internal static ToggleAction Decide(string[] activeTargetSymbols, bool dotweenPackageInstalled, bool hasDOTweenAssembly)
        {
            if (ScriptingDefineSymbols.Contains(activeTargetSymbols, DOTweenSymbol))
            {
                return ToggleAction.AlreadyActiveViaDOTween;
            }

            if (dotweenPackageInstalled)
            {
                return ToggleAction.AlreadyActiveViaPackage;
            }

            if (ScriptingDefineSymbols.Contains(activeTargetSymbols, IntegrationSymbol))
            {
                return ToggleAction.Disable;
            }

            return hasDOTweenAssembly ? ToggleAction.Enable : ToggleAction.NoDOTweenAssembly;
        }

        // True when the integration assembly compiles in, through the DOTWEEN define, the package or the integration define; drives the check mark.
        internal static bool IsIntegrationOn(string[] activeTargetSymbols, bool dotweenPackageInstalled)
        {
            return ScriptingDefineSymbols.Contains(activeTargetSymbols, DOTweenSymbol) ||
                   dotweenPackageInstalled ||
                   ScriptingDefineSymbols.Contains(activeTargetSymbols, IntegrationSymbol);
        }

        [MenuItem(MenuPath)]
        private static void ToggleDOTweenIntegration()
        {
            switch (Decide(GetActiveTargetSymbols(), IsDOTweenPackageInstalled(), HasDOTweenAssembly()))
            {
                case ToggleAction.AlreadyActiveViaDOTween:
                    EditorUtility.DisplayDialog(
                        "UIDirector",
                        DOTweenSymbol + " is already defined for the active build target, so the DOTween integration is already on. " +
                        "There is nothing to toggle here.",
                        "OK");
                    break;

                case ToggleAction.AlreadyActiveViaPackage:
                    EditorUtility.DisplayDialog(
                        "UIDirector",
                        "The package " + DOTweenUpmPackageId + " is installed, so " + IntegrationSymbol +
                        " is already set by the integration assembly's version define. " +
                        "This menu only manages the Player Settings define for DOTween copied under Assets; there is nothing to toggle here.",
                        "OK");
                    break;

                case ToggleAction.NoDOTweenAssembly:
                    EditorUtility.DisplayDialog(
                        "UIDirector",
                        "No assembly named \"" + DOTweenAssemblyName + "\" is loaded in this project. Import DOTween first, then enable this integration.",
                        "OK");
                    break;

                case ToggleAction.Disable:
                    ScriptingDefineSymbols.ApplyToAllBuildTargets(symbols => ScriptingDefineSymbols.Remove(symbols, IntegrationSymbol));
                    break;

                case ToggleAction.Enable:
                    ScriptingDefineSymbols.ApplyToAllBuildTargets(symbols => ScriptingDefineSymbols.Add(symbols, IntegrationSymbol));
                    break;
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleDOTweenIntegrationValidate()
        {
            Menu.SetChecked(MenuPath, IsIntegrationOn(GetActiveTargetSymbols(), IsDOTweenPackageInstalled()));
            return true;
        }

        private static bool IsDOTweenPackageInstalled()
        {
            try
            {
                foreach (UnityEditor.PackageManager.PackageInfo package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                {
                    if (IsDOTweenPackageName(package.name))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static bool HasDOTweenAssembly()
        {
            try
            {
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(assembly.GetName().Name, DOTweenAssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // A precompiled DLL that is not loaded yet still shows up here, by file name.
                foreach (string name in CompilationPipeline.GetPrecompiledAssemblyNames())
                {
                    if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(name), DOTweenAssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static string[] GetActiveTargetSymbols()
        {
            try
            {
                BuildTargetGroup activeGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
                NamedBuildTarget activeTarget = NamedBuildTarget.FromBuildTargetGroup(activeGroup);
                return ScriptingDefineSymbols.Parse(PlayerSettings.GetScriptingDefineSymbols(activeTarget));
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }
    }
}
