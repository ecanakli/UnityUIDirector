using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;
using UnityEngine;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>A custom transition that fades and scales the view on unscaled time; the example for writing your own.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class PopTransition : ViewTransition
    {
        // One frame advances at most this much: a hitch cannot skip the animation, and below 30 fps it runs slower.
        private const float MaxStepSeconds = 1f / 30f;

        [SerializeField, Min(0f)] private float _duration = 0.2f;
        [SerializeField] private float _hiddenScale = 0.9f;

        private CanvasGroup _group;

        // Snap and a newer play bump this, which ends a play that is still running.
        private int _generation;

        public override async UniTask PlayAsync(bool show, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || !TryGetGroup(out var group))
                return;

            var target = transform;
            var fromAlpha = group.alpha;
            var fromScale = target.localScale.x;
            var toAlpha = show ? 1f : 0f;
            var toScale = show ? 1f : _hiddenScale;
            var generation = ++_generation;
            var elapsed = 0f;

            while (elapsed < _duration)
            {
                await UniTask.NextFrame(PlayerLoopTiming.Update);

                // Stop on cancel, on Snap, and when the view was destroyed mid-play.
                if (ct.IsCancellationRequested || generation != _generation || group == null || target == null)
                    return;

                // Unscaled, so a paused game still shows and hides views.
                elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxStepSeconds);
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _duration));
                Apply(group, target, Mathf.Lerp(fromAlpha, toAlpha, t), Mathf.Lerp(fromScale, toScale, t));
            }

            // The last write is exact, whatever the frame steps added up to.
            Apply(group, target, toAlpha, toScale);
        }

        public override void Snap(bool shown)
        {
            // Never throws: a destroyed view is skipped.
            if (!TryGetGroup(out var group))
                return;

            _generation++;
            Apply(group, transform, shown ? 1f : 0f, shown ? 1f : _hiddenScale);
        }

        private static void Apply(CanvasGroup group, Transform target, float alpha, float scale)
        {
            if (group == null || target == null)
                return;

            group.alpha = alpha;
            target.localScale = new Vector3(scale, scale, 1f);
        }

        private bool TryGetGroup(out CanvasGroup group)
        {
            // A destroyed component cannot reach its transform, so it counts as missing.
            if (this == null)
            {
                group = null;
                return false;
            }

            if (_group == null)
                _group = GetComponent<CanvasGroup>();

            group = _group;
            return group != null;
        }
    }
}
