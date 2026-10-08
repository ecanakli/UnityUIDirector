# ADR-004: The queue sits above the navigator

- **Status:** Accepted, implemented in 0.1.0
- **Related:** [Architecture section 13](../Architecture.md#13-the-queue-above-the-navigator),
  [Architecture section 10](../Architecture.md#10-events-and-the-order-at-close),
  [ADR-001](ADR-001-Outcomes-As-Data.md), [ADR-005](ADR-005-Token-Owners.md),
  [Queue](../../Queue.md)

## Context

Several features of one app want to show a popup at about the same time: a maintenance notice, a
what-is-new card, a request for feedback, a tip that belongs to a tutorial. Nobody owns the decision of
which one comes first, and each feature knows only about itself. What an app needs is a small set of
rules that sit outside every feature:

- present them one at a time, the most important first;
- do not present anything while a flow is running (a tutorial, a purchase);
- do not present an item whose condition no longer holds when its turn comes;
- never open on top of a screen change, a modal that is already up, or a load in flight.

Most projects need none of this. A project with one confirm dialog should not pay for it, and the
navigator that runs every view should not carry state for it. The navigator's state is already subtle:
a list per layer, one operation at a time, shield holders, derived visibility. A second holder of
"what is allowed to be shown now" inside it would have to stay consistent with that state.

## Decision

**The queue is a separate public class, `ViewQueue`, that talks to the navigator through `INavigator`
only. The navigator knows nothing about queues.**

- **What it needs from the navigator is a few facts and one call.** `IsLayerClear(layer)` says whether
  the layer has no request, no modal layer above it has a request, and the navigator is not busy.
  `ReserveRequestId()` gives an id before the request exists. `ShowAsync` with `OpenOptions` carrying
  that id, the priority and the source tag presents the item. `AddObserver` delivers the navigator's
  `Settled`, `Closed` and `NotShown` events. A queue on a fake navigator in a test works the same way.
- **Per layer, by priority.** `new ViewQueue(navigator, layer, lifetime, name)` presents on one layer.
  Higher `QueueOptions.Priority` goes first and equal priorities keep enqueue order. A game can have
  several queues. The layer must exist on the navigator: a queue on the root's own navigator throws
  `ArgumentException` for a layer it does not have.
- **Holds pause it.** `Hold(reason, owner)` takes a counted hold that ends with its owner or on
  `Remove()`. While any hold exists nothing is presented. A hold has no automatic timeout; a hold older
  than 60 s warns once with `UIDIRECTOR111`. An atomic flow over the queue is an async method that takes
  a hold for its duration and opens its own views immediately.
- **Gates decide.** An `IViewGate` is a synchronous rule that answers `Allow` or `Deny(reason)`. Gates
  run in ascending order, at enqueue and again right before presentation, and the first deny ends the
  item as `NotShown / Denied` with the gate's reason. A gate that throws counts as a deny and is
  reported (`UIDIRECTOR119`).
- **The pump is coalesced and runs on the next tick.** Triggers are an enqueue, a hold removed, a gate
  added or removed, an item ending, and a navigator `Settled`, `Closed` or `NotShown`. Same-frame
  enqueues are therefore ordered by priority, and the continuation of a view that just closed runs
  before the queue presents the next item. The clear check, the gates, the re-check and the `ShowAsync`
  call happen in one synchronous block, so no other open can slip in between.
- **`Closed` and `NotShown` are triggers as well as `Settled`.** A request that ends without an
  operation (its owner ended, it was lost from outside, a screen change closed it) publishes no
  `Settled`, yet the layer it held may now be clear. Listening to those two events keeps the queue from
  waiting for a signal that never comes.
- **One id from enqueue to close.** The queue publishes `Enqueued`, and `NotShown` for items that never
  reached the navigator, to its own observers. Everything after presentation is the navigator's, with
  the same request id.
- **A queue has a lifetime, and it ends with the root.** When the lifetime token ends while the root still
  exists, waiting items end as `NotShown / Cancelled`, every registration ends, and the queue is inert.
  When the navigator's `RootToken` is cancelled, including when it is the lifetime itself, the same
  happens with `NotShown / RootGone` as the result. A view that is already
  presented is left to its own owner; it ends once through the navigator.
- **Immediate opens bypass it.** A direct `ShowAsync` ignores the queue and its holds. An immediate
  popup makes the layer busy, and the queue waits for it.

## Consequences

**Positive**

- A project that does not use a queue pays nothing: no state, no checks, no allocation in the
  navigator.
- One holder of "what is shown": the navigator. The queue reads facts and never writes layer state.
- The queue is testable against a fake navigator and a manual ticker, and the navigator is tested
  without it.
- Features register their own gates and holds with their own owners, and the registrations end with
  those owners.

**Negative / accepted costs**

- **Two systems to learn.** A reader has to know both what the navigator does and what the queue adds.
- **The queue sees only what `INavigator` exposes.** A rule that needs game state is a gate that reads
  that state; the package cannot provide a vocabulary for it.
- **Gates are synchronous.** A gate cannot await a server. A flow that must wait for something takes a
  hold, then releases it.
- **A tick of latency.** An item that could be shown now is shown on the next `Update` tick.
- **No expiry, no de-duplication and no re-evaluation while an item waits.** Gates run at enqueue and at
  presentation, and nothing runs in between. An item that should not wait forever is given an owner
  token that the game cancels.
- **An item that the navigator ended without showing is not retried.** The queue reports the navigator's
  result and moves on.
- **A queue on an `INavigator` other than the package's own is invisible to the editor tools.**

**Enforced by tests**

- `ViewQueueOrderTests`: priority and enqueue order, the next-tick rule, the layer-clear rule, the
  pump triggers including a layer that clears without `Settled`, the reserved id and the options handed
  to `ShowAsync`, and the events of the queue.
- `ViewQueueRulesTests`: counted holds and their owners, the 60 s warning, gates at enqueue and at
  presentation, ordering, a throwing gate, `Clear`, the lifetime, the root token ending, calls off the
  main thread, and that every enqueued item ends exactly once on every path.
- `ViewQueueNavigatorTests`: the layer check at construction.
- `ViewQueueOnRealRootTests`: a queue on a real root ends its waiting items as `NotShown / RootGone`
  when the root is destroyed, whatever its lifetime, and leaves the presented item to the navigator.
- `CloseOrderContractTests`: an awaiting continuation runs before the queue presents the next item.
- `RandomInvariantTests`: a seeded random run with queue steps checks that at most one item is
  presenting, that nothing is presented while a hold exists or the layer is not clear, that the
  presented item has the highest priority, and that no item waits forever.

## Amendment 2026-10-08

Two rules were tightened before 0.1.0 shipped. Neither changes the decision above; both follow from the
queue holding no navigator state of its own.

- **The queue also ends with its root.** A queue built on a navigator now reads `RootToken` once and
  ends when that token is cancelled, whatever its own lifetime is. Waiting items end as
  `NotShown / RootGone` with the detail "The root was destroyed.", later `Enqueue` and `EnqueueAsync`
  calls complete the same way, and the queue's registrations on both tokens are disposed, so neither
  token keeps a dead queue alive. A queue whose lifetime is `RootToken`, the usual case, therefore ends
  its waiting items as `RootGone` and not as `Cancelled`. A queue built after the root is gone is inert
  from the start. When both tokens end, the reason comes from the state of `RootToken`, not from which
  callback ran first. The presented item is the navigator's, so it ends once through the navigator,
  possibly after the queue's waiting items.
- **The layer is checked.** A queue on the package's own navigator throws `ArgumentException` at
  construction for a layer the navigator does not have. A queue on another `INavigator` cannot be
  checked that way. At presentation, a key whose catalog row is on a different layer than the queue's
  is refused by the navigator as `NotShown / WrongLayer`
  ([UIDIRECTOR126](../../Troubleshooting.md#uidirector126)).

## Alternatives considered

### Queueing as a layer policy inside the navigator

Rejected. The strongest alternative: give each layer an admission mode (immediate, queued, replace) and
let the navigator order requests. Every user would pay for it, the navigator would hold two copies of
the state of a layer (what is open and what is waiting), and the queue's rules would be tangled with
the operation list. A separate class keeps the navigator's contract small.

### A declarative rules engine

Rejected. A set of named conditions such as "not during a tutorial" or "at most once a day" is a
vocabulary, and the vocabulary is the structure of one particular game under another name. A
synchronous gate that the game implements covers every condition without the package guessing them.

### Automatic release of holds after a timeout

Rejected. An auto-release lets popups through in the middle of a flow that is merely slow. A hold is
tied to its owner, and the package only reports a hold that has lasted too long.

### Extras: a resolver, an awaitable "turn", a de-duplication key, expiry, interruption

Rejected for 0.1.0. Each one makes the queue a second scheduler. Interrupting the view that is
presented loses that view. An awaitable turn is a hold taken by an async method. Expiry is an owner
token that the game cancels. A de-duplication key is a gate that checks the game's state.

## See also

- [Queue](../../Queue.md)
- [Events and analytics](../../Events-and-Analytics.md)
- [Architecture](../Architecture.md)
- [Troubleshooting](../../Troubleshooting.md)
