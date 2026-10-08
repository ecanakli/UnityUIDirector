using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Ecanakli.UIDirector
{
    /// <summary>
    /// Resolves asset keys through Addressables; add it next to <see cref="ViewRoot"/> or pass it in
    /// <see cref="ViewRootSetup"/>.AssetProvider. It owns every handle it creates and is the only caller
    /// that releases them, so view code never sees a handle. Call it from the main thread only.
    /// </summary>
    public sealed class AddressablesViewProvider : MonoBehaviour, IViewProvider
    {
        private readonly HandleTable _instances = new HandleTable();
        private readonly HashSet<Lease> _leases = new HashSet<Lease>();
        private bool _disposed;

        internal int LiveInstanceCount
        {
            get { return _instances.Count; }
        }

        internal int LivePreloadCount
        {
            get { return _leases.Count; }
        }

        /// <summary>
        /// Loads the addressable prefab asset and, only if the call is still wanted and the parent still exists,
        /// instantiates it under the inactive parent and returns it still inactive. When the token is cancelled
        /// first, the call ends at once, nothing is instantiated and the asset is released when its load completes.
        /// </summary>
        /// <param name="assetKey">The addressable key or label of a prefab; must not be null or empty.</param>
        /// <param name="inactiveParent">The parent that keeps the instance dormant; must not be null.</param>
        /// <param name="ct">Cancels the wait; whatever was loaded is released.</param>
        /// <returns>The instance. Hand it back with <see cref="Release"/> and never destroy it by hand.</returns>
        /// <exception cref="ObjectDisposedException">The provider was destroyed.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        /// <exception cref="ArgumentException">The key is null or empty.</exception>
        /// <exception cref="ArgumentNullException">The parent is null.</exception>
        /// <exception cref="InvalidOperationException">The load failed or produced nothing, or the parent was destroyed before the load completed; the message names the key.</exception>
        public UniTask<GameObject> AcquireAsync(string assetKey, Transform inactiveParent, CancellationToken ct)
        {
            ThrowIfDisposed();
            ThrowIfKeyInvalid(assetKey);

            // A C# null is a usage error; a destroyed parent is reported through the task below.
            if (ReferenceEquals(inactiveParent, null))
                throw new ArgumentNullException(nameof(inactiveParent));

            // Usage errors throw at the call; a cancelled token is reported through the task, as async calls do.
            if (ct.IsCancellationRequested)
                return UniTask.FromCanceled<GameObject>(ct);

            if (inactiveParent == null)
            {
                return UniTask.FromException<GameObject>(new InvalidOperationException(
                    "The parent for '" + assetKey + "' was destroyed before its load started."));
            }

            var request = new Request(this, assetKey, inactiveParent, true);
            return request.Run(ct);
        }

        /// <summary>
        /// Releases the instance that <see cref="AcquireAsync"/> returned and destroys it. Does nothing for null,
        /// for an unknown instance or for one that was already released; safe on a destroyed instance; never throws.
        /// </summary>
        /// <param name="instance">The instance returned by <see cref="AcquireAsync"/>.</param>
        public void Release(GameObject instance)
        {
            try
            {
                if (_disposed)
                    return;

                if (_instances.TryTake(instance, out HandleTable.Entry entry))
                    ReleaseInstance(entry.Instance, entry.Asset);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// Loads the prefab asset without instantiating it and keeps it loaded until the returned lease is disposed.
        /// A lease and the instances made from the same key are independent, so their release order does not matter.
        /// </summary>
        /// <param name="assetKey">The addressable key or label of a prefab; must not be null or empty.</param>
        /// <param name="ct">Cancels the wait; the asset is released when its load completes.</param>
        /// <returns>A lease; the first <c>Dispose</c> releases the asset, later calls do nothing.</returns>
        /// <exception cref="ObjectDisposedException">The provider was destroyed.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        /// <exception cref="ArgumentException">The key is null or empty.</exception>
        /// <exception cref="InvalidOperationException">The load failed or produced nothing; the message names the key.</exception>
        public UniTask<IDisposable> PreloadAsync(string assetKey, CancellationToken ct)
        {
            ThrowIfDisposed();
            ThrowIfKeyInvalid(assetKey);

            if (ct.IsCancellationRequested)
                return UniTask.FromCanceled<IDisposable>(ct);

            var request = new Request(this, assetKey, null, false);
            return AwaitLease(request, ct);
        }

        private static async UniTask<IDisposable> AwaitLease(Request request, CancellationToken ct)
        {
            await request.Run(ct);
            return request.Lease;
        }

        // Releases every handle that is still owned, instances first; late completions release themselves.
        private void OnDestroy()
        {
            _disposed = true;

            try
            {
                var entries = new List<HandleTable.Entry>();
                _instances.TakeAll(entries);
                foreach (HandleTable.Entry entry in entries)
                    ReleaseInstance(entry.Instance, entry.Asset);

                var leases = new List<Lease>(_leases);
                _leases.Clear();
                foreach (Lease lease in leases)
                {
                    if (lease.TryClose())
                        ReleaseAsset(lease.Handle);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AddressablesViewProvider));
        }

        private static void ThrowIfKeyInvalid(string assetKey)
        {
            if (string.IsNullOrEmpty(assetKey))
                throw new ArgumentException("The asset key is null or empty.", nameof(assetKey));
        }

        private void CloseLease(Lease lease)
        {
            if (!lease.TryClose())
                return;

            _leases.Remove(lease);
            ReleaseAsset(lease.Handle);
        }

        // Every asset release goes through here: guarded by IsValid, and a failure is logged, never thrown.
        private static void ReleaseAsset(AsyncOperationHandle<GameObject> handle)
        {
            try
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        // Destroys the instance if it still exists, then releases the asset it came from; never throws.
        private static void ReleaseInstance(GameObject instance, AsyncOperationHandle<GameObject> asset)
        {
            try
            {
                if (instance != null)
                    UnityEngine.Object.Destroy(instance);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            ReleaseAsset(asset);
        }

        // One prefab asset load. For an acquire, the completion callback instantiates only while the call is still
        // wanted, the provider is alive and the parent exists; it is the single place that stores or releases the handle.
        private sealed class Request
        {
            private readonly AddressablesViewProvider _owner;
            private readonly string _key;
            private readonly Transform _parent;
            private readonly bool _isInstance;
            private readonly UniTaskCompletionSource<GameObject> _source = new UniTaskCompletionSource<GameObject>();

            private CancellationToken _token;
            private CancellationTokenRegistration _registration;
            private AsyncOperationHandle<GameObject> _handle;
            private bool _completed;
            private bool _abandoned;

            public Request(AddressablesViewProvider owner, string key, Transform parent, bool isInstance)
            {
                _owner = owner;
                _key = key;
                _parent = parent;
                _isInstance = isInstance;
            }

            // Set when a preload succeeds; read by the caller right after the wait.
            public Lease Lease { get; private set; }

            public UniTask<GameObject> Run(CancellationToken ct)
            {
                _token = ct;
                _registration = ct.Register(OnCancelled);

                try
                {
                    _handle = Addressables.LoadAssetAsync<GameObject>(_key);
                }
                catch (Exception exception)
                {
                    _completed = true;
                    _registration.Dispose();
                    throw new InvalidOperationException("Addressables could not start loading '" + _key + "'.", exception);
                }

                _handle.Completed += OnCompleted;
                return _source.Task;
            }

            // The caller stops waiting now; OnCompleted releases whatever arrives later.
            private void OnCancelled()
            {
                if (_completed)
                    return;

                _abandoned = true;
                _source.TrySetCanceled(_token);
            }

            private void OnCompleted(AsyncOperationHandle<GameObject> completed)
            {
                _completed = true;
                _registration.Dispose();

                bool stored = false;
                GameObject instance = null;
                try
                {
                    if (_abandoned)
                    {
                        ReleaseAsset(completed);
                        return;
                    }

                    if (_owner._disposed)
                    {
                        ReleaseAsset(completed);
                        _source.TrySetException(new ObjectDisposedException(nameof(AddressablesViewProvider)));
                        return;
                    }

                    GameObject prefab = null;
                    Exception error = null;
                    if (completed.IsValid())
                    {
                        if (completed.Status == AsyncOperationStatus.Succeeded)
                            prefab = completed.Result;
                        else
                            error = completed.OperationException;
                    }

                    // A null or destroyed result counts as a failed load.
                    if (prefab == null)
                    {
                        ReleaseAsset(completed);
                        _source.TrySetException(new InvalidOperationException(
                            "Addressables failed to load '" + _key + "'.", error));
                        return;
                    }

                    if (!_isInstance)
                    {
                        Lease = new Lease(_owner, completed);
                        _owner._leases.Add(Lease);
                        stored = true;
                        _source.TrySetResult(prefab);
                        return;
                    }

                    if (_parent == null)
                    {
                        ReleaseAsset(completed);
                        _source.TrySetException(new InvalidOperationException(
                            "The parent for '" + _key + "' was destroyed before its load completed."));
                        return;
                    }

                    instance = UnityEngine.Object.Instantiate(prefab, _parent, false);
                    _owner._instances.Add(instance, completed);
                    stored = true;
                    _source.TrySetResult(instance);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    if (!stored)
                    {
                        if (instance != null)
                            ReleaseInstance(instance, completed);
                        else
                            ReleaseAsset(completed);

                        _source.TrySetException(exception);
                    }
                }
            }
        }

        // The preload lease: the first Dispose releases the asset handle, later ones do nothing.
        private sealed class Lease : IDisposable
        {
            private readonly AddressablesViewProvider _owner;
            private bool _closed;

            public Lease(AddressablesViewProvider owner, AsyncOperationHandle<GameObject> handle)
            {
                _owner = owner;
                Handle = handle;
            }

            public AsyncOperationHandle<GameObject> Handle { get; }

            public void Dispose()
            {
                _owner.CloseLease(this);
            }

            // True once, for the caller that may release the handle.
            public bool TryClose()
            {
                if (_closed)
                    return false;

                _closed = true;
                return true;
            }
        }
    }
}
