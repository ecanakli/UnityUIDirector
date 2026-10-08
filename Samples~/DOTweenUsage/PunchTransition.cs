using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using UnityEngine;

namespace Ecanakli.UIDirector.Samples.DOTweenUsage
{
    /// <summary>A custom transition on DOTween: the view fades in with a scale punch and fades out shrinking; the example for writing your own.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class PunchTransition : ViewTransition
    {
        private const float HiddenScale = 0.9f;
        private const float PunchStrength = 0.12f;
        private const int PunchVibrato = 4;
        private const float PunchElasticity = 0.5f;

        [SerializeField, Min(0f)] private float _duration = 0.35f;

        private CanvasGroup _group;
        private DOGetter<float> _getAlpha;
        private DOSetter<float> _setAlpha;

        // The only handle to the running sequence; no SetLink, so a kill happens only here.
        private Sequence _running;

        public override UniTask PlayAsync(bool show, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || !Prepare())
                return UniTask.CompletedTask;

            KillRunning();
            if (_duration <= 0f)
            {
                Snap(show);
                return UniTask.CompletedTask;
            }

            // A punch returns to the scale it starts from, so a show starts at the shown scale.
            Write(show ? 0f : 1f, 1f);

            var sequence = DOTween.Sequence();
            if (show)
            {
                sequence.Join(DOTween.To(_getAlpha, _setAlpha, 1f, _duration).SetEase(Ease.OutQuad));
                sequence.Join(transform.DOPunchScale(new Vector3(PunchStrength, PunchStrength, 0f), _duration, PunchVibrato, PunchElasticity).SetEase(Ease.OutQuad));
            }
            else
            {
                sequence.Join(DOTween.To(_getAlpha, _setAlpha, 0f, _duration).SetEase(Ease.InQuad));
                sequence.Join(transform.DOScale(HiddenScale, _duration).SetEase(Ease.InQuad));
            }

            // Explicit, so project-wide DOTween defaults cannot change the ease or the lifetime of this tween.
            // Unscaled time, so a paused game still shows and hides the view.
            sequence.SetEase(Ease.Linear).SetAutoKill(true).SetUpdate(true).SetRecyclable(false).Play();
            _running = sequence;
            return WaitAsync(sequence, ct);
        }

        public override void Snap(bool shown)
        {
            KillRunning();

            // Never throws: a destroyed view is skipped.
            if (Prepare())
                Write(shown ? 1f : 0f, shown ? 1f : HiddenScale);
        }

        private void OnDestroy()
        {
            KillRunning();
        }

        // Polls on the player loop instead of resuming from OnKill or OnComplete: a kill from OnDestroy
        // would then resume the runner inside Unity's destroy message.
        private static async UniTask WaitAsync(Sequence sequence, CancellationToken ct)
        {
            while (sequence.IsActive() && sequence.IsPlaying())
            {
                var cancelled = await UniTask.Yield(ct).SuppressCancellationThrow();
                if (!cancelled)
                    continue;

                if (sequence.IsActive())
                    sequence.Kill();

                return;
            }

            // Paused from outside, the sequence would never end; finish it at its end pose.
            if (sequence.IsActive())
                sequence.Kill(true);
        }

        private void KillRunning()
        {
            // Dropped before the kill; the sequence is not recyclable, so a stale reference is inert.
            var running = _running;
            _running = null;
            if (running.IsActive())
                running.Kill();
        }

        private void Write(float alpha, float scale)
        {
            _group.alpha = alpha;
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        private float GetAlpha()
        {
            return _group.alpha;
        }

        private void SetAlpha(float value)
        {
            _group.alpha = value;
        }

        // False on a destroyed component, which cannot reach its group.
        private bool Prepare()
        {
            if (this == null)
                return false;

            if (_group == null)
                _group = GetComponent<CanvasGroup>();

            // Created once per component, so a play allocates no getter or setter.
            _getAlpha ??= GetAlpha;
            _setAlpha ??= SetAlpha;
            return _group != null;
        }
    }
}
