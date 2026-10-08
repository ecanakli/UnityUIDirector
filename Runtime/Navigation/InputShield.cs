using System;
using System.Collections.Generic;
using System.Text;

namespace Ecanakli.UIDirector
{
    internal enum ShieldHolderKind
    {
        // An open or another operation in flight on a shielding layer.
        Operation,

        // A close that was latched and has not been finalized yet.
        PendingClose,

        // A HoldInput registration.
        InputHold
    }

    // A counted input blocker: holders are taken at acceptance and returned on every path; the stage draws it.
    internal sealed class InputShield
    {
        public const double InputHoldWarnSeconds = 30.0;

        private static readonly Action<object> LongHoldCallback = OnLongHold;

        private readonly NavigatorClock _clock;
        private readonly Diagnostics _diagnostics;
        private readonly Action<bool> _blockingChanged;
        private readonly List<Holder> _holders = new List<Holder>();
        private int _nextHolderId = 1;
        private int _inputHolds;

        // blockingChanged is called on every 0 to 1 (true) and 1 to 0 (false) change of the count; it may be null.
        public InputShield(NavigatorClock clock, Diagnostics diagnostics, Action<bool> blockingChanged)
        {
            _clock = clock;
            _diagnostics = diagnostics;
            _blockingChanged = blockingChanged;
        }

        public int Count
        {
            get { return _holders.Count; }
        }

        public bool IsBlocking
        {
            get { return _holders.Count > 0; }
        }

        public int InputHoldCount
        {
            get { return _inputHolds; }
        }

        // Takes one hold and returns its id for Release; requestId is 0 for an input hold.
        public int Acquire(ShieldHolderKind kind, int requestId, string reason, string callSite)
        {
            var holder = new Holder(this, _nextHolderId++, kind, requestId, reason, callSite, _clock.Now);
            _holders.Add(holder);

            if (kind == ShieldHolderKind.InputHold)
            {
                _inputHolds++;
                holder.TimerId = _clock.Schedule(InputHoldWarnSeconds, LongHoldCallback, holder);
            }

            if (_holders.Count == 1)
                NotifyChanged(true);

            return holder.Id;
        }

        // Returns a hold; false when the id is zero or was already returned.
        public bool Release(int holderId)
        {
            if (holderId == 0)
                return false;

            for (var i = 0; i < _holders.Count; i++)
            {
                var holder = _holders[i];
                if (holder.Id != holderId)
                    continue;

                _holders.RemoveAt(i);
                if (holder.Kind == ShieldHolderKind.InputHold)
                    _inputHolds--;

                if (holder.TimerId != 0)
                {
                    _clock.Cancel(holder.TimerId);
                    holder.TimerId = 0;
                }

                if (_holders.Count == 0)
                    NotifyChanged(false);

                return true;
            }

            return false;
        }

        // Drops every hold at once; used when the root is destroyed.
        public void ReleaseAll()
        {
            if (_holders.Count == 0)
                return;

            for (var i = 0; i < _holders.Count; i++)
            {
                var holder = _holders[i];
                if (holder.TimerId == 0)
                    continue;

                _clock.Cancel(holder.TimerId);
                holder.TimerId = 0;
            }

            _holders.Clear();
            _inputHolds = 0;
            NotifyChanged(false);
        }

        public void Describe(StringBuilder output)
        {
            output.Append("shield: ").Append(_holders.Count).Append('\n');
            for (var i = 0; i < _holders.Count; i++)
            {
                var holder = _holders[i];
                output.Append("  #").Append(holder.Id).Append(' ').Append(holder.Kind);
                if (holder.RequestId != 0)
                    output.Append(" request ").Append(holder.RequestId);
                if (holder.Reason != null)
                    output.Append(" '").Append(holder.Reason).Append('\'');
                output.Append(" age ").Append((_clock.Now - holder.AcquiredAt).ToString("0.0")).Append("s");
                if (holder.CallSite != null)
                    output.Append(" at ").Append(holder.CallSite);
                output.Append('\n');
            }
        }

        // Clears into and copies the holders in acquisition order; only reads.
        internal void CaptureHolders(List<ShieldHolderSnapshot> into)
        {
            into.Clear();
            var now = _clock.Now;
            for (var i = 0; i < _holders.Count; i++)
            {
                var holder = _holders[i];
                into.Add(new ShieldHolderSnapshot
                {
                    HolderId = holder.Id,
                    Kind = holder.Kind,
                    RequestId = holder.RequestId,
                    Reason = holder.Reason,
                    AgeSeconds = now - holder.AcquiredAt,
                    CallSite = holder.CallSite
                });
            }
        }

        private static void OnLongHold(object state)
        {
            var holder = (Holder)state;
            holder.TimerId = 0;
            holder.Shield._diagnostics.Warn(
                DiagnosticIds.InputHoldTooLong,
                "The input hold '" + holder.Reason + "' is older than 30 s. Registered at " + (holder.CallSite ?? "an unknown call site") + ".");
        }

        private void NotifyChanged(bool blocking)
        {
            if (_blockingChanged == null)
                return;

            try
            {
                _blockingChanged(blocking);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("shield change", exception);
            }
        }

        private sealed class Holder
        {
            public readonly InputShield Shield;
            public readonly int Id;
            public readonly ShieldHolderKind Kind;
            public readonly int RequestId;
            public readonly string Reason;
            public readonly string CallSite;
            public readonly double AcquiredAt;
            public int TimerId;

            public Holder(InputShield shield, int id, ShieldHolderKind kind, int requestId, string reason, string callSite, double acquiredAt)
            {
                Shield = shield;
                Id = id;
                Kind = kind;
                RequestId = requestId;
                Reason = reason;
                CallSite = callSite;
                AcquiredAt = acquiredAt;
            }
        }
    }
}
