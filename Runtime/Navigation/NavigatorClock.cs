using System;
using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    // The logic's only clock: the owner of the navigator ticks it with a delta, so nothing reads UnityEngine.Time.
    internal sealed class NavigatorClock
    {
        // One tick advances at most this much, so a hitch or a suspension adds one step.
        public const float MaxStepSeconds = 0.1f;

        private const double Epsilon = 1e-6;
        internal const int MaxFiresPerTick = 4096;

        private readonly Diagnostics _diagnostics;
        private readonly List<TimerEntry> _timers = new List<TimerEntry>();
        private double _now;
        private int _nextTimerId = 1;

        public NavigatorClock(Diagnostics diagnostics)
        {
            _diagnostics = diagnostics;
        }

        // Seconds accumulated by Tick; excludes time the owner did not tick.
        public double Now
        {
            get { return _now; }
        }

        public bool HasTimers
        {
            get { return _timers.Count > 0; }
        }

        public int TimerCount
        {
            get { return _timers.Count; }
        }

        // Advances by min(delta, MaxStepSeconds) and runs every timer that became due, earliest first.
        public void Tick(float unscaledDeltaSeconds)
        {
            if (!(unscaledDeltaSeconds > 0f))
                return;

            var step = unscaledDeltaSeconds > MaxStepSeconds ? MaxStepSeconds : unscaledDeltaSeconds;
            _now += step;
            FireDue();
        }

        // Returns a timer id for Cancel; the callback runs once, on a later Tick, and a throwing callback is isolated.
        public int Schedule(double delaySeconds, Action<object> callback, object state)
        {
            var delay = delaySeconds > 0.0 ? delaySeconds : 0.0;
            var id = _nextTimerId++;
            _timers.Add(new TimerEntry(id, _now + delay, callback, state));
            return id;
        }

        // Cancels a pending timer; false when it already ran, was cancelled or the id is zero.
        public bool Cancel(int timerId)
        {
            if (timerId == 0)
                return false;

            for (var i = 0; i < _timers.Count; i++)
            {
                if (_timers[i].Id != timerId)
                    continue;

                _timers.RemoveAt(i);
                return true;
            }

            return false;
        }

        // Drops every pending timer without running it; used when the root is destroyed.
        public void Clear()
        {
            _timers.Clear();
        }

        private void FireDue()
        {
            for (var guard = 0; guard < MaxFiresPerTick; guard++)
            {
                var index = -1;
                for (var i = 0; i < _timers.Count; i++)
                {
                    var candidate = _timers[i];
                    if (candidate.Due > _now + Epsilon)
                        continue;

                    if (index < 0)
                    {
                        index = i;
                        continue;
                    }

                    var best = _timers[index];
                    if (candidate.Due < best.Due || (candidate.Due == best.Due && candidate.Id < best.Id))
                        index = i;
                }

                if (index < 0)
                    return;

                var timer = _timers[index];
                _timers.RemoveAt(index);

                try
                {
                    timer.Callback(timer.State);
                }
                catch (Exception exception)
                {
                    _diagnostics.ReportFault("timer callback", exception);
                }
            }
        }

        private readonly struct TimerEntry
        {
            public readonly int Id;
            public readonly double Due;
            public readonly Action<object> Callback;
            public readonly object State;

            public TimerEntry(int id, double due, Action<object> callback, object state)
            {
                Id = id;
                Due = due;
                Callback = callback;
                State = state;
            }
        }
    }
}
