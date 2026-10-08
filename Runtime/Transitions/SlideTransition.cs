using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>The layer edge a <see cref="SlideTransition"/> slides in from and out to.</summary>
    public enum SlideEdge
    {
        /// <summary>Hidden one layer width to the left.</summary>
        Left,

        /// <summary>Hidden one layer width to the right.</summary>
        Right,

        /// <summary>Hidden one layer height above.</summary>
        Top,

        /// <summary>Hidden one layer height below.</summary>
        Bottom
    }

    /// <summary>
    /// Built-in transition that offsets a target's anchoredPosition by the layer rect's size in canvas units on unscaled time:
    /// shown is the authored position, hidden is that position moved past the serialized edge.
    /// </summary>
    public sealed class SlideTransition : ViewTransition
    {
        // Cached once; the component itself is the state, so a play allocates no closure.
        private static readonly Action<SlideTransition, float> StepAction = Step;

        [SerializeField, Tooltip("The RectTransform to move; empty means this object's own RectTransform.")]
        private RectTransform _target;

        [SerializeField, Tooltip("The layer edge the view slides in from and out to.")]
        private SlideEdge _edge = SlideEdge.Bottom;

        [SerializeField, Min(0f), Tooltip("Seconds of unscaled time per play.")]
        private float _duration = 0.25f;

        [SerializeField, Tooltip("Easing curve when showing.")]
        private ViewEase _showEase = ViewEase.OutCubic;

        [SerializeField, Tooltip("Easing curve when hiding.")]
        private ViewEase _hideEase = ViewEase.InCubic;

        private RectTransform _resolvedTarget;
        private bool _targetResolved;
        private Vector2 _shownPosition;
        private bool _shownCaptured;
        private Vector2 _from;
        private Vector2 _to;
        private ViewEase _activeEase;

        /// <summary>Slides from the opposite end pose to the shown or hidden pose; returns early when ct is cancelled.</summary>
        public override UniTask PlayAsync(bool show, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return UniTask.CompletedTask;

            var target = ResolveTarget();
            if (target == null)
                return UniTask.CompletedTask;

            CaptureShownPosition(target);
            var hidden = _shownPosition + HiddenOffset(target);
            _from = show ? hidden : _shownPosition;
            _to = show ? _shownPosition : hidden;
            _activeEase = show ? _showEase : _hideEase;
            target.anchoredPosition = _from;
            return UnscaledAnimation.RunAsync(_duration, this, StepAction, ct);
        }

        /// <summary>Writes the authored position when shown, or that position moved past the edge when hidden; synchronous, idempotent and never throws.</summary>
        public override void Snap(bool shown)
        {
            var target = ResolveTarget();
            if (target == null)
                return;

            CaptureShownPosition(target);
            target.anchoredPosition = shown ? _shownPosition : _shownPosition + HiddenOffset(target);
        }

        private static void Step(SlideTransition self, float t)
        {
            // The target may be destroyed mid-play.
            var target = self._resolvedTarget;
            if (target == null)
                return;

            target.anchoredPosition = Vector2.LerpUnclamped(self._from, self._to, Easing.Evaluate(self._activeEase, t));
        }

        private void CaptureShownPosition(RectTransform target)
        {
            if (_shownCaptured)
                return;

            _shownPosition = target.anchoredPosition;
            _shownCaptured = true;
        }

        // Measured at play time from the view's parent (the layer rect), so a resized layer is honoured; never Screen size.
        private Vector2 HiddenOffset(RectTransform target)
        {
            var layer = transform.parent as RectTransform;
            if (layer == null)
                layer = target.parent as RectTransform;

            var size = layer != null ? layer.rect.size : target.rect.size;
            switch (_edge)
            {
                case SlideEdge.Left:
                    return new Vector2(-size.x, 0f);
                case SlideEdge.Right:
                    return new Vector2(size.x, 0f);
                case SlideEdge.Top:
                    return new Vector2(0f, size.y);
                default:
                    return new Vector2(0f, -size.y);
            }
        }

        private RectTransform ResolveTarget()
        {
            if (this == null)
                return null;

            // Resolved once, so a destroyed assigned target is skipped rather than replaced by this object.
            if (!_targetResolved)
            {
                _resolvedTarget = _target != null ? _target : transform as RectTransform;
                _targetResolved = true;
            }

            return _resolvedTarget;
        }
    }
}
