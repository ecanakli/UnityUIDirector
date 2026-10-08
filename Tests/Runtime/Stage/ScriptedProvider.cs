using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // A view provider the test drives by hand: every load waits until the test completes it; releases and preloads are recorded.
    [AddComponentMenu("")]
    internal sealed class ScriptedProvider : MonoBehaviour, IViewProvider
    {
        private readonly List<Load> _loads = new List<Load>();
        private readonly List<GameObject> _released = new List<GameObject>();
        private readonly List<bool> _activeAtRelease = new List<bool>();
        private readonly List<string> _preloadKeys = new List<string>();

        // Thrown by every Release when set; the instance is then left as it is, like a broken provider would.
        public Exception ReleaseFault;

        // Every PreloadAsync fails with it when set, through the returned task as an async provider would.
        public Exception PreloadFault;

        public int AcquireCount
        {
            get { return _loads.Count; }
        }

        public int ReleaseCount
        {
            get { return _released.Count; }
        }

        public IReadOnlyList<Load> Loads
        {
            get { return _loads; }
        }

        // The objects handed to Release, in call order.
        public IReadOnlyList<GameObject> Released
        {
            get { return _released; }
        }

        // activeSelf of each released object at the moment Release ran.
        public IReadOnlyList<bool> ActiveAtRelease
        {
            get { return _activeAtRelease; }
        }

        public IReadOnlyList<string> PreloadKeys
        {
            get { return _preloadKeys; }
        }

        public int LeaseDisposeCount { get; private set; }

        // The most recent load; throws when none was started.
        public Load LastLoad
        {
            get
            {
                if (_loads.Count == 0)
                    throw new InvalidOperationException("The provider has not been asked to load anything.");

                return _loads[_loads.Count - 1];
            }
        }

        // The token is recorded, not honoured, so a late result reaches the stage as a real slow load would.
        public UniTask<GameObject> AcquireAsync(string assetKey, Transform inactiveParent, CancellationToken ct)
        {
            var load = new Load(assetKey, inactiveParent, ct);
            _loads.Add(load);
            return load.Source.Task;
        }

        public void Release(GameObject instance)
        {
            _released.Add(instance);
            _activeAtRelease.Add(instance != null && instance.activeSelf);

            if (ReleaseFault != null)
                throw ReleaseFault;

            if (instance != null)
                Object.Destroy(instance);
        }

        public UniTask<IDisposable> PreloadAsync(string assetKey, CancellationToken ct)
        {
            _preloadKeys.Add(assetKey);
            if (PreloadFault != null)
                return UniTask.FromException<IDisposable>(PreloadFault);

            return UniTask.FromResult<IDisposable>(new Lease(this));
        }

        // One AcquireAsync call, completed by the test.
        public sealed class Load
        {
            public readonly string AssetKey;
            public readonly Transform Parent;
            public readonly CancellationToken Token;
            public readonly UniTaskCompletionSource<GameObject> Source = new UniTaskCompletionSource<GameObject>();

            public Load(string assetKey, Transform parent, CancellationToken token)
            {
                AssetKey = assetKey;
                Parent = parent;
                Token = token;
            }

            // Instantiates the inactive template under the parent the stage passed, completes the load with it and returns it.
            public GameObject CompleteWithInstanceOf(GameObject template)
            {
                var instance = Object.Instantiate(template, Parent, false);
                Source.TrySetResult(instance);
                return instance;
            }

            public void Complete(GameObject instance)
            {
                Source.TrySetResult(instance);
            }

            public void Fail(Exception exception)
            {
                Source.TrySetException(exception);
            }
        }

        private sealed class Lease : IDisposable
        {
            private ScriptedProvider _owner;

            public Lease(ScriptedProvider owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (ReferenceEquals(_owner, null))
                    return;

                _owner.LeaseDisposeCount++;
                _owner = null;
            }
        }
    }
}
