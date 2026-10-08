# Architecture

This is the design document for the UIDirector package, written against the shipped code. It is the
page to read before reviewing the source or adding a feature. The user-facing pages explain *how to
use* the package; this one explains *why it is shaped the way it is* and *what the shape
guarantees*.

- Usage: [index](../index.md), [Concepts](../Concepts.md),
  [Opening and results](../Opening-and-Results.md), [Navigation and back](../Navigation-and-Back.md),
  [Popups and backdrop](../Popups-and-Backdrop.md), [System overlays](../System-Overlays.md),
  [View authoring](../View-Authoring.md), [Queue](../Queue.md),
  [Catalogs and keys](../Catalogs-and-Keys.md),
  [Assets and Addressables](../Assets-and-Addressables.md), [Transitions](../Transitions.md),
  [Events and analytics](../Events-and-Analytics.md), [Diagnostics](../Diagnostics.md),
  [Troubleshooting](../Troubleshooting.md)
- Decisions:
  [ADR-001 Outcomes as data](Decisions/ADR-001-Outcomes-As-Data.md),
  [ADR-002 Strict serialization](Decisions/ADR-002-Strict-Serialization.md),
  [ADR-003 Begin-only hooks and scopes](Decisions/ADR-003-Begin-Only-Hooks-And-Scopes.md),
  [ADR-004 The queue sits above the navigator](Decisions/ADR-004-Queue-Above-The-Navigator.md),
  [ADR-005 Token owners](Decisions/ADR-005-Token-Owners.md)

---

## 1. Problem and goals

A Unity project that opens screens and popups with a few ad-hoc scripts meets the same failures
again and again. The failure classes below are the requirements list of the package; each one is
mapped to the mechanism that removes it in the sections that follow.

- **Input during motion.** A second tap lands while a transition runs. A screen opens twice. A close
  and an open race and leave a view half shown.
- **A back key that means different things.** The hardware back key closes a popup that sits under a
  dialog, or reaches a screen that is covered by a modal.
- **Work that outlives its view.** A handler stays subscribed to a button of a closed view. A task
  keeps running against an object that is gone.
- **An answer that cannot be awaited.** A popup that returns a value turns into a callback chain.
  When the code that waits for the answer is destroyed, the chain breaks without a trace.
- **A view lost from outside.** Project code or a scene change destroys or deactivates a view
  directly, and a stale stack entry, a backdrop and an input block stay behind.
- **Popups that collide.** Several systems want to show a popup in the same frame and nobody decides
  the order.
- **Assets that arrive late.** A load finishes after the request that wanted it was withdrawn.
- **A transition that never ends.** A custom animation runs on scaled time while the game is paused,
  and the input block never lifts.

**Goals**

- **One model for every view kind.** Screens, popups and blocking overlays are prefab views on one
  root. They differ only in the rules of the layer they open on.
- **Typed keys and awaitable results.** A `ViewKey<TArgs, TResult>` fixes the arguments and the
  result at compile time. `ShowAsync` completes with a `ViewResult<TResult>` in every case
  ([ADR-001](Decisions/ADR-001-Outcomes-As-Data.md)).
