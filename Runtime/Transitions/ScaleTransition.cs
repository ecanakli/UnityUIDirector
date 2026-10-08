using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>
    /// Built-in transition that scales a target RectTransform on unscaled time, with an optional fade:
    /// shown is scale 1, hidden is the serialized start scale.
    /// </summary>
    public sealed class ScaleTransition : ViewTransition
    {
        // Cached once; the component itself is the state, so a play allocates no closure.
        private static readonly Action<ScaleTransition, float> StepAction = Step;

        [SerializeField, Tooltip("The RectTransform to scale; empty means this object's own RectTransform.")]
        private RectTransform _target;

        [SerializeField, Tooltip("Uniform scale of the hidden pose.")]
        private float _hiddenScale = 0.85f;

        [SerializeField, Tooltip("Also fades a CanvasGroup on this object, added when missing.")]
        private bool _fade;

        [SerializeField, Min(0f), Tooltip("Seconds of unscaled time per play.")]
        private float _duration = 0.25f;

        [SerializeField, Tooltip("Easing curve when showing.")]
        private ViewEase _showEase = ViewEase.OutBack;

        [SerializeField, Tooltip("Easing curve when hiding.")]
        private ViewEase _hideEase = ViewEase.InCubic;

        private RectTransform _resolvedTarget;
        private bool _targetResolved;
        private CanvasGroup _group;
        private bool _show;
        private ViewEase _activeEase;

        /// <summary>Scales (and fades, when enabled) from the opposite end pose to the shown or hidden pose; returns early when ct is cancelled.</summary>
        public override UniTask PlayAsync(bool show, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || this == null)
                return UniTask.CompletedTask;

            _show = show;
            _activeEase = show ? _showEase : _hideEase;
            WritePose(!show);
            return UnscaledAnimation.RunAsync(_duration, this, StepAction, ct);
        }

        /// <summary>Writes the shown or hidden pose; synchronous, idempotent and never throws.</summary>
        public override void Snap(bool shown)
        {
            if (this == null)
                return;

            // Snap never throws; adding the fade group can fail on an object that is being destroyed.
            try
            {
                WritePose(shown);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private static void Step(ScaleTransition self, float t)
        {
            var eased = Easing.Evaluate(self._activeEase, t);
            var progress = self._show ? eased : 1f - eased;
            self.Apply(progress);
        }

        private void WritePose(bool shown)
        {
            Apply(shown ? 1f : 0f);
        }

        // progress 0 is the hidden pose, 1 the shown pose; values outside allow overshoot.
        private void Apply(float progress)
        {
            var target = ResolveTarget();
            if (target != null)
            {
                var scale = Mathf.LerpUnclamped(_hiddenScale, 1f, progress);
                target.localScale = new Vector3(scale, scale, 1f);
            }

            if (_fade)
            {
                var group = ResolveGroup();
                if (group != null)
                    group.alpha = Mathf.Clamp01(progress);
            }
        }

        private RectTransform ResolveTarget()
        {
            // Resolved once, so a destroyed assigned target is skipped rather than replaced by this object.
            if (!_targetResolved && this != null)
            {
                _resolvedTarget = _target != null ? _target : transform as RectTransform;
                _targetResolved = true;
            }

            return _resolvedTarget;
        }

        private CanvasGroup ResolveGroup()
        {
            if (_group == null && this != null)
            {
                _group = GetComponent<CanvasGroup>();
                if (_group == null)
                    _group = gameObject.AddComponent<CanvasGroup>();
            }

            return _group;
        }
    }
}
