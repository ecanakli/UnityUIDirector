using System;
using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    // A queue's gates in ascending order; the first Deny wins and a throwing gate counts as Deny.
    internal sealed class GateChain
    {
        private readonly ErrorRouter _errors;
        private readonly Diagnostics _diagnostics;
        private readonly List<Entry> _entries = new List<Entry>();

        // Evaluate walks this copy, rebuilt only after a change, so a gate that edits the chain affects the next evaluation.
        private Entry[] _snapshot = Array.Empty<Entry>();
        private bool _dirty;

        public GateChain(ErrorRouter errors, Diagnostics diagnostics)
        {
            _errors = errors;
            _diagnostics = diagnostics;
        }

        public int Count
        {
            get { return _entries.Count; }
        }

        // Returns the entry for Remove; ascending order, ties keep add order.
        public object Add(IViewGate gate, int order)
        {
            if (gate == null)
                throw new ArgumentNullException(nameof(gate));

            var entry = new Entry(gate, order);
            var index = _entries.Count;
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Order <= order)
                    continue;

                index = i;
                break;
            }

            _entries.Insert(index, entry);
            _dirty = true;
            return entry;
        }

        // Removes an entry returned by Add; anything else does nothing.
        public void Remove(object entry)
        {
            if (!(entry is Entry typed))
                return;

            if (_entries.Remove(typed))
                _dirty = true;
        }

        public void RemoveAll()
        {
            if (_entries.Count == 0)
                return;

            _entries.Clear();
            _dirty = true;
        }

        public GateDecision Evaluate(in GateRequest request)
        {
            if (_dirty)
            {
                _snapshot = _entries.Count == 0 ? Array.Empty<Entry>() : _entries.ToArray();
                _dirty = false;
            }

            var snapshot = _snapshot;
            for (var i = 0; i < snapshot.Length; i++)
            {
                var entry = snapshot[i];
                GateDecision decision;
                try
                {
                    decision = entry.Gate.Evaluate(in request);
                }
                catch (Exception exception)
                {
                    _errors.Route(exception, ViewErrorSource.Gate, request.ViewId, request.RequestId, null);
                    _diagnostics.Error(
                        DiagnosticIds.GateThrew,
                        "The gate " + entry.TypeName + " threw for view '" + request.ViewId + "' (request " + request.RequestId + "); treated as Deny.");
                    return GateDecision.Deny("gate " + entry.TypeName + " threw");
                }

                if (!decision.IsAllowed)
                    return decision;
            }

            return GateDecision.Allow;
        }

        private sealed class Entry
        {
            public readonly IViewGate Gate;
            public readonly int Order;
            public readonly string TypeName;

            public Entry(IViewGate gate, int order)
            {
                Gate = gate;
                Order = order;
                TypeName = gate.GetType().Name;
            }
        }
    }
}
