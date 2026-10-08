# Limitations

[Back to index](index.md)

What UIDirector does not do, stated plainly. Each item says what the limit is and, where there is one, what to do instead. "Not in 0.1.0" means exactly that: the feature is absent from this version. It is not a promise about a later one.

---

## Setup

**One root per application is the documented setup.** A second `ViewRoot` works as an independent system: it has its own layers, input shield, back rule, queues and sorting band. Nothing coordinates two roots. `Back()` on one root knows nothing about the other, and each root numbers its own sorting band from its own `Base Sorting Order`, so two roots that share a canvas sorting layer need bases that keep their bands apart. See [Root canvas](Root-Canvas.md).

**Catalogs and rows cannot be built from code.** 0.1.0 has no public API to create a `ViewCatalog` with rows or a `ViewEntry`: a row's values are read-only, its constructors are not public and a catalog's row list cannot be filled from code, so a catalog is an asset authored in the Inspector (`Assets > Create > UIDirector/View Catalog`), and the root's `Catalogs` list is Inspector data too. What code can do is register an existing catalog asset with `INavigator.AddCatalog` and pass the code-only settings in `ViewRootSetup`. For content that differs by feature or build, author one catalog per set and register the sets you need. See [Catalogs and keys](Catalogs-and-Keys.md#make-a-view-and-its-row).

**uGUI only, and no world-space UI.** The package builds its layers, shield and backdrops from uGUI objects under one root canvas. The root canvas is `Screen Space - Overlay` or `Screen Space - Camera`. A world-space root canvas is not a supported setup and nothing in the package handles it. UI Toolkit is not supported either.

**No nested navigators inside a view.** A view prefab is a leaf of its layer. A `ViewRoot` inside a view prefab is not a supported setup and the package does not detect it. A view that needs a sub-flow opens other views through the one navigator of its root.

**Direct prefabs are instantiated synchronously.** A catalog row with a `Prefab` reference is instantiated with `Instantiate` in the call that opens it, under an inactive parent so `Awake` does not run yet. A heavy prefab therefore costs its instantiation time inside one frame. Rows with an asset key go through the view provider and load asynchronously; see [Assets and Addressables](Assets-and-Addressables.md).

