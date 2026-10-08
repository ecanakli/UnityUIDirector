using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    // Events only the stage can see; the navigator implements this and the stage calls it.
    internal interface IStageEvents
    {
        // An instance was destroyed (true) or deactivated (false) by something other than the package.
        void OnInstanceLost(object instance, bool destroyed);

        // The shared backdrop of a modal layer was tapped.
        void OnBackdropTapped(ViewLayer layer);
    }

    // The only thing the logic knows about Unity UI. An instance is an opaque handle the stage understands.
    internal interface IViewStage
    {
        // Registers the sink for events the stage raises on its own; called once, before any other member.
        void SetEvents(IStageEvents events);

        // Gets an inactive instance for the row; throws or returns null on failure; stops under ct and releases a late result itself.
        UniTask<object> AcquireAsync(ViewEntry row, CancellationToken ct);

        // Finds the typed view and its optional hooks on the instance, or says why it cannot.
        ViewResolution ResolveView<TArgs, TResult>(object instance, out ViewBinding<TArgs, TResult> binding);

        // Runs the root's inject step on a new instance while it is inactive; may throw.
        void Inject(object instance);

        // Parents the instance under the layer, links it and activates it so Awake and OnEnable run; its canvases and raycasters stay as the view leaves them until the first Apply, which the caller makes in the same synchronous step.
        void Place(object instance, ViewLayer layer, ViewHandle handle);

        // Links an instance that stays in place to the request that took it over (reopen in place).
        void Relink(object instance, ViewHandle handle);

        // Applies the derived hidden, interactive and sorting state to the instance.
        void Apply(object instance, in ViewVisualState state);

        // Writes the transition end pose now; idempotent; a fault from the transition propagates and the caller routes it.
        void Snap(object instance, bool shown);

        // Plays the show or hide transition on unscaled time; stops when ct is cancelled.
        UniTask PlayAsync(object instance, bool show, CancellationToken ct);

        // Places the layer's shared backdrop at the sorting order, or hides it when not active.
        void ApplyBackdrop(ViewLayer layer, bool active, int sortingOrder);

        // Raises (true) or drops the shield at the sorting order; the first raise also clears the UI selection.
        void SetShield(bool blocking, int sortingOrder);

        // Shows or hides the load indicator.
        void SetLoadIndicator(bool visible);

        // Parks a kept instance hidden and inactive under staging for the next open of its id.
        void Stash(object instance);

        // Releases the instance through the provider or destroys it; it counts as released even when the provider throws, and that fault propagates to the caller; safe on a destroyed instance.
        void Release(object instance);

        // Loads the row's asset and keeps it loaded until the lease is disposed; a non-null lease means success, null or a throw means failure.
        UniTask<IDisposable> PreloadAsync(ViewEntry row, CancellationToken ct);

        // Gets the GameObject behind an instance for ViewHandle.TryGetInstance; false when it has none.
        bool TryGetGameObject(object instance, out GameObject gameObject);
    }
}
