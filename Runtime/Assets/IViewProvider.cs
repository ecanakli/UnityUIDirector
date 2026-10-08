using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>Creates and releases instances for rows that carry an asset key.</summary>
    public interface IViewProvider
    {
        /// <summary>Returns an inactive instance under the given inactive parent; on cancel it releases whatever it loaded, also when the load completes later.</summary>
        UniTask<GameObject> AcquireAsync(string assetKey, Transform inactiveParent, CancellationToken ct);

        /// <summary>Releases exactly what AcquireAsync returned; never throws; safe on a destroyed instance.</summary>
        void Release(GameObject instance);

        /// <summary>Keeps the asset loaded until the returned lease is disposed.</summary>
        UniTask<IDisposable> PreloadAsync(string assetKey, CancellationToken ct);
    }
}
