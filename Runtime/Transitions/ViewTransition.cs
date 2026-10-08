using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>
    /// Base of every transition; a component on the view prefab, referenced by its ViewHost.
    /// A custom transition uses unscaled time, stops when the token is cancelled, keeps <see cref="Snap"/>
    /// synchronous, idempotent and non-throwing, and never calls the navigator.
    /// </summary>
    public abstract class ViewTransition : MonoBehaviour
    {
        /// <summary>Animates to the shown or hidden pose on unscaled time, starting from the opposite end pose; stops when ct is cancelled.</summary>
        public abstract UniTask PlayAsync(bool show, CancellationToken ct);

        /// <summary>Writes the end pose now; synchronous, idempotent and never throws.</summary>
        public abstract void Snap(bool shown);
    }
}
