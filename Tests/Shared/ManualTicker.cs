using System;
using System.Collections.Generic;

namespace Ecanakli.UIDirector.Tests
{
    // A queue scheduler the test drives: Tick runs what waits for the next tick, Advance moves time and fires due timers.
    internal sealed class ManualTicker : IQueueScheduler
    {
        private struct Pending
        {
            public Action<object> Callback;
            public object State;
        }

        private sealed class Timer
        {
            public int Id;
            public double Due;
            public Action<object> Callback;
            public object State;
        }

        private readonly List<Pending> _nextTick = new List<Pending>();
        private readonly List<Pending> _running = new List<Pending>();
        private readonly List<Timer> _timers = new List<Timer>();
        private int _nextTimerId = 1;

        public double Now { get; private set; }

        // Callbacks waiting for the next Tick.
        public int PendingTicks
        {
            get { return _nextTick.Count; }
        }

        public int PendingTimers
        {
            get { return _timers.Count; }
        }

        // How many times Tick ran at least one callback.
        public int TicksRun { get; private set; }

        public void NextTick(Action<object> callback, object state)
        {
            if (callback != null)
                _nextTick.Add(new Pending { Callback = callback, State = state });
        }

        public int After(double seconds, Action<object> callback, object state)
        {
            var id = _nextTimerId++;
            if (callback != null)
                _timers.Add(new Timer { Id = id, Due = Now + Math.Max(0.0, seconds), Callback = callback, State = state });

            return id;
        }

        public void Cancel(int timerId)
        {
            for (var i = 0; i < _timers.Count; i++)
            {
                if (_timers[i].Id != timerId)
                    continue;

                _timers.RemoveAt(i);
                return;
            }
        }

        // Runs every callback scheduled before this call; callbacks scheduled while it runs wait for the next Tick.
        public void Tick()
        {
            if (_nextTick.Count == 0)
                return;

            TicksRun++;
            _running.Clear();
            _running.AddRange(_nextTick);
            _nextTick.Clear();

            for (var i = 0; i < _running.Count; i++)
                _running[i].Callback(_running[i].State);

            _running.Clear();
        }

        // Ticks until nothing waits for the next tick, at most the given number of times.
        public void TickUntilIdle(int maxTicks = 100)
        {
            for (var i = 0; i < maxTicks && _nextTick.Count > 0; i++)
                Tick();
        }

        // Moves time forward and fires the timers that became due, earliest first.
        public void Advance(double seconds)
        {
            Now += Math.Max(0.0, seconds);

            while (true)
            {
                Timer due = null;
                for (var i = 0; i < _timers.Count; i++)
                {
                    if (_timers[i].Due <= Now + 1e-9 && (due == null || _timers[i].Due < due.Due))
                        due = _timers[i];
                }

                if (due == null)
                    return;

                _timers.Remove(due);
                due.Callback(due.State);
            }
        }
    }
}
