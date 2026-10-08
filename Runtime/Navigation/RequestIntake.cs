using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    internal enum IntakeKind
    {
        // Refused at the call; nothing was added, no shield, no load.
        Rejected,

        // A new record was added to its layer in Waiting.
        Accepted,

        // History layer, key without arguments: the caller joins Existing; nothing new was created.
        ReturnToExisting,

        // History layer, key with arguments: Record is the new request, Existing is the one it replaces.
        ReopenInPlace
    }

    // The answer of RequestIntake.Submit; a struct so the reject path allocates nothing.
    internal readonly struct IntakeDecision
    {
        public readonly IntakeKind Kind;
        public readonly int RequestId;
        public readonly int LayerIndex;

        // The new record for Accepted and ReopenInPlace; null otherwise.
        public readonly ViewRecord Record;

        // The record already in the stack for ReturnToExisting and ReopenInPlace; null otherwise.
        public readonly ViewRecord Existing;

        public readonly RejectReason Reason;
        public readonly string Detail;

        private IntakeDecision(
            IntakeKind kind,
            int requestId,
            int layerIndex,
            ViewRecord record,
            ViewRecord existing,
            RejectReason reason,
            string detail)
        {
            Kind = kind;
            RequestId = requestId;
            LayerIndex = layerIndex;
            Record = record;
            Existing = existing;
            Reason = reason;
            Detail = detail;
        }

        public bool IsRejected
        {
            get { return Kind == IntakeKind.Rejected; }
        }

        public static IntakeDecision Rejected(int requestId, RejectReason reason, string detail)
        {
            return new IntakeDecision(IntakeKind.Rejected, requestId, -1, null, null, reason, detail);
        }

        public static IntakeDecision Accepted(int requestId, int layerIndex, ViewRecord record)
        {
            return new IntakeDecision(IntakeKind.Accepted, requestId, layerIndex, record, null, RejectReason.None, null);
        }

        public static IntakeDecision ReturnTo(int requestId, int layerIndex, ViewRecord existing)
        {
            return new IntakeDecision(IntakeKind.ReturnToExisting, requestId, layerIndex, null, existing, RejectReason.None, null);
        }

        public static IntakeDecision Reopen(int requestId, int layerIndex, ViewRecord record, ViewRecord existing)
        {
            return new IntakeDecision(IntakeKind.ReopenInPlace, requestId, layerIndex, record, existing, RejectReason.None, null);
        }
    }

    // What intake needs from the navigator; the navigator implements it.
    internal interface IIntakeHost
    {
        bool IsRootGone { get; }

        // True while the navigator is not settled: an operation runs or an input hold stands.
        bool IsBusy { get; }

        // Navigator clock time in seconds, the same time base the records use.
        double Now { get; }

        // Runs once per record, when its first outcome is latched (handed to the record as its callback).
        void OnOutcomeLatched(ViewRecord record);

        // The owner token of an accepted record was cancelled; may arrive on any thread, never throw out of it.
        void OnOwnerCancelled(ViewRecord record);
    }

    // Accepts or rejects one open request synchronously at the call; it owns the request id and accept sequence counters.
    internal sealed class RequestIntake
    {
        public const int DefaultWaitingCap = 4;

        private const string RootGoneDetail = "The root was destroyed.";
        private const string OwnerEndedDetail = "The owner token was already cancelled.";
        private const string BusyDetail = "The navigator is busy and the call asked not to wait.";

        private readonly ViewRegistry _registry;
        private readonly IReadOnlyList<LayerState<ViewRecord>> _layers;
        private readonly InputShield _shield;
        private readonly Outbox _outbox;
        private readonly Diagnostics _diagnostics;
        private readonly ErrorRouter _errors;
        private readonly IViewStage _stage;
        private readonly INavigator _navigator;
        private readonly int _sortingBase;
        private readonly IIntakeHost _host;
        private readonly int _waitingCap;
        private readonly Action<ViewRecord> _onOutcomeLatched;
        private readonly Action<object> _ownerCancelled;
        private int _nextRequestId = 1;
        private long _acceptSequence;

        // layers[i] is the state of registry.LayerAt(i); sortingBase is the root's base sorting order.
        public RequestIntake(
            ViewRegistry registry,
            IReadOnlyList<LayerState<ViewRecord>> layers,
            InputShield shield,
            Outbox outbox,
            Diagnostics diagnostics,
            ErrorRouter errors,
            IViewStage stage,
            INavigator navigator,
            int sortingBase,
            IIntakeHost host,
            int waitingCap = DefaultWaitingCap)
        {
            _registry = registry;
            _layers = layers;
            _shield = shield;
            _outbox = outbox;
            _diagnostics = diagnostics;
            _errors = errors;
            _stage = stage;
            _navigator = navigator;
            _sortingBase = sortingBase;
            _host = host;
            _waitingCap = waitingCap > 0 ? waitingCap : 1;
            _onOutcomeLatched = host.OnOutcomeLatched;
            _ownerCancelled = OnOwnerCancelledCallback;
        }

        // Hands out an id that OpenOptions.RequestId can carry into Submit.
        public int ReserveRequestId()
        {
            return _nextRequestId++;
        }

        // Runs the checks in order; on success adds the record to its layer. The caller queues the operation and starts the runner.
        public IntakeDecision Submit<TArgs, TResult>(
            in ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            in OpenOptions options,
            string callerMember,
            int callerLine)
        {
            var viewId = key.Id;
            var source = options.Source;
            var priority = options.Priority;
            var requestId = options.RequestId != 0 ? options.RequestId : _nextRequestId++;

            if (_host.IsRootGone)
                return Reject(requestId, viewId, null, source, priority, RejectReason.RootGone, RootGoneDetail);

            if (owner.IsCancellationRequested)
                return Reject(requestId, viewId, null, source, priority, RejectReason.OwnerEnded, OwnerEndedDetail);

            ViewEntry row;
            if (!_registry.TryGetRow(viewId, out row))
            {
                var detail = "No catalog row has the id '" + viewId + "'.";
                _diagnostics.Error(DiagnosticIds.UnknownViewId, detail);
                return Reject(requestId, viewId, null, source, priority, RejectReason.UnknownKey, detail);
            }

            int layerIndex;
            if (!_registry.TryGetLayerIndex(row.LayerId, out layerIndex) || layerIndex >= _layers.Count)
            {
                var detail = "The row '" + viewId + "' names the unknown layer '" + row.LayerId + "'.";
                _diagnostics.Error(DiagnosticIds.UnknownLayer, detail);
                return Reject(requestId, viewId, null, source, priority, RejectReason.UnknownKey, detail);
            }

            var state = _layers[layerIndex];
            var layer = state.Layer;

            if (options.ExpectedLayerId != null && !string.Equals(options.ExpectedLayerId, layer.Id, StringComparison.Ordinal))
            {
                var detail = "The row '" + viewId + "' is on layer '" + layer.Id + "', not on the queue's layer '" + options.ExpectedLayerId + "'.";
                _diagnostics.Error(DiagnosticIds.QueueLayerMismatch, detail);
                return Reject(requestId, viewId, options.ExpectedLayerId, source, priority, RejectReason.WrongLayer, detail);
            }

            // Duplicates: a history layer returns to the id in its stack, any other layer refuses.
            ViewRecord existing = null;
            if (!row.AllowMultiple)
            {
                existing = FindLive(state, viewId);
                if (existing != null && !layer.Has(ViewLayerRules.History))
                {
                    var detail = "The id '" + viewId + "' is already open or in flight on layer '" + layer.Id + "'.";
                    return Reject(requestId, viewId, layer.Id, source, priority, RejectReason.Duplicate, detail);
                }
            }

            if (options.RejectWhenBusy && _host.IsBusy)
                return Reject(requestId, viewId, layer.Id, source, priority, RejectReason.Busy, BusyDetail);

            // Nothing new is created, so the cap and the sort band do not apply.
            if (existing != null && typeof(TArgs) == typeof(NoArgs))
                return IntakeDecision.ReturnTo(requestId, layerIndex, existing);

            if (CountWaiting(state) >= _waitingCap)
            {
                var detail = "Layer '" + layer.Id + "' already has " + _waitingCap + " requests waiting.";
                _diagnostics.Warn(DiagnosticIds.WaitingCapReached, detail);
                return Reject(requestId, viewId, layer.Id, source, priority, RejectReason.Busy, detail);
            }

            var slot = state.NextSlot();
            if (!Projection.FitsBand(_sortingBase, layer.Order, slot)
                || !Projection.ShieldFits(_sortingBase, _registry.HighestLayerOrder()))
            {
                var detail = "The sort band of layer '" + layer.Id + "' has no free slot (next slot " + slot + ", base " + _sortingBase + ").";
                _diagnostics.Error(DiagnosticIds.SortBandOverflow, detail);
                return Reject(requestId, viewId, layer.Id, source, priority, RejectReason.Busy, detail);
            }

            // Accepted.
            var record = new ViewRecord<TArgs, TResult>(
                requestId,
                viewId,
                row,
                layerIndex,
                ++_acceptSequence,
                _host.Now,
                source,
                callerMember,
                callerLine,
                args,
                _navigator,
                _stage,
                _errors,
                _onOutcomeLatched);
            record.Priority = priority;

            state.Add(record, slot);

            if (layer.Has(ViewLayerRules.ShieldsInput))
                record.ShieldHolderId = _shield.Acquire(ShieldHolderKind.Operation, requestId, viewId, callerMember);

            var requested = new ViewEvent(
                ViewEventKind.Requested,
                requestId,
                viewId,
                layer.Id,
                source,
                priority,
                default(CloseReason),
                RejectReason.None,
                null,
                0f,
                0f);
            _outbox.PublishOrDefer(in requested);

            // Registered last: a token that fires inside Register finds the shield in place, and Requested unless it was held back.
            RegisterOwner(record, owner);

            return existing != null
                ? IntakeDecision.Reopen(requestId, layerIndex, record, existing)
                : IntakeDecision.Accepted(requestId, layerIndex, record);
        }

        private IntakeDecision Reject(
            int requestId,
            string viewId,
            string layerId,
            string source,
            int priority,
            RejectReason reason,
            string detail)
        {
            var notShown = new ViewEvent(
                ViewEventKind.NotShown,
                requestId,
                viewId,
                layerId,
                source,
                priority,
                CloseReason.NotShown,
                reason,
                detail,
                0f,
                0f);
            _outbox.PublishOrDefer(in notShown);

            return IntakeDecision.Rejected(requestId, reason, detail);
        }

        // The topmost record with the id that has no latched outcome yet.
        private static ViewRecord FindLive(LayerState<ViewRecord> state, string viewId)
        {
            for (var i = state.Count - 1; i >= 0; i--)
            {
                var record = state[i];
                // A record whose close is latched, or that a newer request is replacing, does not count.
                if (record.HasOutcome || record.Phase == ViewPhase.Ended || record.BeingReplaced)
                    continue;

                if (string.Equals(record.ViewId, viewId, StringComparison.Ordinal))
                    return record;
            }

            return null;
        }

        private static int CountWaiting(LayerState<ViewRecord> state)
        {
            var count = 0;
            for (var i = 0; i < state.Count; i++)
            {
                if (state[i].Phase == ViewPhase.Waiting)
                    count++;
            }

            return count;
        }

        private void RegisterOwner(ViewRecord record, CancellationToken owner)
        {
            // A record an observer already ended must not hold a registration on a long-lived token.
            if (!owner.CanBeCanceled || record.Phase == ViewPhase.Ended)
                return;

            var registration = owner.RegisterWithoutCaptureExecutionContext(_ownerCancelled, record);
            record.SetOwnerRegistration(registration);
        }

        // Runs on the thread that cancels the token; the host decides how to marshal.
        private void OnOwnerCancelledCallback(object state)
        {
            try
            {
                _host.OnOwnerCancelled((ViewRecord)state);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("owner cancelled", exception);
            }
        }
    }
}
