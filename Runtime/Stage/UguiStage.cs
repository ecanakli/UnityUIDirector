using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ecanakli.UIDirector
{
    // The uGUI implementation of the stage: builds the layer hierarchy under the root and moves view objects through it.
    internal sealed class UguiStage : IViewStage, IViewHostSink
    {
        private readonly RectTransform _root;
        private readonly Canvas _rootCanvas;
        private readonly IViewProvider _assetProvider;
        private readonly Action<GameObject> _inject;
        private readonly GameObject _loadIndicator;
        private readonly Diagnostics _diagnostics;
        private readonly ErrorRouter _errors;
        private readonly bool _cameraMode;

        private readonly DirectViewProvider _direct = new DirectViewProvider();
        private readonly Dictionary<ViewLayer, RectTransform> _layerRects = new Dictionary<ViewLayer, RectTransform>();
        private readonly Dictionary<ViewLayer, Backdrop> _backdrops = new Dictionary<ViewLayer, Backdrop>();
        private readonly Action<ViewLayer> _backdropTapped;

        private readonly Transform _staging;
        private readonly Canvas _shieldCanvas;
        private readonly ShieldGraphic _shield;
        private Canvas _indicatorCanvas;

        // Two lists swapped per flush, so a report that arrives during a flush waits for the next one.
        private List<StageInstance> _pendingLosses = new List<StageInstance>();
        private List<StageInstance> _flushingLosses = new List<StageInstance>();
        private bool _flushScheduled;

        private IStageEvents _events;
        private bool _cameraWarned;
        private bool _disposed;

        internal UguiStage(
            RectTransform root,
            Canvas rootCanvas,
            IReadOnlyList<ViewLayer> layers,
            IViewProvider assetProvider,
            Action<GameObject> inject,
            GameObject loadIndicator,
            Color backdropColor,
            GameObject backdropPrefab,
            Diagnostics diagnostics,
            ErrorRouter errors,
            bool cameraMode)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (rootCanvas == null)
                throw new ArgumentNullException(nameof(rootCanvas));
            if (layers == null)
                throw new ArgumentNullException(nameof(layers));
            if (diagnostics == null)
                throw new ArgumentNullException(nameof(diagnostics));
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));

            // A failure part-way removes what was already built, so the root is left as it was.
            try
            {
                _root = root;
                _rootCanvas = rootCanvas;
                _assetProvider = assetProvider;
                _inject = inject;
                _loadIndicator = loadIndicator;
                _diagnostics = diagnostics;
                _errors = errors;
                _cameraMode = cameraMode;
                _backdropTapped = OnBackdropTapped;

                for (var i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer == null)
                        throw new ArgumentException("The layer list contains null.", nameof(layers));
                    if (_layerRects.ContainsKey(layer))
                        throw new ArgumentException("The layer '" + layer.Id + "' is listed twice.", nameof(layers));

                    var layerRect = CreateRect("Layer " + layer.Id, _root);
                    _layerRects.Add(layer, layerRect);

                    if (layer.Has(ViewLayerRules.Modal))
                        _backdrops.Add(layer, CreateBackdrop(layer, layerRect, backdropColor, backdropPrefab));
                }

                var staging = CreateRect("Staging", _root);
                _staging = staging;
                staging.gameObject.SetActive(false);

                // Always active with an empty mesh, so a raise blocks in the same frame without a canvas rebuild.
                var shieldRect = CreateRect("Shield", _root);
                _shieldCanvas = shieldRect.gameObject.AddComponent<Canvas>();
                _shieldCanvas.overrideSorting = true;
                _shieldCanvas.sortingLayerID = _rootCanvas.sortingLayerID;
                shieldRect.gameObject.AddComponent<GraphicRaycaster>();
                _shield = shieldRect.gameObject.AddComponent<ShieldGraphic>();
                _shield.raycastTarget = false;
            }
            catch
            {
                DestroyBuiltObjects();
                throw;
            }
        }

        // Test accessors

        internal Transform Staging
        {
            get { return _staging; }
        }

        internal ShieldGraphic Shield
        {
            get { return _shield; }
        }

        internal RectTransform LayerRect(ViewLayer layer)
        {
            RectTransform rect;
            if (layer != null && _layerRects.TryGetValue(layer, out rect))
                return rect;

            return null;
        }

        // Null for non-modal layers.
        internal Backdrop BackdropOf(ViewLayer layer)
        {
            Backdrop backdrop;
            if (layer != null && _backdrops.TryGetValue(layer, out backdrop))
                return backdrop;

            return null;
        }

        // Stops backdrop fades and drops pending loss reports; the objects go with the root.
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _events = null;
            _pendingLosses.Clear();

            foreach (var backdrop in _backdrops.Values)
            {
                if (backdrop != null)
                    backdrop.StopFade();
            }
        }

        // Removes what the constructor built under the root; for a root whose initialisation failed after the stage was built.
        internal void DestroyBuiltObjects()
        {
            foreach (var rect in _layerRects.Values)
                DestroyGameObjectOf(rect);

            DestroyGameObjectOf(_staging);
            DestroyGameObjectOf(_shieldCanvas);
        }

        private static void DestroyGameObjectOf(Component component)
        {
            if (component != null)
                UnityEngine.Object.Destroy(component.gameObject);
        }

        // IViewStage

        public void SetEvents(IStageEvents events)
        {
            _events = events;
        }

        public async UniTask<object> AcquireAsync(ViewEntry row, CancellationToken ct)
        {
            ThrowIfDisposed();
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            CheckCamera();

            GameObject gameObject;
            IViewProvider provider = null;

            if (row.Prefab != null)
            {
                ct.ThrowIfCancellationRequested();
                gameObject = _direct.Acquire(row.Prefab, _staging);
            }
            else
            {
                if (_assetProvider == null)
                {
                    _diagnostics.Error(
                        DiagnosticIds.NoProviderForAssetKey,
                        "The view '" + row.Id + "' has the asset key '" + row.AssetKey + "' but the root has no view provider.");
                    throw new InvalidOperationException("No view provider for the asset key of view '" + row.Id + "'.");
                }

                provider = _assetProvider;
                gameObject = await provider.AcquireAsync(row.AssetKey, _staging, ct);

                // The caller stopped waiting; the provider contract makes the stage release a late result.
                if (gameObject != null && (ct.IsCancellationRequested || _disposed))
                {
                    ReleaseQuietly(provider, gameObject, row.Id);
                    ct.ThrowIfCancellationRequested();
                    throw new ObjectDisposedException(nameof(UguiStage));
                }
            }

            // The logic treats null as a failed load.
            if (gameObject == null)
                return null;

            var host = gameObject.GetComponent<ViewHost>();
            var instance = new StageInstance(gameObject, host, provider);
            if (host != null)
                host.Attach(this, instance);

            return instance;
        }

        public ViewResolution ResolveView<TArgs, TResult>(object instance, out ViewBinding<TArgs, TResult> binding)
        {
            binding = default;
            var stage = Live(instance);

            if (stage.Host == null)
                return ViewResolution.MissingHost;

            IView<TArgs, TResult> view;
            if (!stage.GameObject.TryGetComponent(out view) || view == null)
                return ViewResolution.MissingView;

            // Hooks are interfaces on the view component itself, not separate components.
            binding = new ViewBinding<TArgs, TResult>(view, view as IViewVisible, view as IViewBack);
            return ViewResolution.Resolved;
        }

        public void Inject(object instance)
        {
            // A destroyed instance is skipped here and fails at ResolveView instead.
            var stage = LiveOrDestroyed(instance);
            if (stage == null || _inject == null)
                return;

            _inject(stage.GameObject);
        }

        public void Place(object instance, ViewLayer layer, ViewHandle handle)
        {
            var stage = Live(instance);
            var host = RequireHost(stage);
            var layerRect = LayerRect(layer);
            if (layerRect == null)
                throw new InvalidOperationException("The stage has no layer '" + (layer == null ? "null" : layer.Id) + "'.");

            var gameObject = stage.GameObject;
            host.BeginPackageChange();
            try
            {
                // Inactive under staging, so no message runs; reparenting an active-self object would wake it before Link.
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);

                host.PrepareForPlace();
                gameObject.transform.SetParent(layerRect, false);
                host.Link(handle);
                gameObject.SetActive(true);
            }
            finally
            {
                host.EndPackageChange();
            }
        }

        public void Relink(object instance, ViewHandle handle)
        {
            var stage = LiveOrDestroyed(instance);
            if (stage == null)
                return;

            RequireHost(stage).Link(handle);
        }

        public void Apply(object instance, in ViewVisualState state)
        {
            // A destroyed view has nothing to show; its loss report ends the request on the next frame.
            var stage = LiveOrDestroyed(instance);
            if (stage == null)
                return;

            RequireHost(stage).Apply(in state, _rootCanvas.sortingLayerID);
        }

        public void Snap(object instance, bool shown)
        {
            var stage = instance as StageInstance;
            if (stage == null || stage.Released || stage.Host == null)
                return;

            var transition = stage.Host.Transition;
            if (transition != null)
                transition.Snap(shown);
        }

        public UniTask PlayAsync(object instance, bool show, CancellationToken ct)
        {
            var stage = instance as StageInstance;
            if (stage == null || stage.Released || stage.Host == null)
                return UniTask.CompletedTask;

            var transition = stage.Host.Transition;
            if (transition == null)
                return UniTask.CompletedTask;

            return transition.PlayAsync(show, ct);
        }

        public void ApplyBackdrop(ViewLayer layer, bool active, int sortingOrder)
        {
            if (_disposed)
                return;

            var backdrop = BackdropOf(layer);
            if (backdrop == null)
                return;

            // The project owns the root's sorting layer and may change it, so it is read at every placement.
            if (active)
                backdrop.Show(sortingOrder, _rootCanvas.sortingLayerID);
            else
                backdrop.Hide();
        }

        public void SetShield(bool blocking, int sortingOrder)
        {
            if (_disposed || _shield == null || _shieldCanvas == null)
                return;

            var raised = blocking && !_shield.raycastTarget;
            _shieldCanvas.sortingLayerID = _rootCanvas.sortingLayerID;
            _shieldCanvas.sortingOrder = sortingOrder;
            _shield.raycastTarget = blocking;

            // Submit and Cancel go to the selection and pass the shield, so a raise clears it.
            if (raised)
            {
                var eventSystem = EventSystem.current;
                if (eventSystem != null && !eventSystem.alreadySelecting)
                    eventSystem.SetSelectedGameObject(null);
            }
        }

        public void SetLoadIndicator(bool visible)
        {
            if (_loadIndicator == null)
                return;

            if (_loadIndicator.activeSelf != visible)
                _loadIndicator.SetActive(visible);

            if (visible)
                SortLoadIndicator();
        }

        // Views sort with override canvases in the band, so the indicator needs its own canvas above the shield to be seen.
        private void SortLoadIndicator()
        {
            if (_disposed || _shieldCanvas == null)
                return;

            // Added once; never a raycaster, so the indicator takes no input.
            if (_indicatorCanvas == null)
                _indicatorCanvas = GetOrAdd<Canvas>(_loadIndicator);

            // Read at every show, as the band does at placement, since the project may change the root's sorting layer.
            _indicatorCanvas.overrideSorting = true;
            _indicatorCanvas.sortingLayerID = _rootCanvas.sortingLayerID;
            _indicatorCanvas.sortingOrder = IndicatorOrder(_shieldCanvas.sortingOrder);
        }

        // One above the shield, kept below the sorting ceiling because Canvas.sortingOrder wraps silently past 16 bits.
        internal static int IndicatorOrder(int shieldOrder)
        {
            var order = (long)shieldOrder + 1;
            if (order > Projection.SortingCeiling - 1)
                return Projection.SortingCeiling - 1;
            if (order < Projection.SortingFloor)
                return Projection.SortingFloor;

            return (int)order;
        }

        public void Stash(object instance)
        {
            var stage = Live(instance);
            var host = RequireHost(stage);

            host.BeginPackageChange();
            try
            {
                host.PrepareForStash();
                stage.GameObject.transform.SetParent(_staging, false);
            }
            finally
            {
                host.EndPackageChange();
            }
        }

        public void Release(object instance)
        {
            var stage = instance as StageInstance;
            if (stage == null || stage.Released)
                return;

            // Marked first, so the instance counts as released even when the provider throws.
            stage.Released = true;

            // Reference check: a destroyed host still needs its sink cleared.
            if (!ReferenceEquals(stage.Host, null))
                stage.Host.Detach();

            _pendingLosses.Remove(stage);

            // Hidden before the provider runs, so a provider that throws leaves no drawn or hittable object; the sink is already cleared.
            var gameObject = stage.GameObject;
            if (gameObject != null && gameObject.activeSelf)
                gameObject.SetActive(false);

            if (stage.Provider != null)
                stage.Provider.Release(gameObject);
            else
                _direct.Release(gameObject);
        }

        public UniTask<IDisposable> PreloadAsync(ViewEntry row, CancellationToken ct)
        {
            ThrowIfDisposed();
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            // A direct prefab is resident while its catalog is loaded.
            if (row.Prefab != null)
                return UniTask.FromResult<IDisposable>(NoOpLease.Instance);

            if (_assetProvider == null)
            {
                _diagnostics.Error(
                    DiagnosticIds.NoProviderForAssetKey,
                    "The view '" + row.Id + "' has the asset key '" + row.AssetKey + "' but the root has no view provider to preload it.");
                return UniTask.FromResult<IDisposable>(null);
            }

            return _assetProvider.PreloadAsync(row.AssetKey, ct);
        }

        public bool TryGetGameObject(object instance, out GameObject gameObject)
        {
            var stage = instance as StageInstance;
            if (stage != null && stage.GameObject != null)
            {
                gameObject = stage.GameObject;
                return true;
            }

            gameObject = null;
            return false;
        }

        // IViewHostSink

        // Runs inside OnDisable or OnDestroy, so it only records; the flush acts on the next frame, outside the engine callback.
        public void OnHostLost(object instance, bool destroyed)
        {
            var stage = instance as StageInstance;
            if (_disposed || stage == null || stage.Released || _pendingLosses.Contains(stage))
                return;

            _pendingLosses.Add(stage);
            if (_flushScheduled)
                return;

            _flushScheduled = true;

            // Fire-and-forget: nobody awaits a loss report; the flush checks Released and _disposed itself.
            FlushLossesAsync().Forget();
        }

        private async UniTaskVoid FlushLossesAsync()
        {
            try
            {
                await UniTask.NextFrame();

                var batch = _pendingLosses;
                _pendingLosses = _flushingLosses;
                _flushingLosses = batch;
                _flushScheduled = false;

                try
                {
                    for (var i = 0; i < batch.Count; i++)
                    {
                        if (_disposed)
                            break;

                        var stage = batch[i];
                        if (stage.Released)
                            continue;

                        var gameObject = stage.GameObject;
                        var destroyed = gameObject == null;

                        // Active again (a reparent or a quick toggle by the project): nothing was lost.
                        if (!destroyed && gameObject.activeInHierarchy)
                            continue;

                        var events = _events;
                        if (events == null)
                            continue;

                        try
                        {
                            events.OnInstanceLost(stage, destroyed);
                        }
                        catch (Exception exception)
                        {
                            _diagnostics.ReportFault("instance lost", exception);
                        }
                    }
                }
                finally
                {
                    batch.Clear();
                }
            }
            catch (Exception exception)
            {
                _flushScheduled = false;
                _diagnostics.ReportFault("instance loss flush", exception);
            }
        }

        // Helpers

        private void OnBackdropTapped(ViewLayer layer)
        {
            var events = _events;
            if (_disposed || events == null)
                return;

            try
            {
                events.OnBackdropTapped(layer);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("backdrop tap", exception);
            }
        }

        // A root initialised in camera mode whose camera is gone; warns once per missing-camera span, not per open.
        private void CheckCamera()
        {
            if (!_cameraMode)
                return;

            if (_rootCanvas != null && _rootCanvas.worldCamera != null)
            {
                _cameraWarned = false;
                return;
            }

            if (_cameraWarned)
                return;

            _cameraWarned = true;
            _diagnostics.Warn(
                DiagnosticIds.CameraRootWithoutCamera,
                "The root canvas was initialised in Screen Space - Camera mode but has no camera now; views may not render.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UguiStage));
        }

        // A released or destroyed instance fails plainly.
        private StageInstance Live(object instance)
        {
            var stage = LiveOrDestroyed(instance);
            if (stage == null)
                throw new InvalidOperationException("The view object was destroyed.");

            return stage;
        }

        // Null when the GameObject is destroyed; throws for a foreign, released or post-dispose instance.
        private StageInstance LiveOrDestroyed(object instance)
        {
            ThrowIfDisposed();

            var stage = instance as StageInstance;
            if (stage == null)
                throw new ArgumentException("The instance does not belong to this stage.", nameof(instance));
            if (stage.Released)
                throw new InvalidOperationException("The view instance was already released.");

            return stage.GameObject == null ? null : stage;
        }

        private static ViewHost RequireHost(StageInstance stage)
        {
            var host = stage.Host;
            if (host == null)
                throw new InvalidOperationException("The view object '" + stage.GameObject.name + "' has no ViewHost on its root.");

            return host;
        }

        // Nobody waits for a late result any more, so its release fault is routed here.
        private void ReleaseQuietly(IViewProvider provider, GameObject gameObject, string viewId)
        {
            try
            {
                provider.Release(gameObject);
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.Provider, viewId, 0, null);
            }
        }

        private RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = _root.gameObject.layer;
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            return rect;
        }

        private Backdrop CreateBackdrop(ViewLayer layer, RectTransform layerRect, Color color, GameObject prefab)
        {
            RectTransform rect;
            if (prefab != null)
            {
                var instance = UnityEngine.Object.Instantiate(prefab, layerRect, false);
                instance.name = "Backdrop";
                if (!instance.activeSelf)
                    instance.SetActive(true);

                rect = instance.transform as RectTransform;
                if (rect == null)
                    rect = instance.AddComponent<RectTransform>();

                Stretch(rect);
            }
            else
            {
                rect = CreateRect("Backdrop", layerRect);
            }

            var gameObject = rect.gameObject;
            var canvas = GetOrAdd<Canvas>(gameObject);
            canvas.overrideSorting = true;
            canvas.sortingLayerID = _rootCanvas.sortingLayerID;
            GetOrAdd<GraphicRaycaster>(gameObject);
            GetOrAdd<CanvasGroup>(gameObject);

            if (prefab == null)
            {
                var image = gameObject.AddComponent<Image>();
                image.color = color;
            }

            var backdrop = GetOrAdd<Backdrop>(gameObject);
            backdrop.Initialize(layer, _backdropTapped);
            return backdrop;
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            T component;
            if (gameObject.TryGetComponent(out component))
                return component;

            return gameObject.AddComponent<T>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = Vector3.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        // The lease of a direct-prefab row: nothing to keep loaded.
        private sealed class NoOpLease : IDisposable
        {
            public static readonly NoOpLease Instance = new NoOpLease();

            public void Dispose()
            {
            }
        }
    }
}
