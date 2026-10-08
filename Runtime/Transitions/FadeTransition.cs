using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>
    /// Built-in transition that fades the view's CanvasGroup alpha on unscaled time: shown is alpha 1, hidden is alpha 0.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class FadeTransition : ViewTransition
    {
        // Cached once; the component itself is the state, so a play allocates no closure.
        private static readonly Action<FadeTransition, float> StepAction = Step;

        [SerializeField, Min(0f), Tooltip("Seconds of unscaled time per play.")]
        private float _duration = 0.25f;

        [SerializeField, Tooltip("Easing curve when showing.")]
        private ViewEase _showEase = ViewEase.OutCubic;

        [SerializeField, Tooltip("Easing curve when hiding.")]
        private ViewEase _hideEase = ViewEase.InCubic;

        private CanvasGroup _group;
        private float _from;
        private float _to;
        private ViewEase _activeEase;

        /// <summary>Fades from the opposite end alpha to 1 when showing or 0 when hiding; returns early when ct is cancelled.</summary>
        public override UniTask PlayAsync(bool show, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || !TryGetGroup(out var group))
                return UniTask.CompletedTask;

            _from = show ? 0f : 1f;
            _to = show ? 1f : 0f;
            _activeEase = show ? _showEase : _hideEase;
            group.alpha = _from;
            return UnscaledAnimation.RunAsync(_duration, this, StepAction, ct);
        }

        /// <summary>Sets alpha to 1 when shown or 0 when hidden; synchronous, idempotent and never throws.</summary>
        public override void Snap(bool shown)
        {
            if (TryGetGroup(out var group))
                group.alpha = shown ? 1f : 0f;
        }

        private static void Step(FadeTransition self, float t)
        {
            // The group may be destroyed mid-play.
            var group = self._group;
            if (group == null)
                return;

            group.alpha = Mathf.Clamp01(Mathf.LerpUnclamped(self._from, self._to, Easing.Evaluate(self._activeEase, t)));
        }

        private bool TryGetGroup(out CanvasGroup group)
        {
            if (_group == null && this != null)
                _group = GetComponent<CanvasGroup>();

            group = _group;
            return group != null;
        }
    }
}
