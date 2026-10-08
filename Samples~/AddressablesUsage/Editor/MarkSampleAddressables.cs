using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Ecanakli.UIDirector.Samples.AddressablesUsage.EditorTools
{
    /// <summary>Optional helper: puts this sample's two prefabs into the default Addressables group with the addresses the catalog uses.</summary>
    internal static class MarkSampleAddressables
    {
        private const string GalleryAddress = "uidirector-samples/gallery";
        private const string PhotoAddress = "uidirector-samples/photo";

        // Runs only on this menu click, never on load or import.
        [MenuItem("Tools/UIDirector/Samples/Make Addressables Usage Prefabs Addressable")]
        private static void MakeAddressable()
        {
            var folder = FindPrefabsFolder();
            if (folder == null)
            {
                Report("Could not find the sample's Prefabs folder; this script must stay in the sample's Editor folder.");
                return;
            }

            // Creates the settings asset only here, on the explicit click.
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                Report("The Addressables settings could not be created.");
                return;
            }

            var gallery = Mark(settings, folder + "/Gallery.prefab", GalleryAddress);
            var photo = Mark(settings, folder + "/Photo.prefab", PhotoAddress);

            AssetDatabase.SaveAssets();
            Report(gallery && photo
                ? $"Marked Gallery and Photo as addressable ('{GalleryAddress}', '{PhotoAddress}')."
                : "A prefab was not found; see the warning in the console.");
        }

        // The script sits at <sample>/Editor/MarkSampleAddressables.cs, so the prefabs are one folder up.
        private static string FindPrefabsFolder()
        {
            foreach (var guid in AssetDatabase.FindAssets("MarkSampleAddressables t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var cut = path.LastIndexOf("/Editor/", StringComparison.Ordinal);
                if (cut > 0 && path.EndsWith("/MarkSampleAddressables.cs", StringComparison.Ordinal))
                    return path.Substring(0, cut) + "/Prefabs";
            }

            return null;
        }

        private static bool Mark(AddressableAssetSettings settings, string prefabPath, string address)
        {
            var guid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[UIDirector sample] '{prefabPath}' does not exist.");
                return false;
            }

            var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.SetAddress(address);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
            return true;
        }

        // A dialog needs a person; a batch run only logs.
        private static void Report(string message)
        {
            Debug.Log($"[UIDirector sample] {message}");
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Addressables Usage", message, "OK");
        }
    }
}
