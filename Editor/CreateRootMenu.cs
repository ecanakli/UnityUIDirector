using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Ecanakli.UIDirector.EditorTools
{
    // GameObject > UI > UIDirector Root. Logic sits in internal methods the tests call without dialogs.
    internal static class CreateRootMenu
    {
        private const string RootMenuPath = "GameObject/UI/UIDirector Root";
        private const string CameraRootMenuPath = "GameObject/UI/UIDirector Root (Camera)";
        private const int RootMenuPriority = 2200;
        private const int CameraRootMenuPriority = 2201;

        private const string RootName = "UIDirector Root";
        private const string HolderName = "UIDirector";
        private const string CameraName = "UI Camera";
        private const string EventSystemName = "EventSystem";

        // The built-in "UI" layer is always layer 5.
        private const int UiLayer = 5;
        private const float CameraPlaneDistance = 100f;
        private static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);
        private const float ReferenceMatch = 0.5f;

        private const string OfferedSessionKey = "Ecanakli.UIDirector.UIEnvironmentOffered";

        [MenuItem(RootMenuPath, false, RootMenuPriority)]
        private static void CreateRootFromMenu()
        {
            CreateAndSelect(false);
        }

        [MenuItem(CameraRootMenuPath, false, CameraRootMenuPriority)]
        private static void CreateCameraRootFromMenu()
        {
            CreateAndSelect(true);
        }

        // Creates a scene-root UIDirector object in the active scene; never parented to the selection.
        internal static GameObject CreateRoot(bool cameraVariant)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create UIDirector Root");

            var root = new GameObject(RootName, typeof(RectTransform));
            MoveToActiveScene(root);
            root.layer = UiLayer;

            // Inactive while it is built, so that in Play Mode ViewRoot.Awake sees the final render mode and camera.
            root.SetActive(false);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = ReferenceMatch;

            root.AddComponent<ViewRoot>();
            root.AddComponent<BackInput>();

            // The created object: the root itself, or the holder of the root and its camera.
            var created = root;
            if (cameraVariant)
            {
                // A camera-mode canvas sits on its camera's position when the camera is its child, and is not drawn; so the two are siblings.
                var holder = new GameObject(HolderName);
                MoveToActiveScene(holder);
                holder.layer = UiLayer;
                root.transform.SetParent(holder.transform, false);

                var uiCamera = CreateUiCamera(holder.transform);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCamera;
                canvas.planeDistance = CameraPlaneDistance;
                created = holder;
            }

            root.SetActive(true);

            Undo.RegisterCreatedObjectUndo(created, "Create UIDirector Root");
            EnsureEventSystem();
            Undo.CollapseUndoOperations(undoGroup);

            if (!EditorApplication.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(root.scene);
            }

            return root;
        }

        // Returns the GameObject of the scene's active EventSystem, or creates one with the module that fits the input handling.
        internal static GameObject EnsureEventSystem()
        {
            var existing = Object.FindAnyObjectByType<EventSystem>();
            if (existing != null)
            {
                return existing.gameObject;
            }

            var eventSystem = new GameObject(EventSystemName, typeof(EventSystem));
            MoveToActiveScene(eventSystem);
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            eventSystem.AddComponent<InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<StandaloneInputModule>();
#endif
            Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");

            if (!EditorApplication.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(eventSystem.scene);
            }

            return eventSystem;
        }

        private static void CreateAndSelect(bool cameraVariant)
        {
            var root = CreateRoot(cameraVariant);
            Selection.activeGameObject = root;
            OfferUiEnvironment(root);
        }

        // Once per editor session, and only while no UI environment is assigned and not in batch mode, so the dialog never nags or blocks automation.
        internal static bool ShouldOfferUiEnvironment(bool isBatchMode, Object currentEnvironment, bool alreadyOffered)
        {
            return !isBatchMode && currentEnvironment == null && !alreadyOffered;
        }

        private static void OfferUiEnvironment(GameObject root)
        {
            if (!ShouldOfferUiEnvironment(Application.isBatchMode, EditorSettings.prefabUIEnvironment, SessionState.GetBool(OfferedSessionKey, false)))
            {
                return;
            }

            SessionState.SetBool(OfferedSessionKey, true);

            bool create = EditorUtility.DisplayDialog(
                "UIDirector",
                "Prefab Mode has no UI environment, so a view prefab whose root is a nested canvas opens without a scaler " +
                "and at the wrong size. Create a UI environment scene now, copied from the new root?",
                "Create",
                "Not now");
            if (!create)
            {
                return;
            }

            PrefabUIEnvironment.RunInteractive(root.GetComponent<Canvas>());
        }

        private static Camera CreateUiCamera(Transform parent)
        {
            var cameraObject = new GameObject(CameraName, typeof(Camera));
            cameraObject.layer = UiLayer;
            cameraObject.transform.SetParent(parent, false);

            var uiCamera = cameraObject.GetComponent<Camera>();
            uiCamera.orthographic = true;
            uiCamera.clearFlags = CameraClearFlags.Depth;
            uiCamera.cullingMask = 1 << UiLayer;
            uiCamera.depth = DepthAboveSceneCameras();
            return uiCamera;
        }

        // One above the highest game camera in the loaded scenes, so the UI draws last; 0 when there is none.
        private static float DepthAboveSceneCameras()
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool found = false;
            float highest = 0f;
            foreach (var sceneCamera in cameras)
            {
                if (sceneCamera.cameraType != CameraType.Game)
                {
                    continue;
                }

                if (!found || sceneCamera.depth > highest)
                {
                    highest = sceneCamera.depth;
                    found = true;
                }
            }

            return found ? highest + 1f : 0f;
        }

        private static void MoveToActiveScene(GameObject gameObject)
        {
            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded && gameObject.scene != active)
            {
                SceneManager.MoveGameObjectToScene(gameObject, active);
            }
        }
    }
}
