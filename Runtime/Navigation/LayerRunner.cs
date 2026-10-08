using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    // What only the navigator knows; it extends the terminator host, so one object serves both.
    internal interface ILayerRunnerHost : ITerminatorHost
    {
        // A kept instance for the record's view id, taken out of the retained set; false when none, then the stage acquires one.
        bool TryTakeRetained(ViewRecord record, out object instance);

        // The record's load starts (true) or ends (false); the host drives the load indicator.
        void SetLoading(ViewRecord record, bool loading);

        // History layers: closes views on ClosedByNavigation layers by snap, reason Navigated; other layers do nothing.
        void CloseViewsOnNavigation(ViewRecord opened);

        // The record reached Open and its shield hold is settled; publish Shown and check Settled.
        void OnShown(ViewRecord record);

        // The layer's runner found no more work and stopped; IsRunning is already false.
        void OnLayerIdle(ViewLayer layer);

        // A pop-to or close-all is about to hide the record: it holds the shield while it runs, unless the record holds already.
        void HoldForOperation(ViewRecord record);

        // A close cause was latched while OnOpen ran and the record is now Opening: adds the pending-close hold.
        void HoldForPendingClose(ViewRecord record);

        // Reopen in place after a pop-to: hands the live instance over and ends the replaced request as Navigated; if that one ended, only clears the link.
        void FinishReplaced(ViewRecord record);
    }

    // Drains one layer's FIFO, one operation at a time; steps are never interrupted except by a terminate cause or a watchdog.
    internal sealed class LayerRunner
    {
        public const float DefaultLoadTimeoutSeconds = 15f;
        public const float DefaultTransitionTimeoutSeconds = 3f;
        internal const float MinTimeoutSeconds = 0.1f;

        private enum StepOutcome
        {
            Completed,
            Failed,
            TimedOut,
            Terminated
        }

        private readonly LayerState<ViewRecord> _layer;
        private readonly IViewStage _stage;
        private readonly Terminator _terminator;
        private readonly NavigatorClock _clock;
        private readonly ErrorRouter _errors;
        private readonly Diagnostics _diagnostics;
        private readonly ILayerRunnerHost _host;
        private readonly float _loadTimeoutSeconds;
        private readonly float _transitionTimeoutSeconds;
        private readonly Action<object> _onWatchdog;
        private readonly List<ViewRecord> _revealed = new List<ViewRecord>(4);
        private readonly List<ViewRecord> _popped = new List<ViewRecord>(4);

        // One operation runs at a time, so the step fields below have a single user.
        private ViewRecord _current;
        private ViewRecord _stepRecord;
        private CancellationTokenSource _stepSource;
        private int _timerId;
        private bool _watching;
        private bool _timedOut;
        private Exception _stepError;
        private object _loaded;

        public LayerRunner(
            LayerState<ViewRecord> layer,
            IViewStage stage,
            Terminator terminator,
            NavigatorClock clock,
            ErrorRouter errors,
            Diagnostics diagnostics,
            ILayerRunnerHost host,
            float loadTimeoutSeconds = DefaultLoadTimeoutSeconds,
            float transitionTimeoutSeconds = DefaultTransitionTimeoutSeconds)
        {
            _layer = layer;
            _stage = stage;
            _terminator = terminator;
            _clock = clock;
            _errors = errors;
            _diagnostics = diagnostics;
            _host = host;
            _loadTimeoutSeconds = ClampTimeout(loadTimeoutSeconds);
            _transitionTimeoutSeconds = ClampTimeout(transitionTimeoutSeconds);
            _onWatchdog = OnWatchdog;
        }

        // Written so NaN fails the comparison and falls to the minimum; infinity stays and means no timeout.
        private static float ClampTimeout(float seconds)
        {
            return seconds >= MinTimeoutSeconds ? seconds : MinTimeoutSeconds;
        }

        // Drains the layer's FIFO; a no-op while a drain runs. Call it last, after Enqueue: the first steps run inside this call.
        public void Start()
        {
            if (_layer.IsRunning)
                return;

            _layer.IsRunning = true;
            DriveAsync().Forget();
        }

        // Driver

        private async UniTaskVoid DriveAsync()
        {
            try
            {
                LayerOperation<ViewRecord> operation;
                while (_layer.TryDequeue(out operation))
                {
                    _current = operation.Record;
                    var faulted = false;

                    try
                    {
                        await ExecuteAsync(operation);
                    }
                    catch (Exception exception)
                    {
                        faulted = true;
                        HandleFault(exception);
                    }
                    finally
                    {
                        if (faulted)
                            ReleaseHolder(_current);

                        _current = null;
                        ReleaseOperationHold(operation);
                    }
                }
            }
            finally
            {
                _layer.IsRunning = false;
                NotifyIdle();
            }
        }

        private UniTask ExecuteAsync(LayerOperation<ViewRecord> operation)
        {
            switch (operation.Kind)
            {
                case LayerOperationKind.Open:
                    return OpenAsync(operation.Record);
                case LayerOperationKind.Close:
                    return CloseAsync(operation.Record, operation.Reason);
                case LayerOperationKind.PopTo:
                    return PopToAsync(operation.Record, operation.Reason);
                case LayerOperationKind.CloseAll:
                    return CloseAllAsync(operation.Reason);
                default:
                    return UniTask.CompletedTask;
            }
        }

        private void HandleFault(Exception exception)
        {
            AbandonStep();

            var record = _current;
            if (record == null)
            {
                _diagnostics.ReportFault("layer runner", exception);
                return;
            }

            RouteFor(record, exception, record.Phase <= ViewPhase.Loading ? ViewErrorSource.Provider : ViewErrorSource.Transition);
            EndRecord(record, CloseReason.NotShown);
        }

        private void NotifyIdle()
        {
            try
            {
                _host.OnLayerIdle(_layer.Layer);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("layer idle", exception);
            }
        }

        // Open

        private async UniTask OpenAsync(ViewRecord record)
        {
            if (record == null)
                return;

            // Reopen in place: the pop-to has run, so the request this one replaces ends now, right before the open starts.
            if (record.Replaced != null)
                FinishReplaced(record);

            if (record.Phase == ViewPhase.Ended)
                return;

            // Withdrawn or rejected while it waited.
            if (record.HasOutcome)
            {
                EndRecord(record, record.OutcomeReason);
                return;
            }

            if (!record.TryAdvancePhase(ViewPhase.Loading))
                return;

            // Reopen in place: the navigator handed over the live instance, so no load, inject or place.
            var reopen = record.Instance != null;

            object instance;
            var inject = true;
            if (reopen)
            {
                instance = record.Instance;
                inject = false;
            }
            else if (_host.TryTakeRetained(record, out instance))
            {
                // A kept instance was injected on its first open.
                inject = false;
                record.Instance = instance;
            }
            else
            {
                _host.SetLoading(record, true);
                var loadOutcome = await LoadAsync(record);
                _host.SetLoading(record, false);

                if (loadOutcome == StepOutcome.Terminated)
                {
                    await FinishCutAsync(record);
                    return;
                }

                instance = _loaded;
                _loaded = null;

                if (loadOutcome != StepOutcome.Completed || instance == null)
                {
                    FailLoad(record, loadOutcome);
                    return;
                }

                if (record.Phase == ViewPhase.Ended)
                {
                    // Ended without cancelling the step: nobody else owns this instance.
                    try
                    {
                        _stage.Release(instance);
                    }
                    catch (Exception exception)
                    {
                        RouteFor(record, exception, ViewErrorSource.Provider);
                    }

                    await record.WaitForCloseAsync(CancellationToken.None);
                    return;
                }

                record.Instance = instance;
            }

            if (record.HasOutcome)
            {
                EndRecord(record, record.OutcomeReason);
                return;
            }

            if (!Prepare(record, instance, inject, !reopen))
                return;

            if (record.Phase == ViewPhase.Ended)
            {
                await record.WaitForCloseAsync(CancellationToken.None);
                return;
            }

            var accepted = record.InvokeOnOpen();
            ReportOnOpen(record);
            if (record.Phase == ViewPhase.Ended)
            {
                await record.WaitForCloseAsync(CancellationToken.None);
                return;
            }

            if (!accepted)
            {
                // Declined or faulted: not shown, no transition.
                EndRecord(record, CloseReason.NotShown);
                return;
            }

            _host.CloseViewsOnNavigation(record);
            if (record.Phase == ViewPhase.Ended)
            {
                await record.WaitForCloseAsync(CancellationToken.None);
                return;
            }

            if (reopen)
            {
                // No transition: the instance is already on screen, so it snaps to shown.
                SnapRecord(record, true);
                if (!record.TryAdvancePhase(ViewPhase.Open))
                {
                    await record.WaitForCloseAsync(CancellationToken.None);
                    return;
                }

                record.CountAsShown();
                ApplyProjection();
                BeginVisible(record);
                if (record.Phase == ViewPhase.Ended)
                {
                    await record.WaitForCloseAsync(CancellationToken.None);
                    return;
                }
            }
            else
            {
                SnapRecord(record, false);
                if (!record.TryAdvancePhase(ViewPhase.Opening))
                {
                    await record.WaitForCloseAsync(CancellationToken.None);
                    return;
                }

                // A terminate during Opening ends the request as shown, so the record counts as shown from here.
                record.CountAsShown();

                // A close latched inside OnOpen adds its hold now, on top of the open's.
                if (record.ClosePending)
                    _host.HoldForPendingClose(record);

                ApplyProjection();
                BeginVisible(record);
                if (record.Phase == ViewPhase.Ended)
                {
                    await record.WaitForCloseAsync(CancellationToken.None);
                    return;
                }

                var showOutcome = await TransitionAsync(record, true);
                if (showOutcome == StepOutcome.Terminated)
                {
                    await FinishCutAsync(record);
                    return;
                }

                if (record.Phase == ViewPhase.Ended || !record.TryAdvancePhase(ViewPhase.Open))
                {
                    await record.WaitForCloseAsync(CancellationToken.None);
                    return;
                }
            }

            // The open's hold goes with the show; a pending close or a queued pop-to or close-all may keep it.
            ReleaseOpenHold(record);

            ApplyProjection();
            _host.OnShown(record);

            // Complete or Close inside OnOpen: the view opened, and now it closes.
            if (record.OutcomeLatchedInOnOpen && record.ClosePending && record.Phase == ViewPhase.Open)
                await CloseAsync(record, record.OutcomeReason);
        }

        // UIDIRECTOR117 when OnOpen threw; UIDIRECTOR118 when it latched Complete or Close, which Decline is for.
        private void ReportOnOpen(ViewRecord record)
        {
            var fault = record.OpenFault;
            if (fault != null)
            {
                _diagnostics.Error(
                    DiagnosticIds.OnOpenThrew,
                    "OnOpen of '" + record.ViewId + "' threw " + fault.GetType().Name + ": " + fault.Message);
                return;
            }

            if (record.OutcomeLatchedInOnOpen && record.ClosePending)
            {
                _diagnostics.Warn(
                    DiagnosticIds.CompleteInsideOnOpen,
                    "Complete or Close was called inside OnOpen of '" + record.ViewId + "'; the view opens and then closes. Call Decline to refuse the open instead.");
            }
        }

        // Inject, bind and place; on any failure the record is already ended and false is returned.
        private bool Prepare(ViewRecord record, object instance, bool inject, bool place)
        {
            if (inject)
            {
                try
                {
                    _stage.Inject(instance);
                }
                catch (Exception exception)
                {
                    FailPrepare(record, exception, ViewErrorSource.Inject, "Injecting");
                    return false;
                }
            }

            var resolution = ViewResolution.Resolved;
            try
            {
                resolution = record.BindView(_stage, instance);
                if (resolution == ViewResolution.Resolved)
                {
                    // A reopened instance stays where it is; only its link moves to the new request.
                    if (place)
                        record.PlaceView(_stage, instance, _layer.Layer);
                    else
                        record.RelinkView(_stage, instance);
                }
            }
            catch (Exception exception)
            {
                FailPrepare(record, exception, ViewErrorSource.Provider, "Preparing");
                return false;
            }

            if (resolution == ViewResolution.Resolved)
                return true;

            if (resolution == ViewResolution.MissingHost)
            {
                var detail = "The instance of '" + record.ViewId + "' has no view host.";
                _diagnostics.Error(DiagnosticIds.MissingViewHost, detail);
                Reject(record, RejectReason.TypeMismatch, detail);
                return false;
            }

            var mismatch = "The instance of '" + record.ViewId + "' has no view for the requested argument and result types.";
            _diagnostics.Error(DiagnosticIds.ViewTypeMismatch, mismatch);
            Reject(record, RejectReason.TypeMismatch, mismatch);
            return false;
        }

        private void FailPrepare(ViewRecord record, Exception exception, ViewErrorSource source, string step)
        {
            RouteFor(record, exception, source);
            _diagnostics.Error(DiagnosticIds.LoadFailed, step + " '" + record.ViewId + "' failed: " + exception.Message);
            Reject(record, RejectReason.LoadFailed, exception.Message);
        }

        private void FailLoad(ViewRecord record, StepOutcome outcome)
        {
            if (outcome == StepOutcome.TimedOut)
            {
                var timeout = "Loading '" + record.ViewId + "' exceeded " + Seconds(_loadTimeoutSeconds) + " s.";
                _diagnostics.Error(DiagnosticIds.LoadTimedOut, timeout);
                Reject(record, RejectReason.LoadTimedOut, timeout);
                return;
            }

            var error = _stepError;
            _stepError = null;
            if (error != null)
                RouteFor(record, error, ViewErrorSource.Provider);

            var detail = error != null ? error.Message : "The provider returned no instance.";
            _diagnostics.Error(DiagnosticIds.LoadFailed, "Loading '" + record.ViewId + "' failed: " + detail);
            Reject(record, RejectReason.LoadFailed, detail);
        }

        private void Reject(ViewRecord record, RejectReason reason, string detail)
        {
            record.TryLatchRejected(reason, detail);
            EndRecord(record, CloseReason.NotShown);
        }

        // Close

        private async UniTask CloseAsync(ViewRecord record, CloseReason reason)
        {
            if (record == null || record.Phase == ViewPhase.Ended)
                return;

            _current = record;

            // Never reached Open (or is already closing): finish it now.
            if (record.Phase < ViewPhase.Open)
            {
                EndRecord(record, reason);
                return;
            }

            record.TryLatchClose(reason);
            if (!record.TryAdvancePhase(ViewPhase.Closing))
            {
                EndRecord(record, reason);
                return;
            }

            // Reveal what it covered, then hide.
            ApplyProjection();
            if (record.Phase == ViewPhase.Ended)
            {
                await record.WaitForCloseAsync(CancellationToken.None);
                return;
            }

            var outcome = await TransitionAsync(record, false);
            if (outcome == StepOutcome.Terminated)
            {
                await FinishCutAsync(record);
                return;
            }

            EndRecord(record, reason);
        }

        // Pop-to and close-all

        private UniTask PopToAsync(ViewRecord target, CloseReason reason)
        {
            _popped.Clear();

            if (target != null)
            {
                var index = _layer.IndexOf(target);
                if (index >= 0)
                {
                    // Top first; waiting requests above the target were accepted after this operation and stay.
                    for (var i = _layer.Count - 1; i > index; i--)
                    {
                        if (Projection.IsOnScreen(_layer[i].Phase))
                            _popped.Add(_layer[i]);
                    }
                }
                else
                {
                    // The target ended before the pop ran: the views latched for it above its old slot still go.
                    for (var i = _layer.Count - 1; i >= 0; i--)
                    {
                        var above = _layer[i];
                        if (above.Slot > target.Slot && Projection.IsOnScreen(above.Phase))
                            _popped.Add(above);
                    }
                }
            }

            return DismissAsync(reason);
        }

        private UniTask CloseAllAsync(CloseReason reason)
        {
            _popped.Clear();

            for (var i = _layer.Count - 1; i >= 0; i--)
            {
                if (Projection.IsOnScreen(_layer[i].Phase))
                    _popped.Add(_layer[i]);
            }

            return DismissAsync(reason);
        }

        // _popped holds the views to remove, top first: reveal, hide the top animated, end it, then snap-end the rest.
        private async UniTask DismissAsync(CloseReason reason)
        {
            if (_popped.Count == 0)
                return;

            for (var i = 0; i < _popped.Count; i++)
            {
                // A view on its way out never counts as live; a reason latched earlier wins.
                _popped[i].TryLatchClose(reason);
                _popped[i].TryAdvancePhase(ViewPhase.Closing);
            }

            ApplyProjection();

            var top = _popped[0];
            _current = top;
            if (top.Phase != ViewPhase.Ended)
            {
                // The hide runs under a hold even when the view the operation was accepted for is gone.
                _host.HoldForOperation(top);

                var outcome = await TransitionAsync(top, false);
                if (outcome == StepOutcome.Terminated)
                    await FinishCutAsync(top);
                else
                    EndRecord(top, reason);
            }

            for (var i = 1; i < _popped.Count; i++)
            {
                var other = _popped[i];
                _current = other;
                if (other.Phase == ViewPhase.Ended)
                    continue;

                SnapRecord(other, false);
                EndRecord(other, reason);
            }

            _popped.Clear();
        }

        // Steps

        private async UniTask<StepOutcome> LoadAsync(ViewRecord record)
        {
            BeginStep(record, _loadTimeoutSeconds);
            var token = _stepSource.Token;
            var outcome = StepOutcome.Completed;

            try
            {
                _loaded = await _stage.AcquireAsync(record.Row, token).AttachExternalCancellation(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                outcome = _timedOut ? StepOutcome.TimedOut : StepOutcome.Terminated;
            }
            catch (Exception exception)
            {
                _stepError = exception;
                outcome = StepOutcome.Failed;
            }

            EndStep(record);
            return outcome;
        }

        // A watchdog or a fault snaps and reports as Completed; only a terminate cut returns Terminated.
        private async UniTask<StepOutcome> TransitionAsync(ViewRecord record, bool show)
        {
            var instance = record.Instance;
            if (instance == null)
                return StepOutcome.Completed;

            BeginStep(record, _transitionTimeoutSeconds);
            var token = _stepSource.Token;
            var outcome = StepOutcome.Completed;

            try
            {
                await _stage.PlayAsync(instance, show, token).AttachExternalCancellation(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                outcome = _timedOut ? StepOutcome.TimedOut : StepOutcome.Terminated;
            }
            catch (Exception exception)
            {
                _stepError = exception;
                outcome = StepOutcome.Failed;
            }

            EndStep(record);

            if (outcome == StepOutcome.TimedOut)
            {
                SnapRecord(record, show);
                _diagnostics.Warn(
                    DiagnosticIds.TransitionTimedOut,
                    "The " + (show ? "show" : "hide") + " transition of '" + record.ViewId + "' exceeded " + Seconds(_transitionTimeoutSeconds) + " s and was snapped.");
                return StepOutcome.Completed;
            }

            if (outcome == StepOutcome.Failed)
            {
                var error = _stepError;
                _stepError = null;
                if (error != null)
                    RouteFor(record, error, ViewErrorSource.Transition);

                SnapRecord(record, show);
                return StepOutcome.Completed;
            }

            return outcome;
        }

        // The step was cut by a terminate cause: finish the record if nobody did, then wait for its end routine, so the next operation never runs inside it.
        private async UniTask FinishCutAsync(ViewRecord record)
        {
            if (record.Phase != ViewPhase.Ended)
                EndRecord(record, CloseReason.NotShown);

            await record.WaitForCloseAsync(CancellationToken.None);
        }

        private void BeginStep(ViewRecord record, float timeoutSeconds)
        {
            _timedOut = false;
            _stepError = null;
            _stepRecord = record;
            _stepSource = new CancellationTokenSource();
            record.StepSource = _stepSource;
            _watching = true;
            _timerId = _clock.Schedule(timeoutSeconds, _onWatchdog, null);
        }

        private void EndStep(ViewRecord record)
        {
            _watching = false;
            if (_timerId != 0)
            {
                _clock.Cancel(_timerId);
                _timerId = 0;
            }

            var source = _stepSource;
            _stepSource = null;
            _stepRecord = null;
            if (source == null)
                return;

            if (ReferenceEquals(record.StepSource, source))
                record.StepSource = null;

            // A cancelled plain source holds nothing, and disposing it inside its own cancel callbacks is avoided.
            if (!source.IsCancellationRequested)
                source.Dispose();
        }

        // Drops the step bookkeeping after a fault; the record's own end cancels what is left.
        private void AbandonStep()
        {
            if (_stepSource == null && _timerId == 0)
                return;

            var record = _stepRecord;
            _watching = false;
            if (_timerId != 0)
            {
                _clock.Cancel(_timerId);
                _timerId = 0;
            }

            _stepSource = null;
            _stepRecord = null;
            if (record != null)
                record.StepSource = null;
        }

        private void OnWatchdog(object state)
        {
            if (!_watching)
                return;

            _watching = false;
            _timerId = 0;
            _timedOut = true;

            // The runner resumes inside this call; nothing may touch the step fields after it.
            var record = _stepRecord;
            if (record != null)
                record.CancelStep();
        }

        // Helpers

        // Recomputes derived state and starts the visible scope of records it revealed; views on their way out are skipped.
        private void ApplyProjection()
        {
            _revealed.Clear();

            try
            {
                _host.RecomputeAndApply(_revealed);
            }
            catch (Exception exception)
            {
                RouteCurrent(exception, ViewErrorSource.Transition);
            }

            for (var i = 0; i < _revealed.Count; i++)
            {
                var other = _revealed[i];
                if (other == null || other.Phase == ViewPhase.Ended || other.Phase == ViewPhase.Closing)
                    continue;

                BeginVisible(other);
            }

            _revealed.Clear();
        }

        private void BeginVisible(ViewRecord record)
        {
            try
            {
                _host.BeginVisible(record);
            }
            catch (Exception exception)
            {
                RouteFor(record, exception, ViewErrorSource.ViewHook);
            }
        }

        private void SnapRecord(ViewRecord record, bool shown)
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
                RouteFor(record, exception, ViewErrorSource.Transition);
            }
        }

        private void EndRecord(ViewRecord record, CloseReason cause)
        {
            try
            {
                _terminator.End(record, cause);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("layer runner end", exception);
            }
        }

        private void FinishReplaced(ViewRecord record)
        {
            try
            {
                _host.FinishReplaced(record);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("layer runner replaced", exception);
            }
        }

        // The open's hold goes when the show ends, unless a close hold or a queued operation still needs the shield.
        private void ReleaseOpenHold(ViewRecord record)
        {
            var covered = record.CloseHolderId != 0;
            if (covered || (!record.ClosePending && record.OperationHolds == 0))
                ReleaseHolder(record);
        }

        // A pop-to or close-all ended: its carrier lets go of the hold unless something else still needs it.
        private void ReleaseOperationHold(LayerOperation<ViewRecord> operation)
        {
            var carrier = operation == null ? null : operation.HoldRecord;
            if (carrier == null)
                return;

            if (carrier.OperationHolds > 0)
                carrier.OperationHolds--;

            if (carrier.OperationHolds > 0 || carrier.ClosePending || carrier.Phase != ViewPhase.Open)
                return;

            ReleaseHolder(carrier);
        }

        private void ReleaseHolder(ViewRecord record)
        {
            if (record == null)
                return;

            var holderId = record.ShieldHolderId;
            if (holderId == 0)
                return;

            record.ShieldHolderId = 0;

            try
            {
                _host.ReleaseShield(holderId);
            }
            catch (Exception exception)
            {
                RouteFor(record, exception, ViewErrorSource.Transition);
            }
        }

        private void RouteFor(ViewRecord record, Exception exception, ViewErrorSource source)
        {
            _errors.Route(exception, source, record.ViewId, record.RequestId, null);
        }

        private void RouteCurrent(Exception exception, ViewErrorSource source)
        {
            var record = _current;
            if (record == null)
                _errors.Route(exception, source, null, 0, null);
            else
                RouteFor(record, exception, source);
        }

        private static string Seconds(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
