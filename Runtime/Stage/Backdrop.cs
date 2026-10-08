using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ecanakli.UIDirector
{
    // The shared backdrop of one modal layer: fades on unscaled time and forwards taps to the stage.
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup))]
    internal sealed class Backdrop : MonoBehaviour, IPointerClickHandler
    {
        private const float FadeSeconds = 0.2f;

        // Cached once, so a fade frame allocates nothing.
        private static readonly Action<Backdrop, float> FadeStep = StepFade;

        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private CanvasGroup _group;
        private ViewLayer _layer;
        private Action<ViewLayer> _tapped;
        private CancellationTokenSource _fade;
        private float _fromAlpha;
        private float _toAlpha;

        // True while a fade runs; tests read it to tell a settled backdrop from a fading one.
        internal bool IsFading
        {
            get { return _fade != null; }
        }

        internal void Initialize(ViewLayer layer, Action<ViewLayer> tapped)
        {
            _layer = layer;
            _tapped = tapped;
            _canvas = GetComponent<Canvas>();
            _raycaster = GetComponent<GraphicRaycaster>();
            _group = GetComponent<CanvasGroup>();
            Snap(false);
        }

        // Canvas and raycaster on, order set at once (jumps between stacked views), fades in from the current alpha.
        internal void Show(int sortingOrder, int sortingLayerId)
        {
            _canvas.sortingLayerID = sortingLayerId;
            _canvas.sortingOrder = sortingOrder;
            _canvas.enabled = true;
            _raycaster.enabled = true;

            if (_fade == null && _group.alpha >= 1f)
                return;

            StartFade(1f);
        }

        // Raycasts stop now; the canvas turns off when the fade ends.
        internal void Hide()
        {
            _raycaster.enabled = false;

            if (_fade == null && !_canvas.enabled)
                return;

            StartFade(0f);
        }

        // Stops the fade and writes the end state.
        internal void Snap(bool shown)
        {
            StopFade();
            _group.alpha = shown ? 1f : 0f;
            _canvas.enabled = shown;
            _raycaster.enabled = shown;
        }

        // Stops the fade where it is; used when the stage is disposed.
        internal void StopFade()
        {
            if (_fade == null)
                return;

            var fade = _fade;
            _fade = null;
            fade.Cancel();
            fade.Dispose();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_tapped != null)
                _tapped(_layer);
        }

        private void OnDestroy()
        {
            StopFade();
        }

        private void StartFade(float target)
        {
            // The same fade is already running; restarting it would only slow it down.
            if (_fade != null && _toAlpha == target)
                return;

            StopFade();
            _fromAlpha = _group.alpha;
            _toAlpha = target;
            _fade = new CancellationTokenSource();

            // Fire-and-forget: the backdrop owns its fade, and the next Show, Hide, Snap or OnDestroy cancels it.
            RunFadeAsync(_fade).Forget();
        }

        private async UniTaskVoid RunFadeAsync(CancellationTokenSource fade)
        {
            await UnscaledAnimation.RunAsync(FadeSeconds, this, FadeStep, fade.Token);

            // A newer call replaced or stopped this fade.
            if (_fade != fade)
                return;

            _fade = null;
            fade.Dispose();

            if (_toAlpha <= 0f)
                _canvas.enabled = false;
        }

        private static void StepFade(Backdrop backdrop, float t)
        {
            backdrop._group.alpha = Mathf.LerpUnclamped(backdrop._fromAlpha, backdrop._toAlpha, t);
        }
    }
}
