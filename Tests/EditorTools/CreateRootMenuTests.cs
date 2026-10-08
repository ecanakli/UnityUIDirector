using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.EditorTools.Tests
{
    /// <summary>The create-root menu logic in a fresh empty scene per test.</summary>
    [TestFixture]
    public sealed class CreateRootMenuTests
    {
        private const string HolderName = "UIDirector";
        private const string CameraName = "UI Camera";

        private const string InputSystemModuleTypeName = "UnityEngine.InputSystem.UI.InputSystemUIInputModule";
        private const string OfferedSessionKey = "Ecanakli.UIDirector.UIEnvironmentOffered";
        private const string RootMenuPath = "GameObject/UI/UIDirector Root";
        private const string CameraRootMenuPath = "GameObject/UI/UIDirector Root (Camera)";

        private bool _offeredBefore;
        private SceneAsset _environmentBefore;
        private string _folder;

        private static int UiLayer => LayerMask.NameToLayer("UI");

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Selection.activeObject = null;
            Undo.ClearAll();

            _offeredBefore = SessionState.GetBool(OfferedSessionKey, false);
            _environmentBefore = EditorSettings.prefabUIEnvironment;
            _folder = null;
        }

        [TearDown]
        public void TearDown()
        {
            Selection.activeObject = null;

            // Replacing the scene destroys everything the test created; it also closes a scene saved into the temporary folder.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();

            if (_offeredBefore)
            {
                SessionState.SetBool(OfferedSessionKey, true);
            }
            else
            {
                SessionState.EraseBool(OfferedSessionKey);
            }

            // The setting goes back before the folder holding its asset is deleted.
            if (EditorSettings.prefabUIEnvironment != _environmentBefore)
            {
                EditorSettings.prefabUIEnvironment = _environmentBefore;
            }

            if (!string.IsNullOrEmpty(_folder))
            {
                AssetDatabase.DeleteAsset(_folder);
                _folder = null;
            }
        }

        // Saves the active scene into a temporary folder and returns its scene asset.
        private SceneAsset SaveActiveSceneAsAsset()
        {
            string name = "UIDirectorCreateRootTests_" + System.Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _folder = "Assets/" + name;
            string path = _folder + "/Saved.unity";

            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path), Is.True, "the test scene is saved");
            SceneAsset asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            Assert.That(asset != null, Is.True);
            return asset;
        }

        // Runs a menu entry's method by its menu path, as the menu would.
        private static void InvokeMenu(string menuPath)
        {
            FieldInfo pathField = typeof(MenuItem).GetField("menuItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo validateField = typeof(MenuItem).GetField("validate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(pathField, Is.Not.Null);
            Assert.That(validateField, Is.Not.Null);

            MethodInfo method = typeof(CreateRootMenu)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.GetCustomAttributes<MenuItem>()
                    .Any(attribute => (string)pathField.GetValue(attribute) == menuPath && !(bool)validateField.GetValue(attribute)));

            Assert.That(method, Is.Not.Null, "a menu method at " + menuPath);
            method.Invoke(null, null);
        }

        private static string ExpectedInputModuleTypeName()
        {
#if ENABLE_INPUT_SYSTEM
            if (System.Type.GetType(InputSystemModuleTypeName + ", Unity.InputSystem") != null)
            {
                return InputSystemModuleTypeName;
            }
#endif
            return typeof(StandaloneInputModule).FullName;
        }

        private static ViewRoot FindViewRoot()
        {
            ViewRoot[] roots = Object.FindObjectsByType<ViewRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(roots.Length, Is.EqualTo(1), "exactly one ViewRoot is expected in the scene");
            return roots[0];
        }

        private static GameObject[] SceneRoots()
        {
            return SceneManager.GetActiveScene().GetRootGameObjects();
        }

        private static EventSystem[] EventSystems()
        {
            return Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private static void AssertRootComponents(ViewRoot viewRoot)
        {
            GameObject root = viewRoot.gameObject;

            Assert.That(root.GetComponent<RectTransform>(), Is.Not.Null, "RectTransform");
            Assert.That(root.GetComponent<Canvas>(), Is.Not.Null, "Canvas");
            Assert.That(root.GetComponent<BackInput>(), Is.Not.Null, "BackInput");
            Assert.That(root.activeSelf, Is.True, "the root is left active");

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null, "CanvasScaler");
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1080f, 1920f)));
            Assert.That(scaler.screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight));
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f).Within(0.0001f));
        }

        // ---- CreateRoot(false) ----

        // The create-root menu offers the UI environment once per session, only while none is set, never in batch mode.
        [TestCase(false, false, false, true)]
        [TestCase(true, false, false, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, true, false)]
        public void ShouldOfferUiEnvironment_FollowsBatchModeTheCurrentEnvironmentAndTheSessionFlag(bool batch, bool hasEnvironment, bool alreadyOffered, bool expected)
        {
            var environment = hasEnvironment ? ScriptableObject.CreateInstance<ViewCatalog>() : null;
            try
            {
                Assert.That(CreateRootMenu.ShouldOfferUiEnvironment(batch, environment, alreadyOffered), Is.EqualTo(expected));
            }
            finally
            {
                if (environment != null)
                    Object.DestroyImmediate(environment);
            }
        }

        [Test]
        public void CreateRoot_Overlay_MakesOneSceneRootObjectWithTheRequiredComponents()
        {
            GameObject created = CreateRootMenu.CreateRoot(false);

            Assert.That(created, Is.Not.Null);
            ViewRoot viewRoot = FindViewRoot();
            Assert.That(viewRoot.transform.parent, Is.Null, "the root is at the scene root");
            Assert.That(viewRoot.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));
            AssertRootComponents(viewRoot);
        }

        [Test]
        public void CreateRoot_Overlay_CanvasIsScreenSpaceOverlay()
        {
            CreateRootMenu.CreateRoot(false);

            Canvas canvas = FindViewRoot().GetComponent<Canvas>();

            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        }

        [Test]
        public void CreateRoot_Overlay_CreatesNoCameraAndNoHolder()
        {
            CreateRootMenu.CreateRoot(false);

            Assert.That(Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            Assert.That(SceneRoots().Any(go => go.name == HolderName), Is.False);
        }

        [Test]
        public void CreateRoot_ReturnsAnObjectOfTheCreatedHierarchy()
        {
            GameObject created = CreateRootMenu.CreateRoot(false);

            Assert.That(created.transform.root, Is.EqualTo(FindViewRoot().transform.root));
        }

        [Test]
        public void CreateRoot_OtherObjectSelected_RootIsStillCreatedAtTheSceneRoot()
        {
            var selected = new GameObject("Selected Canvas", typeof(RectTransform), typeof(Canvas));
            Selection.activeGameObject = selected;

            CreateRootMenu.CreateRoot(false);

            ViewRoot viewRoot = FindViewRoot();
            Assert.That(viewRoot.transform.parent, Is.Null);
            Assert.That(selected.transform.childCount, Is.EqualTo(0), "the selection gets no child");
        }

        [Test]
        public void CreateRoot_CameraVariant_OtherObjectSelected_HolderIsAtTheSceneRoot()
        {
            var selected = new GameObject("Selected Canvas", typeof(RectTransform), typeof(Canvas));
            Selection.activeGameObject = selected;

            CreateRootMenu.CreateRoot(true);

            Transform holder = FindViewRoot().transform.parent;
            Assert.That(holder, Is.Not.Null);
            Assert.That(holder.parent, Is.Null);
            Assert.That(selected.transform.childCount, Is.EqualTo(0), "the selection gets no child");
        }

        // ---- EventSystem ----

        [Test]
        public void CreateRoot_SceneWithoutEventSystem_CreatesExactlyOneWithAnInputModule()
        {
            CreateRootMenu.CreateRoot(false);

            EventSystem[] eventSystems = EventSystems();
            Assert.That(eventSystems.Length, Is.EqualTo(1));
            Assert.That(eventSystems[0].GetComponent<BaseInputModule>(), Is.Not.Null);
            Assert.That(eventSystems[0].transform.parent, Is.Null, "the EventSystem is not nested under the root");
        }

        [Test]
        public void CreateRoot_SceneWithoutEventSystem_InputModuleFitsTheActiveInputHandler()
        {
            CreateRootMenu.CreateRoot(false);

            BaseInputModule[] modules = EventSystems().Single().GetComponents<BaseInputModule>();
            Assert.That(modules.Length, Is.EqualTo(1), "exactly one input module");
            Assert.That(modules[0].GetType().FullName, Is.EqualTo(ExpectedInputModuleTypeName()));
        }

        [Test]
        public void EnsureEventSystem_NoneInScene_InputModuleFitsTheActiveInputHandler()
        {
            GameObject created = CreateRootMenu.EnsureEventSystem();

            BaseInputModule[] modules = created.GetComponents<BaseInputModule>();
            Assert.That(modules.Length, Is.EqualTo(1), "exactly one input module");
            Assert.That(modules[0].GetType().FullName, Is.EqualTo(ExpectedInputModuleTypeName()));
        }

        [Test]
        public void CreateRoot_SceneWithEventSystem_ReusesItAndAddsNone()
        {
            var existing = new GameObject("My EventSystem", typeof(EventSystem));

            CreateRootMenu.CreateRoot(false);

            EventSystem[] eventSystems = EventSystems();
            Assert.That(eventSystems.Length, Is.EqualTo(1));
            Assert.That(eventSystems[0].gameObject, Is.EqualTo(existing));
        }

        [Test]
        public void CreateRoot_CameraVariant_SceneWithEventSystem_AddsNone()
        {
            var existing = new GameObject("My EventSystem", typeof(EventSystem));

            CreateRootMenu.CreateRoot(true);

            Assert.That(EventSystems().Length, Is.EqualTo(1));
            Assert.That(EventSystems()[0].gameObject, Is.EqualTo(existing));
        }

        [Test]
        public void CreateRoot_CalledTwice_StillOneEventSystem()
        {
            CreateRootMenu.CreateRoot(false);
            CreateRootMenu.CreateRoot(false);

            Assert.That(EventSystems().Length, Is.EqualTo(1));
        }

        [Test]
        public void EnsureEventSystem_ExistingOne_ReturnsItsObject()
        {
            var existing = new GameObject("My EventSystem", typeof(EventSystem));

            GameObject result = CreateRootMenu.EnsureEventSystem();

            Assert.That(result, Is.EqualTo(existing));
            Assert.That(EventSystems().Length, Is.EqualTo(1));
        }

        [Test]
        public void EnsureEventSystem_NoneInScene_CreatesOneAndASecondCallReusesIt()
        {
            GameObject first = CreateRootMenu.EnsureEventSystem();
            GameObject second = CreateRootMenu.EnsureEventSystem();

            Assert.That(first.GetComponent<EventSystem>(), Is.Not.Null);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(EventSystems().Length, Is.EqualTo(1));
        }

        // ---- Undo ----

        [Test]
        public void CreateRoot_Undo_RemovesWhatItMade()
        {
            CreateRootMenu.CreateRoot(false);
            Assert.That(SceneRoots().Length, Is.GreaterThan(0));

            Undo.PerformUndo();

            Assert.That(Object.FindObjectsByType<ViewRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            Assert.That(EventSystems(), Is.Empty, "the EventSystem it created goes with it");
            Assert.That(SceneRoots(), Is.Empty);
        }

        [Test]
        public void CreateRoot_Undo_KeepsAnExistingEventSystem()
        {
            var existing = new GameObject("My EventSystem", typeof(EventSystem));
            CreateRootMenu.CreateRoot(false);

            Undo.PerformUndo();

            Assert.That(Object.FindObjectsByType<ViewRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            Assert.That(existing != null, Is.True);
            Assert.That(EventSystems().Length, Is.EqualTo(1));
        }

        [Test]
        public void CreateRoot_CameraVariant_Undo_RemovesHolderRootAndCamera()
        {
            CreateRootMenu.CreateRoot(true);

            Undo.PerformUndo();

            Assert.That(Object.FindObjectsByType<ViewRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            Assert.That(Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            Assert.That(SceneRoots(), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CreateRoot_AfterAnotherUndoableAction_OneUndoRemovesOnlyWhatItMade(bool cameraVariant)
        {
            // No IncrementCurrentGroup after it: the user's last action is still the open undo group.
            var prior = new GameObject("Prior");
            Undo.RegisterCreatedObjectUndo(prior, "Prior");

            CreateRootMenu.CreateRoot(cameraVariant);
            Undo.PerformUndo();

            Assert.That(prior != null, Is.True, "the first undo leaves the user's earlier action alone");
            Assert.That(Object.FindObjectsByType<ViewRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            Assert.That(Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            Assert.That(EventSystems(), Is.Empty, "the EventSystem it created goes with it");
            Assert.That(SceneRoots(), Is.EquivalentTo(new[] { prior }));

            Undo.PerformUndo();

            Assert.That(prior == null, Is.True, "the second undo removes the earlier action");
        }

        // ---- CreateRoot(true) ----

        [Test]
        public void CreateRoot_CameraVariant_HolderIsAtTheSceneRootAndHoldsTheRootAndTheCamera()
        {
            CreateRootMenu.CreateRoot(true);

            ViewRoot viewRoot = FindViewRoot();
            Transform holder = viewRoot.transform.parent;
            Assert.That(holder, Is.Not.Null, "the root sits under a holder");
            Assert.That(holder.name, Is.EqualTo(HolderName));
            Assert.That(holder.parent, Is.Null, "the holder is at the scene root");
            Assert.That(holder.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));
            Assert.That(holder.childCount, Is.EqualTo(2));

            Transform cameraTransform = holder.Find(CameraName);
            Assert.That(cameraTransform, Is.Not.Null, "the holder has a child named " + CameraName);
            Assert.That(cameraTransform.GetComponent<Camera>(), Is.Not.Null);
        }

        [Test]
        public void CreateRoot_CameraVariant_CameraIsASiblingOfTheRootNotItsChild()
        {
            CreateRootMenu.CreateRoot(true);

            ViewRoot viewRoot = FindViewRoot();
            Camera uiCamera = Object.FindAnyObjectByType<Camera>();

            Assert.That(uiCamera, Is.Not.Null);
            Assert.That(uiCamera.transform.parent, Is.EqualTo(viewRoot.transform.parent));
            Assert.That(uiCamera.transform.IsChildOf(viewRoot.transform), Is.False, "a camera under the canvas does not draw (probe P9b)");
            Assert.That(viewRoot.GetComponentInChildren<Camera>(true), Is.Null);
            Assert.That(viewRoot.transform.childCount, Is.EqualTo(0));
        }

        [Test]
        public void CreateRoot_CameraVariant_CanvasIsScreenSpaceCameraOnTheUiCameraAtPlaneDistance100()
        {
            CreateRootMenu.CreateRoot(true);

            ViewRoot viewRoot = FindViewRoot();
            Canvas canvas = viewRoot.GetComponent<Canvas>();
            Camera uiCamera = viewRoot.transform.parent.Find(CameraName).GetComponent<Camera>();

            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(canvas.worldCamera, Is.EqualTo(uiCamera));
            Assert.That(canvas.planeDistance, Is.EqualTo(100f).Within(0.0001f));
        }

        [Test]
        public void CreateRoot_CameraVariant_CameraIsOrthographicDepthOnlyAndSeesOnlyTheUiLayer()
        {
            CreateRootMenu.CreateRoot(true);

            Camera uiCamera = FindViewRoot().transform.parent.Find(CameraName).GetComponent<Camera>();

            Assert.That(uiCamera.orthographic, Is.True);
            Assert.That(uiCamera.clearFlags, Is.EqualTo(CameraClearFlags.Depth));
            Assert.That(uiCamera.cullingMask, Is.EqualTo(1 << UiLayer));
        }

        [Test]
        public void CreateRoot_CameraVariant_HolderRootAndCameraAreOnTheUiLayer()
        {
            CreateRootMenu.CreateRoot(true);

            ViewRoot viewRoot = FindViewRoot();
            Transform holder = viewRoot.transform.parent;

            Assert.That(holder.gameObject.layer, Is.EqualTo(UiLayer), "holder");
            Assert.That(viewRoot.gameObject.layer, Is.EqualTo(UiLayer), "root");
            Assert.That(holder.Find(CameraName).gameObject.layer, Is.EqualTo(UiLayer), "camera");
        }

        [Test]
        public void CreateRoot_Overlay_RootIsOnTheUiLayer()
        {
            CreateRootMenu.CreateRoot(false);

            Assert.That(FindViewRoot().gameObject.layer, Is.EqualTo(UiLayer));
        }

        [Test]
        public void CreateRoot_CameraVariant_RootHasTheSameComponentsAndScalerAsTheOverlayRoot()
        {
            CreateRootMenu.CreateRoot(true);

            AssertRootComponents(FindViewRoot());
        }

        [Test]
        public void CreateRoot_CameraVariant_RootStaysAtTheHoldersOrigin()
        {
            CreateRootMenu.CreateRoot(true);

            Transform holder = FindViewRoot().transform.parent;

            Assert.That(holder.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(holder.Find(CameraName).localPosition, Is.EqualTo(Vector3.zero));
        }

        // ---- Menu entries ----

        [Test]
        public void MenuItems_AreRegisteredAtTheDocumentedPaths()
        {
            string[] paths = typeof(CreateRootMenu)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(method => method.GetCustomAttributes<MenuItem>())
                .Select(attribute => (string)typeof(MenuItem)
                    .GetField("menuItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .GetValue(attribute))
                .ToArray();

            Assert.That(paths, Does.Contain("GameObject/UI/UIDirector Root"));
            Assert.That(paths, Does.Contain("GameObject/UI/UIDirector Root (Camera)"));
        }

        // ---- Menu entries run ----

        [TestCase(RootMenuPath, false)]
        [TestCase(CameraRootMenuPath, true)]
        public void MenuEntry_OtherObjectSelected_SelectsTheNewRootAndParentsNothingUnderTheSelection(string menuPath, bool cameraVariant)
        {
            SessionState.SetBool(OfferedSessionKey, true);
            var selected = new GameObject("Selected Canvas", typeof(RectTransform), typeof(Canvas));
            Selection.activeGameObject = selected;

            InvokeMenu(menuPath);

            ViewRoot viewRoot = FindViewRoot();
            Assert.That(Selection.activeGameObject, Is.EqualTo(viewRoot.gameObject), "the new root is selected, not the holder");
            Assert.That(selected.transform.childCount, Is.EqualTo(0), "the selection gets no child");
            Assert.That(selected.transform.parent, Is.Null);

            Transform holder = viewRoot.transform.parent;
            if (cameraVariant)
            {
                Assert.That(holder, Is.Not.Null);
                Assert.That(holder.name, Is.EqualTo(HolderName));
                Assert.That(holder.parent, Is.Null, "the holder is at the scene root");
            }
            else
            {
                Assert.That(holder, Is.Null, "the root is at the scene root");
            }
        }

        [TestCase(RootMenuPath)]
        [TestCase(CameraRootMenuPath)]
        public void MenuEntry_NothingSelected_SelectsTheNewRoot(string menuPath)
        {
            SessionState.SetBool(OfferedSessionKey, true);

            InvokeMenu(menuPath);

            Assert.That(Selection.activeGameObject, Is.EqualTo(FindViewRoot().gameObject));
        }

        // ---- UI environment offer ----

        [Test]
        public void MenuEntry_InBatchMode_OffersNoUiEnvironment()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Outside batch mode the offer opens a dialog; run this case with -batchmode.");
            }

            SessionState.EraseBool(OfferedSessionKey);
            if (EditorSettings.prefabUIEnvironment != null)
            {
                EditorSettings.prefabUIEnvironment = null;
            }

            InvokeMenu(RootMenuPath);

            Assert.That(EditorSettings.prefabUIEnvironment == null, Is.True, "nothing is assigned");
            Assert.That(SessionState.GetBool(OfferedSessionKey, false), Is.False, "no offer was made, so none is remembered");
        }

        [TestCase(RootMenuPath)]
        [TestCase(CameraRootMenuPath)]
        public void MenuEntry_UiEnvironmentAlreadyAssigned_MakesNoOffer(string menuPath)
        {
            SceneAsset environment = SaveActiveSceneAsAsset();
            EditorSettings.prefabUIEnvironment = environment;
            SessionState.EraseBool(OfferedSessionKey);

            InvokeMenu(menuPath);

            Assert.That(SessionState.GetBool(OfferedSessionKey, false), Is.False, "no offer was made, so none is remembered");
            Assert.That(EditorSettings.prefabUIEnvironment == environment, Is.True, "the assigned environment stays");
        }
    }
}