- **One back rule.** `Back()` evaluates one ordered rule list
  ([section 8](#8-back-resolution)).
- **Structural cleanup.** Bindings and tasks live on a scope that the package ends exactly once on
  every path ([ADR-003](Decisions/ADR-003-Begin-Only-Hooks-And-Scopes.md)).
- **Derived state.** Visibility, interactivity and sort order are recomputed from the layer lists
  after every change, never accumulated by push and pop ([section 6](#6-projection)).
- **Bounded stalls.** Every wait the package starts has a timeout or a watchdog, and the input
  shield is released in a `finally` ([section 5](#5-the-per-layer-runner)).
- **One owner per request.** Every request names the `CancellationToken` that ends it
  ([ADR-005](Decisions/ADR-005-Token-Owners.md)).
- **Optional integrations in their own assemblies.** Addressables, DOTween and Zenject are gated
  assemblies; the core compiles without them ([section 3](#3-assemblies)).
- **Near-zero idle cost.** One clock tick per frame and a back-key check, no allocation
  ([section 18](#18-allocation-and-idle-cost)).

**Non-goals in 0.1.0**

- Persistence. Nothing is saved, and a view result is a message held in memory
  ([section 11](#11-exactly-once-outcomes)).
- Game-structure concepts. The package has layers, views and requests, and nothing that models a
  part of a particular game.
- A route graph or deep links. Navigation is a stack per history layer plus `CloseTo`.
- Another UI technology. The logic talks to an internal stage seam; uGUI is its only
  implementation and the seam is not public.
- A built-in toast or HUD layer. A project declares such a layer in code; without
  `ShieldsInput` it never takes the shield.
- A global event bus. The event stream has one publisher per navigator and one for each queue
  ([section 10](#10-events-and-the-order-at-close)).
- General pooling. `KeepInstance` keeps one idle instance per view id and nothing more.
- Use off the main thread ([section 17](#17-threading)).

The full list for users is in [Limitations](../Limitations.md).

---

## 2. The root and its layers

The package is **one root with ordered layers**. A `ViewRoot` component sits on a canvas that the
project owns. It builds a fixed hierarchy under itself, creates an internal `Navigator`, and exposes
it as `INavigator`.

```text
Canvas                    owned by the project: render mode, scaler, sorting layer
  ViewRoot                the entry point; BackInput is an optional neighbour
    Layer screens         a stretched RectTransform, order 0
      <view>              a nested Canvas (overrideSorting) + GraphicRaycaster + ViewHost
    Layer popups          order 1
      Backdrop            one shared blocker for the layer
      <view> ...
    Layer system          order 2
      Backdrop
    Layer <project layer> declared in code through ViewRootSetup.ExtraLayers
    Staging               inactive; new and kept instances wait here
    Shield                its own Canvas + ShieldGraphic with an empty mesh, above every layer
    (load indicator)      an optional object assigned on the root
```

**A layer is an immutable value.** `ViewLayer` carries an id, an order and a set of
`ViewLayerRules` flags. A catalog row names its layer by id. A layer holds an ordered list of
requests and has one runner. There is one `CanvasScaler`, on the project's canvas; layers are plain
stretched `RectTransform` objects, and each view is a nested canvas with `overrideSorting`.

| Rule | Meaning |
|---|---|
| `History` | The list is a stack and is the back history. A view covers what is below it by default. Opening an id that is already in the stack returns to it. The bottom view is the root for `Back()`. |
| `Modal` | The layer has one shared backdrop under its top view, and everything below that view is non-interactive. |
| `TakesBack` | `Back()` may close the layer's top view. Without it a modal layer swallows back and a non-modal layer lets it pass. |
| `ShieldsInput` | Opens, closes and loads on this layer hold the input shield. |
| `ClosedByNavigation` | Views on this layer close when a view opens on a `History` layer. |

| Layer | Order | Rules |
|---|---|---|
| `ViewLayers.Screens` | 0 | `History`, `TakesBack`, `ShieldsInput` |
| `ViewLayers.Popups` | 1 | `Modal`, `TakesBack`, `ShieldsInput`, `ClosedByNavigation` |
| `ViewLayers.System` | 2 | `Modal`, `ShieldsInput` |

A project adds layers with `ViewRootSetup.ExtraLayers`. The root merges them with the three defaults
and sorts them stably by order. A layer with `ViewLayerRules.None` is a plain layer: it shields
nothing, takes no back and covers nothing. `ClosedByNavigation` closes only views that were accepted
before the screen that triggered it, so a popup opened by the new screen survives
([section 5](#5-the-per-layer-runner)).

**The root.**

- It is not a singleton. Each root has its own navigator, and several roots can exist.
- `ViewRoot.Initialize(ViewRootSetup)` runs at most once. `ViewRootSetup` carries four code-only
  settings: `Inject` (an `Action<GameObject>` run on each new instance while it is inactive),
  `AssetProvider`, `ExtraLayers` and `ErrorHandler`. Numbers and references are Inspector fields:
  the catalogs, the base sorting order, the load and transition timeouts, the load indicator delay,
  the waiting cap, the load indicator object and the backdrop colour or prefab.
- "Initialize On Awake" is on by default. Reading `ViewRoot.Navigator` on a root that is not
  initialised initialises it with default settings; a root that was destroyed before that throws.
- "Persist Across Scenes" calls `DontDestroyOnLoad` on the root's topmost object, so a UI camera that
  sits beside the root under a shared parent survives with it.
- `ViewRoot.Update` ticks the navigator clock with `Time.unscaledDeltaTime`, and compares the safe area
  only while a `SafeAreaFitter` is registered ([section 18](#18-allocation-and-idle-cost)).
- The project owns the canvas: render mode, reference resolution, sorting layer and shader channels.
  The package reads the root's sorting layer at every placement, so a project that changes it at run
  time moves the whole band. An overlay canvas has no sorting layer; the base sorting order places
  the band in both render modes. A root in camera mode whose camera is destroyed is reported once
  with `UIDIRECTOR114`.

**Teardown.** When the root is destroyed the navigator marks itself gone and clears every layer's
pending operations, so a runner that resumes inside an end finds nothing to dequeue. It then ends
every request: shown requests as `CloseReason.Destroyed`, the others as not shown with
`RejectReason.RootGone`. Awaiters complete inside that routine. It then releases every shield
holder, removes every registration, disposes preload leases, releases kept instances, removes
observers, clears the clock, and cancels `RootToken` last, so whoever waits on that token finds a
settled navigator. After teardown every entry point answers "gone": `ShowAsync` completes at once
with `RootGone`, `Back()` returns `false`, and nothing throws.

A root that is destroyed from inside an observer callback or an outbox entry follows the same order,
but the notifications wait for the dispatch or entry in progress ([section 10](#10-events-and-the-order-at-close)):
the observers are removed before the held-back terminal events drain, so observers and the event
ring miss those events, while every awaiter still completes. `RootToken` is then cancelled before
those notifications run.

---

## 3. Assemblies

| Assembly | Folder | Namespace | Compiled when |
|---|---|---|---|
| `Ecanakli.UIDirector` | `Runtime/` | `Ecanakli.UIDirector` | Always |
| `Ecanakli.UIDirector.Addressables` | `Addressables/` | `Ecanakli.UIDirector` | `com.unity.addressables` is installed (`versionDefines` sets `ECANAKLI_UIDIRECTOR_ADDRESSABLES`) |
| `Ecanakli.UIDirector.DOTween` | `DOTween/` | `Ecanakli.UIDirector` | `DOTWEEN` is defined, or `com.demigiant.dotween` is installed (`ECANAKLI_UIDIRECTOR_DOTWEEN`), or the editor menu set the symbol |
| `Ecanakli.UIDirector.DependencyInjection.Zenject` | `DependencyInjection/Zenject/` | `Ecanakli.UIDirector.DependencyInjection` | `ECANAKLI_UIDIRECTOR_DI_ZENJECT` is defined: by `versionDefines` for `com.svermeulen.extenject` 9.0.0 or newer, or by the editor menu |
| `Ecanakli.UIDirector.Editor` | `Editor/` | `Ecanakli.UIDirector.EditorTools` | In the editor only |

```text
game  ->  Ecanakli.UIDirector.DOTween                      ->  Ecanakli.UIDirector  ->  UniTask, uGUI
                                                           ->  DOTween.dll
game  ->  Ecanakli.UIDirector.Addressables                 ->  Ecanakli.UIDirector
                                                           ->  Addressables, ResourceManager
game  ->  Ecanakli.UIDirector.DependencyInjection.Zenject  ->  Ecanakli.UIDirector
                                                           ->  Zenject
          Ecanakli.UIDirector.Editor                       ->  Ecanakli.UIDirector
```

Rules that the layout follows:

- **The core references UniTask and uGUI.** `package.json` declares `com.unity.ugui` and no other
  dependency: a git package cannot declare a git dependency, so UniTask is a prerequisite that the
  README lists. The Input System is optional; `BackInput` uses it when
  `ECANAKLI_UIDIRECTOR_INPUT_SYSTEM` is set and falls back to the legacy input manager otherwise.
- **Every integration is a gated assembly.** The core never sees a type of Addressables, DOTween or
  Zenject, and removing the define removes the assembly without touching the core. Catalog rows that
  carry an asset key stay valid without Addressables; they fail as `LoadFailed` with
  `UIDIRECTOR120` unless the root has another provider.
- **No assembly depends on another package of the same author.** The integrations use the public
  API only.
- **Internals are visible to tests and to the integration that needs them.** The core grants
  `InternalsVisibleTo` to the Addressables assembly, the editor assembly and the test assemblies.
- **Names avoid collisions.** No namespace segment is `DOTween`, `Zenject` or `Editor`, because such
  a child namespace would capture those identifiers inside `Ecanakli.UIDirector`. No public type is
  named `View`, `Lifetime` or `None`.
- **Every public member of the runtime assemblies has XML documentation,** enforced by a compiler
  warning-as-error in development builds.
- **One static flag.** Keys and the three default layers are immutable values. The only mutable
  static that carries state is `QuitSignal`, the process-wide "the application is quitting" flag (apart
  from a lock-guarded cache of diagnostic link suffixes). It is cleared and its
  single `Application.quitting` subscription is renewed at subsystem registration, so entering Play
  Mode without a domain reload needs no reset ([section 12](#12-external-loss)).

---

## 4. Requests and phases

A **request** is one call to open one view, tracked from acceptance to its end by an internal
`ViewRecord` that carries a request id. `ViewHandle` is a read-only struct over it; it is inert once
the request has ended and it is not awaitable. Callers get a result by awaiting `ShowAsync`, or by
calling `WaitForCloseAsync(CancellationToken)` on a handle.

### Acceptance

`Navigator.ShowAsync` and `Open` pass every call through `RequestIntake.Submit`, which decides
synchronously at the call. The checks run in this order and the first failure ends the request as not
shown:

1. The root is gone: `RootGone`.
2. The owner token is already cancelled: `OwnerEnded`.
3. No catalog row has the id: `UnknownKey` (`UIDIRECTOR101`).
4. The row names a layer the root does not have: `UnknownKey` (`UIDIRECTOR109`).
5. The request came from a queue and the row is on another layer than the queue's: `WrongLayer`
   (`UIDIRECTOR126`). The detail names both layers. Nothing is placed and the shield is not taken.
   The check runs here, against the live registry, because rows can be added or removed after an
   item is enqueued.
6. The id is already in flight or open on a layer that is not `History`, and the row does not set
   `AllowMultiple`: `Duplicate`.
7. The call set `OpenOptions.RejectWhenBusy` and the navigator is busy: `Busy`.
8. The layer is a `History` layer, the id is already in its stack and the key has no arguments
   (`NoArgs`): not rejected and not new. The caller joins the existing request
   ([Reopen in place](#reopen-in-place)).
9. The layer already has the waiting cap of requests in `Waiting` (default 4): `Busy`
   (`UIDIRECTOR112`).
10. The sort band has no free slot: `Busy` (`UIDIRECTOR115`, see [section 6](#6-projection)).

A rejected request still gets a request id and publishes `NotShown` at the call, and `Open` returns
an empty handle. An accepted request is added to its layer in `Waiting`, takes a shield holder when
the layer has `ShieldsInput`, publishes `Requested`, and only then registers its owner token, so a
token that fires during registration finds the shield and the event in place and cannot reorder
events or leak a holder. A call made from inside an observer callback publishes `Requested` and the
rejection's `NotShown` after the event in progress has reached every observer
([section 10](#10-events-and-the-order-at-close)).

A request whose close is already latched, or that a newer request is replacing, does not count as
"already open" for checks 6 and 8, so a new request for its id is accepted and runs after the close.
`IsOpen` and `TryGetOpen` still count it until it ends, because its handle stays useful.

### Phases

`ViewPhase` has six values. A request passes through each at most once and never goes back
(`TryAdvancePhase` refuses to stay or to move backwards).

| Phase | Meaning | What runs |
|---|---|---|
| `Waiting` | Accepted, in its layer's list, not reached by the runner | Nothing. A withdrawal here ends it at once. |
| `Loading` | The runner reached it and is acquiring the instance | A kept instance is taken, or the provider loads under the load timeout. A late result is released by the provider. |
| `Opening` | `OnOpen` returned and the show transition runs | The first projection pass applies the view's visible state (the package switches off only what must be hidden or non-interactive). The visible scope begins. |
| `Open` | The view is fully on screen | The view is interactive when nothing covers it. `Shown` is published here. |
| `Closing` | The hide transition runs | What the view covered is revealed first. |
| `Ended` | The request is finished | Its handle is inert; its awaiters have completed. |

**What a load does, in order.** The provider returns an inactive instance under the inactive staging
object. The root's inject delegate runs on it while it is inactive, so injection happens before
`Awake`. Inject is a step of its own: an exception from the delegate is reported as
`ViewErrorSource.Inject` and ends the request as `LoadFailed`. The package then resolves the
`IView<TArgs, TResult>` on the instance root and the optional `IViewVisible` and `IViewBack` hooks on
that same component. It then places the instance: it deactivates it, reparents it under its layer,
links it to the request and activates it, so `Awake` and `OnEnable` run, without arguments.
Placement does not switch any canvas or raycaster, so `Awake`, `OnEnable` and `OnOpen` run on the
view exactly as authored, and whatever they write to its canvases and raycasters stays.
`OnOpen(args, context)` runs next. Placement, `OnOpen`, the snap to hidden, the move to `Opening` and
the first projection pass run in one synchronous block. The first projection pass is the first
moment the package switches anything, and it happens before any frame renders.

**Failures that end the request without a show.**

| Cause | Reason | Diagnostic |
|---|---|---|
| The provider throws or returns nothing | `LoadFailed` | `UIDIRECTOR104` |
| The inject delegate throws (source `Inject`) | `LoadFailed` | `UIDIRECTOR104` |
| Binding or placing throws, for example on an instance destroyed before it was placed (source `Provider`) | `LoadFailed` | `UIDIRECTOR104` |
| The load exceeds the load timeout (default 15 s) | `LoadTimedOut` | `UIDIRECTOR105` |
| The instance root has no `ViewHost` | `TypeMismatch` | `UIDIRECTOR103` |
| No component implements `IView<TArgs, TResult>` for the key | `TypeMismatch` | `UIDIRECTOR102` |
| `OnOpen` called `Decline` | `Declined` | none |
| `OnOpen` threw | `Faulted` | `UIDIRECTOR117` |

`Complete` or `Close` inside `OnOpen` is latched: the view opens and then closes, and
`UIDIRECTOR118` points to `Decline` as the way to refuse an open. A decline or a throw in `OnOpen`
replaces a close cause latched in the same hook, because the request must end unshown.

### The latched outcome

The first cause that ends a view is latched and later causes are ignored. Causes and the
`CloseReason` they produce:

| Cause | `CloseReason` |
|---|---|
| `ViewContext.Complete(value)` | `Completed` |
| `ViewContext.Close()` and `ViewHost.Close()` | `Dismissed` |
| `Back()` | `Back` |
| A tap on the backdrop of a row with `CloseOnBackdrop` | `Backdrop` |
| `ViewHandle.Close()`, `CloseTo`, `CloseAll` | `Code` |
| A screen change, a return to an id already in the stack, or a reopen in place | `Navigated` |
| The owner token ended | `OwnerEnded` |
| The instance was destroyed or deactivated outside the package, or the root was destroyed | `Destroyed` |

A request that never reached `Opening` always reports `CloseReason.NotShown` with a `RejectReason`.
A request that ended after it entered `Opening` counts as shown even if `Shown` had not been
published yet ([section 10](#10-events-and-the-order-at-close)).

**Latching happens at the call.** `CloseAll`, `CloseTo`, a return to an id already in the stack and
a reopen in place with a screen above it latch the close of every view they will remove when they
are called, not when the runner reaches them. On a layer with `ShieldsInput`, each latched `Opening` or `Open`
view takes a `PendingClose` shield holder unless it already holds one ([section 7](#7-the-input-shield)),
so the shield stays up from the call until the last of them is gone. From then on, a new request for one of those ids is accepted as a new
request that runs after the operation, instead of joining the view that is about to go. Requests
above a `CloseTo`, return or reopen target that have not started yet (`Waiting` or `Loading`) are
withdrawn at the call as `NotShown / Cancelled`, as `CloseAll` does. If the target of the pop ends before the pop runs, the views latched above it
are still popped. A latched view cannot be the target of a later `CloseTo`.

### Reopen in place

On a `History` layer an id is never instantiated twice unless its row sets `AllowMultiple`.

- **A key without arguments** rebinds nothing. `OnOpen` does not run again; the new caller's
  `ShowAsync` joins the existing request's awaiters, and `Open` returns the existing handle. If views
  sit above it, they are latched and popped with `Navigated`, and requests above it that have not
  started are withdrawn, at the call.
- **A key with arguments** reopens the existing instance in place. A new request takes over the same
  list position, slot and instance, runs `OnOpen` with the new arguments on a fresh open scope and
  snaps to shown without a transition. The instance is not released in between, and `ViewHost` is
  relinked to the new handle. The old request is finalized with `Navigated`: its scopes end and its
  awaiters complete. When it happens depends on what sits above the screen:
  - **Nothing on screen above it.** The instance moves to the new request at the call and the old
    request ends then. The new request waits in `Waiting` holding the instance and is projected as
    open, so the view does not flicker. If a `CloseTo` withdraws that stand-in before its open runs,
    it is removed without a hide transition, as `NotShown / Cancelled`; the instance is released once
    and the layer is left at the `CloseTo` target.
  - **A screen above it.** The views above are latched and popped first, and the old request is
    **not** latched. It is marked as being replaced: it stays on screen, non-interactive and with no
    new visible scope, so user code never sees the old arguments again, and it does not count as
    already open for a new request. It ends as `Navigated` when the runner reaches the new open,
    after the pop-to, and only then hands over its instance.
- **The new request ends before its open runs** (Back, its owner, `CloseAll`, root teardown): the old
  request comes back at that moment. It stays open, visible and interactive, and `OnVisible` runs
  again if its visible scope had ended. A new reopen of a request that is itself waiting to replace one takes over that link,
  so the original comes back only if the newest request ends first.
- **The old request ends during the wait.** A close or a terminate cause that reaches it ends it with
  its own reason, not `Navigated`. If its owner ended or its instance was lost, the new request
  loads a fresh instance. After a close through its handle, the new request still takes over the
  instance.
- **The owner of the new request.** A new request whose owner token is the open scope of the screen
  it replaces, or of a screen that the pop removes, ends with that screen and never opens. Such a view
  passes the root's token instead ([Navigation and back](../Navigation-and-Back.md)).
- **A failed relink.** If relinking the instance to the new request throws, the new request ends as
  `NotShown / LoadFailed` and the on-screen instance is released once: the view disappears.
- The earlier arguments are not kept. Back from the reopened screen goes to the screen below it.

---

## 5. The per-layer runner

Each layer has one FIFO of operations and one `LayerRunner`. The runner exists only while there is
work. **One operation runs at a time on a layer, and a started step is never interrupted**
([ADR-002](Decisions/ADR-002-Strict-Serialization.md)). Runners of different layers run
concurrently, and the shield count covers all of them.

| Operation | Steps, each awaited |
|---|---|
| Open | the reopen handover if any; load or take the kept instance; inject; bind; place; `OnOpen`; close the views of `ClosedByNavigation` layers (history layers only, by snap, reason `Navigated`); snap hidden; `Opening`; projection and the visible scope; show transition; `Open`; release the open's shield hold; `Shown` |
| Close | `Closing`; projection reveals what the view covered; hide transition; finalize |
| Pop-to (history layers) | at the call: latch the views above the target and withdraw its unstarted requests; then in the runner: every view on screen above the target to `Closing`; projection; hide the top view with its transition; finalize it; finalize every view between by snap |
| Close-all | at the call: withdraw the requests that have not started (`Waiting` or `Loading`) and latch the views on screen; then in the runner: hide the top visible view with its transition; finalize every other view by snap |

**Rules.**

- **Never interrupted.** A close requested during `Opening` is latched and runs after the open
  finishes. Two causes cut a step short: a terminate cause and a watchdog. Both snap.
- **Terminate causes** end a request at once, outside the operation list: the owner token ended, the
  instance was destroyed or deactivated outside the package, the root was destroyed. The terminator
  cancels the running step, snaps the view if its instance still exists, and finalizes
  ([section 10](#10-events-and-the-order-at-close)). The runner, resuming from the cut step, waits
  until the end routine has finished, so the next operation never runs inside it.
- **Watchdogs.** The load step has a timeout (default 15 s). A show or hide transition has a timeout
  (default 3 s). A transition that times out is cancelled, snapped to its end pose and reported with
  `UIDIRECTOR106`; the runner continues as if it had finished. Both timeouts are Inspector fields
  with a minimum of 0.1 s.
- **Clock.** Timeouts count on the navigator clock. `ViewRoot.Update` advances it by
  `min(Time.unscaledDeltaTime, 0.1 s)` per frame. It keeps running at `timeScale == 0`, and a
  suspension, an editor pause or a breakpoint adds at most one step to any duration.
- **Burst.** Immediate opens are accepted and serialized. A layer holds at most the waiting cap of
  requests in `Waiting`; the next one ends as `NotShown / Busy`. `RejectWhenBusy` is the per-call
  opt-in for tap handlers.
- **`ClosedByNavigation`.** After a view on a history layer passes `OnOpen`, the runner ends every
  view on `ClosedByNavigation` layers that was accepted earlier than the opened view, by snap, with
  `Navigated`. Views accepted later belong to the new screen and stay. Pops, `Back()` and `CloseTo`
  do not trigger this rule.
- **A fault never stalls the layer.** The runner is an `async UniTaskVoid` with an outermost catch. A
  throw is routed to the error handler, the current request ends as not shown, its shield holder is
  released in a `finally`, and the loop continues with the next operation. Every fault is routed with
  the view id (and the request id when a request is live) under the source of the step that failed:
  `Inject` for the inject delegate; `Provider` for the provider, binding, placing and every
  `IViewProvider.Release`, including the release of a kept instance and of the late result of a
  cancelled load; `Transition` for `PlayAsync`, `Snap` and applying state to the stage. A transition's
  `Snap` or a provider's `Release` that throws is not swallowed by the stage; the caller that holds
  the request routes it, and a release with no request (a kept instance, a late load result) is
  routed with the view id and request id 0.
- **The load indicator.** A shared timer starts when a request on a shielding layer begins loading.
  After the indicator delay (default 0.4 s) the root's optional indicator object is activated, and it
  is deactivated when no shielded load runs. The indicator gets its own canvas one above the shield
  and no raycaster, so it takes no input.

---

## 6. Projection

`Projection` is a pure function. After every phase change the navigator lists every live request in
global order (layers ascending, then list position) and recomputes, for each one, whether it is
hidden, whether it is interactive and what its sort order is, and for each modal layer where the
backdrop goes. It applies only the difference to the stage. Nothing is accumulated by push or pop, so
visibility, focus and order cannot drift from the lists.

**Sort order.** `order = base + layer.Order * 1000 + slot * 10`.

- `slot` runs from 1 to 99. It is assigned as the highest live slot plus one and never changes while
  the view is live. A view owns the orders `+1` to `+4` above its own for nested canvases;
  `ViewHost.SortingOrder` exposes the order of its own canvas.
- A modal layer's backdrop sits at the top view's order minus 5.
- The shield sits at `base + (highest layer order + 1) * 1000`, above every layer. The load indicator
  sits one above the shield.
- The whole band must stay below 29999. `Canvas.sortingOrder` is a signed 16-bit value that wraps
  without a warning, so the intake rejects a request whose slot does not fit
  (`UIDIRECTOR115`, an error) instead of letting it wrap. The shield's order counts as part of the
  band, and the base order is clamped to the 16-bit range in the Inspector.

**Hidden.** A view is hidden when it is not on screen, or when a view above it in global order is
`Open` and covers what is below it. A view covers when its row says `CoverPolicy.Covers`, or when it
says `LayerDefault` and its layer has `History`. A covered view stays open: its request, its scopes
other than the visible scope, and its instance all remain.

**Interactive.** A view is interactive only when it is `Open`, not hidden, not under any on-screen
view of a modal layer, and has no close pending and is not waiting to be replaced by a reopen.

**How the stage realises it** (`ViewPresentation`, owned by each `ViewHost`):

- **Covered, `CoveredMode.DisableCanvas` (the default).** The package disables **every enabled
  `Canvas` under the view**, including the view's own and those of inactive children, and records
  the ones it switched. On reveal it enables exactly those. A canvas that was already disabled is
  neither touched nor recorded, so one the view disabled itself stays disabled, and one the view
  turns on while it is covered stays on. Disabling only the view's own canvas is not enough, because
  a nested canvas whose parent canvas is disabled behaves like a root canvas and keeps drawing and
  taking input. The list is collected at the moment of covering, not cached at creation, because
  content created at run time can add canvases; it goes into a reused list, so a warm cover allocates
  nothing.
- **Covered, `CoveredMode.Deactivate`.** The view object is deactivated. This stops `Update`,
  animators and particle systems, at the cost of a rebuild when it returns.
- **Non-interactive.** The package disables **every enabled `GraphicRaycaster` under the view** and
  re-enables exactly those on becoming interactive, with the same rule as for canvases: what the
  package did not switch, it does not restore. A `CanvasGroup` is not used to block input:
  the raycast check stops walking upward at the first canvas that overrides sorting, so a group above
  such a canvas is never consulted. When an interactive view loses interactivity, or an instance is
  parked for reuse, and the event system's selected object is inside it, the selection is cleared,
  because Submit and Cancel are sent to the selection. A selection the view makes in `OnOpen` stays
  through its opening.
- **Nothing is switched at placement.** The presentation switches canvases and raycasters only in
  its first projection pass, after `Awake`, `OnEnable` and `OnOpen` have run, so the view's own
  writes in those hooks survive. A freshly activated graphic has no depth until the canvas rebuild,
  so it cannot be raycast in the frame it is placed. A parked instance gets back exactly what the
  package switched off.
- **The shared backdrop.** Each modal layer has one `Backdrop`: a canvas with a raycaster, a
  `CanvasGroup` and an image (or the root's backdrop prefab). It is shown directly under the layer's
  top on-screen view when that view is not hidden, and hidden otherwise. It fades on unscaled time
  over 0.2 s, jumps between stacked views, stops raycasting at once when it hides, and turns its
  canvas off when the fade ends; an idle backdrop runs nothing. A tap on it closes the top view with
  `CloseReason.Backdrop` when that view is `Open` and its row sets `CloseOnBackdrop`; otherwise the
  tap is swallowed. A tap while the shield is up does nothing.
- **The shield** is the subject of [section 7](#7-the-input-shield).

A projection pass that is requested from inside a stage call is folded into the running pass, with at
most four passes per request. Records that became visible are reported to the runner, which begins
their visible scope ([section 9](#9-scopes)), and the scopes of records that became hidden end after
the state is in place, so user cleanup never runs against half-applied state.

---

## 7. The input shield

The shield is a counted, full-screen input blocker above every layer. It is taken when a request is
accepted on a shielding layer and released when that request's operation ends.

- **Always rendered.** The shield is an always-active `ShieldGraphic` with an empty mesh on its own
  nested canvas. Only its `raycastTarget` flag is toggled. A graphic that is activated on demand has
  no depth in the frame it is raised, and the raycaster skips it; an always-active graphic blocks in
  the same frame, inside the `Open` call, without a canvas rebuild.
- **Holders.** `InputShield` keeps a list of holders, each with a kind, a request id, a reason, an age
  and a call site. The kinds are `Operation` (an open or another operation in flight on a shielding
  layer), `PendingClose` (a close that was latched and has not been finalized) and `InputHold` (a
  `HoldInput` registration). The shield is up while the list is not empty; the 0-to-1 and 1-to-0
  changes are the only calls into the stage.
- **Who takes and releases.**
  - A request on a shielding layer takes an `Operation` holder at acceptance. The open's holder drops
    when the show ends, unless a close is pending or a queued pop-to or close-all still needs it.
  - A close latched while the request is `Opening` adds a `PendingClose` holder on top of the open's.
    A close latched on an open view takes a `PendingClose` holder unless the view still holds one.
  - A pop-to or close-all holds the shield from the call until it ends, also after the open it was
    queued behind has finished. It borrows the top view's existing holder, counted in
    `OperationHolds`, or takes its own when that view holds none. Every view the operation will
    remove is latched at the call and, unless it holds one already, takes its own `PendingClose`
    holder, so a pop of several views holds one holder per view rather than one hold for the whole
    operation. The shield stays up without a gap. A request that is only waiting to be replaced by a reopen takes
    no holder; it is not latched.
  - Finalizing a request releases every holder it owns, on every path.
  - `HoldInput(reason, owner)` takes an `InputHold` that ends with its owner or on `Remove()`.
  - Root teardown releases every holder.
- **Count and `IsBusy`.** `INavigator.IsBusy` is true while an operation runs or pends, or an input
  hold exists. Operation holders are bounded by the two timeouts. An input hold older than 30 s
  warns once with its reason and call site (`UIDIRECTOR110`).
- **Selection.** When the shield goes up and the event system is not already selecting, the stage
  clears the selection. A layer without `ShieldsInput` never takes the shield.
- **Known limits.** A press that began before the shield was raised and ends after it was released is
  delivered if the same control is still under the pointer. A canvas sorted above the shield, such as
  an open dropdown list (a TextMesh Pro dropdown draws at 30000), or interface the package does not
  own with a higher order, is not blocked. The validator warns about dropdowns inside a view
  (`UIDIRECTOR123`).

---

## 8. Back resolution

`BackResolver` is a pure function that turns the layer lists and the shield count into one decision.
`Navigator.Back()` evaluates these rules in order and returns at the first match.

| # | Condition | Result |
|---|---|---|
| 0 | The root is gone, or the call is off the main thread | `false` |
| 1 | A request is in `Waiting` or `Loading` on a `TakesBack` layer and its row policy is not `Ignore` | The newest such request (by acceptance order) is withdrawn as `NotShown / Cancelled`. `true` |
| 2 | The shield count is above zero | Swallowed. `true` |
| 3 | Walking layers from the highest order, the first layer that is `Modal` or `TakesBack` and has an on-screen view; its top view is the target | See below |
| 4 | Nothing matched | `false` |

For the target of rule 3:

- A target that is not `Open` (it is `Opening` or `Closing`) swallows back. Back waits for it.
- On a layer without `TakesBack` (the system layer): a row with `BackPolicy.HandledByView` asks the
  view through `IViewBack.OnBack()`; any other policy swallows. The result is `true` in both cases.
- `BackPolicy.Ignore`: swallowed, `true`.
- `BackPolicy.Close`, and `LayerDefault` on a `TakesBack` layer: if the target is the bottom view of a
  `History` layer, `false`, because that view is the root and the game decides. Otherwise the view
  closes with `CloseReason.Back` and the result is `true`.
- `BackPolicy.HandledByView`: `OnBack()` returning `true` ends it; otherwise it continues as `Close`.
  A hook that throws is routed and counts as not handled.

Layers that are neither `Modal` nor `TakesBack` are skipped, so a plain layer never swallows back.
`BackPolicy` is a policy enum and not a general veto: a view can ask to handle a back press, but code
closes (`handle.Close()`, `CloseTo`, `CloseAll`, the owner ending) are never vetoed
([ADR-003](Decisions/ADR-003-Begin-Only-Hooks-And-Scopes.md)).

`BackInput` is an optional component. It reads the back key (Escape, which the Android back button
raises) and calls `Back()` on its root's navigator. With the Input System it reads Escape through
an `InputAction` that is enabled with the component, so a press whose down and up land in one input
update still counts; without it, it polls the legacy input manager. It does nothing while its root
is not initialised, because reading `Navigator` would initialise the root with defaults before the
bootstrap does. When `Back()` returns `false` it raises its `Unhandled` event, which is the game's
cue to decide, for example by asking to quit. `ViewButton` offers the same entry points from the
Inspector: close its view, call `Back()`, or open a view that takes no arguments; it warns and does
nothing when clicked before its root is initialised. [Navigation and back](../Navigation-and-Back.md) covers the usage.

---

## 9. Scopes

A `ViewScope` is a small single-use object that the package creates and ends. It owns bindings and
tasks for one span of a view's life. There are two kinds.

| Scope | Created | Ends | Reached through |
|---|---|---|---|
| Open scope | When the request is accepted; handed to the view in `OnOpen` | When the request ends, on every path: closed, withdrawn before it was shown, `OnOpen` declined or threw, owner ended, lost from outside, root destroyed | `ViewContext.Open`, the `open` parameter of `ViewBehaviour.OnOpen` |
| Visible scope | Each time the view comes on screen and is not covered | When the view is covered, or when the request ends | The `visible` parameter of `IViewVisible.OnVisible` |

A view that is covered keeps its open scope and loses its visible scope; when it is revealed the
package begins a new visible scope and calls `OnVisible` again. A view that is being replaced by a
reopen in place gets no visible scope.

**What a scope offers.**

- `Bind(UnityEvent, UnityAction)` and `Bind<T>(UnityEvent<T>, UnityAction<T>)` add the listener now
  and remove it when the scope ends. A duplicate pair on one scope is ignored.
- `Subscribe(add, remove, handler)` and a typed form do the same for events that are not
  `UnityEvent`: the package calls `add` now and `remove` when the scope ends.
- `Add(IDisposable)` disposes the object at the end. `OnEnd(Action)` runs an action at the end, and
  `OnEnd<TState>(state, action)` does it without a closure allocation.
- `Run(work)` starts a task with the scope's token. Cancellation is swallowed, and any other
  exception is routed as `ViewErrorSource.ScopeTask`.
- `Token` is created lazily on first read, is cancelled when the scope ends, and is never disposed, so
  a late holder stays valid. A token read after the end is a plain cancelled token.

**The end routine** runs exactly once: the token is cancelled first, so entries already see it
cancelled, then the entries run newest first, each isolated in a `try/catch` that routes the error.
A registration on an ended scope is cleaned up at once: the listener is not added, the disposable is
disposed, the action runs, the task does not start. A scope reference kept after the view closed is
therefore inert, and `ViewContext.Open` after the end returns the ended scope, never `null`.

**Re-entrancy.** An `OnEnd` action runs inside the end routine of its request. A navigator call from
it is legal: state is already consistent, and notifications that it causes run right after the
current one, before older pending ones. A popup whose owner is a screen's open scope therefore
reports `Closed` before that screen does.

Why the view gets begin-only hooks and no close hooks is the subject of
[ADR-003](Decisions/ADR-003-Begin-Only-Hooks-And-Scopes.md).

---

## 10. Events and the order at close

### The stream

`ViewEvent` is one struct, `IViewObserver.OnViewEvent(in ViewEvent)` is the one callback, and
`INavigator.AddObserver(observer, owner)` adds an observer that ends with its owner. Events state
facts in the past tense; no command travels as an event. The payload carries no view arguments, so
nothing is boxed and no argument data leaks into analytics.

| `ViewEventKind` | Publisher | When | Extra payload |
|---|---|---|---|
| `Enqueued` | A queue | The item entered the queue | `Priority` |
| `Requested` | The navigator | The request was accepted | none |
| `Shown` | The navigator | The show transition ended | `SecondsWaiting` |
| `Closed` | The navigator | The view ended after being shown and its instance was released | `Reason`, `SecondsOpen`, `Detail` |
| `NotShown` | The navigator, or a queue for items that never reached it | The request ended without being shown | `Rejected`, `Detail` |
| `Settled` | The navigator | The navigator went from busy to settled; the request id is 0 | none |

Every event also carries the request id, the view id, the layer id, the caller's `Source` tag and the
priority (0 for an immediate open). A queue publishes only to its own observers, and only for items it
holds. It reserves each id with `INavigator.ReserveRequestId`, so one id follows a request from
enqueue to close.

The sequence per request id is one of:

- `NotShown` alone (rejected at the call),
- `Requested`, `NotShown`,
- `Requested`, `Shown`, `Closed`,

and a queued request has `Enqueued` in front. A request that ends after it entered `Opening` but
before `Shown` was published publishes `Shown` immediately before `Closed`, in the same notification
sequence, so observers never see `Closed` without `Shown`. `SecondsWaiting` runs from acceptance to
the `Shown` event and `SecondsOpen` from `Shown` to `Closed`, both measured on the navigator clock.

Dispatch is isolated and allocation-free. An observer that throws is routed as
`ViewErrorSource.Observer` and the rest still run. An observer added during a dispatch receives
events from the next one; a removal takes effect at once.

**Events that a callback causes wait for the callback's event.** While an observer dispatch on the
navigator is in progress, everything a navigator call made from inside a callback would publish is
held back: `Requested`, the `NotShown` of a call that is rejected, `Shown`, and the whole
end-of-request sequence below (scope ends, instance release, `OnVisible` of revealed views, `Closed`
or `NotShown`, awaiters, `Settled`). The held-back entries run in the order they were raised, right
after the event in progress has reached every observer; the code that owns the dispatch drains them,
so nothing deadlocks, and every entry runs (a root torn down inside a callback removes its observers
first, so they miss the held-back events, see [section 2](#2-the-root-and-its-layers)). Every observer therefore sees an event before anything
that event's callbacks caused. State changes are not held back: a latch, a phase change and the
projection take effect at the call. A held-back `Requested` or `Shown` can describe a state that
has since moved on, for example a request that is already loading. Nothing is held back when no
dispatch is running. In particular a continuation of `await ShowAsync(A)` that opens `B` runs in the
awaiter step of the outbox, not inside a dispatch, so the events of `B` arrive at once and describe
the state that is in place.

### Order at close

For one view that ends, the terminator first applies state, with no user code: it marks the record
`Ended`, removes it from its layer, cancels its running step, applies the hidden and non-interactive
state to its instance, unregisters the owner callback, releases its shield holders and recomputes the
projection. Then it queues notifications in the outbox, which runs them one at a time, each isolated:

1. the visible scope ends
2. the open scope ends
3. the instance is released: handed to the provider, destroyed, or stashed for `KeepInstance`
4. views that were revealed begin their visible scopes (`OnVisible`)
5. `Shown`, if it is owed
6. `Closed` or `NotShown` is published
7. the `ShowAsync` awaiters complete, in join order, then the `WaitForCloseAsync` awaiters
8. `Settled` is published if the navigator is now settled

A notification caused by another notification runs right after it. The consequence for callers: the
code that awaited the view resumes before the queue presents the next item, because the queue pumps on
the next tick ([section 13](#13-the-queue-above-the-navigator)). A follow-up popup opened by that
continuation makes the layer busy, and the queued item waits for it. While the root is being torn
down `Settled` is not published, since holders are being released one by one.

### Observing

In the editor and in development builds the navigator keeps the last 100 events in a ring, recorded
by an internal observer added first in the constructor, so it sees every event before user
observers. Registered queues write `Enqueued` and their own `NotShown` into the same ring, marked as
queue events. Release builds have no ring. `INavigator.Describe(StringBuilder)` writes the current
state as text on any build.

---

## 11. Exactly-once outcomes

- **One latch.** The first close cause is latched ([section 4](#4-requests-and-phases)). A later cause
  does not change the outcome, including the owner ending after a close was latched: the awaiters
  get the latched outcome.
- **One completion per awaiter.** Each `ShowAsync` call and each `WaitForCloseAsync` call gets its
  own completion source. The records are not pooled, and a plain completion source can be awaited
  late. After the end, a late `ShowAsync` join or `WaitForCloseAsync` returns the stored outcome at
  once, every time. A request ends exactly once, and its awaiters complete exactly once.
- **The value.** `ViewResult<TResult>.HasValue` is true only when the view closed with
  `Complete(value)` and was shown. `ShowAsync` never throws for anything that happens to the view
  ([ADR-001](Decisions/ADR-001-Outcomes-As-Data.md)).
- **Who can cancel an await.** The owner token decides the life of the **view** and never cancels an
  awaiter. An awaiter ends only through its own token:
  `handle.WaitForCloseAsync(waiter)` throws `OperationCanceledException` for that waiter and leaves
  the view alone. A third party observes a close through `WaitForCloseAsync` or through the `Closed`
  event with the request id.
- **A joined request** that was opened with other argument or result types shares the outcome but not
  the value (`HasValue` is false).
- **Teardown.** Every awaiter completes exactly once in every phase, and a late load cannot complete
  one again.

A result is an intent signal held in memory. It is not delivered if the process dies, and an awaiter
can be abandoned. A one-time side effect, such as a payment or a reward, must not be performed by the
awaiting continuation alone. It belongs to a service that performs and persists it, keyed by a
transaction id and safe to repeat; the view calls that service and uses `Complete` only to report.
The package offers no exactly-once delivery mechanism.

---

## 12. External loss

Project code can destroy or deactivate a view object directly, or deactivate a layer object or the
root. The package handles this on the **next frame**, never inside the Unity message.

- `ViewHost` reports every `OnDisable` and `OnDestroy` that the package did not cause. The package's
  own `SetActive`, reparenting and stashing are wrapped in a change counter on the host, and reports
  are ignored while it is above zero and while the application is quitting. "Quitting" is one
  process-wide flag, `QuitSignal`, set when `Application.quitting` is raised and cleared at the start
  of the next play session; one subscription serves every view, and in the Editor the flag stays
  set after Play Mode ends until the next play session.
- The stage does not act on a report. It records the instance in a pending list and schedules one
  flush on the next frame (`UniTask.NextFrame`). At the flush, "destroyed" means the `GameObject` is
  null; an instance that is active again, already released, or whose stage was disposed is not
  reported. Two reports for one instance end the request once.
- Why: Unity calls `OnDisable` before `OnDestroy` when an object is destroyed, so the first report
  cannot say which one happened; and activating, deactivating or reparenting inside `OnDisable` is an
  engine error, which the navigator's synchronous finalize would trigger.
- At the flush the navigator ends a live request with `CloseReason.Destroyed` and `UIDIRECTOR107`
  (a request that was not shown ends `NotShown / Cancelled`). The stack entry, the backdrop and the
  shield holders go with it, awaiters complete and the queue can advance. A view that was only
  deactivated is snapped first; a destroyed one is not touched. An instance lost this way, destroyed
  or only deactivated, is never kept for reuse, even when its row says `KeepInstance`: it is
  released, and the next open of the id makes a new instance.
- A kept idle instance destroyed from outside is dropped from the cache and released.
- Deactivating the root or a layer object therefore ends every view under it. To hide the whole UI
  without ending views, set a `CanvasGroup` on the root to alpha 0 and take a `HoldInput` registration.
  Disabling the root canvas hides nothing, because each view is a nested canvas that then draws as a
  root canvas, and `CanvasGroup.blocksRaycasts` does not reach nested canvases that override sorting.
- Views that end because the root itself is destroyed report nothing, so a scene unload does not log
  `UIDIRECTOR107`.
- An instance that is destroyed before it was ever activated gets no `OnDestroy`, so `ViewHost`
  cannot report it. The request then ends through the step that finds the object gone, as
  `LoadFailed`, and the provider releases it.

---

## 13. The queue above the navigator

`ViewQueue` is a separate public class that presents waiting requests on one layer, one at a time, by
priority. It uses `INavigator` only and knows nothing of the navigator's internals. A project that
needs no queue pays nothing for it; the navigator has no queue state
([ADR-004](Decisions/ADR-004-Queue-Above-The-Navigator.md)).

`new ViewQueue(navigator, layer, lifetime, name)` presents on `layer` until `lifetime` ends or the
navigator's root is destroyed. A game can have several queues, for different layers or for different
features. On the package's own navigator, a layer the root does not have throws `ArgumentException`
at construction, before anything is registered.

**Items.** `Enqueue` and `EnqueueAsync` take a `ViewKey`, the arguments, the item's owner token and
`QueueOptions` (`Priority`, `Source`). Higher priority is presented first and equal priorities keep
enqueue order. An item moves `Waiting`, `Presenting`, `Done`. `EnqueueAsync` completes with a
`ViewResult` in every case, also when the item ends inside the queue without being shown.
A waiting item whose owner ends is ended in the queue as `NotShown / OwnerEnded`. Once an item is
presented, the navigator owns its owner token.

**Holds.** `Hold(reason, owner)` takes a counted hold and records its caller's member and line. While
any hold exists the queue presents nothing. A hold ends with its owner or on `Remove()`; it has no
automatic timeout, because an automatic release would let popups through in the middle of a flow. A
hold older than 60 s warns once (`UIDIRECTOR111`, naming the reason and the call site; the editor
window shows both). Holds do not affect immediate opens. An atomic multi-step flow over the queue is
an async method that takes a queue hold for its duration and opens its views immediately.

**Gates.** An `IViewGate` is a synchronous rule that answers `GateDecision.Allow` or
`GateDecision.Deny(reason)` for a `GateRequest`. Gates run in ascending `order` (ties keep add order),
**at enqueue and again right before presentation**. The first deny ends the item as
`NotShown / Denied` with the gate's reason and later gates are not asked. A gate that throws is routed
as `ViewErrorSource.Gate`, reported as `UIDIRECTOR119` and counts as a deny. A deny at enqueue
publishes only `NotShown`; the item never entered the queue.

**The pump.** Every trigger schedules one pump on the next `Update` tick, coalesced: an enqueue, a
hold removed, a gate added or removed, an item ending, and a navigator event of kind `Settled`,
`Closed` or `NotShown`. The queue listens to `Closed` and `NotShown` as well as `Settled` because a
request that ends without an operation (owner ended, lost from outside, closed by navigation)
publishes no `Settled`, yet the layer it held may now be clear. Scheduling on the next tick also means
same-frame enqueues are ordered by priority instead of call order.

A pump does nothing while a hold exists, while an item is presenting, or while
`navigator.IsLayerClear(layer)` is false. `IsLayerClear` is true when the layer has no request, no
modal layer above it has a request, and the navigator is not busy. The pump then walks a snapshot of
the waiting items by priority: an item whose owner ended is ended; gates are asked; the first allowed
item is presented. The clear check, the gates, the re-check and the `ShowAsync` call run in **one
synchronous block**, so no other open can slip in between. Presentation calls `ShowAsync` with the
reserved request id, the arguments, the owner, and the priority and source in `OpenOptions`. A
presented item ends when its request ends; the next pump follows on the next tick. The call also
carries the queue's layer id, and the navigator's intake refuses a row that is on another layer
(`NotShown / WrongLayer`, `UIDIRECTOR126`, [section 4](#acceptance)); the queue needs no code for
that, because the navigator's `NotShown` ends a presented item like any other. If `ShowAsync`
throws, or its task faults later, the item ends as `NotShown / Faulted` and the queue carries on. For
a fault after the call the queue publishes that `NotShown` itself, on the main thread, unless the
navigator already published an end for the request. An item that the navigator ended without showing
is not retried.

**Lifetime.** The queue ends when `lifetime` ends and also when the navigator's `RootToken` is
cancelled; it reads the root token once, at construction, and registers on it as well as on `lifetime`
(once, when the two are the same token). It becomes inert:
waiting items end as `NotShown / Cancelled`, or as `NotShown / RootGone` when the root is gone, every
hold, gate and observer registration ends, the navigator observer is removed, and later calls are
inert (`Enqueue` publishes the same `NotShown` and returns the reserved id; registrations come back
empty). The reason follows the state of the tokens, not the order their callbacks ran in, so a queue
whose lifetime is the root token (the common case) ends its items as `RootGone`. A queue built
after its lifetime or the root has ended is inert from the start. Both token registrations are
disposed when the queue ends, so neither token keeps a dead queue alive, and a queue that ends
while the root lives does not stay on the root token. A view that was already presented is left to
the navigator and its own owner: it ends once through the navigator, possibly after the queue's
waiting items, so the queue does not complete the presented call itself. `Clear()` ends every
waiting item as `NotShown / Cancelled` and leaves the presenting one alone.

The queue's own events are `Enqueued` and `NotShown` for items that never reached the navigator (and
the `NotShown / Faulted` of a presentation fault the navigator did not report), sent to the observers
added with `ViewQueue.AddObserver`. Everything after presentation is the navigator's. A queue built on the package's own navigator registers itself with it, so the editor
window lists it and its events reach the ring; a queue built on any other `INavigator` is invisible to
the tools. See [Queue](../Queue.md) for usage.

---

## 14. The asset provider seam

A catalog row names its source in one of two ways, and a project can mix both in one catalog.

- **A direct prefab reference.** The prefab stays resident while its catalog is loaded, and nothing
  loads when the view opens. The stage instantiates it with `Object.Instantiate` under the inactive
  staging object. `DirectViewProvider` is internal, not an `IViewProvider`; it has no key to resolve.
- **An asset key** with no prefab. The root's `IViewProvider` resolves it, so nothing is resident
  until the view opens.

The seam is public:

<!-- signature -->
```csharp
public interface IViewProvider
{
    UniTask<GameObject> AcquireAsync(string assetKey, Transform inactiveParent, CancellationToken ct);
    void Release(GameObject instance);
    UniTask<IDisposable> PreloadAsync(string assetKey, CancellationToken ct);
}
```

The contract: `AcquireAsync` returns an inactive instance under the inactive parent; on cancellation
it releases whatever it loaded, also when the load completes later. `Release` releases exactly what
`AcquireAsync` returned, never throws and is safe on a destroyed instance. The stage deactivates an
instance before it hands it to `Release`, so a pooling provider receives it inactive, and it counts
the instance as released even when the provider throws anyway; that exception is not swallowed but
routed as `ViewErrorSource.Provider` with the view id. `PreloadAsync` keeps the
asset loaded until the returned lease is disposed. The root takes the provider from
`ViewRootSetup.AssetProvider`, or from an `IViewProvider` component on the same object. A row with an
asset key and no provider ends as `LoadFailed` with `UIDIRECTOR120`.

The core carries no Addressables type; the key is a plain string. The id and the key are separate,
so renaming an address breaks one catalog row, which the validator reports, and code and remote data
keep naming the id.

**`AddressablesViewProvider`** (the optional assembly) is a `MonoBehaviour : IViewProvider`:

- It loads the prefab asset with `LoadAssetAsync`, then instantiates it itself under the inactive
  parent, and only if the call is still wanted, the provider is alive and the parent still exists.
  It stores the asset handle in a table keyed by the instance and is the only caller that releases
  it, so Addressables tracks the asset for as long as the instance lives. View code never sees a
  handle. Every release is guarded by `IsValid()`, because a scene unload can invalidate a handle on
  its own.
- If the token is cancelled before the load completes, the call ends at once with
  `OperationCanceledException`, nothing is instantiated, and the single completion callback releases
  the asset when its load completes. A load timeout takes the same path. Usage errors (a destroyed
  provider, an empty key) throw at the call; a token that is already cancelled returns a cancelled
  task.
- A failed or empty load releases its handle and throws `InvalidOperationException` naming the key.
- `Release` takes the handle by instance id, so an instance destroyed from outside still releases.
  It destroys the instance with `Object.Destroy`, which is for Play Mode only, then releases the
  asset handle.
- `PreloadAsync` loads the asset without instantiating it. A lease and an instance handle are
  independent reference counts, so their release order does not matter.
- When the provider is destroyed it destroys every instance and releases its handle, then releases
  every lease; later calls throw `ObjectDisposedException` or do nothing, and a load still in flight
  releases its late result.

**Retention.** `ViewRetention.ReleaseOnClose` releases the instance at finalize.
`ViewRetention.KeepInstance` parks it snapped hidden and deactivated under staging, one idle instance
per id, and reuses it on the next open of that id without injecting again. An extra concurrent
instance (`AllowMultiple`) is released on close when the slot is taken. There is no budget and no
timer. `INavigator.Trim()` releases every idle kept instance; the root calls it on
`Application.lowMemory`; it subscribes in `Awake`, or at initialisation when that comes later, and
unsubscribes when it is destroyed. Trim never touches a request in flight or
a preload lease. Removing a catalog releases the kept instances of its rows, and a view of a removed
row that closes later is released, not kept. A release that throws is routed as
`ViewErrorSource.Provider` with the view id and does not stop the others.

**Preload.** `INavigator.PreloadAsync(viewId, owner)` loads the asset and holds it until the owner ends.
For a direct-prefab row it returns `true` at once. It returns `false` when the id is unknown, the
owner ended or the load failed. It never creates an instance. See
[Assets and Addressables](../Assets-and-Addressables.md) for usage.

---

## 15. Transitions

A transition is a component on the view prefab, referenced by `ViewHost`. With no reference the view
shows and hides instantly. `ViewTransition` has two members:

<!-- signature -->
```csharp
public abstract class ViewTransition : MonoBehaviour
{
    public abstract UniTask PlayAsync(bool show, CancellationToken ct);
    public abstract void Snap(bool shown);
}
```

**The contract for a custom transition.**

- Use unscaled time. A transition on scaled time never finishes while `timeScale` is 0, and the
  watchdog snaps it after the transition timeout and reports `UIDIRECTOR106`.
- Start from the opposite end pose and stop when `ct` is cancelled. There is no "continue from the
  current pose" requirement, because the runner never interrupts a transition with another one
  ([ADR-002](Decisions/ADR-002-Strict-Serialization.md)).
- `Snap` is synchronous, idempotent and never throws. It writes the end pose. It must not complete the
  task of a play that is running from inside `Snap`: a play that `Snap` ends finishes one step later,
  because the runner would otherwise resume inside `Snap` and carry on as if the play had ended
  normally. The same holds for a play ended from a destroy or disable message.
- Do not call the navigator.

**The sequence.** Before the first show the runner calls `Snap(false)`, moves the request to `Opening`, runs the first
projection pass and plays the show. Before a hide it plays the hide. On a terminate cause or a watchdog it cancels `ct` and calls
`Snap` with the target pose if the instance survives. A transition that throws, in `PlayAsync` or in
`Snap`, is routed as `ViewErrorSource.Transition` with the view id, and a failed play is then
snapped. The stage does not swallow a `Snap` exception: the caller that holds the request routes it.

**Built-ins** (`FadeTransition`, `ScaleTransition`, `SlideTransition`) need no tween library. They run
on one internal loop, `UnscaledAnimation`: it waits one frame at a time, adds
`min(Time.unscaledDeltaTime, 1/30 s)` per frame so that a long frame does not make the animation
jump, checks its token after every frame so that no step runs after a cancel, and allocates nothing
per frame once warm. The clamp has a cost: below 30 fps a transition takes longer than its duration,
and the transition timeout, which counts on the navigator clock, can snap one that is still
animating. Defaults: 0.25 s, show `OutCubic` (`OutBack` for scale), hide `InCubic`. Easing is a
serialized internal enum with no public easing API. Sizes come from the layer rect in canvas units,
not from `Screen.width`. The backdrop's fade uses the same loop.

**`TweenTransition`** (the DOTween assembly) builds one `Sequence` of `DOTween.To` tweens per play from
serialized fade, scale and offset settings. It uses only the core DOTween library, because the module
extension methods live in assemblies a package cannot reference. It sets its own ease, auto-kill and
unscaled update explicitly, so project-wide DOTween defaults cannot compound the ease or stop the kill
callback from firing. The await ends in the tween's own kill callback, bridged by an internal
`TweenAwaiter`; a cancelled token kills the tween with `Kill(false)` and ends the await at once, even
when DOTween defers the kill inside its own update. The reference is dropped inside the callback, so a
recycled tween is never touched. A kill that the component makes itself, from `Snap`, from a newer
play or from `OnDestroy`, resumes the caller on the next player-loop step instead of inside that
call or inside Unity's destroy message. See [Transitions](../Transitions.md) and
the DOTween page for usage.

---

## 16. Editor tooling

The editor assembly is internal and compiles in the editor only. It reads state through structured
read-outs and never parses text.

**Read-outs.** `Navigator.CaptureState` fills a reused `NavigatorState`: layers (id, order, rules,
whether a runner is active, pending operations) with their requests (id, view, phase, slot, close
pending, latched outcome, priority, source, whether an owner is bound), shield holders (kind, request,
reason, age, call site), kept and preloaded view ids, and counts. `ViewQueue.CaptureState` does the
same for a queue: name, layer, held and holds, gates, the presenting item and the waiting items in
presentation order. Warm captures allocate nothing. The event ring of [section 10](#10-events-and-the-order-at-close)
is the third source.

**`Window > UIDirector`.** A UI Toolkit window (`CreateGUI`). A plain `UIDirectorWindowModel` holds
everything the window shows; the view updates rows in place four times a second in Play Mode and
keeps foldout states. A dropdown picks the root. The page shows a status line
(settled or busy, with the shield count), one foldout per layer with its requests, shield holders,
queues, kept instances, preload leases and the last 100 events newest first. "Copy state" puts the
navigator's text trace on the clipboard and "?" opens the documentation for the installed version.

**Validator.** `Tools > UIDirector > Validate Catalogs` checks every `ViewCatalog` in the project and
every static class marked `[ViewKeys]`. It logs one line per finding, with the id first and a link to
its Troubleshooting section, then a summary. The same checks run in the catalog's inspector, which has
a Validate button. `CatalogValidation.RunFromCommandLine` is the batch-mode entry point for
`-executeMethod`: it exits with code 1 when any error exists and 0 otherwise; warnings never fail a
run.

| Id | Severity | Finding |
|---|---|---|
| `UIDIRECTOR101` | Error | A key field in a `[ViewKeys]` class has no catalog row |
| `UIDIRECTOR102` | Error | The row's prefab root has no component implementing `IView<TArgs, TResult>` for the key |
| `UIDIRECTOR103` | Error | The row's prefab root has no `ViewHost` |
| `UIDIRECTOR108` | Error | Two rows share an id; the first wins |
| `UIDIRECTOR109` | Error | A row names a layer that does not exist |
| `UIDIRECTOR120` | Warning | A row has an asset key but Addressables is not installed |
| `UIDIRECTOR121` | Error | A row has neither a prefab nor an asset key |
| `UIDIRECTOR122` | Warning | `BackPolicy.Close` on a layer that does not take back |
| `UIDIRECTOR123` | Warning | The prefab contains a dropdown |
| `UIDIRECTOR124` | Warning | The asset key matches no Addressables address, GUID or label |
| `UIDIRECTOR125` | Error | A row has an empty id |

The root also reports `UIDIRECTOR125` at run time when a catalog is registered, and refuses the row.
Test assemblies are skipped when keys are collected. The validator is pure and deterministic: catalogs
in the given order, rows in order, then keys by type name and field name.

**Create menus.**

- `GameObject > UI > UIDirector Root` creates, in the active scene, a canvas (screen space overlay)
  with a `CanvasScaler`, a `ViewRoot` and a `BackInput`, and an `EventSystem` if the scene has none.
  The root is built inactive and activated last, so `Awake` sees the final render mode and camera.
- `GameObject > UI > UIDirector Root (Camera)` creates a scene-root `UIDirector` object that holds the
  root and a sibling `UI Camera` that draws the UI layer only. They are siblings because a
  camera-mode canvas that is a child of its own camera is not drawn. View prefabs must be on the UI
  layer, or the camera culls them.
- `Tools > UIDirector > Create Prefab UI Environment` writes a scene holding a canvas and scaler,
  copied from the selected root, for Prefab Mode. A view prefab whose root is a nested canvas has no
  scaler of its own and would otherwise open at the wrong size. The first root creation of an editor
  session offers it once, never in batch mode, and never saves the user's open scene.
- `Tools > UIDirector > DOTween Integration` and `Zenject Integration` toggle the scripting define
  that enables the optional assembly, on every build target.
- `Assets > Create > UIDirector/View Catalog` creates a `ViewCatalog`; `Tools > UIDirector >
  Documentation` opens the docs for the installed version.

---

## 17. Threading

The package is main-thread only. One internal seam, `IMainThread`, answers "am I on the main thread"
and "run this there"; the real implementation uses UniTask's main thread id and `UniTask.Post`.

| Call off the main thread | Result |
|---|---|
| `Open`, `ShowAsync`, `HoldInput`, `AddObserver`, `AddCatalog`, `PreloadAsync` | Throws `InvalidOperationException` at the call, and changes nothing |
| `ViewQueue.Enqueue`, `EnqueueAsync`, `Hold`, `AddGate`, `AddObserver` | Throws `InvalidOperationException`, also after the queue's lifetime ended |
| `Back`, `CloseTo` | Returns `false` |
| `CloseAll`, `Trim`, `ViewQueue.Clear` | Posted to the main thread |
| `ViewRegistration.Remove()` | Posted to the main thread |

After teardown nothing throws. **Owner cancellation** is the one place that foreign threads reach in:
a `CancellationToken` can be cancelled from a timer or a worker thread. The callbacks are registered
without capturing the execution context, and when one arrives off the main thread the package posts the
handling to the main thread and logs `UIDIRECTOR113`. The request, the registration, the queued item or
the queue (its lifetime token or the root token) then ends on the main thread, in the normal way. A cancellation that arrives after
the request ended posts nothing. See [Threading](../Threading.md) for the user-facing rules.

---

## 18. Allocation and idle cost

**Idle.** An idle root costs one call per frame and nothing else:

- `ViewRoot.Update` ticks the navigator clock. It allocates 0 B, and the clock keeps a timer list that
  is empty unless a watchdog, the load indicator, an input-hold warning or a queue-hold warning is pending.
- `BackInput` reads the back key, 0 B.
- The safe area and screen size are compared once per frame, only while a `SafeAreaFitter` is
  registered with the root; with none registered the comparison does not run.
- An idle backdrop runs no animation, an idle queue runs no timer, and nothing is scheduled per frame
  by the queue. A queue hold adds one timer for its 60 s warning: on the navigator clock when the
  queue is on the package's own navigator, which costs nothing per frame. A queue on any other
  `INavigator` waits for that warning with a UniTask delay, which polls once per frame for the first
  60 s of the hold.
- Each view carries a nested canvas and a graphic raycaster; the shield is one always-active graphic
  with an empty mesh.

**Queries.** `Back()` with nothing to do, `IsOpen`, `IsBusy` and `IsLayerClear` allocate nothing once
warm. A frame of a running built-in transition allocates nothing once warm. The editor read-outs
allocate nothing on a warm capture.

**Per open.** Opening is not a hot path, and the package does not claim zero allocation for it. An open
allocates a record with two awaiter lists, a handle, a context, an open scope (its token source is
created lazily), a visible scope per on-screen span, a cancellation source for each load or transition
step, and a completion source for each awaiter. The delegates that the terminator, the outbox and the
queue use are cached. Instance reuse is `KeepInstance`.

**Cover and reveal.** One hierarchy walk per cover and per reveal, into reused lists. `DisableCanvas`
is the default because reveal is the most frequent transition and `SetActive` rebuilds exactly there.

**Struct arguments.** Generic code over struct `TArgs` and `TResult` is instantiated once per
combination under IL2CPP, which costs code size. Small structs or classes keep that low.

---

## 19. Responsibility table

Public types are marked (P). Everything else is internal.

| Class | Owns | Does | Does not know |
|---|---|---|---|
| `Navigator` | Composition of the pieces | The `INavigator` facade: thread and root checks, delegation, registrations, teardown | Canvases, tweens, queues |
| `RequestIntake` | The request id counter | The acceptance checks in order, including the refusal of a queued row on another layer; creates records; registers owner callbacks | Unity objects |
| `ViewRegistry` | Rows by id; the root's layers | Lookup; refuses duplicate ids and unknown layers | Instances |
| `ViewRecord`, `ViewRecord<TArgs, TResult>` | One request: phase, row, owner registration, latched outcome, awaiters, scopes, instance | Holds state; completes awaiters | How anything is displayed |
| `LayerState` | The ordered records and the operation FIFO of one layer; slot numbers | Add, remove, order | Other layers |
| `LayerRunner` | The running step and its cancellation source | Executes operations through the stage; the watchdogs | Back and queueing decisions |
| `Projection` | Nothing (pure) | Hidden, interactive, order and backdrop placement from lists and phases | Unity types |
| `Terminator` | Nothing between calls | The one synchronous routine that ends a request | Loading |
| `Outbox` | Pending notifications | Runs them one at a time, isolated, in the defined order; holds events and notifications back while an observer dispatch is in progress | What a notification means |
| `InputShield` | The holder list | Raise and drop; the long-hold diagnostic | Views |
| `BackResolver` | Nothing (pure) | One back decision from a snapshot | Input devices |
| `RetainedInstances` | One idle instance per id | Stash, take, drop, trim | Why a view closed |
| `PreloadLeases` | Leases by owner | Acquire through the stage; dispose on owner end | Instances |
| `ObserverList`, `ErrorRouter`, `Diagnostics` | Observers; the handler; stable ids | Isolated dispatch; routes exceptions; logs diagnostics in the editor and development builds | Game types |
| `ViewScope` (P) | A token source and entries | Bind, subscribe, add, run; ends exactly once | The view it belongs to |
| `ViewContext<TResult>` (P) | A record reference | Complete, close, decline | Other requests |
| `IViewStage`, `UguiStage` | Layer rects, shield, backdrops, staging | What the logic needs from Unity; the uGUI implementation; routes the release of a late load result | Why something is shown |
| `ViewRoot` (P) | Serialized settings; the navigator | Initialise; tick the clock; forward destruction, low memory and safe-area changes | Navigation rules |
| `ViewHost` (P) | The view's presentation state and its handle | Reports loss; Inspector `Close()` | Layers and stacks |
| `QuitSignal` | The process-wide quitting flag | One `Application.quitting` subscription, renewed at subsystem registration | Views and roots |
| `Backdrop`, `ShieldGraphic` | A fade; a flag | Forward a tap; an empty-mesh raycast target | Counts and policies |
| `ViewTransition` and built-ins (P) | Serialized animation settings | Play, snap | The navigator |
| `BackInput`, `ViewButton`, `SafeAreaFitter` (P) | Nothing, a serialized action, edge flags | Back key; Inspector actions; fit a rect | The stack |
| `ViewQueue` (P) with `HoldSet`, `GateChain`, `QueueItem` | Waiting items, holds, gates | Pump; present through `INavigator` only | Layer contents |
| `AddressablesViewProvider` (P) | A handle per instance; preload leases | Instantiate, release, preload | Navigation |
| `TweenTransition` (P) | Tween settings | Play and kill a tween | Navigation |
| `NavigatorInstaller` (P) | Nothing | Bind and initialise through a Zenject container | Views |

---

## 20. Limits

Known limits of 0.1.0, as the code stands:

- The shield does not cover canvases sorted above it, including an open dropdown list and any
  interface the package does not own with a higher order ([section 7](#7-the-input-shield)).
- A kept instance is one idle instance per id, with no budget. A deep stack of heavy screens stays
  resident. `CloseTo` and return-to-existing keep stacks shallow.
- A cancelled `WaitForCloseAsync` waiter stays in the record's list until the request ends.
- An item that the navigator ended without showing is not retried by the queue.
- A queue on an `INavigator` other than the package's own is invisible to the editor tools, does not
  check its layer at construction, and waits for its hold warning with a delay that polls once per
  frame for the first 60 s of a hold ([section 18](#18-allocation-and-idle-cost)).
- A root destroyed from inside an observer callback removes the observers before the held-back
  terminal events drain, so observers and the event ring miss those events; awaiters still complete
  ([section 2](#2-the-root-and-its-layers)).
- A request whose owner token is the open scope of a screen that a reopen in place or a return
  replaces or pops ends with that screen ([Reopen in place](#reopen-in-place)).
- Unity 6000.0 or newer is required.

See [Limitations](../Limitations.md) for the user-facing list.

## See also

- [Concepts](../Concepts.md)
- [Performance](../Performance.md)
- [Threading](../Threading.md)
- [Diagnostics](../Diagnostics.md)
- [Troubleshooting](../Troubleshooting.md)