**The sorting band is finite.** Each layer has 99 slots, the whole band including the input shield must stay below 29999, and Unity stores sorting orders as signed 16-bit values. The package refuses a request instead of letting the order wrap ([UIDIRECTOR115](Troubleshooting.md#uidirector115)). Content that sorts itself above the band, such as a TextMesh Pro dropdown list, draws over everything the package shows.

**Only the Escape key is polled for back.** `BackInput` reads Escape from the Input System when that package is installed and active in Active Input Handling (on any keyboard, through an input action it owns), otherwise from the legacy input manager, and does nothing when neither is enabled. Android's back button is expected to arrive as Escape. The package does not manage `Input.backButtonLeavesApp` and offers no predictive back animation. Before the root is initialized `BackInput` ignores the key: it does not call `Back()`, does not raise `Unhandled` and does not initialize the root. A `ViewButton` with the Back or Open View action logs a warning and does nothing in that window. See [Input and platform](Input-and-Platform.md#the-back-button).

**Keyboard and gamepad focus is not managed.** Focus restore after a view closes and on-screen keyboard avoidance are not provided. The package clears the selection when a view loses input and does nothing more, so navigation with a keyboard, a gamepad or a TV remote can still move the selection from the top view into a covered or non-interactive one. That is standard uGUI behaviour: navigation starts from the selected object, and `Automatic` navigation searches every `Selectable` in the scene. To keep moves inside a view, set the `Navigation` of its `Selectable` components to `Explicit`. A row whose covered screens must be unreachable can use `When Covered` = `Deactivate`, which unregisters their `Selectable` components. The package does not set `CanvasGroup.interactable` or `Selectable.navigation` for you, because both are state your views own.

---

## Navigation

**Popups snap when navigation closes them.** When a screen opens, popups that are older than that screen are ended at once: they jump to their hidden pose and end with `CloseReason.Navigated`. They get no hide transition. A popup that closes by `Complete`, `Close`, Back or `ViewHandle.Close()` plays its hide transition as usual. Views that were destroyed or deactivated outside the package also end without a transition ([UIDIRECTOR107](Troubleshooting.md#uidirector107)).

**Scene changes close nothing.** The package does not load scenes and does not close views when a scene changes. A view lives as long as its owner token. Tie the owner to something that survives the scene, such as `INavigator.RootToken`, or to something that ends with it. Scene-placed view instances cannot be adopted; a view is always a prefab instance created by the package.

**A covered screen stays loaded.** Screens below the top of the history stack keep their instances. Releasing covered screens by depth is not provided. `CloseTo` and returning to an existing screen keep the stack short.

**Transitions are not interruptible.** A show or hide transition plays to its end, is snapped by the transition timeout, or is snapped when the request ends. A transition is never reversed from the middle. The input shield blocks input for the views involved while it runs.

**A call that joins an existing request does not bind its token.** `ShowAsync` or `Open` with a key without arguments on a history layer returns to the request that is already in the stack when there is one. That request keeps its own owner: the `owner` token of the joining call is ignored, `ShowAsync` completes with the outcome of the existing request, and `Open` returns its handle. To stop waiting at a token without ending the request, attach the token to the wait:

```text
var (stopped, result) = await navigator.ShowAsync(key, ct).AttachExternalCancellation(ct).SuppressCancellationThrow();
```

`stopped` is true whenever that token ended. For a call that starts a new request the same token is also its owner, so the view ends as `OwnerEnded` at the same time. When you need the outcome of the request, pass the wait its own token. See [Opening and results](Opening-and-Results.md).

**A queue never interrupts a shown view.** The queue presents waiting requests one at a time by priority. A higher priority does not close a view that is on screen. Urgent content opens immediately or goes to the `system` layer.

**Gates answer yes or no.** `GateDecision` has `Allow` and `Deny`. There is no "defer" and no "redirect". "Not now" is a queue hold; a variant of a view is chosen by your code before it enqueues. Requests do not expire and there is no cooldown or frequency gate; a stale request is ended by its owner token or by a gate that checks again at presentation.

**No sequence or plan types.** A conditional flow is an async method that awaits results. To keep the queue from presenting something in between, the method takes a queue hold.

---

## Timeouts, defaults and the frame clock

These numbers are defaults. Each is a field on the `ViewRoot` Inspector; change it when your project has numbers of its own.

| Default | `ViewRoot` field | Minimum | What it bounds |
|---|---|---|---|
| 15 s | `Load Timeout Seconds` | 0.1 | A view load; then the request ends with `RejectReason.LoadTimedOut` ([UIDIRECTOR105](Troubleshooting.md#uidirector105)) |
| 3 s | `Transition Timeout Seconds` | 0.1 | A transition; then it is snapped to its end ([UIDIRECTOR106](Troubleshooting.md#uidirector106)) |
| 0.4 s | `Indicator Delay Seconds` | 0 | How long a load runs before the `Load Indicator` is shown |
| 4 | `Waiting Cap` | 1 | Requests one layer may hold waiting before new ones are refused ([UIDIRECTOR112](Troubleshooting.md#uidirector112)) |

The timeouts and the delay are measured in unscaled time that the root advances in its `Update`. One frame adds at most 0.1 s, so a long hitch or a suspension counts as one short step, not as the time that really passed.

Built-in transitions and the backdrop fade advance at most 1/30 s per frame, while the clock of the timeouts advances at most 0.1 s. Below 30 fps they therefore take longer than their duration: a duration of D seconds takes at least 30 x D frames, so a built-in transition at its default 0.25 s takes about 0.4 s at 20 fps. Between 10 and 30 fps, a built-in transition longer than about fps / 10 seconds is snapped by the 3 s transition timeout with [UIDIRECTOR106](Troubleshooting.md#uidirector106) although it animates correctly (at 20 fps, 2 s). Below 10 fps the clock is capped as well, so any built-in transition of 1 s or longer is snapped. Raise `Transition Timeout Seconds`, or shorten the transition. Custom `ViewTransition` classes do not use this loop. See [Transitions](Transitions.md).

The warning for a queue hold that is older than 60 s ([UIDIRECTOR111](Troubleshooting.md#uidirector111)) uses the same clock when the queue's navigator is the one a `ViewRoot` creates, so below 10 fps it fires later in wall time. For any other `INavigator` implementation the queue uses a delay timer instead, which is checked every frame for the first 60 s of a hold.

---

## Tooling

**No custom transition contract checker ships.** A custom `ViewTransition` must animate on unscaled time, stop when its token is cancelled, keep `Snap` synchronous, idempotent and non-throwing, and never call the navigator. The package documents that contract and protects itself with the transition timeout and a synchronous `Snap`, but it does not ship a test that checks your transition against it. See [Transitions](Transitions.md).

**No shipped testing assembly.** There is no probe, dry-run or fake navigator in the package. Decision logic is plain classes, and the `ViewResult` factories are public (`Completed`, `Closed`, `NotShown`), so a game can fake `INavigator` itself.

**The validator checks the project, not a build.** `Tools/UIDirector/Validate Catalogs` runs in the editor. It skips `[ViewKeys]` classes in assemblies that reference `nunit.framework`, and its Addressables checks run only when the Addressables editor package is present.

**Stripping can remove a view script that only a bundle references.** The package finds the view component on a prefab by interface. A view script that no scene or build-time reference uses, only a prefab inside an Addressables bundle, may be stripped by managed code stripping. If a build reports a missing view component, keep the assembly with a `link.xml` that preserves it.

---

## Lifetime and cleanup

**A staged instance that is destroyed before it is shown ends through the load failure.** A view instance waits inactive under the staging object until it is prepared. A GameObject that was never activated sends no destroy message, so the package does not hear about its loss. It finds out when it prepares the instance, and the request ends as not shown with `RejectReason.LoadFailed` ([UIDIRECTOR104](Troubleshooting.md#uidirector104)), not through UIDIRECTOR107.

**Views lost outside the package end their request.** Destroying or deactivating a view that the package shows ends its request ([UIDIRECTOR107](Troubleshooting.md#uidirector107)). Deactivating the root object or a layer object ends every view under it. A view with `KeepInstance` that is deactivated or destroyed outside the package is not kept: its instance is released, and the next open creates a new one. To hide the whole UI for a moment without ending anything, use the recipe in [Hide the whole UI for a moment](Root-Canvas.md#hide-the-whole-ui-for-a-moment).

**A view lost within one frame is not noticed.** The package decides one frame after a loss. A view that is deactivated and active again within that frame ends nothing.

**A failed reopen in place removes the view.** When a request reopens a view that is already on screen and preparing the instance fails, the on-screen instance is released once and the new request ends as not shown with `RejectReason.LoadFailed` ([UIDIRECTOR104](Troubleshooting.md#uidirector104)). The view disappears.

**A root that is destroyed from inside an observer callback skips queued events for observers.** The package holds back events that are published while an observer is being called, and delivers them afterwards. When the root is destroyed during that call, its observers are removed first, so observers, and the last-events list that the editor window shows, miss those held-back terminal events. Awaiters still complete.

**A root that is never activated never tears down.** The root tears down in Unity's `OnDestroy`, and Unity sends that message only to an object whose `Awake` has run. A root that was initialized while inactive and is destroyed without ever becoming active receives no message, so `RootToken` is not cancelled and the views it holds do not end through the root. Initializing an inactive root and activating it later, as a dependency injection container does, is fine.

---

## Threading

**Main thread only.** Opening, registering and queueing throw on another thread. A few stop-type calls and owner cancellations are posted to the main thread and take effect up to one frame later. Several calls, such as `ViewHandle.Close()` and the queries, are not checked at all. See [Threading](Threading.md).

---

## Not in 0.1.0

The following are outside the package on purpose. Each belongs to the game.

- A scene hook, scene groups and scene-backed views.
- Pooling of views in general, least-recently-used budgets, asset linger timers and lease counting. Keeping an instance is one choice per catalog row (`KeepInstance`), and `Trim` releases the idle ones.
- Toasts and HUD elements as built-in kinds, auto-close and bounded transient lists. A project-declared layer that does not shield input can host them.
- VContainer, UI Toolkit, MVVM, data binding and presenter base types. The inject seam is one delegate, so another container is a few lines of your code; Zenject has an integration, see [Zenject](Zenject.md).
- An animator-driven transition. A custom `ViewTransition` covers it.
- A signal that everything is covered, for disabling a world camera. Your code can derive it from the shown and closed events; see [Events and analytics](Events-and-Analytics.md).
- Purchases and rewards with exactly-once outcomes. That is not a UI concern.
- Restoring the stack after the process was killed, deep-link parsing, analytics adapters, localization, and calling `Resources.UnloadUnusedAssets`.

---

## Versions

**Unity 6000.0 is the declared minimum.**

**Required dependencies are UniTask and `com.unity.ugui`.** Addressables, DOTween and Zenject are optional integrations that compile only when they are present.

## See also

- [Concepts](Concepts.md)
- [Troubleshooting](Troubleshooting.md)
- [Threading](Threading.md)
- [Performance](Performance.md)
- [Migration](Migration.md)
- [FAQ](FAQ.md)
