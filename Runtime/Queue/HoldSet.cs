using System;
using System.Collections.Generic;
using System.Text;

namespace Ecanakli.UIDirector
{
    // The queue's counted holds: while any exists the queue presents nothing; a long one warns once.
    internal sealed class HoldSet
    {
        public const double HoldWarnSeconds = 60.0;

        private static readonly Action<object> LongHoldCallback = OnLongHold;

        private readonly IQueueScheduler _scheduler;
        private readonly Diagnostics _diagnostics;
        private readonly Action _changed;
        private readonly List<Holder> _holders = new List<Holder>();
        private int _nextHolderId = 1;

        // changed is called after the count drops; it may be null.
        public HoldSet(IQueueScheduler scheduler, Diagnostics diagnostics, Action changed)
        {
            _scheduler = scheduler;
            _diagnostics = diagnostics;
            _changed = changed;
        }

        public int Count
        {
            get { return _holders.Count; }
        }

        // Takes one hold and returns its id for Remove.
        public int Add(string reason, string callSite)
        {
            var id = _nextHolderId++;
            if (_nextHolderId <= 0)
                _nextHolderId = 1;

            var holder = new Holder(this, id, reason, callSite, _scheduler.Now);
            _holders.Add(holder);
            holder.TimerId = _scheduler.After(HoldWarnSeconds, LongHoldCallback, holder);
            return id;
        }

        // Returns a hold; an unknown or zero id does nothing.
        public void Remove(int holderId)
        {
            if (holderId == 0)
                return;

            for (var i = 0; i < _holders.Count; i++)
            {
                var holder = _holders[i];
                if (holder.Id != holderId)
                    continue;

                _holders.RemoveAt(i);
                CancelTimer(holder);
                NotifyChanged();
                return;
            }
        }

        // Drops every hold at once; changed runs once if any were removed.
        public void RemoveAll()
        {
            if (_holders.Count == 0)
                return;

            for (var i = 0; i < _holders.Count; i++)
                CancelTimer(_holders[i]);

            _holders.Clear();
            NotifyChanged();
        }

        public void Describe(StringBuilder output)
        {
            var now = _scheduler.Now;
            for (var i = 0; i < _holders.Count; i++)
            {
                var holder = _holders[i];
                output.Append("  hold #").Append(holder.Id);
                if (holder.Reason != null)
                    output.Append(" '").Append(holder.Reason).Append('\'');
                output.Append(" age ").Append((now - holder.AddedAt).ToString("0.0")).Append("s");
                if (holder.CallSite != null)
                    output.Append(" at ").Append(holder.CallSite);
                output.Append('\n');
            }
        }

        // Appends every hold in add order; ages are measured from now.
        internal void CaptureHolds(List<QueueHoldSnapshot> into, double now)
        {
            for (var i = 0; i < _holders.Count; i++)
            {
                var holder = _holders[i];
                into.Add(new QueueHoldSnapshot
                {
                    Reason = holder.Reason,
                    AgeSeconds = now - holder.AddedAt,
                    CallSite = holder.CallSite
                });
            }
        }

        private static void OnLongHold(object state)
        {
            var holder = (Holder)state;
            holder.TimerId = 0;

            var set = holder.Set;
            var age = set._scheduler.Now - holder.AddedAt;
            set._diagnostics.Warn(
                DiagnosticIds.QueueHoldTooLong,
                "The queue hold '" + holder.Reason + "' is " + age.ToString("0.0") + " s old (over 60 s). Registered at " + (holder.CallSite ?? "an unknown call site") + ".");
        }

        private void CancelTimer(Holder holder)
        {
            if (holder.TimerId == 0)
                return;

            _scheduler.Cancel(holder.TimerId);
            holder.TimerId = 0;
        }

        private void NotifyChanged()
        {
            if (_changed == null)
                return;

            try
            {
                _changed();
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("queue hold change", exception);
            }
        }

        private sealed class Holder
        {
            public readonly HoldSet Set;
            public readonly int Id;
            public readonly string Reason;
            public readonly string CallSite;
            public readonly double AddedAt;
            public int TimerId;

            public Holder(HoldSet set, int id, string reason, string callSite, double addedAt)
            {
                Set = set;
                Id = id;
                Reason = reason;
                CallSite = callSite;
                AddedAt = addedAt;
            }
        }
    }
}
