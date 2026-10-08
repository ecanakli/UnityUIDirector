using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    // One request from acceptance to its end: the state the navigator drives and the latched outcome.
    internal abstract class ViewRecord : ILayerRecord
    {
        private readonly IViewStage _stage;
        private readonly ErrorRouter _errors;
        private readonly Action<ViewRecord> _onOutcomeLatched;
        private ViewPhase _phase;
        private CancellationTokenRegistration _ownerRegistration;

        // The callback runs once, when the first outcome is latched by Complete, Close, Decline or RequestClose.
        protected ViewRecord(
            int requestId,
            string viewId,
            ViewEntry row,
            int layerIndex,
            long acceptSequence,
            double acceptTime,
            string sourceTag,
            string callerMember,
            int callerLine,
            IViewStage stage,
            ErrorRouter errors,
            Action<ViewRecord> onOutcomeLatched)
        {
            RequestId = requestId;
            ViewId = viewId;
            Row = row;
            LayerIndex = layerIndex;
            AcceptSequence = acceptSequence;
            AcceptTime = acceptTime;
            SourceTag = sourceTag;
            CallerMember = callerMember;
            CallerLine = callerLine;
            _stage = stage;
            _errors = errors;
            _onOutcomeLatched = onOutcomeLatched;
            _phase = ViewPhase.Waiting;
        }

        // State

        public int RequestId { get; }

        public string ViewId { get; }

        public ViewPhase Phase
        {
            get { return _phase; }
        }

        public ViewEntry Row { get; }

        public int LayerIndex { get; }

        public long AcceptSequence { get; }

        public int Slot { get; set; }

        public string SourceTag { get; }

        // The queue priority the request came with; 0 for an immediate open.
        public int Priority { get; set; }

        public string CallerMember { get; }

        public int CallerLine { get; }

        public double AcceptTime { get; }

        // The moment the Shown event was published; 0 until then.
        public double ShownTime { get; private set; }

        // True once the request entered Opening (or snapped to shown): an end from there counts as shown.
        public bool WasShown { get; private set; }

        // True once the Shown event went out.
        public bool ShownPublished { get; private set; }

        // The stage's opaque instance; null before the load ends and after the references are released.
        public object Instance { get; set; }

        // The instance was destroyed or deactivated outside the package, so it can only be released, never kept.
        public bool InstanceLost { get; set; }

        public ViewScope OpenScope { get; set; }

        // The request this one replaces in place; it ends as Navigated right before this one's open starts. Cleared when this request opens or ends.
        public ViewRecord Replaced { get; set; }

        // True while a newer request waits to take this one's place: no visible scope, not interactive, and not live for a new open; cleared if the newer request ends first.
        public bool BeingReplaced { get; set; }

        public ViewScope VisibleScope { get; set; }

        // The source of the running load or transition step; the runner owns it and disposes it.
        public CancellationTokenSource StepSource { get; set; }

        // The id of the shield hold this request owns; 0 when it holds none.
        public int ShieldHolderId { get; set; }

        // The hold a close cause adds on top of the open's own hold while the request is Opening; 0 when none.
        public int CloseHolderId { get; set; }

        // Queued pop-to and close-all operations that rely on this request's primary hold.
        public int OperationHolds { get; set; }

        // True while OnOpen runs on this record.
        public bool IsInOnOpen { get; protected set; }

        // The exception OnOpen threw; null when it did not throw. The runner logs it once.
        public Exception OpenFault { get; protected set; }

        // Moves forward only; returns false when the phase would stay or go back.
        public bool TryAdvancePhase(ViewPhase next)
        {
            if (next <= _phase)
                return false;

            _phase = next;
            return true;
        }

        // From now on an end counts as shown; this does not publish anything.
        public void CountAsShown()
        {
            WasShown = true;
        }

        // Records the moment Shown is published; false when it was published before.
        public bool TryMarkShownPublished(double now)
        {
            if (ShownPublished)
                return false;

            ShownPublished = true;
            WasShown = true;
            ShownTime = now;
            return true;
        }

        // Time from acceptance to the Shown event, or to now while it has not been published.
        public double SecondsWaiting(double now)
        {
            var end = ShownPublished ? ShownTime : now;
            return end > AcceptTime ? end - AcceptTime : 0d;
        }

        // Time from the Shown event; 0 when it was never published.
        public double SecondsOpen(double now)
        {
            if (!ShownPublished)
                return 0d;

            return now > ShownTime ? now - ShownTime : 0d;
        }

        // True while an owner that can end the request is registered; for the tools' read-out.
        public bool OwnerBound { get; private set; }

        public void SetOwnerRegistration(CancellationTokenRegistration registration)
        {
            _ownerRegistration = registration;
            OwnerBound = true;
        }

        // Unregisters the owner callback without invoking it; safe to call more than once.
        public void DisposeOwnerRegistration()
        {
            var registration = _ownerRegistration;
            _ownerRegistration = default(CancellationTokenRegistration);
            OwnerBound = false;

            try
            {
                registration.Dispose();
            }
            catch (Exception exception)
            {
                RouteError(exception, ViewErrorSource.ViewHook);
            }
        }

        // Cancels the running step; never throws.
        public void CancelStep()
        {
            var source = StepSource;
            if (source == null)
                return;

            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The step already ended and its source was disposed.
            }
            catch (Exception exception)
            {
                RouteError(exception, _phase <= ViewPhase.Loading ? ViewErrorSource.Provider : ViewErrorSource.Transition);
            }
        }

        // Drops the view references but keeps the ended open scope, so a kept context stays inert; call last, after the awaiters.
        public virtual void ReleaseReferences()
        {
            DisposeOwnerRegistration();
            Instance = null;
            VisibleScope = null;
            StepSource = null;
            // OpenFault stays: the runner reports UIDIRECTOR117 after OnOpen, and a fault can end the record synchronously first.

            // The end already gave a live replaced request back.
            Replaced = null;

            // The ended scope must not keep the instance alive.
            if (OpenScope != null)
                OpenScope.Target = null;
        }

        // The live instance while the view is opening, open or closing.
        public bool TryGetInstance(out GameObject instance)
        {
            instance = null;
            if (Instance == null || _stage == null)
                return false;

            if (_phase < ViewPhase.Opening || _phase == ViewPhase.Ended)
                return false;

            GameObject found;
            if (!_stage.TryGetGameObject(Instance, out found) || found == null)
                return false;

            instance = found;
            return true;
        }

        // Latch

        public bool HasOutcome { get; private set; }

        // NotShown when the latched outcome is a rejection.
        public CloseReason OutcomeReason { get; private set; }

        public RejectReason OutcomeRejected { get; private set; }

        public string OutcomeDetail { get; private set; }

        // True when the latched outcome is a rejection (the request ends without a show).
        public bool IsRejected
        {
            get { return HasOutcome && OutcomeReason == CloseReason.NotShown; }
        }

        // True when a close cause is latched and the close has not run yet.
        public bool ClosePending { get; private set; }

        // True when the first outcome was latched while OnOpen ran.
        public bool OutcomeLatchedInOnOpen { get; private set; }

        // Latches a close cause if none is latched yet; does not call the navigator callback.
        public bool TryLatchClose(CloseReason reason)
        {
            if (_phase == ViewPhase.Ended || HasOutcome)
                return false;

            SetOutcome(reason, RejectReason.None, null);
            return true;
        }

        // Latches a not-shown outcome if none is latched yet; does not call the navigator callback.
        public bool TryLatchRejected(RejectReason reason, string detail)
        {
            if (_phase == ViewPhase.Ended || HasOutcome)
                return false;

            SetOutcome(CloseReason.NotShown, reason, detail);
            return true;
        }

        // A decline or fault in OnOpen ends the request unshown, so it replaces a close cause latched in OnOpen.
        public bool TryLatchOpenFailure(RejectReason reason, string detail, out bool wasFirstLatch)
        {
            wasFirstLatch = !HasOutcome;
            if (_phase == ViewPhase.Ended || IsRejected)
                return false;

            SetOutcome(CloseReason.NotShown, reason, detail);
            return true;
        }

        // Latches the cause, or withdraws the request if it is not shown yet; never throws.
        public void RequestClose(CloseReason reason)
        {
            if (_phase == ViewPhase.Ended || HasOutcome)
                return;

            bool latched;
            if (reason == CloseReason.NotShown || _phase <= ViewPhase.Loading)
            {
                var rejected = reason == CloseReason.OwnerEnded ? RejectReason.OwnerEnded : RejectReason.Cancelled;
                latched = TryLatchRejected(rejected, null);
            }
            else
            {
                latched = TryLatchClose(reason);
            }

            if (latched)
                NotifyLatched();
        }

        // Hooks for the generic subclass

        // Completes every awaiter once with the outcome, in join order.
        public abstract void CompleteAwaiters();

        // Calls the view's OnOpen; returns false when the request now carries a not-shown outcome.
        public abstract bool InvokeOnOpen();

        // Completes when the request ends; throws only when the waiter token is cancelled.
        public abstract UniTask<ViewClosed> WaitForCloseAsync(CancellationToken waiter);

        // Pending WaitForCloseAsync waiters, for tests.
        public abstract int CloseWaiterCount { get; }

        // Resolves the typed view on the instance and stores the binding when resolved.
        public abstract ViewResolution BindView(IViewStage stage, object instance);

        // Places the instance under its layer, linked to this request's handle.
        public abstract void PlaceView(IViewStage stage, object instance, ViewLayer layer);

        // Links an instance that stays in place (reopen in place) to this request's handle.
        public abstract void RelinkView(IViewStage stage, object instance);

        // Calls the view's optional OnVisible; a throwing hook is routed.
        public abstract void InvokeOnVisible(ViewScope visible);

        // Asks the view's optional back hook; false when it has none or it throws.
        public abstract bool InvokeOnBack();

        // Runs the navigator callback; a throwing callback is routed, never rethrown.
        protected void NotifyLatched()
        {
            if (_onOutcomeLatched == null)
                return;

            try
            {
                _onOutcomeLatched(this);
            }
            catch (Exception exception)
            {
                RouteError(exception, ViewErrorSource.ViewHook);
            }
        }

        protected void RouteError(Exception exception, ViewErrorSource source)
        {
            if (_errors == null)
                return;

            UnityEngine.Object target = null;
            GameObject found;
            if (Instance != null && _stage != null && _stage.TryGetGameObject(Instance, out found))
                target = found;

            _errors.Route(exception, source, ViewId, RequestId, target);
        }

        // The outcome the awaiters see: a request that was never shown always reports NotShown.
        protected void ResolveFinalOutcome(out CloseReason reason, out RejectReason rejected, out string detail)
        {
            detail = OutcomeDetail;

            if (WasShown)
            {
                rejected = RejectReason.None;
                reason = HasOutcome && OutcomeReason != CloseReason.NotShown ? OutcomeReason : CloseReason.Code;
                return;
            }

            reason = CloseReason.NotShown;
            rejected = OutcomeRejected;
            if (rejected == RejectReason.None)
                rejected = HasOutcome ? CauseToRejection(OutcomeReason) : RejectReason.Cancelled;
        }

        private void SetOutcome(CloseReason reason, RejectReason rejected, string detail)
        {
            if (!HasOutcome)
                OutcomeLatchedInOnOpen = IsInOnOpen;

            HasOutcome = true;
            OutcomeReason = reason;
            OutcomeRejected = rejected;
            OutcomeDetail = detail;
            ClosePending = reason != CloseReason.NotShown;
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
    }
}
