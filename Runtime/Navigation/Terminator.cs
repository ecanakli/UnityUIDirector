using System;
using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    // What the terminator needs from the navigator: facts and steps only the navigator can do.
    internal interface ITerminatorHost
    {
        // The navigator clock in seconds, without suspended time.
        double Now { get; }

        // Takes the record out of its own layer list.
        void RemoveFromLayer(ViewRecord record);

        // Drops the shield hold with this id.
        void ReleaseShield(int holderId);

        // The record's instance state as hidden and not interactive, keeping its sorting order and covered mode.
        ViewVisualState HiddenStateOf(ViewRecord record);

        // Recomputes derived state, applies it to the stage and appends the records that became visible to revealed.
        void RecomputeAndApply(List<ViewRecord> revealed);

        // Starts the visible scope and calls OnVisible; the host re-checks the record is still live, visible and has no visible scope.
        void BeginVisible(ViewRecord record);

        // The id of the layer the record is on, for events.
        string LayerIdOf(ViewRecord record);

        // The queue priority of the record, 0 when it did not come from a queue.
        int PriorityOf(ViewRecord record);

        // True once per busy-to-settled change: settled now and Settled not yet published since it was last busy.
        bool TryTakeSettled();

        // True while the row is still registered; an instance of a removed row is released, never kept.
        bool IsRowRegistered(ViewEntry row);

        // The record ended before it replaced the request it was taking over: that request is live again; adds it to revealed when user code may see it.
        void ReleaseReplaced(ViewRecord record, List<ViewRecord> revealed);
    }

    // The one synchronous routine that ends a record, for a finalize and for an immediate terminate; it never throws.
    internal sealed class Terminator
    {
        private readonly IViewStage _stage;
        private readonly ErrorRouter _errors;
        private readonly ObserverList _observers;
        private readonly RetainedInstances _retained;
        private readonly Outbox _outbox;
        private readonly ITerminatorHost _host;
        private readonly Stack<List<ViewRecord>> _revealedPool = new Stack<List<ViewRecord>>();
        private readonly Action<object> _endVisibleScope;
        private readonly Action<object> _endOpenScope;
        private readonly Action<object> _releaseInstance;
        private readonly Action<object> _beginVisible;
        private readonly Action<object> _publishOwedShown;
        private readonly Action<object> _publishOutcome;
        private readonly Action<object> _completeAwaiters;
        private readonly Action<object> _publishSettled;

        public Terminator(
            IViewStage stage,
            ErrorRouter errors,
            ObserverList observers,
            RetainedInstances retained,
            Outbox outbox,
            ITerminatorHost host)
        {
            _stage = stage;
            _errors = errors;
            _observers = observers;
            _retained = retained;
            _outbox = outbox;
            _host = host;

            _endVisibleScope = EndVisibleScope;
            _endOpenScope = EndOpenScope;
            _releaseInstance = ReleaseInstance;
            _beginVisible = BeginVisible;
            _publishOwedShown = PublishOwedShown;
            _publishOutcome = PublishOutcome;
            _completeAwaiters = CompleteAwaiters;
            _publishSettled = PublishSettled;
        }

        // Ends the record once; the cause is latched only when no outcome is latched yet.
        public void End(ViewRecord record, CloseReason cause)
        {
            if (record == null || record.Phase == ViewPhase.Ended)
                return;

            var previousPhase = record.Phase;
            record.TryLatchClose(cause);
            if (!record.TryAdvancePhase(ViewPhase.Ended))
                return;

            var revealed = RentList();
            ApplyState(record, previousPhase, revealed);
            EnqueueNotifications(record, revealed);
            ReturnList(revealed);

            _outbox.Run();
        }

        // State first: no user code runs here.
        private void ApplyState(ViewRecord record, ViewPhase previousPhase, List<ViewRecord> revealed)
        {
            try
            {
                _host.RemoveFromLayer(record);
            }
            catch (Exception exception)
            {
                Fault(record, exception);
            }

            try
            {
                _host.ReleaseReplaced(record, revealed);
            }
            catch (Exception exception)
            {
                Fault(record, exception);
            }

            record.CancelStep();

            var instance = record.Instance;
            if (instance != null && previousPhase >= ViewPhase.Opening)
            {
                try
                {
                    var hidden = _host.HiddenStateOf(record);
                    _stage.Apply(instance, in hidden);
                }
                catch (Exception exception)
                {
                    Fault(record, exception);
                }
            }

            record.DisposeOwnerRegistration();

            var holderId = record.ShieldHolderId;
            var closeHolderId = record.CloseHolderId;
            record.ShieldHolderId = 0;
            record.CloseHolderId = 0;
            ReleaseShield(record, holderId);
            ReleaseShield(record, closeHolderId);

            try
            {
                _host.RecomputeAndApply(revealed);
            }
            catch (Exception exception)
            {
                Fault(record, exception);
            }
        }

        private void EnqueueNotifications(ViewRecord record, List<ViewRecord> revealed)
        {
            var viewId = record.ViewId;
            var requestId = record.RequestId;

            _outbox.Enqueue(_endVisibleScope, record, ViewErrorSource.ViewHook, viewId, requestId);
            _outbox.Enqueue(_endOpenScope, record, ViewErrorSource.ViewHook, viewId, requestId);
            _outbox.Enqueue(_releaseInstance, record, ViewErrorSource.Provider, viewId, requestId);

            for (var i = 0; i < revealed.Count; i++)
            {
                var other = revealed[i];
                if (other == null || other == record)
                    continue;

                _outbox.Enqueue(_beginVisible, other, ViewErrorSource.ViewHook, other.ViewId, other.RequestId);
            }

            // Ended after it entered Opening but before Shown went out: Shown comes right before Closed, so observers never see Closed without Shown.
            if (record.WasShown && !record.ShownPublished)
                _outbox.Enqueue(_publishOwedShown, record, ViewErrorSource.Observer, viewId, requestId);

            _outbox.Enqueue(_publishOutcome, record, ViewErrorSource.Observer, viewId, requestId);
            _outbox.Enqueue(_completeAwaiters, record, ViewErrorSource.ViewHook, viewId, requestId);
            _outbox.Enqueue(_publishSettled, null, ViewErrorSource.Observer, null, 0);
        }

        // Notifications

        private void EndVisibleScope(object state)
        {
            var scope = ((ViewRecord)state).VisibleScope;
            if (scope != null)
                scope.End();
        }

        private void EndOpenScope(object state)
        {
            var scope = ((ViewRecord)state).OpenScope;
            if (scope != null)
                scope.End();
        }

        private void ReleaseInstance(object state)
        {
            var record = (ViewRecord)state;
            var instance = record.Instance;
            if (instance == null)
                return;

            record.Instance = null;

            if (record.Row.Retention == ViewRetention.KeepInstance
                && !record.InstanceLost
                && _host.IsRowRegistered(record.Row)
                && _retained.TryStash(record.ViewId, instance))
            {
                try
                {
                    _stage.Stash(instance);
                    return;
                }
                catch (Exception exception)
                {
                    Fault(record, exception, ViewErrorSource.Provider);
                    string droppedId;
                    _retained.TryDrop(instance, out droppedId);
                }
            }

            _stage.Release(instance);
        }

        private void BeginVisible(object state)
        {
            var record = (ViewRecord)state;
            if (record.Phase == ViewPhase.Ended)
                return;

            _host.BeginVisible(record);
        }

        // Publishes Shown once per record; its seconds run from acceptance to this moment, and SecondsOpen starts here.
        public void PublishShown(ViewRecord record)
        {
            var now = _host.Now;
            if (!record.TryMarkShownPublished(now))
                return;

            var shown = new ViewEvent(
                ViewEventKind.Shown,
                record.RequestId,
                record.ViewId,
                _host.LayerIdOf(record),
                record.SourceTag,
                _host.PriorityOf(record),
                default(CloseReason),
                RejectReason.None,
                null,
                (float)record.SecondsWaiting(now),
                0f);
            _observers.Publish(in shown);
        }

        private void PublishOwedShown(object state)
        {
            PublishShown((ViewRecord)state);
        }

        private void PublishOutcome(object state)
        {
            var record = (ViewRecord)state;

            CloseReason reason;
            RejectReason rejected;
            string detail;
            ResolveOutcome(record, out reason, out rejected, out detail);

            var layerId = _host.LayerIdOf(record);
            var priority = _host.PriorityOf(record);

            ViewEvent viewEvent;
            if (record.WasShown)
            {
                var secondsOpen = (float)record.SecondsOpen(_host.Now);
                viewEvent = new ViewEvent(
                    ViewEventKind.Closed,
                    record.RequestId,
                    record.ViewId,
                    layerId,
                    record.SourceTag,
                    priority,
                    reason,
                    rejected,
                    detail,
                    0f,
                    secondsOpen);
            }
            else
            {
                viewEvent = new ViewEvent(
                    ViewEventKind.NotShown,
                    record.RequestId,
                    record.ViewId,
                    layerId,
                    record.SourceTag,
                    priority,
                    reason,
                    rejected,
                    detail,
                    0f,
                    0f);
            }

            _observers.Publish(in viewEvent);
        }

        private void CompleteAwaiters(object state)
        {
            var record = (ViewRecord)state;
            try
            {
                record.CompleteAwaiters();
            }
            finally
            {
                record.ReleaseReferences();
            }
        }

        private void PublishSettled(object state)
        {
            if (!_host.TryTakeSettled())
                return;

            var viewEvent = new ViewEvent(
                ViewEventKind.Settled,
                0,
                null,
                null,
                null,
                0,
                default(CloseReason),
                RejectReason.None,
                null,
                0f,
                0f);
            _observers.Publish(in viewEvent);
        }

        // Helpers

        // Mirrors ViewRecord.ResolveFinalOutcome so the event and the awaiters agree.
        private static void ResolveOutcome(ViewRecord record, out CloseReason reason, out RejectReason rejected, out string detail)
        {
            detail = record.OutcomeDetail;

            if (record.WasShown)
            {
                rejected = RejectReason.None;
                reason = record.HasOutcome && record.OutcomeReason != CloseReason.NotShown ? record.OutcomeReason : CloseReason.Code;
                return;
            }

            reason = CloseReason.NotShown;
            rejected = record.OutcomeRejected;
            if (rejected == RejectReason.None)
                rejected = record.HasOutcome ? CauseToRejection(record.OutcomeReason) : RejectReason.Cancelled;
        }

        private static RejectReason CauseToRejection(CloseReason cause)
        {
            switch (cause)
            {
                case CloseReason.OwnerEnded:
                    return RejectReason.OwnerEnded;
                case CloseReason.Destroyed:
                    return RejectReason.RootGone;
                default:
                    return RejectReason.Cancelled;
            }
        }

        private void ReleaseShield(ViewRecord record, int holderId)
        {
            if (holderId == 0)
                return;

            try
            {
                _host.ReleaseShield(holderId);
            }
            catch (Exception exception)
            {
                Fault(record, exception);
            }
        }

        private void Fault(ViewRecord record, Exception exception)
        {
            Fault(record, exception, ViewErrorSource.Transition);
        }

        private void Fault(ViewRecord record, Exception exception, ViewErrorSource source)
        {
            _errors.Route(exception, source, record.ViewId, record.RequestId, null);
        }

        private List<ViewRecord> RentList()
        {
            return _revealedPool.Count > 0 ? _revealedPool.Pop() : new List<ViewRecord>(4);
        }

        private void ReturnList(List<ViewRecord> list)
        {
            list.Clear();
            _revealedPool.Push(list);
        }
    }
}
