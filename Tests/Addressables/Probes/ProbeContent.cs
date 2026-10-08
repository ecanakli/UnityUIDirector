using UnityEngine.TestTools;
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Names of the temporary probe content.
    public static class ProbeContent
    {
        public const string ViewAddress = "uidirector-probe-view";
        public const string HeavyAddress = "uidirector-probe-heavy";
        public const string TempFolderName = "UIDirectorProbeTemp";
        public const string TempFolder = "Assets/" + TempFolderName;
        public const string ViewPrefabPath = TempFolder + "/ProbeView.prefab";
        public const string HeavyPrefabPath = TempFolder + "/ProbeHeavy.prefab";
        public const int HeavyImageCount = 300;

        // A view prefab for the provider tests: ViewHost (with its Canvas and GraphicRaycaster) and AddressableTestView.
        public const string ProviderViewAddress = "uidirector-provider-view";
        public const string ProviderViewPrefabPath = TempFolder + "/ProviderView.prefab";

        // The same view as a separate asset, so it loads (simulated delay) while the first asset is held.
        public const string ProviderViewAltAddress = "uidirector-provider-view-alt";
        public const string ProviderViewAltPrefabPath = TempFolder + "/ProviderViewAlt.prefab";
    }

    // Resolves editor-only facts about the temporary content.
    public static class ProbeContentLookup
    {
        public static string ViewGuid()
        {
#if UNITY_EDITOR
            return AssetDatabase.AssetPathToGUID(ProbeContent.ViewPrefabPath);
#else
            return null;
#endif
        }

        public static string ProviderViewGuid()
        {
#if UNITY_EDITOR
            return AssetDatabase.AssetPathToGUID(ProbeContent.ProviderViewPrefabPath);
#else
            return null;
#endif
        }

        public static string ProviderViewAltGuid()
        {
#if UNITY_EDITOR
            return AssetDatabase.AssetPathToGUID(ProbeContent.ProviderViewAltPrefabPath);
#else
            return null;
#endif
        }
    }

    // Shared prebuild setup and post-build cleanup; the test framework runs it once per run.
    public sealed class ProbeContentSetup : IPrebuildSetup, IPostBuildCleanup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            ProbeContentEditor.Create();
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            ProbeContentEditor.Remove();
#endif
        }
    }

