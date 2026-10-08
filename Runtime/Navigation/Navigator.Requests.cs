using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    internal sealed partial class Navigator
    {
        // Open

        public UniTask<ViewResult<TResult>> ShowAsync<TArgs, TResult>(
            ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            OpenOptions options = default,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0)
        {
            var decision = SubmitRequest(in key, args, owner, in options, member, line);
            switch (decision.Kind)
            {
                case IntakeKind.Rejected:
                    return UniTask.FromResult(NotShownResult<TResult>(decision.RequestId, decision.Reason, decision.Detail));

                case IntakeKind.ReturnToExisting:
                    return Join<TArgs, TResult>(decision.Existing);

                default:
                    return Join<TArgs, TResult>(decision.Record);
            }
        }

        public ViewHandle Open<TArgs, TResult>(
            ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            OpenOptions options = default,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0)
        {
            var decision = SubmitRequest(in key, args, owner, in options, member, line);
            switch (decision.Kind)
            {
                case IntakeKind.Rejected:
                    return default(ViewHandle);

                case IntakeKind.ReturnToExisting:
                    return new ViewHandle(decision.Existing);

                default:
                    return new ViewHandle(decision.Record);
            }
        }

        // Submits, then acts on the decision; the caller reads the record it needs from the decision.
        private IntakeDecision SubmitRequest<TArgs, TResult>(
            in ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            in OpenOptions options,
            string member,
            int line)
        {
            ThrowIfOffMainThread();

            var decision = _intake.Submit(in key, args, owner, in options, member, line);
            switch (decision.Kind)
            {
                case IntakeKind.Accepted:
                    QueueOpen(decision.Record, decision.LayerIndex);
                    break;

                case IntakeKind.ReturnToExisting:
                    PopAbove(decision.Existing, decision.LayerIndex, CloseReason.Navigated);
                    break;

                case IntakeKind.ReopenInPlace:
                    ReopenInPlace(decision);
                    break;
            }

            return decision;
        }

        private void QueueOpen(ViewRecord record, int layerIndex)
        {
            // An owner callback ended it inside Submit: nothing to run.
            if (record.Phase == ViewPhase.Ended)
                return;

            record.OpenScope = new ViewScope(_errors, record.ViewId, record.RequestId);

            Enqueue(layerIndex, LayerOperationKind.Open, record, CloseReason.NotShown);
            _runners[layerIndex].Start();
        }

        // The new request takes over the live instance of the one it replaces, after the pop-to when a view sits above.
        private void ReopenInPlace(IntakeDecision decision)
        {
            var record = decision.Record;
            var old = decision.Existing;
            var layerIndex = decision.LayerIndex;
            var state = _layerList[layerIndex];

            // Ended inside Submit: the view already on screen is left alone.
            if (record.Phase == ViewPhase.Ended)
                return;

            record.OpenScope = new ViewScope(_errors, record.ViewId, record.RequestId);

            // The existing request is itself waiting to replace an older one: the newest request inherits it.
            if (old.Replaced != null)
            {
                record.Replaced = old.Replaced;
                old.Replaced = null;
            }

            if (old.Phase == ViewPhase.Ended || !state.Contains(old))
            {
                Enqueue(layerIndex, LayerOperationKind.Open, record, CloseReason.NotShown);
                _runners[layerIndex].Start();
                return;
            }

            var withdrawn = RentList();
            try
            {
                // The new record sits last in the list and is skipped; what is on screen above the old one is latched.
                var top = LatchAbove(layerIndex, old, record, CloseReason.Navigated, withdrawn);

                // A view above needs a pop-to first: the old request stays where it is and ends when the runner reaches the new open.
                if (top != null && old.Instance != null && Projection.IsOnScreen(old.Phase))
                {
                    // Not latched: the old request ends only when the new open takes its place.
                    record.Replaced = old;
                    old.BeingReplaced = true;

                    QueuePopTo(layerIndex, old, top, CloseReason.Navigated);
                    Enqueue(layerIndex, LayerOperationKind.Open, record, CloseReason.NotShown);

                    ApplyProjection();
                    EndWithdrawn(withdrawn);
                }
                else
                {
                    var instance = old.Instance;
                    if (instance != null)
                    {
                        record.Instance = instance;
                        old.Instance = null;
                        MoveAppliedState(old, record);
                    }

                    state.Replace(old, record);

                    // Queue the work before the old request ends, so the navigator never looks settled in between.
                    if (top != null)
                        QueuePopTo(layerIndex, record, top, CloseReason.Navigated);

                    Enqueue(layerIndex, LayerOperationKind.Open, record, CloseReason.NotShown);

                    _terminator.End(old, CloseReason.Navigated);
                    EndWithdrawn(withdrawn);
                }
            }
            finally
            {
                ReturnList(withdrawn);
            }

            _runners[layerIndex].Start();
        }

        // Queues a pop to the target when a view on screen sits above it; unstarted requests above it are withdrawn.
        private void PopAbove(ViewRecord target, int layerIndex, CloseReason reason)
        {
            ViewRecord top = null;
            var withdrawn = RentList();
            try
            {
                top = LatchAbove(layerIndex, target, null, reason, withdrawn);
                if (top != null)
                {
                    QueuePopTo(layerIndex, target, top, reason);
                    ApplyProjection();
                }

                EndWithdrawn(withdrawn);
            }
            finally
            {
                ReturnList(withdrawn);
            }

            if (top != null)
                _runners[layerIndex].Start();
        }

        // Latches what sits above the target: unstarted requests into withdrawn, views on screen as closing; returns the topmost on screen.
        private ViewRecord LatchAbove(int layerIndex, ViewRecord target, ViewRecord keep, CloseReason reason, List<ViewRecord> withdrawn)
        {
            var state = _layerList[layerIndex];
            var index = state.IndexOf(target);
            if (index < 0)
                return null;

            ViewRecord top = null;
            for (var i = state.Count - 1; i > index; i--)
            {
                var record = state[i];
                if (record.Phase == ViewPhase.Ended || ReferenceEquals(record, keep))
                    continue;

                var phase = record.Phase;
                if (phase == ViewPhase.Waiting || phase == ViewPhase.Loading)
                {
                    if (record.TryLatchRejected(RejectReason.Cancelled, null))
                        withdrawn.Add(record);

                    continue;
                }

                // A closing view latched its close already.
                if (phase == ViewPhase.Opening || phase == ViewPhase.Open)
                {
                    if (record.TryLatchClose(reason))
                        TakePendingCloseHold(record);
                }

                if (top == null && Projection.IsOnScreen(phase))
                    top = record;
            }

            return top;
        }

        // Ends the requests LatchAbove withdrew; call it after the operation is queued.
        private void EndWithdrawn(List<ViewRecord> withdrawn)
        {
            for (var i = 0; i < withdrawn.Count; i++)
                OnOutcomeLatched(withdrawn[i]);
        }

        private UniTask<ViewResult<TResult>> Join<TArgs, TResult>(ViewRecord record)
        {
            var typed = record as ViewRecord<TArgs, TResult>;
            return typed != null ? typed.JoinShowAsync() : JoinUntyped<TResult>(record);
        }

        // The record was opened with other argument or result types: the outcome is shared, the value is not.
        private static async UniTask<ViewResult<TResult>> JoinUntyped<TResult>(ViewRecord record)
        {
            var closed = await record.WaitForCloseAsync(CancellationToken.None);
            return new ViewResult<TResult>(
                record.RequestId,
                closed.WasShown,
                closed.Reason,
                closed.Rejected,
                closed.Detail,
                false,
                default(TResult));
        }

        private static ViewResult<TResult> NotShownResult<TResult>(int requestId, RejectReason reason, string detail)
        {
            return new ViewResult<TResult>(requestId, false, CloseReason.NotShown, reason, detail, false, default(TResult));
        }

        private void Enqueue(int layerIndex, LayerOperationKind kind, ViewRecord record, CloseReason reason, ViewRecord holdRecord = null)
        {
            _layerList[layerIndex].Enqueue(new LayerOperation<ViewRecord>(kind, record, reason, holdRecord));
            _settledOwed = true;
        }

        // Queues a pop-to; the top view carries the shield for it until it ends.
        private void QueuePopTo(int layerIndex, ViewRecord target, ViewRecord top, CloseReason reason)
        {
            var carrier = TakeOperationHold(top);
            Enqueue(layerIndex, LayerOperationKind.PopTo, target, reason, carrier);
        }

        // A pop-to or close-all relies on the record's hold from now on; a record that holds already covers it.
        private ViewRecord TakeOperationHold(ViewRecord record)
        {
            if (record == null || record.Phase == ViewPhase.Ended)
                return null;

            if (!_layerList[record.LayerIndex].Layer.Has(ViewLayerRules.ShieldsInput))
                return null;

            HoldForOperation(record);
            record.OperationHolds++;
            return record;
        }

        // A close cause adds one hold: on top of the open's hold while Opening, otherwise the record's one hold covers it.
        private void TakePendingCloseHold(ViewRecord record)
        {
            if (record.Phase == ViewPhase.Ended || !_layerList[record.LayerIndex].Layer.Has(ViewLayerRules.ShieldsInput))
                return;

            if (record.Phase == ViewPhase.Opening)
            {
                if (record.CloseHolderId == 0)
                    record.CloseHolderId = _shield.Acquire(ShieldHolderKind.PendingClose, record.RequestId, record.ViewId, record.CallerMember);

                return;
            }

            if (record.ShieldHolderId == 0 && record.CloseHolderId == 0)
                record.ShieldHolderId = _shield.Acquire(ShieldHolderKind.PendingClose, record.RequestId, record.ViewId, record.CallerMember);
        }

        private void MoveAppliedState(ViewRecord from, ViewRecord to)
        {
            ViewVisualState state;
            if (!_applied.TryGetValue(from, out state))
                return;

            _applied.Remove(from);
            _applied[to] = state;
        }

        // Back

        public bool Back()
        {
            if (_rootGone || !_mainThread.IsMainThread)
                return false;

            var decision = BackResolver.Resolve(_layerList, _shield.Count);
            switch (decision.Kind)
            {
                case BackKind.Swallow:
                    return true;

                case BackKind.Withdraw:
                    decision.Target.RequestClose(CloseReason.NotShown);
                    return true;

                case BackKind.Close:
                    decision.Target.RequestClose(CloseReason.Back);
                    return true;

                case BackKind.AskView:
                    return AskView(decision.Target, decision.Fallback);

                default:
                    return false;
            }
        }

        // The view's hook decides; when it declines (or throws) the resolver's fallback applies.
        private bool AskView(ViewRecord target, BackKind fallback)
        {
            var handled = false;
            try
            {
                handled = target.InvokeOnBack();
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.ViewHook, target.ViewId, target.RequestId, null);
            }

            if (handled)
                return true;

            switch (fallback)
            {
                case BackKind.Close:
                    target.RequestClose(CloseReason.Back);
                    return true;

                case BackKind.Swallow:
                    return true;

                default:
                    return false;
            }
        }

        public bool CloseTo(string viewId)
        {
            if (viewId == null || _rootGone || !_mainThread.IsMainThread)
                return false;

            for (var layerIndex = _layerList.Count - 1; layerIndex >= 0; layerIndex--)
            {
                var state = _layerList[layerIndex];
                if (!state.Layer.Has(ViewLayerRules.History))
                    continue;

                for (var i = state.Count - 1; i >= 0; i--)
                {
                    var record = state[i];
                    if (record.Phase == ViewPhase.Ended || record.HasOutcome)
                        continue;

                    if (!string.Equals(record.ViewId, viewId, StringComparison.Ordinal))
                        continue;

                    PopAbove(record, layerIndex, CloseReason.Code);
                    return true;
                }
            }

            return false;
        }

        public void CloseAll(ViewLayer layer)
        {
            if (_rootGone)
                return;

            if (!_mainThread.IsMainThread)
            {
                _mainThread.Post(() => CloseAll(layer));
                return;
            }

            var layerIndex = FindLayerIndex(layer);
            if (layerIndex < 0)
                return;

            var state = _layerList[layerIndex];
            var queued = false;

            // Waiting requests are latched first and ended after, so the runner never reaches one that is not withdrawn yet.
            var pending = RentList();
            try
            {
                for (var i = 0; i < state.Count; i++)
                {
                    var record = state[i];
                    var phase = record.Phase;
                    if (phase != ViewPhase.Waiting && phase != ViewPhase.Loading)
                        continue;

                    pending.Add(record);
                    record.TryLatchRejected(RejectReason.Cancelled, null);
                }

                // Views on screen are latched at the call, so a new open of their id does not join them.
                for (var i = 0; i < state.Count; i++)
                {
                    var record = state[i];
                    var phase = record.Phase;
                    if (phase != ViewPhase.Opening && phase != ViewPhase.Open)
                        continue;

                    if (record.TryLatchClose(CloseReason.Code))
                        TakePendingCloseHold(record);
                }

                // What is on screen closes in one operation; the top view holds the shield meanwhile.
                ViewRecord top = null;
                for (var i = state.Count - 1; i >= 0; i--)
                {
                    var candidate = state[i];
                    if (candidate.Phase == ViewPhase.Ended || candidate.IsRejected)
                        continue;

                    top = candidate;
                    break;
                }

                // Queued before the withdrawals end, so the layer never looks idle in between.
                if (top != null)
                {
                    var carrier = TakeOperationHold(top);
                    Enqueue(layerIndex, LayerOperationKind.CloseAll, null, CloseReason.Code, carrier);
                    queued = true;
                    ApplyProjection();
                }

                for (var i = 0; i < pending.Count; i++)
                    OnOutcomeLatched(pending[i]);
            }
            finally
            {
                ReturnList(pending);
            }

            if (queued)
                _runners[layerIndex].Start();
        }

        // Queries

        public bool IsOpen(string viewId)
        {
            if (viewId == null)
                return false;

            for (var layerIndex = 0; layerIndex < _layerList.Count; layerIndex++)
            {
                var state = _layerList[layerIndex];
                for (var i = 0; i < state.Count; i++)
                {
                    var record = state[i];
                    if (record.Phase != ViewPhase.Ended && string.Equals(record.ViewId, viewId, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        public bool TryGetOpen(string viewId, out ViewHandle handle)
        {
            handle = default(ViewHandle);
            if (viewId == null)
                return false;

            for (var layerIndex = _layerList.Count - 1; layerIndex >= 0; layerIndex--)
            {
                var state = _layerList[layerIndex];
                for (var i = state.Count - 1; i >= 0; i--)
                {
                    var record = state[i];
                    if (record.Phase == ViewPhase.Ended || !string.Equals(record.ViewId, viewId, StringComparison.Ordinal))
                        continue;

                    handle = new ViewHandle(record);
                    return true;
                }
            }

            return false;
        }

        public bool TryGetTop(ViewLayer layer, out ViewHandle handle)
        {
            handle = default(ViewHandle);

            var layerIndex = FindLayerIndex(layer);
            if (layerIndex < 0)
                return false;

            var state = _layerList[layerIndex];
            for (var i = state.Count - 1; i >= 0; i--)
            {
                if (state[i].Phase == ViewPhase.Ended)
                    continue;

                handle = new ViewHandle(state[i]);
                return true;
            }

            return false;
        }

        // IIntakeHost

        public bool IsRootGone
        {
            get { return _rootGone; }
        }

        // The navigator clock in seconds; ITerminatorHost shares this member.
        public double Now
        {
            get { return _clock.Now; }
        }

        // First outcome of a record latched: a withdrawal ends it now, a close cause queues the close.
        public void OnOutcomeLatched(ViewRecord record)
        {
            if (record == null || _rootGone || record.Phase == ViewPhase.Ended)
                return;

            // OnOpen is running: the runner reads the latched outcome when it returns.
            if (record.IsInOnOpen)
                return;

            // Withdrawn or rejected before it was shown: end it now, which also cancels a load.
            if (record.IsRejected)
            {
                _terminator.End(record, CloseReason.NotShown);
                return;
            }

            // Not on screen yet, or already closing: the runner reads the latched outcome itself.
            if (!Projection.IsOnScreen(record.Phase) || record.Phase == ViewPhase.Closing)
                return;

            TakePendingCloseHold(record);
            ApplyProjection();

            Enqueue(record.LayerIndex, LayerOperationKind.Close, record, record.OutcomeReason);
            _runners[record.LayerIndex].Start();
        }

        // Runs on the thread that cancelled the owner token; off the main thread the end is posted.
        public void OnOwnerCancelled(ViewRecord record)
        {
            if (record == null || _rootGone)
                return;

            if (_mainThread.IsMainThread)
            {
                EndByOwner(record);
                return;
            }

            _diagnostics.Warn(
                DiagnosticIds.OwnerCancelledOffThread,
                "An owner token was cancelled off the main thread; the request '" + record.ViewId + "' ends on the main thread.");
            _mainThread.Post(() => EndByOwner(record));
        }

        // The owner ending is a terminate cause: shown requests end now, the others are withdrawn.
        private void EndByOwner(ViewRecord record)
        {
            if (_rootGone || record.Phase == ViewPhase.Ended)
                return;

            // OnOpen is running: latch only, the runner ends the request when OnOpen returns.
            if (record.IsInOnOpen)
            {
                record.RequestClose(CloseReason.OwnerEnded);
                return;
            }

            if (Projection.IsOnScreen(record.Phase))
            {
                SnapInstance(record, false);
                _terminator.End(record, CloseReason.OwnerEnded);
                return;
            }

            record.TryLatchRejected(RejectReason.OwnerEnded, null);
            _terminator.End(record, CloseReason.NotShown);
        }

        private void SnapInstance(ViewRecord record, bool shown)
        {
            var instance = record.Instance;
            if (instance == null)
                return;

            try
            {
                _stage.Snap(instance, shown);
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.Transition, record.ViewId, record.RequestId, null);
            }
        }
    }
}
