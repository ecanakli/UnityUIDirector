using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ecanakli.UIDirector
{
    /// <summary>The UI root: hosts the layers, shield, backdrops and staging under its canvas, and exposes the navigator.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
    public sealed class ViewRoot : MonoBehaviour
    {
        [SerializeField, Tooltip("Catalogs registered when the root initialises.")]
        private ViewCatalog[] _catalogs;

        [SerializeField, Tooltip("Sorting order of the lowest layer; the package's sorting band starts here.")]
        private int _baseSortingOrder;

        [SerializeField, Tooltip("Keeps the root's top-level object across scene loads, with a UI camera that shares it.")]
        private bool _persistAcrossScenes;

        [SerializeField, Tooltip("Initialises with default settings in Awake; turn off to call Initialize from code.")]
        private bool _initializeOnAwake = true;

        [SerializeField, Tooltip("Seconds of unscaled time a view may take to load before the request fails.")]
        private float _loadTimeoutSeconds = LayerRunner.DefaultLoadTimeoutSeconds;

        [SerializeField, Tooltip("Seconds of unscaled time a transition may run before it is snapped to its end.")]
        private float _transitionTimeoutSeconds = LayerRunner.DefaultTransitionTimeoutSeconds;

        [SerializeField, Tooltip("Seconds a load must run before the load indicator is shown.")]
        private float _indicatorDelaySeconds = global::Ecanakli.UIDirector.Navigator.DefaultLoadIndicatorDelaySeconds;

        [SerializeField, Tooltip("Requests one layer may hold waiting before new ones are rejected.")]
        private int _waitingCap = RequestIntake.DefaultWaitingCap;

        [SerializeField, Tooltip("Object activated while a load runs longer than the indicator delay; optional.")]
        private GameObject _loadIndicator;

        [SerializeField, Tooltip("Colour of the generated backdrop behind modal layers; unused when a backdrop prefab is set.")]
        private Color _backdropColor = new Color(0f, 0f, 0f, 0.6f);

        [SerializeField, Tooltip("Backdrop instantiated behind modal layers instead of the generated one; optional.")]
        private GameObject _backdropPrefab;

        // One per root: the stage, the navigator and the root itself report through it.
        private readonly Diagnostics _diagnostics = new Diagnostics();
        private readonly List<SafeAreaFitter> _fitters = new List<SafeAreaFitter>(4);

        private Navigator _core;
        private UguiStage _stage;
        private Application.LowMemoryCallback _onLowMemory;
        private bool _lowMemorySubscribed;
        private bool _awoken;
        private bool _destroyed;
        private Rect _lastSafeArea;
        private Vector2 _lastScreenSize;

        /// <summary>True once Initialize has completed, also after the root was destroyed.</summary>
        public bool IsInitialized
        {
            get { return _core != null; }
        }

        /// <summary>The navigator of this root; the first access initialises the root with default settings when it is not yet initialised.</summary>
        public INavigator Navigator
        {
            get
            {
                if (_core == null)
                {
                    if (IsDestroyed())
                        throw new InvalidOperationException("The root was destroyed before it was initialised.");

                    Initialize(new ViewRootSetup());
                }

                return _core;
            }
        }

        // The logic core, for tests and components; null before initialisation.
        internal Navigator Core
        {
            get { return _core; }
        }

        // The stage behind the navigator, for tests; null before initialisation.
        internal UguiStage Stage
        {
            get { return _stage; }
        }

        /// <summary>Builds the stage and the navigator; call it before the first access to Navigator, at most once.</summary>
        public void Initialize(ViewRootSetup setup)
        {
            if (setup == null)
                throw new ArgumentNullException(nameof(setup));
            if (_core != null)
                throw new InvalidOperationException("The root is already initialised.");
            if (IsDestroyed())
                throw new InvalidOperationException("The root was destroyed.");

            var canvas = GetComponent<Canvas>();
            var root = (RectTransform)transform;
            var layers = BuildLayers(setup.ExtraLayers);
            var provider = setup.AssetProvider;
            if (provider == null)
                TryGetComponent(out provider);

            // renderMode reads back as overlay once the camera is gone, so the mode is remembered now.
            var cameraMode = canvas.renderMode == RenderMode.ScreenSpaceCamera;

            UguiStage stage = null;
            Navigator core;
            try
            {
                stage = new UguiStage(
                    root,
                    canvas,
                    layers,
                    provider,
                    setup.Inject,
                    _loadIndicator,
                    _backdropColor,
                    _backdropPrefab,
                    _diagnostics,
                    new ErrorRouter(setup.ErrorHandler, _diagnostics),
                    cameraMode);

                core = new Navigator(
                    stage,
                    null,
                    null,
                    _catalogs,
                    layers,
                    _loadTimeoutSeconds,
                    _transitionTimeoutSeconds,
                    setup.ErrorHandler,
                    _diagnostics,
                    _baseSortingOrder,
                    _indicatorDelaySeconds,
                    _waitingCap);
            }
            catch (Exception)
            {
                if (stage != null)
                {
                    DisposeStage(stage);
                    stage.DestroyBuiltObjects();
                }

                throw;
            }

            _stage = stage;
            _core = core;

            // Unity sends OnDestroy only after Awake, so before Awake the subscription waits for it.
            if (_awoken)
                SubscribeLowMemory();

            // Without an EventSystem no view gets input, which is easy to miss, so it is reported once here.
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                _diagnostics.Warn(
                    DiagnosticIds.NoEventSystem,
                    "No EventSystem in the scene; views will not receive input. Add one with GameObject > UI > Event System.");
            }
        }

        // True while the root listens to Application.lowMemory, for tests.
        internal bool IsLowMemorySubscribed
        {
            get { return _lowMemorySubscribed; }
        }

        // Registers a fitter and fits it at once; the root refits every fitter when the safe area or screen size changes.
        internal void AddFitter(SafeAreaFitter fitter)
        {
            if (fitter == null || _fitters.Contains(fitter))
                return;

            _fitters.Add(fitter);
            fitter.Fit(Screen.safeArea, new Vector2(Screen.width, Screen.height));
        }

        internal void RemoveFitter(SafeAreaFitter fitter)
        {
            if (fitter == null)
                return;

            _fitters.Remove(fitter);
        }

        private void Awake()
        {
            _awoken = true;

            // The top-level object, so a UI camera that sits beside the root under a shared parent survives with it.
            if (_persistAcrossScenes)
                DontDestroyOnLoad(transform.root.gameObject);

            if (_initializeOnAwake && _core == null)
                Initialize(new ViewRootSetup());

            // A root initialised while inactive subscribes now, when OnDestroy is guaranteed to follow.
            if (_core != null)
                SubscribeLowMemory();
        }

        private void Update()
        {
            // Allocates 0 B per frame.
            if (_core != null)
                _core.Clock.Tick(Time.unscaledDeltaTime);

            // The safe area is compared only while a fitter is registered, so an idle root costs nothing.
            if (_fitters.Count > 0)
                CheckSafeArea();
        }

        private void OnDestroy()
        {
            _destroyed = true;

            if (_core != null)
            {
                try
                {
                    _core.Teardown();
                }
                catch (Exception exception)
                {
                    _diagnostics.ReportFault("root navigator teardown", exception);
                }
            }

            if (_stage != null)
            {
                DisposeStage(_stage);
                _stage = null;
            }

            if (_lowMemorySubscribed)
            {
                try
                {
                    Application.lowMemory -= _onLowMemory;
                }
                catch (Exception exception)
                {
                    _diagnostics.ReportFault("root low memory unsubscribe", exception);
                }

                _lowMemorySubscribed = false;
            }

            _fitters.Clear();
        }

        private void OnValidate()
        {
            _baseSortingOrder = Mathf.Clamp(_baseSortingOrder, short.MinValue, short.MaxValue);

            // Written so NaN falls to the minimum; Mathf.Max would keep NaN.
            _loadTimeoutSeconds = _loadTimeoutSeconds >= LayerRunner.MinTimeoutSeconds ? _loadTimeoutSeconds : LayerRunner.MinTimeoutSeconds;
            _transitionTimeoutSeconds = _transitionTimeoutSeconds >= LayerRunner.MinTimeoutSeconds ? _transitionTimeoutSeconds : LayerRunner.MinTimeoutSeconds;
            _indicatorDelaySeconds = Mathf.Max(0f, _indicatorDelaySeconds);
            _waitingCap = Mathf.Max(1, _waitingCap);
        }

        private void SubscribeLowMemory()
        {
            if (_lowMemorySubscribed)
                return;

            if (_onLowMemory == null)
                _onLowMemory = OnLowMemory;
            Application.lowMemory += _onLowMemory;
            _lowMemorySubscribed = true;
        }

        private void OnLowMemory()
        {
            if (_core == null)
                return;

            try
            {
                _core.Trim();
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("root low memory trim", exception);
            }
        }

        private void CheckSafeArea()
        {
            var safeArea = Screen.safeArea;
            var screenSize = new Vector2(Screen.width, Screen.height);
            if (safeArea == _lastSafeArea && screenSize == _lastScreenSize)
                return;

            _lastSafeArea = safeArea;
            _lastScreenSize = screenSize;

            // Backwards, so a fitter may remove itself during its call.
            for (var i = _fitters.Count - 1; i >= 0; i--)
            {
                if (i >= _fitters.Count)
                    continue;

                var fitter = _fitters[i];
                if (fitter == null)
                {
                    _fitters.RemoveAt(i);
                    continue;
                }

                try
                {
                    fitter.Fit(safeArea, screenSize);
                }
                catch (Exception exception)
                {
                    _diagnostics.ReportFault("safe area fit", exception);
                }
            }
        }

        private void DisposeStage(UguiStage stage)
        {
            try
            {
                stage.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("root stage dispose", exception);
            }
        }

        // Unity's null check also catches a root destroyed before its OnDestroy ran.
        private bool IsDestroyed()
        {
            return _destroyed || this == null;
        }

        // The default layers plus the extra ones (nulls skipped), stably sorted by order ascending; a repeated layer throws before anything is built.
        internal static List<ViewLayer> BuildLayers(IReadOnlyList<ViewLayer> extraLayers)
        {
            var extraCount = extraLayers == null ? 0 : extraLayers.Count;
            var layers = new List<ViewLayer>(3 + extraCount)
            {
                ViewLayers.Screens,
                ViewLayers.Popups,
                ViewLayers.System
            };

            for (var i = 0; i < extraCount; i++)
            {
                var layer = extraLayers[i];
                if (layer == null)
                    continue;

                for (var k = 0; k < layers.Count; k++)
                {
                    var existing = layers[k];
                    if (ReferenceEquals(existing, layer) || string.Equals(existing.Id, layer.Id, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "ViewRootSetup.ExtraLayers: the layer id '" + layer.Id + "' is used twice; the default layers screens, popups and system are always present.",
                            "setup");
                    }
                }

                layers.Add(layer);
            }

            // Insertion sort: stable, unlike List.Sort.
            for (var i = 1; i < layers.Count; i++)
            {
                var current = layers[i];
                var j = i - 1;
                while (j >= 0 && layers[j].Order > current.Order)
                {
                    layers[j + 1] = layers[j];
                    j--;
                }

                layers[j + 1] = current;
            }

            return layers;
        }
    }
}
