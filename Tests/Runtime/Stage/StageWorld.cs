using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // One real root for a PlayMode test: canvas, ViewRoot, EventSystem, view templates and catalogs; Dispose removes all of it.
    internal sealed class StageWorld : IDisposable
    {
        private readonly List<Object> _owned = new List<Object>();
        private readonly CancellationTokenSource _owner = new CancellationTokenSource();

        // The root starts inactive so configure can set Inspector fields before Awake; then it is initialised with setup.
        public StageWorld(
            Action<ViewRoot> configure = null,
            ViewRootSetup setup = null,
            bool cameraMode = false,
            bool eventSystem = true)
        {
            // A throw half way (an Assume in configure, a failing Initialize) must not leave objects behind for the next test.
            try
            {
                Build(configure, setup, cameraMode, eventSystem, out var root, out var rootCanvas, out var camera, out var eventSystemObject);
                Root = root;
                RootCanvas = rootCanvas;
                Camera = camera;
                EventSystemObject = eventSystemObject;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Build(
            Action<ViewRoot> configure,
            ViewRootSetup setup,
            bool cameraMode,
            bool eventSystem,
            out ViewRoot root,
            out Canvas rootCanvas,
            out Camera camera,
            out GameObject eventSystemObject)
        {
            root = null;
            camera = null;
            eventSystemObject = null;

            if (eventSystem)
            {
                eventSystemObject = new GameObject("TestEventSystem");
                _owned.Add(eventSystemObject);
                eventSystemObject.AddComponent<EventSystem>();
            }

            var rootObject = new GameObject("TestRoot", typeof(RectTransform));
            rootObject.SetActive(false);
            _owned.Add(rootObject);

            rootCanvas = rootObject.AddComponent<Canvas>();
            if (cameraMode)
            {
                var cameraObject = new GameObject("TestUiCamera");
                _owned.Add(cameraObject);
                camera = cameraObject.AddComponent<Camera>();
                rootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                rootCanvas.worldCamera = camera;
            }
            else
            {
                rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            rootObject.AddComponent<CanvasScaler>();
            root = rootObject.AddComponent<ViewRoot>();
            SetField(root, "_initializeOnAwake", false);
            if (configure != null)
                configure(root);

            rootObject.SetActive(true);
            root.Initialize(setup ?? new ViewRootSetup());
        }

        public ViewRoot Root { get; }
        public Canvas RootCanvas { get; }
        public Camera Camera { get; }
        public GameObject EventSystemObject { get; }

        public INavigator Navigator
        {
            get { return Root.Navigator; }
        }

        public Navigator Core
        {
            get { return Root.Core; }
        }

        public UguiStage Stage
        {
            get { return Root.Stage; }
        }

        // Cancelled in Dispose; the default owner for opens and registrations.
        public CancellationToken Owner
        {
            get { return _owner.Token; }
        }

        // An inactive view template (it serves as the prefab): stretched RectTransform, Canvas, GraphicRaycaster, ViewHost and TView.
        public GameObject Template<TView>(string name, Action<GameObject> build = null) where TView : Component
        {
            var template = new GameObject(name, typeof(RectTransform));
            template.SetActive(false);
            _owned.Add(template);

            var rect = (RectTransform)template.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            template.AddComponent<Canvas>();
            template.AddComponent<GraphicRaycaster>();
            template.AddComponent<ViewHost>();
            template.AddComponent<TView>();

            if (build != null)
                build(template);

            return template;
        }

        // Registers a one-row catalog with the root for the world's lifetime and returns the row.
        public ViewEntry Row(
            string id,
            GameObject prefab,
            ViewLayer layer = null,
            ViewRetention retention = ViewRetention.ReleaseOnClose,
            BackPolicy back = BackPolicy.LayerDefault,
            bool closeOnBackdrop = false,
            CoverPolicy cover = CoverPolicy.LayerDefault,
            CoveredMode whenCovered = CoveredMode.DisableCanvas,
            bool allowMultiple = false,
            string assetKey = null)
        {
            var layerId = (layer ?? ViewLayers.Screens).Id;
            var row = new ViewEntry(id, prefab, assetKey, layerId, retention, back, closeOnBackdrop, cover, whenCovered, allowMultiple);

            var catalog = ViewCatalog.Create(new[] { row });
            _owned.Add(catalog);
            Navigator.AddCatalog(catalog, Owner);
            return row;
        }

        // The live instance of a handle, or null.
        public static GameObject InstanceOf(ViewHandle handle)
        {
            GameObject instance;
            return handle.TryGetInstance(out instance) ? instance : null;
        }

        // Sets a private serialized field by name, for Inspector settings the tests need.
        public static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, name);

            field.SetValue(target, value);
        }

        public void Dispose()
        {
            _owner.Cancel();

            // Destroy is deferred to the end of the frame, so the order here does not matter.
            for (var i = 0; i < _owned.Count; i++)
            {
                if (_owned[i] != null)
                    Object.Destroy(_owned[i]);
            }

            _owned.Clear();
        }
    }
}
