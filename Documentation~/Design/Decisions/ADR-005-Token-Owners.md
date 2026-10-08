# ADR-005: Token owners

- **Status:** Accepted, implemented in 0.1.0
- **Related:** [Architecture section 4](../Architecture.md#4-requests-and-phases),
  [Architecture section 17](../Architecture.md#17-threading),
  [ADR-001](ADR-001-Outcomes-As-Data.md), [ADR-003](ADR-003-Begin-Only-Hooks-And-Scopes.md),
  [Opening and results](../../Opening-and-Results.md), [Threading](../../Threading.md)

## Context

Every request has to end somehow. Usually the view closes itself or the user dismisses it. But the code
that asked for the view can go away first: a screen is closed while the popup it opened is still up, a
feature is torn down while its notice waits in a queue, a scene unloads. If nothing says who the view
belongs to, a view outlives the code that knew what to do with its answer, and the answer goes nowhere.

The same question comes up for everything else that a caller registers with the navigator or a queue: a
shield hold, an observer, a catalog, a preload, a gate, a queue hold. A registration that has to be
removed by a call that the owner remembers is the forgotten removal line again.

The package is built on UniTask and runs in an engine whose async code already speaks one currency for
"until this ends": `CancellationToken`. Objects that provide such a token are everywhere: the destroy
token of a component, the token of a scope, the application's own. A package-defined owner type would
need adapters to each of them, and an optional owner would make "no owner" the default.

## Decision

**Every request and every registration takes a required `CancellationToken` as its owner.**

- **Positional and required.** `ShowAsync` and `Open` take `owner` right after the key and the arguments
  (the no-argument forms take it right after the key). `HoldInput`, `AddObserver`, `AddCatalog`, `PreloadAsync` and the queue's `Enqueue`,
  `EnqueueAsync`, `Hold`, `AddGate` and `AddObserver` take one too, and so does the queue's lifetime.
  There is no overload without it.
- **`CancellationToken.None` is the explicit "until someone closes it".** `INavigator.RootToken` is the
  recommended long-lived owner: it is cancelled when the root is destroyed, as the last step of the
  teardown, so it means "as long as the UI exists".
- **The owner decides the life of the view and never cancels an awaiter.** When it ends while the view
  is on screen, the request is terminated at once: the running step is cancelled, the view snaps, and
  the request is finalized with `CloseReason.OwnerEnded`. A request that is not shown yet is withdrawn
  with `RejectReason.OwnerEnded`. In both cases every awaiter completes with that outcome as data
  ([ADR-001](ADR-001-Outcomes-As-Data.md)). An outcome that was already latched wins over the owner
  ending. An owner that is already cancelled at the call ends the request as not shown, with no shield
  and no load.
- **A popup can be owned by the open scope of the screen that opened it.** Passing the scope's `Token`
  as the owner closes the popup when the screen goes away, and the popup reports `Closed` before the
  screen does ([ADR-003](ADR-003-Begin-Only-Hooks-And-Scopes.md)). A `ViewButton` that opens a view
  uses `RootToken`, so the view outlives the screen that holds the button.
- **The callback is cheap and cleaned up.** The package registers its callback with
  `RegisterWithoutCaptureExecutionContext`, after the `Requested` event, so a token that fires during
  registration cannot reorder events or leak a shield holder. On every normal end the registration is
  disposed, which unregisters without invoking, so a long-lived owner does not accumulate entries. A
  token that cannot be cancelled registers nothing.
- **Registrations end with their owner and nothing restarts.** A hold, an observer, a catalog, a
  preload lease, a gate and a queue's registrations are removed when their owner ends or when
  `ViewRegistration.Remove()` is called, whichever is first. `Remove()` is safe to call twice and from
  any thread. A registration whose owner is already cancelled comes back empty. The game registers
  again if it wants the effect again.
- **A queue also ends with its root.** A queue watches the navigator's `RootToken` as well as its own
  lifetime. When either ends, its waiting items complete as `NotShown`, with `RootGone` if the root is
  gone and `Cancelled` otherwise, and later `Enqueue` calls complete the same way. A queue whose
  lifetime *is* `RootToken` therefore reports `RootGone`. The presented item is left to the navigator,
  which ends it once, possibly after the queue's waiting items. A queue built after the root is gone is
  inert from the start. Both registrations are disposed when the queue ends, so neither token keeps a
  dead queue alive.
- **A foreign thread can cancel.** A token may be cancelled from a timer or a worker thread. The
  callback then posts the handling to the main thread and logs `UIDIRECTOR113`; the request or
  registration ends on the main thread, in the normal way.
- **The scope tokens are different.** A `ViewScope.Token` belongs to the package and ends with the
  scope. It is never the request's owner token.

| Token | Cancelled by | When it fires |
|---|---|---|
| Request owner | The caller | The request is withdrawn, or the shown view is terminated; awaiters get `OwnerEnded` |
| Registration owner (hold, observer, catalog, preload, gate) | The caller | The registration is removed |
| Queue lifetime | The game | Waiting items end as `NotShown / Cancelled`, or `NotShown / RootGone` when the root is already gone; a presented view is left to its own owner |
| `RootToken` | The package | The root is destroyed, as the last step of the teardown; a queue also ends with it |
| Scope token | The package | The open or visible scope ends |
| `WaitForCloseAsync` waiter | The awaiting caller | That one await throws; the view is untouched |

## Consequences

**Positive**

- A request cannot outlive the code that can handle its answer without that being a visible choice at
  the call: the caller wrote `CancellationToken.None` or `RootToken`.
- The type is the one that every async API in the engine and UniTask already accepts. A destroy token,
  a scope token or a service's token is a valid owner with no adapter.
- A registration needs no removal line. The owner ends and the package removes it.
- Teardown is uniform. The root ending cancels `RootToken` and every request, hold, observer and lease
  ends through the same paths.

**Negative / accepted costs**

- **`CancellationToken.None` is easy to type.** A request owned by `None` lives until somebody closes it;
  a view that waits for an answer nobody gives stays up. `RootToken` and the shield make the failure
  visible, and the guide says which to pass.
- **Ending by owner snaps.** A view whose owner ends is not animated out. It is snapped and finalized,
  because the code that owned it is already gone.
- **The owner is a token, not an object.** A diagnostic can name the caller's member and line, but not
  "the screen" that owns the request.
- **Cross-thread cancellation costs a post and a warning.** A project that cancels from a worker thread
  gets `UIDIRECTOR113` each time.
- **Nothing restarts by itself.** A hold or gate registered with a token that ended must be registered
  again by the game.

**Enforced by tests**

- `AwaiterContractTests`: the owner ending succeeds and is never cancelled, whether or not the awaiting
  code is the owner; every awaiter completes with data.
- `LifecycleAcceptanceTests` and `LifecycleTerminateTests`: an owner that is already cancelled is
  rejected at the call; the owner ending terminates a shown view and withdraws a waiting one; a latched
  outcome survives the owner ending.
- `ThreadingContractTests`: an owner cancelled off the main thread is posted and logs `UIDIRECTOR113`;
  a registration removed off the main thread is posted; a cancellation after the end posts nothing.
- `ViewQueueRulesTests`: a hold or an item owner that ends, a queue lifetime that ends, and the removal
  of registrations.

## Alternatives considered

### An optional owner

Rejected. A default of "no owner" is how views outlive the code that opened them. A required parameter
makes the choice appear at the call, and `CancellationToken.None` is a deliberate way to say it.

### A package-defined owner or lifetime object

Rejected. A new owner type would need an adapter to every token source a project already has, and it
would be one more concept to learn. A token carries exactly the information the package needs: "this
ended".

### A dependency on an external lifetime or scope library

Rejected. The package must install and work alone, and its owner type should not bind it to another
project's release cycle. Any lifetime library that exposes a token works with the package unchanged.

### An implicit owner: the calling component or the active scene

Rejected. The package has no knowledge of scenes or of the caller's object, and a hidden default owner
is how work silently outlives what it belonged to. The explicit `RootToken` says "as long as the UI
exists" in one word.

### The awaiter as the owner

Rejected. Throwing into the awaiting code when the owner ends assumes the two are the same. See
[ADR-001](ADR-001-Outcomes-As-Data.md).

## See also

- [Opening and results](../../Opening-and-Results.md)
- [Threading](../../Threading.md)
- [Events and analytics](../../Events-and-Analytics.md)
- [Architecture](../Architecture.md)
