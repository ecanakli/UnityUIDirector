using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    // Preload leases by owner: acquired through the stage, disposed when the owner ends. It never touches instances.
    internal sealed class PreloadLeases
    {
        private readonly IViewStage _stage;
        private readonly IMainThread _mainThread;
        private readonly Diagnostics _diagnostics;
        private readonly ErrorRouter _errors;
        private readonly Action<object> _endLease;
        private readonly List<Lease> _leases = new List<Lease>();

        public PreloadLeases(IViewStage stage, IMainThread mainThread, Diagnostics diagnostics, ErrorRouter errors)
        {
            _stage = stage;
            _mainThread = mainThread;
            _diagnostics = diagnostics;
            _errors = errors;
            _endLease = EndLease;
        }

        // Leases that are loading or held.
        public int Count
        {
            get { return _leases.Count; }
        }

        // True once the asset is loaded and held; false when the owner ended, the load failed or was cancelled.
        public async UniTask<bool> PreloadAsync(ViewEntry row, CancellationToken owner)
        {
            if (owner.IsCancellationRequested)
                return false;

            var lease = new Lease(row.Id);
            lease.Registration = new RegistrationEntry(_mainThread, _diagnostics, _endLease, lease);
            _leases.Add(lease);

            // An owner that is already cancelled ends the lease inline.
            lease.Registration.BindOwner(owner);
            if (lease.Ended)
                return false;

            IDisposable resource = null;
            try
            {
                resource = await _stage.PreloadAsync(row, lease.Cts.Token);
            }
            catch (OperationCanceledException)
            {
                // The owner ended during the load.
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.Provider, row.Id, 0, null);
            }

            if (resource == null)
            {
                lease.Registration.Remove();
                return false;
            }

            if (lease.Ended)
            {
                DisposeResource(resource, lease);
                return false;
            }

            lease.Resource = resource;
            return true;
        }

        // Ends every lease now; used when the root is destroyed.
        public void DisposeAll()
        {
            var snapshot = _leases.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
                snapshot[i].Registration.Remove();
        }

        // Clears into and copies the view id of every lease, loading or held; only reads.
        internal void CaptureViewIds(List<string> into)
        {
            into.Clear();
            for (var i = 0; i < _leases.Count; i++)
                into.Add(_leases[i].ViewId);
        }

        public void Describe(StringBuilder output)
        {
            output.Append("preload leases: ").Append(_leases.Count);
            for (var i = 0; i < _leases.Count; i++)
                output.Append(' ').Append(_leases[i].ViewId);
            output.Append('\n');
        }

        private void EndLease(object state)
        {
            var lease = (Lease)state;
            if (lease.Ended)
                return;

            lease.Ended = true;
            _leases.Remove(lease);

            try
            {
                lease.Cts.Cancel();
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.Provider, lease.ViewId, 0, null);
            }

            var resource = lease.Resource;
            lease.Resource = null;
            if (resource != null)
                DisposeResource(resource, lease);
        }

        private void DisposeResource(IDisposable resource, Lease lease)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.Provider, lease.ViewId, 0, null);
            }
        }

        private sealed class Lease
        {
            public readonly string ViewId;
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            public RegistrationEntry Registration;
            public IDisposable Resource;
            public bool Ended;

            public Lease(string viewId)
            {
                ViewId = viewId;
            }
        }
    }
}
