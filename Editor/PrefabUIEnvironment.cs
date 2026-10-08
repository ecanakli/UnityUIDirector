using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.EditorTools
{
    // Tools > UIDirector > Create Prefab UI Environment: a scene for Prefab Mode holding a root-like canvas and scaler.
    internal static class PrefabUIEnvironment
    {
        private const string MenuPath = "Tools/UIDirector/Create Prefab UI Environment";
        private const string DialogTitle = "UIDirector";
        private const string DefaultFileName = "UIDirectorUIEnvironment";
        private const string CanvasName = "Canvas";

        // The built-in "UI" layer is always layer 5.
        private const int UiLayer = 5;
        private static readonly Vector2 DefaultReferenceResolution = new Vector2(1080f, 1920f);
        private const float DefaultMatch = 0.5f;

        [MenuItem(MenuPath)]
        private static void CreateFromMenu()
        {
            RunInteractive(FindSelectedRootCanvas());
        }

        [MenuItem(MenuPath, true)]
        private static bool CreateFromMenuValidate()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        // Asks for a path, writes the scene, then asks before assigning it; skipped in batch mode.
        internal static void RunInteractive(Canvas source)
        {
            if (Application.isBatchMode)
            {
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Create Prefab UI Environment",
                DefaultFileName,
                "unity",
                "Choose where the UI environment scene is saved.",
                "Assets");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            SceneAsset asset = CreateEnvironmentScene(path, source, false);
            if (asset == null)
            {
                EditorUtility.DisplayDialog(DialogTitle, "The UI environment scene could not be created. See the Console for details.", "OK");
                return;
            }

            EditorGUIUtility.PingObject(asset);

            string message = "Use '" + path + "' as the Prefab Mode UI environment?";
            SceneAsset current = EditorSettings.prefabUIEnvironment;
            if (current != null && current != asset)
            {
                message += " It replaces '" + AssetDatabase.GetAssetPath(current) + "'.";
            }

            if (EditorUtility.DisplayDialog(DialogTitle, message, "Assign", "Keep current"))
            {
                EditorSettings.prefabUIEnvironment = asset;
            }
        }

        private static bool HasUntitledScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    return true;
            }

            return false;
        }

        // Writes a scene asset with a Canvas and CanvasScaler, copied from source when given; the open scenes stay as they are.
        internal static SceneAsset CreateEnvironmentScene(string assetPath, Canvas source, bool assign)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("UIDirector: the UI environment path must end in .unity, got '" + assetPath + "'.");
                return null;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("UIDirector: the UI environment cannot be created in Play Mode.");
                return null;
            }

            if (!assetPath.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
            {
                Debug.LogError("UIDirector: the UI environment must be saved under Assets/, got '" + assetPath + "'.");
                return null;
            }

            if (SceneManager.GetSceneByPath(assetPath).IsValid())
            {
                Debug.LogError("UIDirector: '" + assetPath + "' is an open scene; close it first.");
                return null;
            }

            // Unity cannot add a scene while an untitled one is open, so the user saves it first; it is never saved for them.
            if (HasUntitledScene())
            {
                Debug.LogError("UIDirector: save the open untitled scene first, then create the UI environment.");
                return null;
            }

            string directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                Directory.CreateDirectory(Path.Combine(projectRoot, directory));
            }

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            bool saved;
            try
            {
                BuildCanvas(scene, source);
                saved = EditorSceneManager.SaveScene(scene, assetPath);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded && SceneManager.GetActiveScene() != previousActive)
                {
                    SceneManager.SetActiveScene(previousActive);
                }

                EditorSceneManager.CloseScene(scene, true);
            }

            if (!saved)
            {
                Debug.LogError("UIDirector: could not save the UI environment scene to '" + assetPath + "'.");
                return null;
            }

            AssetDatabase.ImportAsset(assetPath);
            SceneAsset asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(assetPath);
            if (asset != null && assign)
            {
                EditorSettings.prefabUIEnvironment = asset;
            }

            return asset;
        }

        private static void BuildCanvas(Scene scene, Canvas source)
        {
            var canvasObject = new GameObject(CanvasName, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            canvasObject.layer = UiLayer;

            var canvas = canvasObject.AddComponent<Canvas>();
            if (source != null)
            {
                CopyCanvas(source, canvas);
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            var sourceScaler = source != null ? source.GetComponent<CanvasScaler>() : null;
            if (sourceScaler != null)
            {
                CopyScaler(sourceScaler, scaler);
            }
            else
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = DefaultReferenceResolution;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = DefaultMatch;
            }
        }

        // The camera is not copied: the environment scene has none, so a camera-mode canvas renders as overlay there.
        private static void CopyCanvas(Canvas from, Canvas to)
        {
            to.renderMode = from.renderMode;
            to.planeDistance = from.planeDistance;
            to.sortingLayerID = from.sortingLayerID;
            to.sortingOrder = from.sortingOrder;
            to.pixelPerfect = from.pixelPerfect;
            to.additionalShaderChannels = from.additionalShaderChannels;
            to.vertexColorAlwaysGammaSpace = from.vertexColorAlwaysGammaSpace;
        }

        private static void CopyScaler(CanvasScaler from, CanvasScaler to)
        {
            to.uiScaleMode = from.uiScaleMode;
            to.referencePixelsPerUnit = from.referencePixelsPerUnit;
            to.scaleFactor = from.scaleFactor;
            to.referenceResolution = from.referenceResolution;
            to.screenMatchMode = from.screenMatchMode;
            to.matchWidthOrHeight = from.matchWidthOrHeight;
            to.physicalUnit = from.physicalUnit;
            to.fallbackScreenDPI = from.fallbackScreenDPI;
            to.defaultSpriteDPI = from.defaultSpriteDPI;
            to.dynamicPixelsPerUnit = from.dynamicPixelsPerUnit;
        }

        // The canvas of the selected UIDirector root (or of a selected child of one); null when none is selected.
        private static Canvas FindSelectedRootCanvas()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return null;
            }

            var root = selected.GetComponentInParent<ViewRoot>();
            return root != null ? root.GetComponent<Canvas>() : null;
        }
    }
}