#if UNITY_EDITOR
    // The temp folder doubles as the marker: it exists only while probe-created settings exist.
    internal static class ProbeContentEditor
    {
        const string PlayModeIndexKey = "UIDirectorProbe.PreviousPlayModeIndex";

        public static void Create()
        {
            // a leftover temp folder means an earlier run did not clean up
            if (AssetDatabase.IsValidFolder(ProbeContent.TempFolder))
                Remove();

            if (AddressableAssetSettingsDefaultObject.SettingsExists
                || AssetDatabase.IsValidFolder(AddressableAssetSettingsDefaultObject.kDefaultConfigFolder))
            {
                throw new InvalidOperationException(
                    "The probes need a host without Addressables settings, but settings or "
                    + AddressableAssetSettingsDefaultObject.kDefaultConfigFolder + " already exist.");
            }

            AssetDatabase.CreateFolder("Assets", ProbeContent.TempFolderName);
            SessionState.SetInt(PlayModeIndexKey, ProjectConfigData.ActivePlayModeIndex);

            string viewGuid = CreateViewPrefab();
            string heavyGuid = CreateHeavyPrefab();
            string providerViewGuid = CreateProviderViewPrefab("ProviderView", ProbeContent.ProviderViewPrefabPath);
            string providerViewAltGuid = CreateProviderViewPrefab("ProviderViewAlt", ProbeContent.ProviderViewAltPrefabPath);

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
                throw new InvalidOperationException("Addressables settings could not be created.");

            // "Use Asset Database" is the play mode script whose builder is BuildScriptFastMode
            int fastModeIndex = settings.DataBuilders.FindIndex(builder => builder is BuildScriptFastMode);
            if (fastModeIndex < 0)
                throw new InvalidOperationException("BuildScriptFastMode is missing from the settings data builders.");
            settings.ActivePlayModeDataBuilderIndex = fastModeIndex;

            settings.SimulatedLoadDelay = 0.1f;

            AddressableAssetGroup group = settings.DefaultGroup;
            AddEntry(settings, group, viewGuid, ProbeContent.ViewAddress);
            AddEntry(settings, group, heavyGuid, ProbeContent.HeavyAddress);
            AddEntry(settings, group, providerViewGuid, ProbeContent.ProviderViewAddress);
            AddEntry(settings, group, providerViewAltGuid, ProbeContent.ProviderViewAltAddress);

            EditorUtility.SetDirty(group);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (!(settings.ActivePlayModeDataBuilder is BuildScriptFastMode))
                throw new InvalidOperationException("The active play mode script is not Use Asset Database.");
        }

        public static void Remove()
        {
            if (!AssetDatabase.IsValidFolder(ProbeContent.TempFolder))
                return;

            // unlink the default object first; this is the line that touches ProjectSettings/EditorBuildSettings.asset
            if (EditorBuildSettings.TryGetConfigObject(
                    AddressableAssetSettingsDefaultObject.kDefaultConfigObjectName,
                    out AddressableAssetSettingsDefaultObject link))
            {
                string linkPath = link != null ? AssetDatabase.GetAssetPath(link) : string.Empty;
                if (string.IsNullOrEmpty(linkPath)
                    || linkPath.StartsWith(AddressableAssetSettingsDefaultObject.kDefaultConfigFolder + "/", StringComparison.Ordinal))
                {
                    EditorBuildSettings.RemoveConfigObject(AddressableAssetSettingsDefaultObject.kDefaultConfigObjectName);
                }
            }

            string configFolder = AddressableAssetSettingsDefaultObject.kDefaultConfigFolder;
            if (AssetDatabase.IsValidFolder(configFolder) && !AssetDatabase.DeleteAsset(configFolder))
                throw new InvalidOperationException("Could not delete " + configFolder);

            int previousIndex = SessionState.GetInt(PlayModeIndexKey, -1);
            if (previousIndex >= 0)
            {
                ProjectConfigData.ActivePlayModeIndex = previousIndex;
                SessionState.EraseInt(PlayModeIndexKey);
            }

            if (!AssetDatabase.DeleteAsset(ProbeContent.TempFolder))
                throw new InvalidOperationException("Could not delete " + ProbeContent.TempFolder);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void AddEntry(AddressableAssetSettings settings, AddressableAssetGroup group, string guid, string address)
        {
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
            if (entry == null)
                throw new InvalidOperationException("Could not create an Addressables entry for " + address);
            entry.SetAddress(address);
        }

        static string CreateViewPrefab()
        {
            var root = new GameObject("ProbeView", typeof(Canvas), typeof(ProbeView));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, ProbeContent.ViewPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return AssetDatabase.AssetPathToGUID(ProbeContent.ViewPrefabPath);
        }

        static string CreateHeavyPrefab()
        {
            // UnityEngine.UI is not referenced by the test assembly, so Image is resolved by name
            Type imageType = Type.GetType("UnityEngine.UI.Image, UnityEngine.UI");
            if (imageType == null)
                Debug.LogWarning("UnityEngine.UI.Image was not found; the heavy probe prefab has no Images.");

            var root = new GameObject("ProbeHeavy", typeof(Canvas));
            try
            {
                for (int i = 0; i < ProbeContent.HeavyImageCount; i++)
                {
                    var child = new GameObject("Image" + i, typeof(RectTransform));
                    child.transform.SetParent(root.transform, false);
                    if (imageType != null)
                        child.AddComponent(imageType);
                }

                PrefabUtility.SaveAsPrefabAsset(root, ProbeContent.HeavyPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return AssetDatabase.AssetPathToGUID(ProbeContent.HeavyPrefabPath);
        }

        static string CreateProviderViewPrefab(string name, string path)
        {
            var root = new GameObject(name, typeof(RectTransform));
            try
            {
                // ViewHost's RequireComponent adds Canvas and GraphicRaycaster, so UnityEngine.UI needs no reference here
                root.AddComponent<ViewHost>();
                root.AddComponent<AddressableTestView>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return AssetDatabase.AssetPathToGUID(path);
        }
    }
#endif
}
