# ADR-001: Outcomes as data

- **Status:** Accepted, implemented in 0.1.0
- **Related:** [Architecture section 4](../Architecture.md#4-requests-and-phases),
  [Architecture section 11](../Architecture.md#11-exactly-once-outcomes),
  [ADR-005](ADR-005-Token-Owners.md), [Opening and results](../../Opening-and-Results.md),
  [Events and analytics](../../Events-and-Analytics.md)

## Context

Code that opens a view usually wants one thing back: how it ended, and the value if there was one.
The set of ways a view can end is large:

- the view closed itself with a value, or without one;
- the back key, a tap on the backdrop or a close call from code closed it;
- a screen change replaced it;
- the owner of the request ended;
- the instance was destroyed or deactivated from outside, or the root was destroyed;
- it never appeared: unknown id, a duplicate, a busy navigator, a gate that denied it, a failed or
  slow load, an `OnOpen` that declined or threw.

An API that reports some of these as a return value and the rest as exceptions makes every call site
a small decision tree. The exception route also has a worse problem. When the owner of a request
ends, a design that throws `OperationCanceledException` into the awaiting code assumes that the
awaiting code *is* the owner. It often is not: a flow that lives for the whole app can await a view
that is owned by one screen. When that screen goes away, the exception would unwind the app-wide
flow, and an `async` method that ends with a cancellation exception disappears without a message.

There is a second constraint from the async library the package is built on. A UniTask that is backed
by a pooled source can be awaited once. A handle that is a struct copy of an awaitable, passed around
and awaited twice or late, has undefined behaviour.

## Decision

**`ShowAsync` completes with a `ViewResult<TResult>` in every case. Outcomes are data.**

- The result carries `RequestId`, `WasShown`, `Reason` (a `CloseReason`), `Rejected` (a
  `RejectReason`), `Detail`, `HasValue` and `Value`. A request that never reached the screen reports
  `CloseReason.NotShown` and a `RejectReason` that says why. A shown view reports the `CloseReason` that
  ended it and `RejectReason.None`.
- `HasValue` is true only when the view was shown and closed with `Complete(value)`. `Value` is the
  default value otherwise. `TryGetValue` returns `HasValue`.
- The task does not fail for anything that happens to the view: the owner ending, the root being
  destroyed, a rejection, an exception inside `OnOpen`. `QueueItem` follows the same rule, so
  `ViewQueue.EnqueueAsync` completes with data also for an item that ended inside the queue.
- **The only exceptions are usage errors,** raised synchronously at the call: a call off the main
  thread, a null argument, an empty id in `ViewKey`. They are programming mistakes and never depend on
  what happens to a view later.
- **The owner token decides the life of the view and never cancels an awaiter.** When the owner
  ends, every awaiter completes with `OwnerEnded` as data, whether or not the awaiting code is the
  owner.
- **An awaiter is cancelled only by its own token.** `ViewHandle.WaitForCloseAsync(waiter)` throws
  `OperationCanceledException` for that waiter when `waiter` is cancelled, and leaves the view
  untouched. For the typed form the caller attaches a token to the task with UniTask's
  `AttachExternalCancellation`.
- **Handles are not awaitable.** `ViewHandle` is a read-only struct over the request record with
  `Phase`, `IsAlive`, `Close()`, `WaitForCloseAsync` and `TryGetInstance`. Every `ShowAsync` and
  `WaitForCloseAsync` call gets its own completion source, and records are never pooled, so a late or
  second wait returns the stored outcome at once.
- **Observers see the same facts untyped.** `ViewClosed` and the `Closed` and `NotShown` events carry
  `WasShown`, `Reason`, `Rejected` and `Detail`, with no arguments and no value.
- **Fakes need no test assembly.** `ViewResult.Completed`, `ViewResult.Closed` and
  `ViewResult.NotShown` are public factories, so a game can fake an `INavigator` without the package
  shipping a testing assembly.

A request ends exactly once and its awaiters complete exactly once. The first close cause is latched;
later causes, including the owner ending after a close was latched, do not change the outcome.

*Amendment, 2026-10-08:* the factories refuse combinations that are not outcomes. `ViewResult.Closed`
throws `ArgumentException` for `CloseReason.NotShown`, and `ViewResult.NotShown` throws it for
`RejectReason.None`. `Closed(CloseReason.Completed)` stays allowed: it is what a `ShowAsync` joined
with other argument or result types returns, a completion with `HasValue` false. `default(ViewResult<T>)`
is not an outcome the navigator returns. `RejectReason` gained `WrongLayer`, appended after `RootGone`. On
the package's navigator, a queued item whose row is on another layer than the queue's ends as `NotShown / WrongLayer`
(`UIDIRECTOR126`), also as data.

## Consequences

**Positive**

- A call site has one shape: await, then switch on `Reason` or check `HasValue`. There is no
  `try/catch` around an open to be forgotten, and no path on which the awaiting code stops without a
  trace.
- A flow that awaits a view owned by something else survives the owner ending and learns it from
  `OwnerEnded`.
- Analytics and the awaiter agree. Both read the same latched outcome, so the `Closed` event and the
  `ViewResult` never disagree about why a view ended.
- A late `await`, a second `await` and a join of a view that is already open are all well defined.
- A request that ends before it was shown, in any phase, still completes its awaiters, also during
  root teardown.

**Negative / accepted costs**

- **A caller can forget to look.** Code that ignores `WasShown` and `HasValue` treats a rejected open
  like a dismissal. The type makes the check easy, and it cannot force it.
- **No cancellation-driven control flow.** An awaiter that wants to stop waiting cannot cancel a
  `ShowAsync` call; it uses its own token on the wait, and the view lives on. To end the view, the
  caller closes it through its handle or cancels the owner.
- **A result is a message in memory.** It is not delivered if the process dies and an awaiter can be
  abandoned. A one-time side effect (a payment, a reward) must be performed by a service that persists
  it and is safe to repeat, and not by the awaiting continuation alone. The package offers no
  exactly-once delivery.
- **A joined request that was opened with other argument types shares the outcome but not the
  value.** `HasValue` is false for such a join.

**Enforced by tests**

- `AwaiterContractTests`: every close path completes `ShowAsync` exactly once with the reason as data;
  the owner ending succeeds and is never cancelled; many `WaitForCloseAsync` callers get one outcome;
  a cancelled waiter throws only for itself; late waits return the stored outcome; teardown completes
  every awaiter in every phase.
- `EventContractTests` and `CloseOrderContractTests`: the events and the awaiters agree and arrive in
  the defined order.
- `LifecycleTerminateTests`: a latched outcome survives a later terminate cause.
- `RandomInvariantTests`: a seeded random run checks after every step that every awaiter completed
  exactly once and that the event sequence per request id is one of the allowed shapes.

## Alternatives considered

### Throw `OperationCanceledException` when the owner ends

Rejected. It is the usual shape for cancellation, and it silently assumes the awaiter is the owner.
An app-wide flow that awaits a view owned by a screen would end without a message when that screen
goes away. Data is also easier to switch on than a catch block, and the distinction between "the
view ended because its owner ended" and "I stopped waiting" stays visible.

### An awaitable struct handle

Rejected. A UniTask-based handle that is copied, stored and awaited twice or late is undefined. The
package returns a task from `ShowAsync`, gives every call its own completion source, and keeps the
handle as an inert reference for `Close`, `Phase` and `WaitForCloseAsync`.

### One exception type per rejection reason

Rejected. A caller would catch a list of types for one operation, and a successful close with no value
would still need a second path. Reasons are enum values on one struct, which also flow unchanged into
events.

### A value-only return that is the default when the view did not complete

Rejected. It loses the reason, so a dismissal, a back press, a withdrawal and a rejected open all look
alike. The reason is what analytics and flow control need.

## See also

- [Architecture](../Architecture.md)
- [Opening and results](../../Opening-and-Results.md)
- [Events and analytics](../../Events-and-Analytics.md)
- [Concepts](../../Concepts.md)
