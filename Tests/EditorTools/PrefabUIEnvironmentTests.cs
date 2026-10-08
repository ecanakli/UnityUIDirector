using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.EditorTools.Tests
{
    /// <summary>The Prefab Mode UI environment writer. Assets go in a temporary folder; the editor setting is restored.</summary>
    [TestFixture]
    public sealed class PrefabUIEnvironmentTests
    {
        private string _folder;
        private string _outsideFolder;
        private SceneAsset _savedEnvironment;

        [SetUp]
        public void SetUp()
        {
            _savedEnvironment = EditorSettings.prefabUIEnvironment;
            EditorSettings.prefabUIEnvironment = null;

            string name = "UIDirectorEnvTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _folder = "Assets/" + name;
            _outsideFolder = null;

            // Saved, because Unity cannot add the environment scene next to an untitled one (that case has its own test).
            Scene host = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(host, _folder + "/Host.unity");
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            Selection.activeObject = null;

            // The setting goes back first, so it never points at an asset that is about to be deleted.
            EditorSettings.prefabUIEnvironment = _savedEnvironment;

            // A scene saved into the temporary folder may be open; a new Single scene closes it before the folder goes.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!string.IsNullOrEmpty(_folder))
            {
                AssetDatabase.DeleteAsset(_folder);
            }

            if (!string.IsNullOrEmpty(_outsideFolder) && Directory.Exists(_outsideFolder))
            {
                Directory.Delete(_outsideFolder, true);
            }
        }

        private string PathFor(string fileName)
        {
            return _folder + "/" + fileName;
        }

        // Collects every error-level log while it is alive and keeps those logs from failing the test.
        private sealed class ErrorCapture : IDisposable
        {
            private readonly List<string> _errors = new List<string>();

            internal ErrorCapture()
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += OnLog;
            }

            internal IReadOnlyList<string> Errors => _errors;

            internal bool HasPackageError => _errors.Any(message => message.StartsWith("UIDirector: ", StringComparison.Ordinal));

            public void Dispose()
            {
                Application.logMessageReceived -= OnLog;
            }

            private void OnLog(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    _errors.Add(message);
                }
            }
        }

        private static Canvas FindSelectedRootCanvas()
        {
            MethodInfo method = typeof(PrefabUIEnvironment).GetMethod("FindSelectedRootCanvas", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "PrefabUIEnvironment has a source lookup for the menu");
            return (Canvas)method.Invoke(null, null);
        }

        private static void InspectScene(string path, Action<Scene> inspect)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                inspect(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Canvas FindCanvas(Scene scene)
        {
            Canvas canvas = scene.GetRootGameObjects()
                .Select(go => go.GetComponent<Canvas>())
                .FirstOrDefault(c => c != null);
            Assert.That(canvas, Is.Not.Null, "the environment scene has a root Canvas");
            return canvas;
        }

        private static Canvas CreateSourceCanvas()
        {
            var go = new GameObject("Source Root", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 7;
            canvas.pixelPerfect = true;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720f, 1280f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
            scaler.matchWidthOrHeight = 0.8f;
            return canvas;
        }

        // ---- Writing the asset ----

        [Test]
        public void CreateEnvironmentScene_NoSource_WritesASceneAssetAtThePath()
        {
            string path = PathFor("Env.unity");

            SceneAsset asset = PrefabUIEnvironment.CreateEnvironmentScene(path, null, false);

            Assert.That(asset, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(asset), Is.EqualTo(path));
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path)), Is.True);
        }

        [Test]
        public void CreateEnvironmentScene_NoSource_SceneHoldsACanvasAndScalerWithTheRootDefaults()
        {
            string path = PathFor("Env.unity");
            PrefabUIEnvironment.CreateEnvironmentScene(path, null, false);

            InspectScene(path, scene =>
            {
                Canvas canvas = FindCanvas(scene);
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();

                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(scaler, Is.Not.Null, "CanvasScaler");
                Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
                Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1080f, 1920f)));
                Assert.That(scaler.screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight));
                Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f).Within(0.0001f));
            });
        }

        [Test]
        public void CreateEnvironmentScene_SourceWithScaler_CopiesCanvasAndScalerSettings()
        {
            Canvas source = CreateSourceCanvas();
            string path = PathFor("Env.unity");

            PrefabUIEnvironment.CreateEnvironmentScene(path, source, false);

            InspectScene(path, scene =>
            {
                Canvas canvas = FindCanvas(scene);
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();

                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(canvas.sortingOrder, Is.EqualTo(7));
                Assert.That(canvas.pixelPerfect, Is.True);
                Assert.That(scaler, Is.Not.Null, "CanvasScaler");
                Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
                Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(720f, 1280f)));
                Assert.That(scaler.screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.Shrink));
                Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.8f).Within(0.0001f));
            });
        }

        [Test]
        public void CreateEnvironmentScene_ConstantPixelSizeSource_CopiesTheScaleFactor()
        {
            Canvas source = CreateSourceCanvas();
            CanvasScaler sourceScaler = source.GetComponent<CanvasScaler>();
            sourceScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            sourceScaler.scaleFactor = 2.5f;
            sourceScaler.referencePixelsPerUnit = 50f;
            string path = PathFor("Env.unity");

            PrefabUIEnvironment.CreateEnvironmentScene(path, source, false);

            InspectScene(path, scene =>
            {
                CanvasScaler scaler = FindCanvas(scene).GetComponent<CanvasScaler>();

                Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ConstantPixelSize));
                Assert.That(scaler.scaleFactor, Is.EqualTo(2.5f).Within(0.0001f));
                Assert.That(scaler.referencePixelsPerUnit, Is.EqualTo(50f).Within(0.0001f));
            });
        }

        [Test]
        public void CreateEnvironmentScene_SourceWithSortingLayerAndShaderChannels_CopiesBoth()
        {
            // The root owns render mode, sorting layer and shader channels.
            int layerId = SortingLayer.NameToID("UIDirectorTest");
            Assert.That(SortingLayer.IsValid(layerId), Is.True, "the dev project's TagManager defines the sorting layer UIDirectorTest");
            const AdditionalCanvasShaderChannels channels =
                AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            Canvas source = CreateSourceCanvas();
            source.sortingLayerID = layerId;
            source.additionalShaderChannels = channels;
            string path = PathFor("Env.unity");

            PrefabUIEnvironment.CreateEnvironmentScene(path, source, false);

            InspectScene(path, scene =>
            {
                Canvas canvas = FindCanvas(scene);

                // An overlay canvas keeps no sorting layer (it reads back 0), so only what the source really holds is compared.
                Assert.That(canvas.sortingLayerID, Is.EqualTo(source.sortingLayerID));
                Assert.That(canvas.additionalShaderChannels, Is.EqualTo(channels));
            });
        }

        [Test]
        public void CreateEnvironmentScene_SourceWithoutScaler_UsesTheDefaultScaler()
        {
            var go = new GameObject("Source Without Scaler", typeof(RectTransform), typeof(Canvas));
            string path = PathFor("Env.unity");

            PrefabUIEnvironment.CreateEnvironmentScene(path, go.GetComponent<Canvas>(), false);

            InspectScene(path, scene =>
            {
                CanvasScaler scaler = FindCanvas(scene).GetComponent<CanvasScaler>();

                Assert.That(scaler, Is.Not.Null);
                Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
                Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1080f, 1920f)));
                Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f).Within(0.0001f));
            });
        }

        [Test]
        public void CreateEnvironmentScene_CameraModeSource_DoesNotCopyTheCamera()
        {
            Canvas source = CreateSourceCanvas();
            var cameraObject = new GameObject("Source Camera", typeof(Camera));
            source.renderMode = RenderMode.ScreenSpaceCamera;
            source.worldCamera = cameraObject.GetComponent<Camera>();
            string path = PathFor("Env.unity");

            PrefabUIEnvironment.CreateEnvironmentScene(path, source, false);

            InspectScene(path, scene =>
            {
                Assert.That(FindCanvas(scene).worldCamera, Is.Null);
                Assert.That(scene.GetRootGameObjects().Any(go => go.GetComponentInChildren<Camera>(true) != null), Is.False,
                    "the environment scene holds no camera");
            });
        }

        [Test]
        public void CreateEnvironmentScene_LeavesTheSourceUntouched()
        {
            Canvas source = CreateSourceCanvas();
            CanvasScaler sourceScaler = source.GetComponent<CanvasScaler>();

            PrefabUIEnvironment.CreateEnvironmentScene(PathFor("Env.unity"), source, false);

            Assert.That(source != null, Is.True);
            Assert.That(source.sortingOrder, Is.EqualTo(7));
            Assert.That(sourceScaler.referenceResolution, Is.EqualTo(new Vector2(720f, 1280f)));
            Assert.That(sourceScaler.matchWidthOrHeight, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void CreateEnvironmentScene_MissingSubfolders_AreCreated()
        {
            string path = PathFor("Nested/Deeper/Env.unity");

            SceneAsset asset = PrefabUIEnvironment.CreateEnvironmentScene(path, null, false);

            Assert.That(asset, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(asset), Is.EqualTo(path));
        }

        // ---- The user's open scenes ----

        [Test]
        public void CreateEnvironmentScene_DoesNotChangeTheOpenScenes()
        {
            Canvas source = CreateSourceCanvas();
            Scene activeBefore = SceneManager.GetActiveScene();
            int sceneCountBefore = SceneManager.sceneCount;
            int rootCountBefore = activeBefore.rootCount;
            bool dirtyBefore = activeBefore.isDirty;

            PrefabUIEnvironment.CreateEnvironmentScene(PathFor("Env.unity"), source, false);

            Scene activeAfter = SceneManager.GetActiveScene();
            Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCountBefore), "no scene is left open");
            Assert.That(activeAfter, Is.EqualTo(activeBefore), "the active scene stays active");
            Assert.That(activeAfter.rootCount, Is.EqualTo(rootCountBefore), "no object is added to or removed from it");
            Assert.That(activeAfter.isDirty, Is.EqualTo(dirtyBefore));
            Assert.That(source != null, Is.True);
        }

        [Test]
        public void CreateEnvironmentScene_NoSource_DoesNotChangeTheOpenScenes()
        {
            Scene activeBefore = SceneManager.GetActiveScene();
            int sceneCountBefore = SceneManager.sceneCount;

            PrefabUIEnvironment.CreateEnvironmentScene(PathFor("Env.unity"), null, false);

            Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCountBefore));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(activeBefore));
        }

        [Test]
        public void CreateEnvironmentScene_SavedMultiSceneSetup_LeavesEveryOpenSceneAsItWas()
        {
            // Scene A: saved, then dirtied with an unsaved object. Scene B: saved, loaded additively and active.
            Canvas source = CreateSourceCanvas();
            Scene sceneA = SceneManager.GetActiveScene();
            Assert.That(EditorSceneManager.SaveScene(sceneA, PathFor("SceneA.unity")), Is.True, "scene A is saved");

            Scene sceneB = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var inB = new GameObject("In B");
            SceneManager.MoveGameObjectToScene(inB, sceneB);
            Assert.That(EditorSceneManager.SaveScene(sceneB, PathFor("SceneB.unity")), Is.True, "scene B is saved");
            SceneManager.SetActiveScene(sceneB);

            var unsaved = new GameObject("Unsaved");
            SceneManager.MoveGameObjectToScene(unsaved, sceneA);
            EditorSceneManager.MarkSceneDirty(sceneA);

            string pathA = sceneA.path;
            string pathB = sceneB.path;
            int rootsA = sceneA.rootCount;
            int rootsB = sceneB.rootCount;
            bool dirtyA = sceneA.isDirty;
            bool dirtyB = sceneB.isDirty;
            Assert.That(dirtyA, Is.True, "precondition: scene A has unsaved changes");

            SceneAsset asset = PrefabUIEnvironment.CreateEnvironmentScene(PathFor("Env.unity"), source, false);

            Assert.That(asset != null, Is.True, "the environment asset is written");
            Assert.That(AssetDatabase.GetAssetPath(asset), Is.EqualTo(PathFor("Env.unity")));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(2), "no scene is left open and none is closed");
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(sceneB), "the active scene stays active");
            Assert.That(sceneA.isLoaded, Is.True);
            Assert.That(sceneB.isLoaded, Is.True);
            Assert.That(sceneA.path, Is.EqualTo(pathA));
            Assert.That(sceneB.path, Is.EqualTo(pathB));
            Assert.That(sceneA.rootCount, Is.EqualTo(rootsA));
            Assert.That(sceneB.rootCount, Is.EqualTo(rootsB));
            Assert.That(sceneA.isDirty, Is.EqualTo(dirtyA), "scene A keeps its unsaved changes");
            Assert.That(sceneB.isDirty, Is.EqualTo(dirtyB));
            Assert.That(unsaved != null, Is.True, "the unsaved object survives");
        }

        [Test]
        public void CreateEnvironmentScene_UntitledSceneRightAfterCreatingARoot_KeepsTheSceneAndEitherWritesTheAssetOrReportsAnError()
        {
            // The state in which the create-root menu offers the environment: an untitled scene holding the new root.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = CreateRootMenu.CreateRoot(false);
            Canvas source = root.GetComponent<Canvas>();
            Scene untitled = SceneManager.GetActiveScene();
            Assert.That(untitled.path, Is.Empty, "precondition: the scene is untitled");
            int rootCount = untitled.rootCount;
            string path = PathFor("Env.unity");
            SceneAsset result = null;
            ErrorCapture capture;

            using (capture = new ErrorCapture())
            {
                Assert.DoesNotThrow(() => result = PrefabUIEnvironment.CreateEnvironmentScene(path, source, false));
            }

            Assert.That(SceneManager.sceneCount, Is.EqualTo(1), "the untitled scene is the only open scene");
            Assert.That(untitled.isLoaded, Is.True, "the untitled scene stays loaded");
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(untitled), "and active");
            Assert.That(untitled.rootCount, Is.EqualTo(rootCount), "with its objects");
            Assert.That(root != null, Is.True);

            if (result != null)
            {
                Assert.That(AssetDatabase.GetAssetPath(result), Is.EqualTo(path));
            }
            else
            {
                Assert.That(capture.HasPackageError, Is.True,
                    "a failed call reports a 'UIDirector: ' error; logged: " + string.Join(" | ", capture.Errors));
            }
        }

        // ---- Source lookup for the menu ----

        private static GameObject CreateViewRootObject(string name)
        {
            return new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(ViewRoot));
        }

        [Test]
        public void FindSelectedRootCanvas_ViewRootSelected_ReturnsItsCanvas()
        {
            GameObject root = CreateViewRootObject("Root");
            Selection.activeGameObject = root;

            Canvas result = FindSelectedRootCanvas();

            Assert.That(result == root.GetComponent<Canvas>(), Is.True);
        }

        [Test]
        public void FindSelectedRootCanvas_ChildOfViewRootSelected_ReturnsTheRootsCanvas()
        {
            GameObject root = CreateViewRootObject("Root");
            var child = new GameObject("Child", typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            var grandchild = new GameObject("Grandchild", typeof(RectTransform));
            grandchild.transform.SetParent(child.transform, false);

            Selection.activeGameObject = child;
            Assert.That(FindSelectedRootCanvas() == root.GetComponent<Canvas>(), Is.True, "child");

            Selection.activeGameObject = grandchild;
            Assert.That(FindSelectedRootCanvas() == root.GetComponent<Canvas>(), Is.True, "grandchild");
        }

        [Test]
        public void FindSelectedRootCanvas_CanvasWithoutViewRootSelected_ReturnsNull()
        {
            var other = new GameObject("Other Canvas", typeof(RectTransform), typeof(Canvas));
            Selection.activeGameObject = other;

            Assert.That(FindSelectedRootCanvas() == null, Is.True);
        }

        [Test]
        public void FindSelectedRootCanvas_NothingSelected_ReturnsNull()
        {
            CreateViewRootObject("Root");
            Selection.activeObject = null;

            Assert.That(FindSelectedRootCanvas() == null, Is.True);
        }

        // ---- Assigning ----

        [Test]
        public void CreateEnvironmentScene_AssignFalse_DoesNotTouchTheSetting()
        {
            SceneAsset asset = PrefabUIEnvironment.CreateEnvironmentScene(PathFor("Env.unity"), null, false);

            Assert.That(asset, Is.Not.Null);
            Assert.That(EditorSettings.prefabUIEnvironment == null, Is.True);
        }

        [Test]
        public void CreateEnvironmentScene_AssignTrue_SetsThePrefabUIEnvironment()
        {
            SceneAsset asset = PrefabUIEnvironment.CreateEnvironmentScene(PathFor("Env.unity"), null, true);

            Assert.That(asset, Is.Not.Null);
            Assert.That(EditorSettings.prefabUIEnvironment == asset, Is.True);
        }

        [Test]
        public void CreateEnvironmentScene_AssignFalse_KeepsAnEnvironmentAlreadyAssigned()
        {
            SceneAsset first = PrefabUIEnvironment.CreateEnvironmentScene(PathFor("First.unity"), null, true);

            SceneAsset second = PrefabUIEnvironment.CreateEnvironmentScene(PathFor("Second.unity"), null, false);

            Assert.That(second, Is.Not.Null);
            Assert.That(EditorSettings.prefabUIEnvironment == first, Is.True);
        }

        // ---- Bad paths ----

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Assets/NotAScene.txt")]
        [TestCase("Assets/NotAScene.prefab")]
        [TestCase("Assets/NoExtension")]
        public void CreateEnvironmentScene_BadPath_ReturnsNullWithoutThrowing(string badPath)
        {
            LogAssert.Expect(LogType.Error, new Regex("^UIDirector: "));
            SceneAsset result = null;
            int sceneCountBefore = SceneManager.sceneCount;

            Assert.DoesNotThrow(() => result = PrefabUIEnvironment.CreateEnvironmentScene(badPath, null, true));

            Assert.That(result == null, Is.True);
            Assert.That(EditorSettings.prefabUIEnvironment == null, Is.True, "a failed call assigns nothing");
            Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCountBefore));
        }

        [Test]
        public void CreateEnvironmentScene_WrongExtensionInsideTheProject_WritesNoFile()
        {
            LogAssert.Expect(LogType.Error, new Regex("^UIDirector: "));
            string path = PathFor("Env.txt");

            SceneAsset result = PrefabUIEnvironment.CreateEnvironmentScene(path, null, false);

            Assert.That(result == null, Is.True);
            Assert.That(File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path)), Is.False);
        }

        [Test]
        public void CreateEnvironmentScene_PathOfAnOpenScene_FailsWithoutThrowingAndKeepsTheSceneOpen()
        {
            string path = PathFor("Env.unity");
            PrefabUIEnvironment.CreateEnvironmentScene(path, null, false);
            Scene open = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("^UIDirector: "));
                SceneAsset result = null;

                Assert.DoesNotThrow(() => result = PrefabUIEnvironment.CreateEnvironmentScene(path, null, false));

                Assert.That(result == null, Is.True);
                Assert.That(open.isLoaded, Is.True, "the open scene is left as it was");
            }
            finally
            {
                EditorSceneManager.CloseScene(open, true);
            }
        }

        [Test]
        public void CreateEnvironmentScene_UnityPathOutsideAssets_FailsWithoutThrowingAndLeavesNothingBehind()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Unity may open a dialog when a scene is saved outside Assets; run this case with -batchmode.");
            }

            // A saved active scene keeps this case apart from the untitled-scene case.
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), PathFor("Active.unity")), Is.True);

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string folderName = "UIDirectorEnvOutside_" + Guid.NewGuid().ToString("N");
            _outsideFolder = Path.Combine(projectRoot, folderName);
            string path = folderName + "/Env.unity";
            Scene activeBefore = SceneManager.GetActiveScene();
            int sceneCountBefore = SceneManager.sceneCount;
            SceneAsset result = null;
            ErrorCapture capture;

            using (capture = new ErrorCapture())
            {
                Assert.DoesNotThrow(() => result = PrefabUIEnvironment.CreateEnvironmentScene(path, null, true));
            }

            Assert.That(result == null, Is.True);
            Assert.That(EditorSettings.prefabUIEnvironment == null, Is.True, "a failed call assigns nothing");
            Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCountBefore), "no scene is left open");
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(activeBefore));
            Assert.That(capture.HasPackageError, Is.True,
                "a failed call reports a 'UIDirector: ' error; logged: " + string.Join(" | ", capture.Errors));
            Assert.That(File.Exists(Path.Combine(_outsideFolder, "Env.unity")), Is.False, "no scene file is written");
            Assert.That(Directory.Exists(_outsideFolder), Is.False, "no folder is left in the project root");
        }

        // ---- Interactive entry ----

        [Test]
        public void RunInteractive_InBatchMode_ShowsNothingAndWritesNothing()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("RunInteractive opens a file panel outside batch mode; run this case with -batchmode.");
            }

            PrefabUIEnvironment.RunInteractive(CreateSourceCanvas());

            Assert.That(EditorSettings.prefabUIEnvironment == null, Is.True);
            // The host scene SetUp saved there is the only scene allowed in the folder.
            var written = AssetDatabase.FindAssets("t:SceneAsset", new[] { _folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path != _folder + "/Host.unity");
            Assert.That(written, Is.Empty);
        }

        // ---- Menu entries ----

        [Test]
        public void MenuItem_IsRegisteredAtTheDocumentedPath()
        {
            string[] paths = typeof(PrefabUIEnvironment)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(method => method.GetCustomAttributes<MenuItem>())
                .Select(attribute => (string)typeof(MenuItem)
                    .GetField("menuItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .GetValue(attribute))
                .ToArray();

            Assert.That(paths, Does.Contain("Tools/UIDirector/Create Prefab UI Environment"));
        }
    }
}
