using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector.Tests
{
    // Looks at the navigator at the moment each event is published, where the package fixes the order of things.
    internal sealed class InvariantEventProbe : IViewObserver
    {
        private readonly InvariantWorld _world;

        public InvariantEventProbe(InvariantWorld world)
        {
            _world = world;
        }

        public void OnViewEvent(in ViewEvent viewEvent)
        {
            try
            {
                Check(viewEvent.Kind, viewEvent.RequestId);
            }
            catch (Exception exception)
            {
                _world.ReportFromView("M.probe", "The event probe failed: " + exception.Message);
            }
        }

        private void Check(ViewEventKind kind, int requestId)
        {
            switch (kind)
            {
                case ViewEventKind.Requested:
                {
                    var record = Read().Find(requestId);
                    if (record == null)
                        _world.ReportFromView("I1", "Requested for #" + requestId + " was published before the request was in a layer list.");
                    else if (record.Phase != ViewPhase.Waiting)
                        _world.ReportFromView("I1", "Requested for #" + requestId + " was published while the request is " + record.Phase + ".");
                    break;
                }

                case ViewEventKind.Shown:
                {
                    // A request that ends during Opening publishes Shown right before Closed, after state first.
                    var record = Read().Find(requestId);
                    InvariantTrace endedTrace;
                    if (record == null)
                    {
                        if (!_world.Traces.TryGetValue(requestId, out endedTrace) || endedTrace.OpenCalls == 0)
                            _world.ReportFromView("I6", "Shown for #" + requestId + " was published although the request is in no layer list and never opened.");
                    }
                    else if (record.Phase != ViewPhase.Open)
                        _world.ReportFromView("I6", "Shown for #" + requestId + " was published while the request is " + record.Phase + ".");
                    break;
                }

                case ViewEventKind.Closed:
                case ViewEventKind.NotShown:
                {
                    // State first: the record is gone and its scopes are over before the outcome is published.
                    if (requestId != 0 && Read().Find(requestId) != null)
                        _world.ReportFromView("I1", kind + " for #" + requestId + " was published while the request is still in a layer list.");

                    InvariantTrace trace;
                    if (_world.Traces.TryGetValue(requestId, out trace))
                    {
                        if (trace.OpenEnds != trace.OpenCalls)
                            _world.ReportFromView("I3", kind + " for #" + requestId + " was published before its open scope ended.");
                        if (trace.VisibleEnds != trace.VisibleBegins)
                            _world.ReportFromView("I3", kind + " for #" + requestId + " was published before its visible scope ended.");
                    }

                    break;
                }

                case ViewEventKind.Settled:
                {
                    var navigator = _world.Navigator;
                    if (navigator.IsBusy)
                        _world.ReportFromView("I2.zero", "Settled was published while the navigator is busy.");
                    else if (navigator.ShieldCount != 0)
                        _world.ReportFromView("I2.zero", "Settled was published while the shield count is " + navigator.ShieldCount + ".");
                    break;
                }
            }
        }

        private InvariantSnapshot Read()
        {
            return InvariantSnapshot.Parse(_world.Harness.Describe());
        }
    }

    // Asserts the navigator invariants I1 to I11 and the queue invariants Q1 to Q5 (the ids its reports carry) against what the navigator
    // and the queue expose: Describe, the shield count, the stage's recorded calls and applied state, the events and the awaiters.
    internal sealed class InvariantChecker
    {
        // The package's slot and backdrop numbers, written here so the check does not borrow them from the code under test.
        private const int MaxSlot = 99;
        private const int BackdropOffset = 5;
        private const int ReportCalls = 60;

        private sealed class Ledger
        {
            public int RequestId;
            public string ViewId;
            public readonly List<ViewEventKind> Kinds = new List<ViewEventKind>(4);
            public bool HasTerminal;
            public ViewEvent Terminal;
        }

        private sealed class Item
        {
            public InvariantSnapshotRecord Record;
            public InvariantRow Row;
            public ViewLayer Layer;
            public int Global;
            public bool StandIn;
            public ViewPhase Effective;
            public bool OnScreen;
            public bool Hidden;
            public bool Interactive;
            public int Order;
            public FakeInstance Instance;
        }

        private readonly InvariantWorld _world;
        private readonly InvariantOptions _options;
        private readonly FakeStage _stage;
        private readonly List<InvariantViolation> _violations = new List<InvariantViolation>();
        private readonly Dictionary<string, InvariantViolation> _byId = new Dictionary<string, InvariantViolation>(StringComparer.Ordinal);
        private readonly Dictionary<int, Ledger> _ledgers = new Dictionary<int, Ledger>();
        private readonly Dictionary<int, int> _slots = new Dictionary<int, int>();
        private readonly HashSet<FakeInstance> _released = new HashSet<FakeInstance>();
        private readonly List<StageCall> _pendingAcquires = new List<StageCall>();
        private readonly List<StageCall> _pendingPlays = new List<StageCall>();
        private int _eventCursor;
        private int _callCursor;
        private int _errorCursor;
        private int _gateFaultCursor;

        public InvariantChecker(InvariantWorld world, InvariantOptions options = null)
        {
            _world = world;
            _options = options ?? new InvariantOptions();
            _stage = world.Stage;
            world.Checker = this;
        }

        // Every distinct violation found so far, first occurrence of each id.
        public IReadOnlyList<InvariantViolation> Violations
        {
            get { return _violations; }
        }

        public bool HasViolations
        {
            get { return _violations.Count > 0; }
        }

        // True when the test's own model broke; the run cannot go on after that.
        public bool HasModelFailure
        {
            get
            {
                for (var i = 0; i < _violations.Count; i++)
                {
                    if (_violations[i].Id.StartsWith("M.", StringComparison.Ordinal))
                        return true;
                }

                return false;
            }
        }

        public InvariantSnapshot Last { get; private set; }

        // Stage operations the manual stage still holds, oldest first; valid after SyncStage.
        public IReadOnlyList<StageCall> PendingAcquires
        {
            get { return _pendingAcquires; }
        }

        public IReadOnlyList<StageCall> PendingPlays
        {
            get { return _pendingPlays; }
        }

        // The driver completed or failed the pending acquire at the index.
        public void AcquireTaken(int index)
        {
            _pendingAcquires.RemoveAt(index);
        }

        public void PlayTaken(int index)
        {
            _pendingPlays.RemoveAt(index);
        }

        public void Report(string id, string message)
        {
            if (!_options.IsEnabled(id))
                return;

            InvariantViolation known;
            if (_byId.TryGetValue(id, out known))
            {
                known.Count++;
                return;
            }

            var violation = new InvariantViolation(id, _world.CurrentStep, message);
            _byId.Add(id, violation);
            _violations.Add(violation);
        }

        // Reads the stage's new calls: instance use after release, and the operations still pending.
        public void SyncStage()
        {
            var calls = _stage.Calls;
            for (; _callCursor < calls.Count; _callCursor++)
            {
                var call = calls[_callCursor];
                if (call.Instance != null)
                {
                    if (_released.Contains(call.Instance))
                        Report("I7", call.Kind + " on " + call.Instance + " after it was released (stage call " + _callCursor + ").");

                    if (call.Kind == StageCallKind.Release)
                        _released.Add(call.Instance);
                }

                if (call.Kind == StageCallKind.Acquire)
                    _pendingAcquires.Add(call);
                else if (call.Kind == StageCallKind.Play)
                    _pendingPlays.Add(call);
            }
        }

        // Per step

        // Checks every per-step invariant: I1, I2, I3, I5, I7, I8, I9, I10 and the extras X.
        public void CheckStep()
        {
            SyncStage();
            DrainWorldViolations();

            if (_stage.PendingAcquireCount != _pendingAcquires.Count || _stage.PendingPlayCount != _pendingPlays.Count)
            {
                Report("M.mirror", "The test's pending-step mirror (" + _pendingAcquires.Count + " loads, " + _pendingPlays.Count
                    + " transitions) differs from the stage (" + _stage.PendingAcquireCount + ", " + _stage.PendingPlayCount + ").");
                return;
            }

            ConsumeEvents();
            ConsumeErrors();

            InvariantSnapshot snapshot;
            try
            {
                snapshot = InvariantSnapshot.Parse(_world.Harness.Describe());
            }
            catch (FormatException exception)
            {
                Report("M.snapshot", exception.Message);
                return;
            }

            Last = snapshot;

            var liveById = LiveInstancesById();
            var items = BuildItems(snapshot, liveById);

            CheckLists(snapshot);
            CheckShield(snapshot);
            CheckScopes(items);
            CheckRunners(snapshot);
            CheckInstances(snapshot, items, liveById);
            CheckLayout(snapshot, items);
            CheckVisualState(items);
            CheckIndicator(snapshot);
            CheckAwaiters(false);
            CheckQueue();
            CheckFaults();
        }

        // After the root was destroyed: I11, plus the awaiters, the events and the scopes that must be settled by then.
        public void CheckAfterTeardown()
        {
            SyncStage();
            DrainWorldViolations();
            ConsumeEvents();
            ConsumeErrors();

            InvariantSnapshot snapshot;
            try
            {
                snapshot = InvariantSnapshot.Parse(_world.Harness.Describe());
            }
            catch (FormatException exception)
            {
                Report("M.snapshot", exception.Message);
                return;
            }

            Last = snapshot;
            var navigator = _world.Navigator;

            if (snapshot.State != "destroyed")
                Report("I11", "Describe reports '" + snapshot.State + "' after the root was destroyed.");

            if (snapshot.TotalRecords != 0)
                Report("I11", "After root destruction " + snapshot.TotalRecords + " request(s) are still in layer lists.");

            for (var i = 0; i < _world.Layers.Count; i++)
            {
                var count = navigator.RequestCount(_world.Layers[i]);
                if (count != 0)
                    Report("I11", "After root destruction layer '" + _world.Layers[i].Id + "' still holds " + count + " request(s).");
            }

            if (navigator.ShieldCount != 0)
                Report("I11", "After root destruction the shield count is " + navigator.ShieldCount + ".");

            if (_stage.ShieldBlocking)
                Report("I11", "After root destruction the stage still blocks input.");

            if (_stage.LoadIndicatorVisible)
                Report("I11", "After root destruction the load indicator is still visible.");

            if (!navigator.RootToken.IsCancellationRequested)
                Report("I11", "RootToken is not cancelled after root destruction.");

            for (var i = 0; i < _world.Holds.Count; i++)
            {
                if (_world.Holds[i].Registration.IsActive)
                    Report("I2", "Input hold #" + _world.Holds[i].Number + " is still active after root destruction.");
            }

            // Running steps are cancelled when the request ends.
            for (var i = 0; i < _pendingAcquires.Count; i++)
            {
                if (!_pendingAcquires[i].Token.IsCancellationRequested)
                    Report("I11", "A load of '" + _pendingAcquires[i].ViewId + "' is still not cancelled after root destruction.");
            }

            for (var i = 0; i < _pendingPlays.Count; i++)
            {
                if (!_pendingPlays[i].Token.IsCancellationRequested)
                    Report("I11", "A transition of " + _pendingPlays[i].Instance + " is still not cancelled after root destruction.");
            }

            CheckAwaiters(true);
            CheckLedgersFinal();
            CheckTracesFinal();
            CheckQueue();
            CheckQueueItemsFinal();
            CheckFaults();
        }

        // Q5 at the end: called once every queue hold owner has ended.
        public void CheckQueueHoldsFinal()
        {
            DrainWorldViolations();

            if (_world.Queue.IsHeld)
                Report("Q5", "The queue is still held after every hold owner ended.");

            var holds = _world.QueueHolds;
            for (var i = 0; i < holds.Count; i++)
            {
                if (holds[i].Registration.IsActive)
                    Report("Q5", "Queue hold #" + holds[i].Number + " is still active after its owner ended.");
            }

            CheckFaults();
        }

        // After the late results of cancelled loads were delivered: every instance is accounted for (I7).
        public void CheckInstancesFinal()
        {
            SyncStage();

            var instances = _stage.Instances;
            var stashedById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                if (instance.ReleaseCount > 1)
                    Report("I7", instance + " was released " + instance.ReleaseCount + " times.");

                if (instance.ReleaseCount == 0)
                {
                    if (!instance.Stashed)
                    {
                        Report("I7", instance + " was never released and is not kept.");
                        continue;
                    }

                    int count;
                    stashedById.TryGetValue(instance.ViewId, out count);
                    stashedById[instance.ViewId] = count + 1;
                    if (count + 1 > 1)
                        Report("I7", "More than one kept instance of '" + instance.ViewId + "'.");
                }
            }

            CheckFaults();
        }

        // Describes the last observed state for a failure report.
        public string DescribeForReport()
        {
            var text = new StringBuilder();
            text.Append(_world.Harness.Describe());
            text.Append("pending loads: ").Append(_pendingAcquires.Count)
                .Append(", pending transitions: ").Append(_pendingPlays.Count).Append('\n');

            // The tail is enough to read the last steps; the whole log can run to thousands of lines.
            var calls = _stage.Calls;
            var from = calls.Count > ReportCalls ? calls.Count - ReportCalls : 0;
            text.Append("stage calls ").Append(from).Append(" to ").Append(calls.Count - 1).Append(":\n");
            for (var i = from; i < calls.Count; i++)
                text.Append("  ").Append(i).Append(' ').Append(calls[i]).Append('\n');

            _world.Queue.Describe(text);
            text.Append("queue ticks pending: ").Append(_world.Ticker.PendingTicks).Append('\n');
            text.Append("gates model:\n");
            for (var i = 0; i < _world.AllGates.Count; i++)
            {
                var gate = _world.AllGates[i];
                text.Append("  ").Append(gate).Append(_world.IsGateLive(gate) ? " live" : " ended").Append('\n');
            }

            text.Append("queue model:\n");
            foreach (var pair in _world.QueueItems)
            {
                if (pair.Value.State != InvariantQueueItemState.Ended)
                    text.Append("  ").Append(pair.Value).Append('\n');
            }

            return text.ToString();
        }

        // Events and errors

        private void DrainWorldViolations()
        {
            var pending = _world.PendingViolations;
            for (var i = 0; i < pending.Count; i++)
                Report(pending[i].Id, pending[i].Message);
            pending.Clear();
        }

        private Ledger GetLedger(int requestId, string viewId)
        {
            Ledger ledger;
            if (!_ledgers.TryGetValue(requestId, out ledger))
            {
                ledger = new Ledger { RequestId = requestId, ViewId = viewId };
                _ledgers.Add(requestId, ledger);
            }

            return ledger;
        }

        private bool IsEnded(int requestId)
        {
            Ledger ledger;
            return _ledgers.TryGetValue(requestId, out ledger) && ledger.HasTerminal;
        }

        // I6: per request id, across the navigator and the queue, the sequence is [Enqueued] NotShown,
        // [Enqueued] Requested NotShown, or [Enqueued] Requested Shown Closed.
        private void ConsumeEvents()
        {
            var events = _world.Events;
            for (; _eventCursor < events.Count; _eventCursor++)
            {
                var e = events[_eventCursor].Event;
                var fromQueue = events[_eventCursor].FromQueue;

                if (e.Kind == ViewEventKind.Settled)
                {
                    if (fromQueue)
                        Report("I6", "The queue published Settled; only the navigator does.");
                    else if (e.RequestId != 0)
                        Report("I6", "Settled carries request id " + e.RequestId + ".");
                    continue;
                }

                if (e.Kind == ViewEventKind.Enqueued && !fromQueue)
                {
                    Report("I6", "The navigator published Enqueued for #" + e.RequestId + "; only a queue does.");
                    continue;
                }

                // The queue publishes Enqueued, and NotShown only for items that never reached the navigator.
                if (fromQueue && e.Kind != ViewEventKind.Enqueued && e.Kind != ViewEventKind.NotShown)
                {
                    Report("I6", "The queue published " + e.Kind + " for #" + e.RequestId + "; it publishes only Enqueued and NotShown.");
                    continue;
                }

                var ledger = GetLedger(e.RequestId, e.ViewId);
                var kinds = ledger.Kinds;

                // The optional Enqueued prefix; the rest follows the navigator's sequences.
                var skip = kinds.Count > 0 && kinds[0] == ViewEventKind.Enqueued ? 1 : 0;
                var rest = kinds.Count - skip;

                if (ledger.HasTerminal)
                    Report("I6", e.Kind + " for #" + e.RequestId + " came after its terminal event.");

                switch (e.Kind)
                {
                    case ViewEventKind.Enqueued:
                        if (kinds.Count != 0)
                            Report("I6", "Enqueued for #" + e.RequestId + " is not its first event: " + Join(kinds) + ".");
                        break;

                    case ViewEventKind.Requested:
                        if (rest != 0)
                            Report("I6", "Requested for #" + e.RequestId + " is not its first event after the Enqueued prefix: " + Join(kinds) + ".");
                        break;

                    case ViewEventKind.Shown:
                        if (!(rest == 1 && kinds[skip] == ViewEventKind.Requested))
                            Report("I6", "Shown for #" + e.RequestId + " does not follow Requested directly: " + Join(kinds) + ".");
                        break;

                    case ViewEventKind.Closed:
                        if (rest == 2 && kinds[skip] == ViewEventKind.Requested && kinds[skip + 1] == ViewEventKind.Shown)
                        {
                            // Valid.
                        }
                        else if (rest == 1 && kinds[skip] == ViewEventKind.Requested)
                        {
                            Report("I6.shown", "Closed for #" + e.RequestId + " (" + e.Reason + ") has no Shown before it. The only sequence that ends in Closed is Requested, Shown, Closed.");
                        }
                        else
                        {
                            Report("I6", "Closed for #" + e.RequestId + " follows " + Join(kinds) + ".");
                        }

                        if (e.Reason == CloseReason.NotShown)
                            Report("I6", "Closed for #" + e.RequestId + " carries the reason NotShown.");
                        if (e.Rejected != RejectReason.None)
                            Report("I6", "Closed for #" + e.RequestId + " carries the rejection " + e.Rejected + ".");

                        ledger.HasTerminal = true;
                        ledger.Terminal = e;
                        break;

                    case ViewEventKind.NotShown:
                        if (fromQueue)
                        {
                            if (rest != 0)
                                Report("I6", "The queue published NotShown for #" + e.RequestId + " after the navigator took it: " + Join(kinds) + ".");
                        }
                        else if (!(rest == 0 || (rest == 1 && kinds[skip] == ViewEventKind.Requested)))
                        {
                            Report("I6", "NotShown for #" + e.RequestId + " follows " + Join(kinds) + ".");
                        }

                        if (e.Reason != CloseReason.NotShown)
                            Report("I6", "NotShown for #" + e.RequestId + " carries the reason " + e.Reason + ".");
                        if (e.Rejected == RejectReason.None)
                            Report("I6", "NotShown for #" + e.RequestId + " carries no rejection.");

                        ledger.HasTerminal = true;
                        ledger.Terminal = e;
                        break;
                }

                kinds.Add(e.Kind);
            }
        }

        // Only exceptions the test injected may reach the error handler; anything else is a fault inside the package.
        private void ConsumeErrors()
        {
            var errors = _world.Harness.Errors;
            for (; _errorCursor < errors.Count; _errorCursor++)
            {
                var error = errors[_errorCursor];
                if (error.Exception is InvariantInjectedException)
                {
                    CheckInjectedError(error);
                    continue;
                }

                Report("X.error", "An unexpected " + error.Exception.GetType().Name + " reached the error handler (" + error.Context.Source
                    + ", view " + error.Context.ViewId + ", request " + error.Context.RequestId + "): " + error.Exception.Message);
            }

            // A throwing gate is routed with the source Gate, logs UIDIRECTOR119 and counts as Deny.
            var faults = _world.GateFaults;
            for (; _gateFaultCursor < faults.Count; _gateFaultCursor++)
            {
                var fault = faults[_gateFaultCursor];
                if (fault.Routed == 0)
                    Report("X.gatefault", fault + " but the exception never reached the error handler.");

                InvariantQueueItem item;
                if (!_world.QueueItems.TryGetValue(fault.RequestId, out item) || item.TerminalIndex < 0)
                {
                    Report("X.gatefault", fault + " but the item did not end; a throwing gate counts as Deny.");
                }
                else if (!item.TerminalFromQueue || item.TerminalKind != ViewEventKind.NotShown || item.TerminalRejected != RejectReason.Denied)
                {
                    Report("X.gatefault", fault + " but the item ended " + DescribeTerminal(item) + "; a throwing gate counts as Deny.");
                }
            }

            var logged = _world.Harness.Diagnostics.Count("UIDIRECTOR119");
            if (logged < faults.Count)
                Report("X.gatefault", faults.Count + " gate(s) threw but UIDIRECTOR119 was logged " + logged + " time(s).");
        }

        // An injected exception from a toggle gate must arrive as a Gate error for its request; any other must not claim the source Gate.
        private void CheckInjectedError(RecordedError error)
        {
            InvariantGateFault fault = null;
            var faults = _world.GateFaults;
            for (var i = 0; i < faults.Count; i++)
            {
                if (ReferenceEquals(faults[i].Exception, error.Exception))
                {
                    fault = faults[i];
                    break;
                }
            }

            if (fault == null)
            {
                if (error.Context.Source == ViewErrorSource.Gate)
                    Report("X.gatefault", "An injected failure that no gate threw was routed with the source Gate: " + error.Exception.Message);
                return;
            }

            fault.Routed++;
            if (fault.Routed > 1)
                Report("X.gatefault", fault + " and the exception reached the error handler " + fault.Routed + " times.");

            if (error.Context.Source != ViewErrorSource.Gate || error.Context.RequestId != fault.RequestId
                || !string.Equals(error.Context.ViewId, fault.ViewId, StringComparison.Ordinal))
            {
                Report("X.gatefault", fault + " but it was routed as (" + error.Context.Source + ", view " + error.Context.ViewId + ", request "
                    + error.Context.RequestId + "), not (Gate, view " + fault.ViewId + ", request " + fault.RequestId + ").");
            }
        }

        private static string DescribeTerminal(InvariantQueueItem item)
        {
            if (item.TerminalIndex < 0)
                return "not at all";

            return "by " + (item.TerminalFromQueue ? "a queue " : "a navigator ") + item.TerminalKind
                + (item.TerminalKind == ViewEventKind.NotShown ? " / " + item.TerminalRejected : string.Empty)
                + (item.TerminalDetail != null ? " ('" + item.TerminalDetail + "')" : string.Empty);
        }

        private void CheckFaults()
        {
            var faults = _world.Harness.Diagnostics.FaultCount;
            if (faults == 0)
                return;

            var text = new StringBuilder();
            var entries = _world.Harness.Diagnostics.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Id == null)
                    text.Append(entries[i].Message).Append(" | ");
            }

            Report("X.fault", faults + " internal fault(s) reported by the package: " + text);
        }

        // Model of the derived state, built independently of the navigator

        private Dictionary<string, List<FakeInstance>> LiveInstancesById()
        {
            var result = new Dictionary<string, List<FakeInstance>>(StringComparer.Ordinal);
            var instances = _stage.Instances;
            for (var i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                if (!instance.IsLive)
                    continue;

                List<FakeInstance> list;
                if (!result.TryGetValue(instance.ViewId, out list))
                {
                    list = new List<FakeInstance>(2);
                    result.Add(instance.ViewId, list);
                }

                list.Add(instance);
            }

            return result;
        }

        private List<Item> BuildItems(InvariantSnapshot snapshot, Dictionary<string, List<FakeInstance>> liveById)
        {
            var items = new List<Item>();
            var layers = _world.Layers;

            // Global order: layers ascending, then list position.
            for (var l = 0; l < layers.Count; l++)
            {
                var snapLayer = snapshot.Layer(layers[l].Id);
                if (snapLayer == null)
                {
                    Report("M.snapshot", "Describe has no block for layer '" + layers[l].Id + "'.");
                    continue;
                }

                for (var r = 0; r < snapLayer.Records.Count; r++)
                {
                    var record = snapLayer.Records[r];
                    var row = _world.RowById(record.ViewId);
                    if (row == null)
                    {
                        Report("M.snapshot", "Describe lists the unknown view '" + record.ViewId + "'.");
                        continue;
                    }

                    items.Add(new Item { Record = record, Row = row, Layer = layers[l], Global = items.Count });
                }
            }

            // Effective phase: a reopened request holds the live instance while it waits and stands in for the view it replaced.
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.Effective = item.Record.Phase;

                List<FakeInstance> live;
                if (item.Record.Phase == ViewPhase.Waiting
                    && !item.Row.AllowMultiple
                    && liveById.TryGetValue(item.Row.Id, out live)
                    && live.Count > 0
                    && !HasOnScreenSibling(items, item))
                {
                    item.StandIn = true;
                    item.Effective = ViewPhase.Open;
                }

                item.OnScreen = IsOnScreen(item.Effective);
            }

            // Hidden and interactive by the layer rules, from the views above.
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var hidden = false;
                var modalAbove = false;

                for (var j = i + 1; j < items.Count; j++)
                {
                    var above = items[j];
                    if (above.Effective == ViewPhase.Open && above.Row.Covers)
                        hidden = true;
                    if (_world.IsModal(above.Layer) && above.OnScreen)
                        modalAbove = true;
                }

                item.Hidden = hidden;
                item.Interactive = item.Effective == ViewPhase.Open && !hidden && !modalAbove && !item.Record.ClosePending && !item.Record.BeingReplaced;
                item.Order = item.Layer.Order * 1000 + item.Record.Slot * 10;

                if (item.OnScreen)
                    item.Instance = FindInstance(item, liveById);
            }

            return items;
        }

        private static bool HasOnScreenSibling(List<Item> items, Item item)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var other = items[i];
                if (!ReferenceEquals(other, item)
                    && string.Equals(other.Record.ViewId, item.Record.ViewId, StringComparison.Ordinal)
                    && IsOnScreen(other.Record.Phase))
                    return true;
            }

            return false;
        }

        private static FakeInstance FindInstance(Item item, Dictionary<string, List<FakeInstance>> liveById)
        {
            List<FakeInstance> live;
            if (!liveById.TryGetValue(item.Row.Id, out live) || live.Count == 0)
                return null;

            if (!item.Row.AllowMultiple)
                return live[0];

            for (var i = 0; i < live.Count; i++)
            {
                if (live[i].Handle.RequestId == item.Record.RequestId)
                    return live[i];
            }

            return null;
        }

        private static bool IsOnScreen(ViewPhase phase)
        {
            return phase == ViewPhase.Opening || phase == ViewPhase.Open || phase == ViewPhase.Closing;
        }

        // I1 and I8

        private void CheckLists(InvariantSnapshot snapshot)
        {
            var listed = new HashSet<int>();

            for (var l = 0; l < snapshot.Layers.Count; l++)
            {
                var layer = snapshot.Layers[l];
                if (layer.Requests != layer.Records.Count)
                    Report("M.snapshot", "Layer '" + layer.Id + "' says " + layer.Requests + " requests but lists " + layer.Records.Count + ".");

                var live = new Dictionary<string, int>(StringComparer.Ordinal);

                for (var r = 0; r < layer.Records.Count; r++)
                {
                    var record = layer.Records[r];

                    if (!listed.Add(record.RequestId))
                        Report("I1", "Request #" + record.RequestId + " is in more than one layer list.");

                    if (record.Phase == ViewPhase.Ended)
                        Report("I1", "Request #" + record.RequestId + " is Ended but still in the list of layer '" + layer.Id + "'.");

                    var row = _world.RowById(record.ViewId);
                    if (row != null && !string.Equals(row.Layer.Id, layer.Id, StringComparison.Ordinal))
                        Report("I1", "Request #" + record.RequestId + " for '" + record.ViewId + "' is in layer '" + layer.Id + "' but its row is on '" + row.Layer.Id + "'.");

                    // I8: a record on its way out (close latched, being replaced, or closing) does not count as live.
                    if (row != null && !row.AllowMultiple && !record.ClosePending && !record.BeingReplaced && record.Phase != ViewPhase.Closing)
                    {
                        int count;
                        live.TryGetValue(record.ViewId, out count);
                        live[record.ViewId] = count + 1;
                        if (count + 1 > 1)
                            Report("I8", "Layer '" + layer.Id + "' holds " + (count + 1) + " live records of '" + record.ViewId + "' without AllowMultiple.");
                    }
                }
            }

            // Not Ended exactly when requested and not yet terminal.
            foreach (var pair in _ledgers)
            {
                var ledger = pair.Value;
                var started = ledger.Kinds.Contains(ViewEventKind.Requested);
                var alive = started && !ledger.HasTerminal;
                var inList = listed.Contains(ledger.RequestId);

                if (alive && !inList)
                    Report("I1", "Request #" + ledger.RequestId + " (" + ledger.ViewId + ") was accepted, has no terminal event and is in no layer list.");
                else if (!alive && inList)
                    Report("I1", "Request #" + ledger.RequestId + " (" + ledger.ViewId + ") " + (started ? "ended" : "was never accepted") + " but is in a layer list.");
            }

            foreach (var id in listed)
            {
                if (!_ledgers.ContainsKey(id))
                    Report("I1", "Request #" + id + " is in a layer list but no event was published for it.");
            }

            var handles = _world.Handles;
            for (var i = 0; i < handles.Count; i++)
            {
                var handle = handles[i];
                if (handle.RequestId == 0)
                    continue;

                var inList = listed.Contains(handle.RequestId);
                if (handle.IsAlive != inList)
                {
                    Report("I1", "The handle of #" + handle.RequestId + " says " + (handle.IsAlive ? "alive" : "ended") + " but the request is "
                        + (inList ? "in a layer list" : "in no layer list") + ".");
                    continue;
                }

                if (inList)
                {
                    var record = snapshot.Find(handle.RequestId);
                    if (record != null && record.Phase != handle.Phase)
                        Report("I1", "The handle of #" + handle.RequestId + " says " + handle.Phase + " but the list says " + record.Phase + ".");
                }
            }
        }

        // I2

        private void CheckShield(InvariantSnapshot snapshot)
        {
            var navigator = _world.Navigator;
            var count = navigator.ShieldCount;
            var inputHolds = _world.LiveHoldCount;

            if (snapshot.ShieldCount != count)
                Report("M.snapshot", "Describe says shield " + snapshot.ShieldCount + " but the navigator says " + count + ".");

            var min = inputHolds;
            var max = inputHolds;
            var needed = inputHolds > 0;

            for (var l = 0; l < _world.Layers.Count; l++)
            {
                var layer = _world.Layers[l];
                if (!_world.Shields(layer))
                    continue;

                var snapLayer = snapshot.Layer(layer.Id);
                if (snapLayer == null)
                    continue;

                var operationInFlight = snapLayer.Running || snapLayer.Operations > 0;
                if (operationInFlight)
                    needed = true;

                for (var r = 0; r < snapLayer.Records.Count; r++)
                {
                    var record = snapLayer.Records[r];
                    switch (record.Phase)
                    {
                        // The open is an operation in flight until the show ends.
                        case ViewPhase.Waiting:
                        case ViewPhase.Loading:
                            min += 1;
                            max += 1;
                            needed = true;
                            break;

                        // The sum allows one or two holds once a close cause is latched; the per-request check below holds each request to two.
                        case ViewPhase.Opening:
                            min += 1;
                            max += record.ClosePending ? 2 : 1;
                            needed = true;
                            break;

                        // A pending close holds one; without it a pop or close-all in flight may hold one for the top view.
                        case ViewPhase.Open:
                            if (record.ClosePending)
                            {
                                min += 1;
                                max += 1;
                                needed = true;
                            }
                            else if (operationInFlight)
                            {
                                max += 1;
                            }

                            break;

                        // The hide transition is an operation in flight; how many holds it carries is not part of the contract.
                        case ViewPhase.Closing:
                            max += 1;
                            needed = true;
                            break;
                    }
                }
            }

            if (count < min || count > max)
            {
                Report("I2.count", "Shield count is " + count + ", the per-request holds give " + (min == max ? min.ToString() : min + " to " + max)
                    + " (input holds " + inputHolds + ").");
            }

            if (needed && count < 1)
                Report("I2.presence", "The shield is down while an operation, a close or an input hold is in flight.");

            if (!navigator.IsBusy && count != 0)
                Report("I2.zero", "The navigator is settled but the shield count is " + count + ".");

            // The stage mirrors the count: raised on 0 to 1, dropped on 1 to 0, drawn above every layer.
            if (_stage.ShieldBlocking != (count > 0))
                Report("I2.stage", "The stage shield is " + (_stage.ShieldBlocking ? "blocking" : "down") + " but the count is " + count + ".");

            if (_stage.ShieldRaiseCount != _stage.ShieldDropCount + (_stage.ShieldBlocking ? 1 : 0))
                Report("I2.stage", "The stage saw " + _stage.ShieldRaiseCount + " raises and " + _stage.ShieldDropCount + " drops while blocking is " + _stage.ShieldBlocking + ".");

            var expectedShieldOrder = (_world.HighestLayerOrder + 1) * 1000;
            if (_stage.ShieldBlocking && _stage.ShieldOrder != expectedShieldOrder)
                Report("I2.stage", "The shield sits at " + _stage.ShieldOrder + ", expected at " + expectedShieldOrder + ".");

            // Holder lines of Describe against the model.
            var perRequest = new Dictionary<int, int>();
            var inputHolders = 0;
            for (var i = 0; i < snapshot.Holders.Count; i++)
            {
                var holder = snapshot.Holders[i];
                if (holder.Kind == ShieldHolderKind.InputHold)
                {
                    inputHolders++;
                    continue;
                }

                var record = snapshot.Find(holder.RequestId);
                if (record == null)
                {
                    Report("I2.holder", "Shield holder #" + holder.HolderId + " (" + holder.Kind + ") belongs to request #" + holder.RequestId + ", which is in no layer list.");
                    continue;
                }

                var layer = _world.LayerById(record.LayerId);
                if (layer != null && !_world.Shields(layer))
                    Report("I2.holder", "Request #" + record.RequestId + " on layer '" + layer.Id + "' holds the shield, but the layer does not shield input.");

                int held;
                perRequest.TryGetValue(holder.RequestId, out held);
                perRequest[holder.RequestId] = held + 1;
                if (held + 1 > 2)
                    Report("I2.holder", "Request #" + holder.RequestId + " carries " + (held + 1) + " shield holders; an operation and a pending close are at most two.");
            }

            if (inputHolders != inputHolds)
                Report("I2.holds", "Describe lists " + inputHolders + " input holds but the test holds " + inputHolds + " live registrations.");

            // Holders per request: +1 at acceptance, -1 when the show ends, +1 for a close cause.
            for (var l = 0; l < _world.Layers.Count; l++)
            {
                if (!_world.Shields(_world.Layers[l]))
                    continue;

                var snapLayer = snapshot.Layer(_world.Layers[l].Id);
                if (snapLayer == null)
                    continue;

                for (var r = 0; r < snapLayer.Records.Count; r++)
                {
                    var record = snapLayer.Records[r];
                    int held;
                    perRequest.TryGetValue(record.RequestId, out held);

                    switch (record.Phase)
                    {
                        case ViewPhase.Waiting:
                        case ViewPhase.Loading:
                            if (held != 1)
                                Report("I2.holder", "Request #" + record.RequestId + " (" + record.ViewId + ") is " + record.Phase + " and holds " + held + " shield holder(s); an open in flight holds one.");
                            break;

                        case ViewPhase.Opening:
                            if (record.ClosePending && held != 2)
                                Report("I2.opening", "Request #" + record.RequestId + " (" + record.ViewId + ") is Opening with a close pending and holds " + held + " shield holder(s); the open holds one and the close cause one more.");
                            else if (!record.ClosePending && held != 1)
                                Report("I2.holder", "Request #" + record.RequestId + " (" + record.ViewId + ") is Opening and holds " + held + " shield holder(s); an open in flight holds one.");
                            break;

                        case ViewPhase.Open:
                            if (record.ClosePending && held != 1)
                                Report("I2.holder", "Request #" + record.RequestId + " (" + record.ViewId + ") is Open with a close pending and holds " + held + " shield holder(s); a pending close holds one.");
                            else if (!record.ClosePending && held > 1)
                                Report("I2.holder", "Request #" + record.RequestId + " (" + record.ViewId + ") is Open and holds " + held + " shield holders.");
                            break;

                        case ViewPhase.Closing:
                            if (held > 1)
                                Report("I2.holder", "Request #" + record.RequestId + " (" + record.ViewId + ") is Closing and holds " + held + " shield holders.");
                            break;
                    }
                }
            }

            for (var i = 0; i < _world.Holds.Count; i++)
            {
                var hold = _world.Holds[i];
                if (hold.Registration.IsActive != hold.ModelLive)
                    Report("I2.holds", "Input hold #" + hold.Number + " reports IsActive=" + hold.Registration.IsActive + " but it should be " + hold.ModelLive + ".");
            }
        }

        // I3: OnOpen once per record, scopes paired, visible spans following the derived hidden state

        private void CheckScopes(List<Item> items)
        {
            foreach (var pair in _world.Traces)
            {
                var trace = pair.Value;
                if (trace.OpenCalls > 1)
                    Report("I3", "OnOpen ran " + trace.OpenCalls + " times for request #" + trace.RequestId + ".");

                if (trace.OpenEnds > trace.OpenCalls)
                    Report("I3", "The open scope of request #" + trace.RequestId + " ended " + trace.OpenEnds + " times for " + trace.OpenCalls + " open(s).");

                var span = trace.VisibleBegins - trace.VisibleEnds;
                if (span < 0 || span > 1)
                    Report("I3", "Visible scopes of request #" + trace.RequestId + " do not alternate: " + trace.VisibleBegins + " begins, " + trace.VisibleEnds + " ends.");

                if (IsEnded(trace.RequestId))
                {
                    if (trace.OpenEnds != trace.OpenCalls)
                        Report("I3", "Request #" + trace.RequestId + " ended with its open scope still open.");
                    if (span != 0)
                        Report("I3", "Request #" + trace.RequestId + " ended with a visible scope still open.");
                }
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var phase = item.Record.Phase;
                if (phase != ViewPhase.Opening && phase != ViewPhase.Open && phase != ViewPhase.Closing)
                    continue;

                InvariantTrace trace;
                _world.Traces.TryGetValue(item.Record.RequestId, out trace);

                if (trace == null || trace.OpenCalls != 1)
                {
                    Report("I3", "Request #" + item.Record.RequestId + " is " + phase + " but OnOpen ran " + (trace == null ? 0 : trace.OpenCalls) + " times.");
                    continue;
                }

                // Visible while on screen and not hidden; a closing view keeps whatever span it had.
                if (phase != ViewPhase.Closing)
                {
                    var open = trace.VisibleBegins - trace.VisibleEnds == 1;
                    // A request with a close latched, or that a newer request is replacing, is not revealed to user code again.
                    var revealSkipped = (item.Record.ClosePending || item.Record.BeingReplaced) && !open && !item.Hidden;
                    if (open == item.Hidden && !revealSkipped)
                    {
                        Report("I3", "Request #" + item.Record.RequestId + " (" + item.Record.ViewId + ", " + phase + ") is "
                            + (item.Hidden ? "hidden" : "not hidden") + " but its visible scope is " + (open ? "open" : "not open") + ".");
                    }
                }
            }
        }

        // I5

        private void CheckRunners(InvariantSnapshot snapshot)
        {
            var live = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < _pendingAcquires.Count; i++)
            {
                var call = _pendingAcquires[i];
                if (call.Token.IsCancellationRequested)
                    continue;

                var row = _world.RowById(call.ViewId);
                AddLive(live, row == null ? "?" : row.Layer.Id);
            }

            for (var i = 0; i < _pendingPlays.Count; i++)
            {
                var call = _pendingPlays[i];
                if (call.Token.IsCancellationRequested)
                    continue;

                var layer = call.Instance == null ? null : call.Instance.Layer;
                AddLive(live, layer == null ? "?" : layer.Id);
            }

            for (var l = 0; l < snapshot.Layers.Count; l++)
            {
                var layer = snapshot.Layers[l];
                int running;
                live.TryGetValue(layer.Id, out running);

                if (running > 1)
                    Report("I5", "Layer '" + layer.Id + "' has " + running + " steps running at once.");
                else if (running == 1 && !layer.Running)
                    Report("I5", "Layer '" + layer.Id + "' is idle although one of its steps is running.");
                else if (running == 0 && layer.Running)
                    Report("I5", "Layer '" + layer.Id + "' runs an operation but no step is in flight (" + layer.Operations + " queued).");
            }

            int unknown;
            if (live.TryGetValue("?", out unknown))
                Report("M.mirror", unknown + " running step(s) belong to no known layer.");
        }

        private static void AddLive(Dictionary<string, int> live, string layerId)
        {
            int count;
            live.TryGetValue(layerId, out count);
            live[layerId] = count + 1;
        }

        // I7

        private void CheckInstances(InvariantSnapshot snapshot, List<Item> items, Dictionary<string, List<FakeInstance>> liveById)
        {
            var instances = _stage.Instances;
            var stashedById = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                if (instance.ReleaseCount > 1)
                    Report("I7", instance + " was released " + instance.ReleaseCount + " times.");

                if (!instance.Stashed)
                    continue;

                var row = _world.RowById(instance.ViewId);
                if (row != null && row.Retention != ViewRetention.KeepInstance)
                    Report("I7", instance + " is kept although its row releases on close.");

                int count;
                stashedById.TryGetValue(instance.ViewId, out count);
                stashedById[instance.ViewId] = count + 1;
                if (count + 1 > 1)
                    Report("I7", "More than one kept instance of '" + instance.ViewId + "'.");
            }

            // The kept ids Describe lists are exactly the stashed instances.
            foreach (var pair in stashedById)
            {
                if (!snapshot.KeptIds.Contains(pair.Key))
                    Report("I7", "'" + pair.Key + "' is stashed on the stage but Describe lists no kept instance for it.");
            }

            for (var i = 0; i < snapshot.KeptIds.Count; i++)
            {
                if (!stashedById.ContainsKey(snapshot.KeptIds[i]))
                    Report("I7", "Describe lists a kept instance of '" + snapshot.KeptIds[i] + "' but the stage has none stashed.");
            }

            // Live instances against the requests that hold them.
            foreach (var pair in liveById)
            {
                var row = _world.RowById(pair.Key);
                var live = pair.Value;
                var requests = 0;
                var onScreen = 0;
                for (var i = 0; i < items.Count; i++)
                {
                    if (!string.Equals(items[i].Record.ViewId, pair.Key, StringComparison.Ordinal))
                        continue;

                    requests++;
                    if (IsOnScreen(items[i].Record.Phase))
                        onScreen++;
                }

                if (row != null && !row.AllowMultiple && live.Count > 1)
                    Report("I7", "'" + pair.Key + "' has " + live.Count + " live instances; a row without AllowMultiple has one.");

                if (requests == 0)
                    Report("I7", live.Count + " live instance(s) of '" + pair.Key + "' belong to no request: they were never released.");
                else if (row != null && row.AllowMultiple && live.Count != onScreen)
                    Report("I7", "'" + pair.Key + "' has " + live.Count + " live instances for " + onScreen + " requests on screen.");
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].OnScreen && items[i].Instance == null && !items[i].StandIn)
                    Report("I7", "Request #" + items[i].Record.RequestId + " (" + items[i].Record.ViewId + ") is " + items[i].Record.Phase + " without a live instance.");
            }
        }

        // I9

        private void CheckLayout(InvariantSnapshot snapshot, List<Item> items)
        {
            for (var l = 0; l < snapshot.Layers.Count; l++)
            {
                var layer = snapshot.Layers[l];
                for (var r = 0; r < layer.Records.Count; r++)
                {
                    var record = layer.Records[r];

                    if (record.Slot < 1 || record.Slot > MaxSlot)
                        Report("I9", "Request #" + record.RequestId + " has slot " + record.Slot + "; slots run 1 to " + MaxSlot + ".");

                    if (r > 0 && record.Slot <= layer.Records[r - 1].Slot)
                    {
                        Report("I9", "Layer '" + layer.Id + "': slot " + record.Slot + " of #" + record.RequestId + " does not exceed slot "
                            + layer.Records[r - 1].Slot + " of the record below it; sort orders must rise with list position.");
                    }

                    // The slot is fixed while the view is open; a request still waiting may take over a replaced request's slot.
                    int first;
                    if (record.Phase < ViewPhase.Opening)
                        continue;
                    if (!_slots.TryGetValue(record.RequestId, out first))
                        _slots.Add(record.RequestId, record.Slot);
                    else if (first != record.Slot)
                        Report("I9", "Request #" + record.RequestId + " moved from slot " + first + " to " + record.Slot + " while it is open.");
                }
            }

            // One backdrop per modal layer, directly under the top view of the layer, or inactive.
            for (var l = 0; l < _world.Layers.Count; l++)
            {
                var layer = _world.Layers[l];
                var active = _stage.IsBackdropActive(layer);

                if (!_world.IsModal(layer))
                {
                    if (active)
                        Report("I9", "Layer '" + layer.Id + "' is not modal but has an active backdrop.");
                    continue;
                }

                Item top = null;
                for (var i = items.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(items[i].Layer, layer) && items[i].OnScreen)
                    {
                        top = items[i];
                        break;
                    }
                }

                var visibleTop = top != null && !top.Hidden;

                if (active)
                {
                    if (!visibleTop)
                        Report("I9", "The backdrop of layer '" + layer.Id + "' is active but the layer has no visible top view.");
                    else if (_stage.BackdropOrder(layer) != top.Order - BackdropOffset)
                        Report("I9", "The backdrop of layer '" + layer.Id + "' sits at " + _stage.BackdropOrder(layer) + ", not " + BackdropOffset
                            + " under its top view at " + top.Order + ".");
                }
                else if (visibleTop)
                {
                    Report("I9.backdrop", "Layer '" + layer.Id + "' is modal and its top view #" + top.Record.RequestId + " is visible, but its backdrop is inactive.");
                }
            }
        }

        // I10: what the stage was told for hidden and interactive equals the derived state.
        // The stage fake has no canvases, so "every Canvas / every GraphicRaycaster under the view" is checked as the applied flags.

        private void CheckVisualState(List<Item> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!item.OnScreen || item.Instance == null)
                    continue;

                var instance = item.Instance;
                if (!instance.HasVisual)
                {
                    Report("I10", "Request #" + item.Record.RequestId + " (" + item.Record.ViewId + ") is " + item.Effective + " but the stage never applied a visual state to " + instance + ".");
                    continue;
                }

                var visual = instance.Visual;

                if (visual.Hidden != item.Hidden)
                {
                    Report("I10", "Request #" + item.Record.RequestId + " (" + item.Record.ViewId + ", " + item.Effective + ") should be "
                        + (item.Hidden ? "hidden (every canvas disabled)" : "shown") + " but the stage applied hidden=" + visual.Hidden + ".");
                }

                if (visual.Interactive != item.Interactive)
                {
                    Report("I10", "Request #" + item.Record.RequestId + " (" + item.Record.ViewId + ", " + item.Effective + ") should be "
                        + (item.Interactive ? "interactive" : "non-interactive (every raycaster disabled)") + " but the stage applied interactive=" + visual.Interactive + ".");
                }

                if (visual.CoveredMode != item.Row.WhenCovered)
                    Report("I10", "Request #" + item.Record.RequestId + " was applied with covered mode " + visual.CoveredMode + ", its row says " + item.Row.WhenCovered + ".");

                if (visual.SortingOrder != item.Order)
                {
                    Report("I9", "Request #" + item.Record.RequestId + " (" + item.Record.ViewId + ") was applied with sorting order " + visual.SortingOrder
                        + ", slot " + item.Record.Slot + " on layer '" + item.Layer.Id + "' gives " + item.Order + ".");
                }
            }
        }

        // The load indicator shows only while a shield-holding request loads.
        private void CheckIndicator(InvariantSnapshot snapshot)
        {
            if (!_stage.LoadIndicatorVisible)
                return;

            for (var l = 0; l < _world.Layers.Count; l++)
            {
                if (!_world.Shields(_world.Layers[l]))
                    continue;

                var snapLayer = snapshot.Layer(_world.Layers[l].Id);
                if (snapLayer == null)
                    continue;

                for (var r = 0; r < snapLayer.Records.Count; r++)
                {
                    if (snapLayer.Records[r].Phase == ViewPhase.Loading)
                        return;
                }
            }

            Report("X.indicator", "The load indicator is visible but no request on a shielding layer is loading.");
        }

        // I4

        private void CheckAwaiters(bool final)
        {
            var awaiters = _world.Awaiters;
            for (var i = 0; i < awaiters.Count; i++)
            {
                var awaiter = awaiters[i];

                // A pooled task must not be asked for its status again once its result was taken.
                if (awaiter.Verified)
                    continue;

                var status = awaiter.Status;

                if (status == UniTaskStatus.Pending)
                {
                    if (final)
                        Report("I4", awaiter + " never completed.");
                    else if (awaiter.RequestId != 0 && IsEnded(awaiter.RequestId))
                        Report("I4", awaiter + " is still pending although its request ended.");
                    else if (awaiter.IsWait && awaiter.Waiter != null && awaiter.Waiter.IsCancellationRequested)
                        Report("I4", awaiter + " is still pending although its waiter token was cancelled.");
                    continue;
                }

                awaiter.Verified = true;

                if (awaiter.IsWait)
                    VerifyWait(awaiter, status);
                else
                    VerifyShow(awaiter, status);
            }
        }

        private void VerifyShow(InvariantAwaiter awaiter, UniTaskStatus status)
        {
            if (status != UniTaskStatus.Succeeded)
            {
                Report("I4", awaiter + " ended " + status + "; ShowAsync completes with an outcome and never throws.");
                return;
            }

            var result = awaiter.Show.GetAwaiter().GetResult();
            awaiter.ShowResult = result;

            if (awaiter.RequestId != 0 && awaiter.RequestId != result.RequestId)
                Report("I4", awaiter + " completed with the outcome of request #" + result.RequestId + ".");

            awaiter.RequestId = result.RequestId;

            Ledger ledger;
            if (!_ledgers.TryGetValue(result.RequestId, out ledger) || !ledger.HasTerminal)
            {
                Report("I4", awaiter + " completed before request #" + result.RequestId + " published its terminal event.");
                return;
            }

            CompareOutcome(awaiter.ToString(), ledger.Terminal, result.WasShown, result.Reason, result.Rejected);

            if (result.HasValue != (result.Reason == CloseReason.Completed))
                Report("I4", awaiter + " has HasValue=" + result.HasValue + " with reason " + result.Reason + ".");
        }

        private void VerifyWait(InvariantAwaiter awaiter, UniTaskStatus status)
        {
            if (status == UniTaskStatus.Canceled)
            {
                if (awaiter.Waiter == null || !awaiter.Waiter.IsCancellationRequested)
                    Report("I4", awaiter + " was cancelled but its waiter token was not; only the waiter's own token may cancel it.");
                return;
            }

            if (status != UniTaskStatus.Succeeded)
            {
                Report("I4", awaiter + " ended " + status + "; WaitForCloseAsync throws only when its own token is cancelled.");
                return;
            }

            var closed = awaiter.Wait.GetAwaiter().GetResult();
            awaiter.WaitResult = closed;

            Ledger ledger;
            if (awaiter.RequestId == 0 || !_ledgers.TryGetValue(awaiter.RequestId, out ledger) || !ledger.HasTerminal)
            {
                Report("I4", awaiter + " completed before its request published a terminal event.");
                return;
            }

            CompareOutcome(awaiter.ToString(), ledger.Terminal, closed.WasShown, closed.Reason, closed.Rejected);
        }

        // The awaiter outcome and the published event describe the same end.
        private void CompareOutcome(string who, ViewEvent terminal, bool wasShown, CloseReason reason, RejectReason rejected)
        {
            if (terminal.Kind == ViewEventKind.Closed)
            {
                if (!wasShown || reason != terminal.Reason || rejected != RejectReason.None)
                {
                    Report("I4", who + " got (shown " + wasShown + ", " + reason + ", " + rejected + ") but Closed for #" + terminal.RequestId
                        + " says " + terminal.Reason + ".");
                }

                return;
            }

            if (wasShown || reason != CloseReason.NotShown || rejected != terminal.Rejected)
            {
                Report("I4", who + " got (shown " + wasShown + ", " + reason + ", " + rejected + ") but NotShown for #" + terminal.RequestId
                    + " says " + terminal.Rejected + ".");
            }
        }

        // Q1 to Q5, from the events of both publishers and what the recording gate saw at presentation

        private void CheckQueue()
        {
            CheckPresentations();

            var presented = 0;
            var ids = new StringBuilder();
            foreach (var pair in _world.QueueItems)
            {
                var item = pair.Value;
                if (item.State == InvariantQueueItemState.Presented)
                {
                    presented++;
                    ids.Append(" #").Append(item.RequestId);
                }

                if (item.Terminals > 1)
                    Report("Q3", "Queued request #" + item.RequestId + " (" + item.ViewId + ") ended " + item.Terminals + " times.");
            }

            if (presented > 1)
                Report("Q1", presented + " queued items are presented at once:" + ids + ".");

            CheckQueueHolds();
            CheckGateRegistrations();
            CheckProgress();
        }

        // Q2 and Q4 for every item the queue handed to the navigator since the last check; Q1 at that moment too.
        private void CheckPresentations()
        {
            foreach (var pair in _world.QueueItems)
            {
                var item = pair.Value;
                if (!item.ReachedNavigator || item.PresentationChecked)
                    continue;

                item.PresentationChecked = true;

                // The check tied to this presentation; when none was tied (Q2.atomic), the newest one for the id is still held to Q2.
                var moment = item.Presentation;
                if (moment == null && !_world.Presentations.TryGetValue(item.RequestId, out moment))
                {
                    Report("X.gates", "Queued request #" + item.RequestId + " (" + item.ViewId + ") reached the navigator without the gates being asked at presentation.");
                    continue;
                }

                if (moment.Unknown)
                    Report("X.gates", "The gates were asked at presentation about #" + item.RequestId + ", which the queue never published Enqueued for.");

                if (moment.QueueHeld || moment.LiveHolds > 0)
                {
                    Report("Q2", "Queued request #" + item.RequestId + " (" + item.ViewId + ") was presented at step " + moment.Step + " while held (IsHeld "
                        + moment.QueueHeld + ", live holds " + moment.LiveHolds + ").");
                }

                // Clear by IsLayerClear, by the navigator's own counters and by the test's model, which reads neither.
                if (!moment.NavigatorClear || !moment.StateClear || !moment.ModelClear)
                {
                    Report("Q2", "Queued request #" + item.RequestId + " (" + item.ViewId + ") was presented at step " + moment.Step + " while the layer was not clear (IsLayerClear "
                        + moment.NavigatorClear + "; navigator counters: busy " + moment.Busy + ", popup requests " + moment.LayerRequests + ", modal view above " + moment.ModalAbove
                        + "; test model: busy " + moment.ModelBusy + (moment.ModelBusyWhy != null ? " (" + moment.ModelBusyWhy + ")" : string.Empty)
                        + ", popup requests " + moment.LedgerPopups + ", modal request above " + moment.LedgerModalAbove + ").");
                }

                if (moment.InFlight > 0)
                    Report("Q1", "Queued request #" + item.RequestId + " (" + item.ViewId + ") was presented while " + moment.InFlight + " other queued item(s) were still presented.");

                if (moment.Better != null)
                {
                    Report("Q4", "Queued request #" + item.RequestId + " (" + item.ViewId + ", priority " + item.Priority + ", seq " + item.Sequence
                        + ") was presented although " + moment.Better + " was waiting.");
                }
            }
        }

        // Holds against the model: an ended hold must not keep the queue held (Q5); a live one must hold it.
        private void CheckQueueHolds()
        {
            var live = _world.LiveQueueHoldCount;
            var held = _world.Queue.IsHeld;

            if (held && live == 0)
                Report("Q5", "The queue is held but every queue hold was removed or its owner ended.");
            else if (!held && live > 0)
                Report("X.qhold", "The queue is not held although " + live + " queue hold(s) are live.");

            var holds = _world.QueueHolds;
            for (var i = 0; i < holds.Count; i++)
            {
                var hold = holds[i];
                var active = hold.Registration.IsActive;
                var modelLive = _world.IsQueueHoldLive(hold);

                if (active && !modelLive)
                    Report("Q5", "Queue hold #" + hold.Number + " is still active although it was removed or its owner, the queue lifetime or the root ended.");
                else if (!active && modelLive)
                    Report("X.qhold", "Queue hold #" + hold.Number + " is inactive although it was not removed and its owner is live.");
            }
        }

        // Registration owner: a removed gate, or one whose owner, queue lifetime or root ended, is no longer registered.
        private void CheckGateRegistrations()
        {
            var gates = _world.AllGates;
            for (var i = 0; i < gates.Count; i++)
            {
                var gate = gates[i];
                var active = gate.Registration.IsActive;
                var live = _world.IsGateLive(gate);

                if (active && !live)
                    Report("X.gate", gate + " is still registered although it was removed or its owner, the queue lifetime or the root ended.");
                else if (!active && live)
                    Report("X.gate", gate + " is not registered although it was not removed and its owner is live.");
            }
        }

        // Pump rules: every trigger schedules a pump, so while items wait with no hold, nothing presented and the layer clear, a tick is pending.
        private void CheckProgress()
        {
            if (_world.IsQueueEnded)
                return;

            int waiting;
            int presented;
            CountQueueModel(out waiting, out presented);

            if (waiting == 0 || presented > 0 || _world.Queue.IsHeld || _world.LiveQueueHoldCount > 0)
                return;

            if (!_world.Navigator.IsLayerClear(ViewLayers.Popups) || _world.Ticker.PendingTicks > 0)
                return;

            Report("X.progress.tick", waiting + " queued item(s) wait, the queue is not held, nothing is presented and the popups layer is clear, but no pump is scheduled.");
        }

        private void CountQueueModel(out int waiting, out int presented)
        {
            waiting = 0;
            presented = 0;
            foreach (var pair in _world.QueueItems)
            {
                if (pair.Value.State == InvariantQueueItemState.Waiting)
                    waiting++;
                else if (pair.Value.State == InvariantQueueItemState.Presented)
                    presented++;
            }
        }

        // Model of "busy" for Q2 that reads nothing the navigator computes: a live load or transition on any layer, or a live input hold.
        public bool ModelBusy(out string why)
        {
            SyncStage();

            for (var i = 0; i < _pendingAcquires.Count; i++)
            {
                if (!_pendingAcquires[i].Token.IsCancellationRequested)
                {
                    why = "load of '" + _pendingAcquires[i].ViewId + "'";
                    return true;
                }
            }

            for (var i = 0; i < _pendingPlays.Count; i++)
            {
                if (!_pendingPlays[i].Token.IsCancellationRequested)
                {
                    why = "transition of " + _pendingPlays[i].Instance;
                    return true;
                }
            }

            var holds = _world.LiveHoldCount;
            if (holds > 0)
            {
                why = holds + " input hold(s)";
                return true;
            }

            why = null;
            return false;
        }

        // Queue calls that end items; the driver calls these right after the call, before CheckStep

        // An owner already cancelled ends the request as NotShown / OwnerEnded; nothing enters the queue.
        public void CheckEnqueueCall(InvariantEnqueueCall call, int eventsBefore)
        {
            if (!call.OwnerCancelledAtCall)
                return;

            // A call that published nothing is reported as X.enqueue.
            InvariantQueueItem item;
            if (!_world.QueueItems.TryGetValue(call.RequestId, out item))
                return;

            var what = "Enqueue call " + call.Number + " (" + call.ViewId + ") on an owner already cancelled";
            if (item.Sequence != 0)
                Report("X.owner", what + " published Enqueued for #" + item.RequestId + ".");

            if (item.ReachedNavigator)
                Report("X.owner", what + " reached the navigator as #" + item.RequestId + ".");

            ExpectQueueEnd(item, eventsBefore, RejectReason.OwnerEnded, "X.owner", what);
        }

        // A waiting item ends when its owner ends, also while the queue is held.
        public void CheckOwnerEnded(InvariantQueueMark mark)
        {
            for (var i = 0; i < mark.Waiting.Count; i++)
                ExpectQueueEnd(mark.Waiting[i], mark.EventIndex, RejectReason.OwnerEnded, "X.owner", "After its owner was cancelled");
        }

        // Clear ends every waiting item as NotShown / Cancelled; a presented item leaves Presenting only when its request ends.
        public void CheckCleared(InvariantQueueMark mark)
        {
            if (_world.Queue.Count != 0)
                Report("X.clear", "Count is " + _world.Queue.Count + " right after Clear.");

            for (var i = 0; i < mark.Waiting.Count; i++)
                ExpectQueueEnd(mark.Waiting[i], mark.EventIndex, RejectReason.Cancelled, "X.clear", "Clear");

            ExpectPresentedUntouched(mark, "X.clear", "Clear");
        }

        // Queue end: waiting items end as NotShown with the expected rejection (Cancelled for the lifetime, RootGone for the root);
        // while the root lives a presented view is left to its own owner.
        public void CheckLifetimeEnded(InvariantQueueMark mark, bool rootAlive, RejectReason expected)
        {
            var byRoot = expected == RejectReason.RootGone;
            var what = byRoot ? "The destruction of the root" : "The end of the queue's lifetime";
            var id = byRoot ? "X.root" : "X.lifetime";

            if (_world.Queue.Count != 0)
                Report(id, what + " left Count at " + _world.Queue.Count + ".");

            for (var i = 0; i < mark.Waiting.Count; i++)
                ExpectQueueEnd(mark.Waiting[i], mark.EventIndex, expected, id, what);

            if (rootAlive)
                ExpectPresentedUntouched(mark, id, what);

            CheckFaults();
        }

        // After the finish drain (every load, transition and pending tick done): items do not stay waiting while the queue may present.
        public void CheckProgressFinal()
        {
            DrainWorldViolations();

            if (_world.IsQueueEnded)
                return;

            int waiting;
            int presented;
            CountQueueModel(out waiting, out presented);

            var count = _world.Queue.Count;
            if ((count == 0 && waiting == 0) || presented > 0 || _world.Queue.IsHeld)
                return;

            if (!_world.Navigator.IsLayerClear(ViewLayers.Popups))
                return;

            Report("X.progress", "After the drain " + count + " item(s) are in the queue (" + waiting + " waiting in the model), the queue is not held, nothing is presented"
                + " and the popups layer is clear, yet nothing was presented.");
        }

        // The item ended during the call, by a queue NotShown with the expected rejection, and its EnqueueAsync completed.
        private void ExpectQueueEnd(InvariantQueueItem item, int fromEvent, RejectReason expected, string id, string what)
        {
            var name = "queued #" + item.RequestId + " (" + item.ViewId + ")";
            if (item.TerminalIndex < 0)
            {
                Report(id, what + ": " + name + " did not end; it must end as NotShown / " + expected + ".");
            }
            else if (item.TerminalIndex < fromEvent)
            {
                Report(id, what + ": " + name + " had already ended " + DescribeTerminal(item) + " before the call.");
            }
            else if (!item.TerminalFromQueue || item.TerminalKind != ViewEventKind.NotShown || item.TerminalRejected != expected)
            {
                Report(id, what + ": " + name + " ended " + DescribeTerminal(item) + "; expected a queue NotShown / " + expected + ".");
            }

            var awaiter = item.Call != null ? item.Call.Awaiter : null;
            if (awaiter != null && !awaiter.Verified && awaiter.Status == UniTaskStatus.Pending)
                Report(id, what + ": the EnqueueAsync of " + name + " is still pending.");
        }

        // The presented request got no terminal event from the call, is still in the popups list and its EnqueueAsync still waits for it.
        private void ExpectPresentedUntouched(InvariantQueueMark mark, string id, string what)
        {
            if (mark.Presented.Count == 0)
                return;

            InvariantSnapshot snapshot;
            try
            {
                snapshot = InvariantSnapshot.Parse(_world.Harness.Describe());
            }
            catch (FormatException exception)
            {
                Report("M.snapshot", exception.Message);
                return;
            }

            for (var i = 0; i < mark.Presented.Count; i++)
            {
                var item = mark.Presented[i];
                var name = "presented #" + item.RequestId + " (" + item.ViewId + ")";

                if (item.TerminalIndex >= mark.EventIndex)
                {
                    Report(id, what + " ended " + name + " " + DescribeTerminal(item) + "; a presented view is left to its own owner.");
                    continue;
                }

                var record = snapshot.Find(item.RequestId);
                if (record == null || !string.Equals(record.LayerId, ViewLayers.Popups.Id, StringComparison.Ordinal))
                    Report(id, what + " removed " + name + " from the popups list.");

                var awaiter = item.Call != null ? item.Call.Awaiter : null;
                if (awaiter != null && !awaiter.Verified && awaiter.Status != UniTaskStatus.Pending)
                    Report(id, what + " completed the EnqueueAsync of " + name + " although its request has not ended.");
            }
        }

        // Q3 at the end: every enqueued item ended exactly once.
        private void CheckQueueItemsFinal()
        {
            foreach (var pair in _world.QueueItems)
            {
                var item = pair.Value;
                if (item.Terminals != 1)
                    Report("Q3", "Queued request #" + item.RequestId + " (" + item.ViewId + ") ended " + item.Terminals + " times by the end of the run.");
            }

            var calls = _world.EnqueueCalls;
            for (var i = 0; i < calls.Count; i++)
            {
                var call = calls[i];
                if (call.RequestId != 0 && !_world.QueueItems.ContainsKey(call.RequestId))
                    Report("Q3", "Enqueue call " + call.Number + " (" + call.ViewId + ", #" + call.RequestId + ") never published an event, so it never ended.");
            }
        }

        // Final

        private void CheckLedgersFinal()
        {
            foreach (var pair in _ledgers)
            {
                var ledger = pair.Value;
                if (!ledger.HasTerminal)
                    Report("I6", "Request #" + ledger.RequestId + " (" + ledger.ViewId + ") never published Closed or NotShown: " + Join(ledger.Kinds) + ".");
            }
        }

        // Hooks paired: every scope that began ended.
        private void CheckTracesFinal()
        {
            foreach (var pair in _world.Traces)
            {
                var trace = pair.Value;
                if (trace.OpenEnds != trace.OpenCalls)
                    Report("I3", "Request #" + trace.RequestId + " ended with " + trace.OpenCalls + " open(s) and " + trace.OpenEnds + " open scope end(s).");
                if (trace.VisibleEnds != trace.VisibleBegins)
                    Report("I3", "Request #" + trace.RequestId + " ended with " + trace.VisibleBegins + " visible begin(s) and " + trace.VisibleEnds + " end(s).");
            }
        }

        private static string Join(List<ViewEventKind> kinds)
        {
            if (kinds.Count == 0)
                return "[]";

            var text = new StringBuilder("[");
            for (var i = 0; i < kinds.Count; i++)
            {
                if (i > 0)
                    text.Append(", ");
                text.Append(kinds[i]);
            }

            return text.Append(']').ToString();
        }
    }
}
