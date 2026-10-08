using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;

namespace Ecanakli.UIDirector
{
    /// <summary>
    /// Bridges a tween's kill callback to a UniTask; the only place a tween meets UniTask.
    /// The await ends when the tween is killed (a completed auto-kill tween kills itself) or when ct is cancelled; it never throws.
    /// </summary>
    internal static class TweenAwaiter
    {
        /// <summary>
        /// Takes over the tween's OnKill callback and completes when it runs; cancelling ct kills the tween with Kill(false) and ends the await at once.
        /// onKilled receives the killed tween before the await completes; that reference is valid only inside the callback.
        /// </summary>
        internal static UniTask AwaitKillAsync(Tween tween, Action<Tween> onKilled, CancellationToken ct)
        {
            if (tween == null)
                return UniTask.CompletedTask;

            // Already killed: report it now, since the kill callback will never reach this caller.
            if (!tween.IsActive())
            {
                if (onKilled != null)
                    onKilled(tween);

                return UniTask.CompletedTask;
            }

            var source = new KillSource(tween, onKilled);
            tween.OnKill(source.HandleKill);
            if (ct.CanBeCanceled)
                source.Register(ct);

            return source.Task;
        }

        private sealed class KillSource
        {
            // Cached once; the source is passed as state, so a cancellation allocates no closure.
            private static readonly Action<object> CancelAction = Cancel;

            private readonly UniTaskCompletionSource _completion = new UniTaskCompletionSource();
            private readonly Action<Tween> _onKilled;
            private Tween _tween;
            private CancellationTokenRegistration _registration;
            private bool _finished;

            public KillSource(Tween tween, Action<Tween> onKilled)
            {
                _tween = tween;
                _onKilled = onKilled;
            }

            public UniTask Task
            {
                get { return _completion.Task; }
            }

            public void Register(CancellationToken ct)
            {
                var registration = ct.RegisterWithoutCaptureExecutionContext(CancelAction, this);

                // An already cancelled token runs the callback inside Register, so the await may be finished here.
                if (_finished)
                    registration.Dispose();
                else
                    _registration = registration;
            }

            // DOTween calls this once, synchronously from Kill or at the end of the update that killed the tween.
            public void HandleKill()
            {
                // Dropped first, so a recycled tween is never touched afterwards.
                var killed = _tween;
                _tween = null;
                try
                {
                    if (killed != null && _onKilled != null)
                        _onKilled(killed);
                }
                finally
                {
                    Finish();
                }
            }

            private static void Cancel(object state)
            {
                var source = (KillSource)state;

                // A kill requested inside DOTween's update loop is deferred, so the await is ended here rather than in HandleKill.
                var tween = source._tween;
                if (tween != null && tween.IsActive())
                    tween.Kill(false);

                source.Finish();
            }

            private void Finish()
            {
                if (_finished)
                    return;

                _finished = true;
                _registration.Dispose();
                _registration = default;
                _completion.TrySetResult();
            }
        }
    }
}
