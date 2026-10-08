using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    // The queue's only source of ticks and time, so tests can drive it without the player loop.
    internal interface IQueueScheduler
    {
        // Seconds, unscaled.
        double Now { get; }

        // Runs the callback once on the next Update tick, never synchronously.
        void NextTick(Action<object> callback, object state);

        // Runs the callback once after the delay; returns a timer id for Cancel; 0 when nothing was scheduled.
        int After(double seconds, Action<object> callback, object state);

        // Cancels a pending timer; an unknown, finished or zero id does nothing.
        void Cancel(int timerId);
    }

    // Player-loop scheduler on UniTask; ticks and delay timers end with the lifetime it is given.
    // With a navigator clock, timers run on that clock and the caller cancels them; the queue does so when it ends.
    internal sealed class PlayerLoopQueueScheduler : IQueueScheduler
    {
        private readonly CancellationToken _lifetime;
        private readonly Diagnostics _diagnostics;
        private readonly NavigatorClock _clock;
        private readonly Dictionary<int, CancellationTokenSource> _timers = new Dictionary<int, CancellationTokenSource>();
        private int _nextTimerId = 1;

        public PlayerLoopQueueScheduler(CancellationToken lifetime, Diagnostics diagnostics, NavigatorClock clock = null)
        {
            _lifetime = lifetime;
            _diagnostics = diagnostics;
            _clock = clock;
        }

        public double Now
        {
            get { return Time.unscaledTimeAsDouble; }
        }

        public void NextTick(Action<object> callback, object state)
        {
            if (callback == null || _lifetime.IsCancellationRequested)
                return;

            // Not awaited: the caller only needs the callback to run on a later tick, and it isolates its own faults.
            RunNextTick(callback, state).Forget();
        }

        public int After(double seconds, Action<object> callback, object state)
        {
            // The clock is shared with the navigator's timers, so this path never invents an id.
            if (_clock != null)
            {
                if (callback == null || _lifetime.IsCancellationRequested)
                    return 0;

                return _clock.Schedule(seconds, callback, state);
            }

            var id = _nextTimerId++;
            if (_nextTimerId <= 0)
                _nextTimerId = 1;

            if (callback == null || _lifetime.IsCancellationRequested)
                return id;

            // Linked to the lifetime, so the lifetime ending cancels every pending timer.
            var source = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
            _timers[id] = source;

            // Not awaited: Cancel and the lifetime end it through its source, and it isolates its own faults.
            RunAfter(id, seconds, source, callback, state).Forget();
            return id;
        }

        public void Cancel(int timerId)
        {
            if (timerId == 0)
                return;

            if (_clock != null)
            {
                _clock.Cancel(timerId);
                return;
            }

            if (!_timers.TryGetValue(timerId, out var source))
                return;

            // The running timer disposes the source once its delay ends.
            _timers.Remove(timerId);
            source.Cancel();
        }

        private async UniTaskVoid RunNextTick(Action<object> callback, object state)
        {
            await UniTask.Yield(PlayerLoopTiming.Update);

            if (_lifetime.IsCancellationRequested)
                return;

            Invoke(callback, state, "queue tick");
        }

        private async UniTaskVoid RunAfter(int id, double seconds, CancellationTokenSource source, Action<object> callback, object state)
        {
            var delay = seconds > 0.0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
            var cancelled = await UniTask.Delay(delay, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, source.Token)
                .SuppressCancellationThrow();

            if (_timers.TryGetValue(id, out var current) && current == source)
                _timers.Remove(id);

            source.Dispose();

            if (cancelled || _lifetime.IsCancellationRequested)
                return;

            Invoke(callback, state, "queue timer");
        }

        private void Invoke(Action<object> callback, object state, string location)
        {
            try
            {
                callback(state);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault(location, exception);
            }
        }
    }
}
