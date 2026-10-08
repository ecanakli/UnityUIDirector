using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    // The typed half of a request: arguments, binding, result value and the awaiters.
    internal sealed class ViewRecord<TArgs, TResult> : ViewRecord, IViewContextHost<TResult>
    {
        private readonly INavigator _navigator;
        private readonly ViewHandle _handle;
        private readonly ViewContext<TResult> _context;
        private TArgs _args;
        private ViewBinding<TArgs, TResult> _binding;
        private bool _hasBinding;
        private TResult _value;
        private bool _hasValue;
        private List<UniTaskCompletionSource<ViewResult<TResult>>> _showWaiters;
        // Also the lock that orders cancel callbacks against the end.
        private readonly List<CloseWaiter> _closeWaiters = new List<CloseWaiter>(1);
        private static readonly Action<object> OnWaiterCancelled = OnWaiterCancelledCallback;
        private bool _awaitersCompleted;
        private ViewResult<TResult> _finalResult;
        private ViewClosed _finalClosed;

        public ViewRecord(
            int requestId,
            string viewId,
            ViewEntry row,
            int layerIndex,
            long acceptSequence,
            double acceptTime,
            string sourceTag,
            string callerMember,
            int callerLine,
            TArgs args,
            INavigator navigator,
            IViewStage stage,
            ErrorRouter errors,
            Action<ViewRecord> onOutcomeLatched)
            : base(
                requestId,
                viewId,
                row,
                layerIndex,
                acceptSequence,
                acceptTime,
                sourceTag,
                callerMember,
                callerLine,
                stage,
                errors,
                onOutcomeLatched)
        {
            _args = args;
            _navigator = navigator;
            _showWaiters = new List<UniTaskCompletionSource<ViewResult<TResult>>>(1);
            _handle = new ViewHandle(this);
            _context = new ViewContext<TResult>(this);
        }

        public TArgs Args
        {
            get { return _args; }
        }

        public ViewContext<TResult> Context
        {
            get { return _context; }
        }

        public bool HasBinding
        {
            get { return _hasBinding; }
        }

        public ViewBinding<TArgs, TResult> Binding
        {
            get { return _binding; }
        }

        // True only while a Complete(value) outcome stands.
        public bool HasValue
        {
            get { return _hasValue && HasOutcome && OutcomeReason == CloseReason.Completed; }
        }

        public TResult Value
        {
            get { return _value; }
        }

        public void SetBinding(in ViewBinding<TArgs, TResult> binding)
        {
            _binding = binding;
            _hasBinding = true;
        }

        public override ViewResolution BindView(IViewStage stage, object instance)
        {
            ViewBinding<TArgs, TResult> binding;
            var resolution = stage.ResolveView<TArgs, TResult>(instance, out binding);
            if (resolution == ViewResolution.Resolved)
                SetBinding(in binding);
            return resolution;
        }

        public override void PlaceView(IViewStage stage, object instance, ViewLayer layer)
        {
            stage.Place(instance, layer, _handle);
        }

        public override void RelinkView(IViewStage stage, object instance)
        {
            stage.Relink(instance, _handle);
        }

        public override void InvokeOnVisible(ViewScope visible)
        {
            if (!_hasBinding || _binding.Visible == null)
                return;
            try
            {
                _binding.Visible.OnVisible(visible);
            }
            catch (Exception exception)
            {
                RouteError(exception, ViewErrorSource.ViewHook);
            }
        }

        public override bool InvokeOnBack()
        {
            if (!_hasBinding || _binding.Back == null)
                return false;
            try
            {
                return _binding.Back.OnBack();
            }
            catch (Exception exception)
            {
                RouteError(exception, ViewErrorSource.ViewHook);
                return false;
            }
        }

        // Join

        // Joins a ShowAsync caller; after the end the stored outcome is returned at once.
        public UniTask<ViewResult<TResult>> JoinShowAsync()
        {
            if (_awaitersCompleted)
                return UniTask.FromResult(_finalResult);

            var source = new UniTaskCompletionSource<ViewResult<TResult>>();
            _showWaiters.Add(source);
            return source.Task;
        }

        public override UniTask<ViewClosed> WaitForCloseAsync(CancellationToken waiter)
        {
            if (_awaitersCompleted)
                return UniTask.FromResult(_finalClosed);

            if (waiter.IsCancellationRequested)
                return UniTask.FromCanceled<ViewClosed>(waiter);

            var entry = new CloseWaiter { Owner = this, Token = waiter };
            lock (_closeWaiters)
            {
                // The end may have frozen the list after the check above.
                if (_awaitersCompleted)
                    return UniTask.FromResult(_finalClosed);

                _closeWaiters.Add(entry);
            }

            // A token cancelled meanwhile runs the callback here, which removes the entry again.
            if (waiter.CanBeCanceled)
                entry.Registration = waiter.RegisterWithoutCaptureExecutionContext(OnWaiterCancelled, entry);

            return entry.Task;
        }

        // Pending WaitForCloseAsync waiters, for tests.
        public override int CloseWaiterCount
        {
            get
            {
                lock (_closeWaiters)
                    return _closeWaiters.Count;
            }
        }

        // May run on any thread; never throws.
        private static void OnWaiterCancelledCallback(object state)
        {
            var entry = (CloseWaiter)state;
            try
            {
                var owner = entry.Owner;
                lock (owner._closeWaiters)
                {
                    if (!owner._awaitersCompleted)
                        owner._closeWaiters.Remove(entry);
                }

                entry.TrySetCanceled(entry.Token);
            }
            catch (Exception exception)
            {
                try
                {
                    entry.Owner.RouteError(exception, ViewErrorSource.ViewHook);
                }
                catch
                {
                    // A cancel callback must never throw into the token's Cancel call.
                }
            }
        }

        // Completes the ShowAsync awaiters first, then the WaitForCloseAsync ones, once, in join order.
        public override void CompleteAwaiters()
        {
            if (_awaitersCompleted)
                return;

            CloseReason reason;
            RejectReason rejected;
            string detail;
            ResolveFinalOutcome(out reason, out rejected, out detail);

            var hasValue = WasShown && reason == CloseReason.Completed && HasValue;
            _finalResult = new ViewResult<TResult>(
                RequestId,
                WasShown,
                reason,
                rejected,
                detail,
                hasValue,
                hasValue ? _value : default(TResult));
            _finalClosed = new ViewClosed(WasShown, reason, rejected, detail);

            // From here the list is frozen: cancel callbacks leave it alone and new waiters get the result.
            lock (_closeWaiters)
                _awaitersCompleted = true;

            var finalClosed = _finalClosed;
            var showWaiters = _showWaiters;
            _showWaiters = null;

            for (var i = 0; i < showWaiters.Count; i++)
            {
                try
                {
                    showWaiters[i].TrySetResult(_finalResult);
                }
                catch (Exception exception)
                {
                    RouteError(exception, ViewErrorSource.ViewHook);
                }
            }

            // Outside the lock: Dispose waits for a callback running on another thread, and that callback takes the lock.
            for (var i = 0; i < _closeWaiters.Count; i++)
            {
                var entry = _closeWaiters[i];
                try
                {
                    entry.Registration.Dispose();
                }
                catch (Exception exception)
                {
                    RouteError(exception, ViewErrorSource.ViewHook);
                }

                try
                {
                    entry.TrySetResult(finalClosed);
                }
                catch (Exception exception)
                {
                    RouteError(exception, ViewErrorSource.ViewHook);
                }
            }

            lock (_closeWaiters)
                _closeWaiters.Clear();
        }

        // Open hook

        public override bool InvokeOnOpen()
        {
            if (Phase == ViewPhase.Ended || IsRejected)
                return false;

            if (!_hasBinding)
            {
                TryLatchRejected(RejectReason.TypeMismatch, "The record has no view binding.");
                return false;
            }

            Exception failure = null;
            IsInOnOpen = true;
            try
            {
                _binding.View.OnOpen(_args, _context);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            IsInOnOpen = false;

            if (failure != null)
            {
                OpenFault = failure;
                RouteError(failure, ViewErrorSource.ViewHook);
                bool first;
                if (TryLatchOpenFailure(RejectReason.Faulted, failure.Message, out first) && first)
                    NotifyLatched();

                return false;
            }

            return !IsRejected;
        }

        public override void ReleaseReferences()
        {
            base.ReleaseReferences();
            _binding = default(ViewBinding<TArgs, TResult>);
            _hasBinding = false;
            _args = default(TArgs);
        }

        // IViewContextHost

        public INavigator Navigator
        {
            get { return _navigator; }
        }

        public ViewHandle Handle
        {
            get { return _handle; }
        }

        public void CompleteView(TResult value)
        {
            if (!TryLatchClose(CloseReason.Completed))
                return;

            _value = value;
            _hasValue = true;
            NotifyLatched();
        }

        public void CloseView()
        {
            if (TryLatchClose(CloseReason.Dismissed))
                NotifyLatched();
        }

        // Valid only inside OnOpen; anywhere else it is ignored.
        public void DeclineOpen(string reason)
        {
            if (!IsInOnOpen)
                return;

            bool first;
            if (TryLatchOpenFailure(RejectReason.Declined, reason, out first) && first)
                NotifyLatched();
        }

        // One WaitForCloseAsync caller; the source carries its own cancel state, so a wait costs one allocation.
        private sealed class CloseWaiter : UniTaskCompletionSource<ViewClosed>
        {
            public ViewRecord<TArgs, TResult> Owner;
            public CancellationToken Token;
            public CancellationTokenRegistration Registration;
        }
    }
}
