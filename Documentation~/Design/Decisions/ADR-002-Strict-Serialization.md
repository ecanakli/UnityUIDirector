# ADR-002: Strict serialization

- **Status:** Accepted, implemented in 0.1.0
- **Related:** [Architecture section 5](../Architecture.md#5-the-per-layer-runner),
  [Architecture section 15](../Architecture.md#15-transitions),
  [ADR-001](ADR-001-Outcomes-As-Data.md), [Transitions](../../Transitions.md),
  [Navigation and back](../../Navigation-and-Back.md), [Limitations](../../Limitations.md)

## Context

A view's life is a sequence of steps that take time: load, show transition, hide transition. The
requests that move a layer arrive from many places and not in a convenient order. A close arrives while
the view is still opening. Two opens arrive in the same frame. A pop-to arrives behind an open that has
not loaded yet.

The common answer is to let a new request interrupt the running one: stop the show transition where
it is and start the hide from the current pose. That answer has costs that show up late.

- Every transition must be written to start from any pose and to handle being superseded at any
  frame. A custom transition that does not is a bug that only shows when two requests collide.
- State is observable in between: a view that is half shown, a backdrop that has moved, a shield that
  was released by the superseded request and not yet taken by the new one.
- Most of the paths that create the overlap are not reachable by a person. The shield is raised when a
  request is accepted, so a user cannot tap into the middle of a transition. The overlap comes from
  code, and code can wait.

## Decision

**Each layer has one FIFO of operations and one runner. One operation runs at a time. A started step
is never interrupted.**

- The operations are open, close, pop-to and close-all. Every request that changes a layer becomes an
  operation on that layer's list. A close that is latched while the view is `Opening` runs after the
  open finishes.
- **Layers are independent.** Runners of different layers run concurrently, and the input shield count
  covers all of them. State that crosses layers is derived, not held: after every change the
  projection recomputes hidden, interactive and sort order for all layers
  ([section 6](../Architecture.md#6-projection)).
- **Two things cut a step short: a terminate cause and a watchdog.** A terminate cause is the owner
  token ending, the instance being lost from outside, or the root being destroyed. The terminator
  cancels the running step, snaps the view to its end pose, and finalizes the request at once. A
  watchdog is a timeout on a step.
- **Both snap.** A snap writes the end pose synchronously, so no step ever leaves a half-shown view
  behind. After a cut step the runner waits for the end routine to finish before it dequeues the next
  operation, so the next operation never runs inside it.
- **Every wait is bounded.** The load step has a timeout (default 15 s, `UIDIRECTOR105`). A show or
  hide transition has a timeout (default 3 s). On a transition timeout the package cancels the step,
  snaps and reports `UIDIRECTOR106`, then continues as if the transition had finished. Both timeouts
  are fields on the root. They count on the navigator clock, which `ViewRoot` advances by
  `min(Time.unscaledDeltaTime, 0.1 s)` per frame, so they run at `timeScale == 0` and a suspension adds
  at most one step.
- **The shield is released in a `finally`.** A faulted operation routes its exception, ends its
  request as not shown, releases its shield holder and lets the loop continue.
- **Immediate opens are accepted and serialized.** A burst of opens on one layer is shown in the order
  it was called. A layer accepts at most a waiting cap of requests in `Waiting` (default 4); the next
  one ends as `NotShown / Busy` with `UIDIRECTOR112`. `OpenOptions.RejectWhenBusy` is the per-call
  opt-in for tap handlers that must not stack.
- **A transition starts from the opposite end pose.** Because it is never interrupted, a transition has
  no "start from the current pose" obligation. `ViewTransition` asks for unscaled time, a stop on
  cancellation, and a synchronous, idempotent, non-throwing `Snap`.

## Consequences

**Positive**

- The superseded-transition bug class does not exist. At each step boundary the state is consistent,
  which is where the projection runs.
- A custom transition is simple: play from one pose to the other, stop when asked. A shared check
  (`TransitionConformance` in the tests) verifies the built-ins and `TweenTransition` against the same
  contract.
- One writer per layer. The list, the slots and the shield holders of a layer are only changed by its
  runner and by the terminator, and the two never overlap.
- A stall has a ceiling. A hung load, a transition on scaled time and a provider that never answers all
  end at a timeout, with a diagnostic that names the view.

**Negative / accepted costs**

- **Latency.** A close requested during a 0.25 s open waits for the open. Opens behind a slow load wait
  for the load. The input shield, the load indicator and back withdrawing a pending open are the
  mitigations, and the load timeout is the ceiling.
- **A custom transition on scaled time costs the whole transition timeout** on every play while the
  game is paused, before it is snapped.
- **At very low frame rates the two clamps differ.** The navigator clock advances at most 0.1 s per
  frame and the built-in animation loop at most 1/30 s per frame. Below 30 frames per second a built-in
  transition therefore takes longer than its duration. Timeouts are in clock seconds, so at 10 frames per
  second or fewer a built-in transition takes up to three times its duration on the clock, and one longer
  than a third of the transition timeout (1 s with the default 3 s) is snapped with `UIDIRECTOR106`.
- **The defaults are fields on the root.** The 15 s load timeout, the 3 s transition timeout, the 0.4 s
  load indicator delay and the waiting cap of 4 are set on `ViewRoot` and can be changed per project.
- **The queue and immediate opens can still meet.** Serialization is per layer; the queue handles
  cross-feature order ([ADR-004](ADR-004-Queue-Above-The-Navigator.md)).

**Enforced by tests**

- `LifecycleSerializationTests`: a close during `Opening` runs after the open; two and three opens on
  one layer run in call order while the first loads and while its transition runs; opens on different
  layers run concurrently; `CloseAll` withdraws waiting requests at call time.
- `LifecycleTerminateTests`: a terminate cause cuts the step, snaps before it finalizes and lets the
  next request run.
- `BuiltInTransitionTests` and the shared `TransitionConformance` check: every transition meets the
  contract.
- `RandomInvariantTests`: at most one operation runs per layer and a record has at most one running
  step, after every step of a seeded random run.

## Alternatives considered

### Interruptible transitions from the current pose

Rejected. The strongest alternative, and the common one. It puts a contract on every custom transition
(start from any pose, handle supersession at any frame) in exchange for a path that user input cannot
reach, because the shield is up from acceptance. The cost lands on every author of a transition; the
benefit is a shorter wait in a code-driven collision.

### A global lock across layers

Rejected. A popup that opens while a screen loads would wait for the screen, and the screen would
wait for the popup's load. Layers do not share list state, so they do not need to share a lock.

### Reject requests while busy by default

Rejected as the default. A tap handler that must not stack can ask for it per call with
`RejectWhenBusy`. As a global default it would drop opens that code issued in sequence on purpose,
for example a close followed by an open on the same frame. Accepting and serializing keeps calls
ordered, and the waiting cap bounds a runaway burst.

### Debounce at the call site

Rejected. It would hide a design problem in the caller and still need the layer to be consistent for
the calls that do get through.

## See also

- [Transitions](../../Transitions.md)
- [Navigation and back](../../Navigation-and-Back.md)
- [Architecture](../Architecture.md)
- [Troubleshooting](../../Troubleshooting.md)
