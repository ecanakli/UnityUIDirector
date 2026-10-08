using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>A reference to one request; inert once the request has ended.</summary>
    public readonly struct ViewHandle
    {
        private readonly ViewRecord _record;

        internal ViewHandle(ViewRecord record)
        {
            _record = record;
        }

        // The request behind the handle; null for an empty handle.
        internal ViewRecord Record
        {
            get { return _record; }
        }

        /// <summary>The id of the request; zero for an empty handle.</summary>
        public int RequestId
        {
            get { return _record == null ? 0 : _record.RequestId; }
        }

        /// <summary>The id of the requested view; null for an empty handle.</summary>
        public string ViewId
        {
            get { return _record == null ? null : _record.ViewId; }
        }

        /// <summary>Where the request is in its life; Ended for an empty handle.</summary>
        public ViewPhase Phase
        {
            get { return _record == null ? ViewPhase.Ended : _record.Phase; }
        }

        /// <summary>True until the request has ended.</summary>
        public bool IsAlive
        {
            get { return _record != null && _record.Phase != ViewPhase.Ended; }
        }

        /// <summary>Closes the view, or withdraws the request if it is not shown yet; never throws.</summary>
        public void Close()
        {
            if (_record != null)
                _record.RequestClose(CloseReason.Code);
        }

        // Closes as the user dismissing the view (ViewHost.Close); never throws.
        internal void Dismiss()
        {
            if (_record != null)
                _record.RequestClose(CloseReason.Dismissed);
        }

        /// <summary>Completes when the request ends; any number of callers; throws only when waiter is cancelled.</summary>
        public UniTask<ViewClosed> WaitForCloseAsync(CancellationToken waiter)
        {
            if (_record == null)
            {
                var empty = new ViewClosed(false, CloseReason.NotShown, RejectReason.Cancelled, "The handle is empty.");
                return UniTask.FromResult(empty);
            }

            return _record.WaitForCloseAsync(waiter);
        }

        /// <summary>The live instance while the view is opening, open or closing.</summary>
        public bool TryGetInstance(out GameObject instance)
        {
            instance = null;
            return _record != null && _record.TryGetInstance(out instance);
        }
    }
}
