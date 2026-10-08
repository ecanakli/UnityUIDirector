using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    // The state behind one ViewRegistration: ends once, on Remove or when the owner token is cancelled.
    internal sealed class RegistrationEntry
    {
        private static readonly Action<object> OwnerCancelled = OnOwnerCancelled;

        private readonly IMainThread _mainThread;
        private readonly Diagnostics _diagnostics;
        private readonly Action<object> _removeAction;
        private readonly object _state;
        private readonly Action _removeOnMainThread;
        private CancellationTokenRegistration _ownerRegistration;
        private int _active;

        // The remove action is package code that undoes the registration; it must not throw.
        public RegistrationEntry(IMainThread mainThread, Diagnostics diagnostics, Action<object> removeAction, object state)
        {
            _mainThread = mainThread;
            _diagnostics = diagnostics;
            _removeAction = removeAction;
            _state = state;
            _removeOnMainThread = RemoveNow;
            _active = 1;
        }

        public bool IsActive
        {
            get { return Volatile.Read(ref _active) != 0; }
        }

        // Ends the registration when the owner is cancelled; an owner that cannot be cancelled needs no callback.
        public void BindOwner(CancellationToken owner)
        {
            if (!owner.CanBeCanceled)
                return;

            _ownerRegistration = owner.RegisterWithoutCaptureExecutionContext(OwnerCancelled, this);

            // A token that was already cancelled ran the callback inline, before the registration was stored.
            if (!IsActive)
                DisposeOwnerRegistration();
        }

        // Safe from any thread: off the main thread the removal is posted.
        public void Remove()
        {
            if (_mainThread.IsMainThread)
                RemoveNow();
            else
                _mainThread.Post(_removeOnMainThread);
        }

        private static void OnOwnerCancelled(object state)
        {
            var entry = (RegistrationEntry)state;
            if (entry._mainThread.IsMainThread)
            {
                entry.RemoveNow();
                return;
            }

            entry._diagnostics.Warn(
                DiagnosticIds.OwnerCancelledOffThread,
                "An owner token was cancelled off the main thread; the registration is removed on the main thread.");
            entry._mainThread.Post(entry._removeOnMainThread);
        }

        private void RemoveNow()
        {
            if (Interlocked.Exchange(ref _active, 0) == 0)
                return;

            DisposeOwnerRegistration();

            try
            {
                _removeAction(_state);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("registration removal", exception);
            }
        }

        private void DisposeOwnerRegistration()
        {
            try
            {
                _ownerRegistration.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("owner registration dispose", exception);
            }
        }
    }
}
