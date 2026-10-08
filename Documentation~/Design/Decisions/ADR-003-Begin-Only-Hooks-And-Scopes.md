# ADR-003: Begin-only hooks and scopes

- **Status:** Accepted, implemented in 0.1.0
- **Related:** [Architecture section 8](../Architecture.md#8-back-resolution),
  [Architecture section 9](../Architecture.md#9-scopes),
  [ADR-005](ADR-005-Token-Owners.md), [View authoring](../../View-Authoring.md),
  [Navigation and back](../../Navigation-and-Back.md)

## Context

A view needs setup when it opens and cleanup when it ends. The usual shape is a pair of hooks:
`OnShow` and `OnHide`, with a subscription in the first and its removal in the second. Every such pair
is a line that someone has to remember to write, and nothing complains when the second half is
missing. A handler that stays subscribed to a long-lived publisher fires on a view that is gone, or
fires once per time the view was opened.

A "guaranteed close hook" does not fix this, because it cannot be guaranteed. A view's component can
be destroyed from outside before any hook runs. The owner of the request can end while the view is
shown. The root can be destroyed during a scene unload. In each case user code in the hook runs when
the objects it needs may already be gone, in an order that nobody chose.

There is also a span between "open" and "closed". A view that a fullscreen screen covers stays open,
and its animators, timers and listeners should stop and then start again when it is revealed. A single
open-to-close span does not describe that.

## Decision

**A view gets hooks that only begin something. Everything that has to end is registered on a scope
that the package ends.**

- **The hooks** are separate one-method interfaces on the view component:
  `IView<TArgs, TResult>.OnOpen(args, context)` (required), `IViewVisible.OnVisible(scope)` (optional)
  and `IViewBack.OnBack()` (optional, asked only for rows with `BackPolicy.HandledByView`). There is no
  `OnClose`, `OnHide` or `OnCovered`. `ViewBehaviour<TArgs, TResult>` is optional sugar over the same
  interfaces; a view can implement them directly.
- **The scopes.** The **open scope** is handed to `OnOpen` and ends when the request ends. A **visible
  scope** is handed to `OnVisible` for each span on screen and ends when the view is covered or the
  request ends. Per-open work (long-lived listening) goes on the open scope; per-span work (an
  animator, a timer) goes on the visible scope.
- **What a scope does.** `Bind` adds a listener to a `UnityEvent` now and removes it at the end.
  `Subscribe(add, remove, handler)` does the same for any event. `Add(IDisposable)`, `OnEnd` and
  `OnEnd<TState>` register cleanup. `Run` starts a task on the scope's token, swallows cancellation
  and routes any other exception. The registration call takes its cleanup in the same call, so there
  is no removal line to write.
- **Exactly once, on every path.** A scope's end routine runs once: the token is cancelled first, then
  the entries run newest first, each isolated so that a throw is routed and the rest still run. It runs
  when the view closes, when it was withdrawn before it was shown, when `OnOpen` declined or threw,
  when the owner ended, when the instance was lost from outside and when the root was destroyed. It
  never depends on the view component being alive.
- **Single-use and inert afterwards.** A registration on an ended scope is cleaned up at once: the
  listener is not added, the disposable is disposed, the action runs, the task does not start.
  `ViewContext.Open` after the end returns the ended scope, never `null`. A stale reference is
  harmless.
- **The package owns the scope.** User code cannot end or restart it.
- **Closing is not vetoed.** A code close (`ViewHandle.Close`, `CloseTo`, `CloseAll`), the owner
  ending and a loss from outside always take effect. Back is the one request that a view may answer:
  a row's `BackPolicy` (`LayerDefault`, `Close`, `Ignore`, `HandledByView`) and the optional
  `IViewBack.OnBack()`. A hook that returns `false` or throws lets the package close the view.
- **A minimal scope of the package's own.** `ViewScope` is small and single-use. The package does not
  depend on a general lifetime library to provide it.

## Consequences

**Positive**

- A forgotten removal line cannot exist. Cleanup is part of the call that registers the work.
- Cleanup does not need the view to be alive. It runs for a destroyed component, an ended owner and a
  torn-down root, because the package runs it, not the view.
- A covered view stops its per-span work and gets it back when revealed, without any code in the view
  that tracks being covered.
- A throwing cleanup cannot stop the others, and cannot stop the request from ending.

**Negative / accepted costs**

- **No code runs "when the view closes" except cleanup actions.** `OnEnd` actions run inside the
  terminate routine, where state is already consistent but the instance may be gone. They must not
  await and must not assume the view object is alive. Work that has to happen before the view goes
  away (saving a form) belongs before `Complete`.
- **Two scopes to learn.** Which work belongs on which scope is part of the guide.
- **A scope allocates.** An open has an open scope, and each span on screen has a visible scope. The
  token source of a scope is created lazily, so a scope that registers no task and reads no token
  creates none.
- **A view cannot veto a code close.** A project that wants a confirmation before closing asks for it
  in its own code before it calls `Close`, or uses `HandledByView` for the back key.
- **A scope cannot be used as a general lifetime** by user code: it cannot be ended by the user and it
  is not reusable.

**Enforced by tests**

- `ScopeContractTests`: bind, subscribe, add, end and run semantics, duplicates, an ended scope,
  ordering (token first, entries newest first), isolation of throwing cleanups and the errors that are
  routed.
- `ScopeOnNavigatorContractTests`: both scopes end exactly once on every close path, including a
  declined and a throwing `OnOpen`; the visible scope ends before the open scope and both end before
  the instance is released; a context stays readable after the end.
- `ReentrancyContractTests`: a navigator call from a hook or a cleanup is legal; `Complete` or `Close`
  inside `OnOpen` is latched and reported with `UIDIRECTOR118`.
- `NavigationBackTests`: `BackPolicy` and the `OnBack` hook.

## Alternatives considered

### Paired open and close hooks

Rejected. Each pair is a line that can be forgotten, and the second half cannot be guaranteed to run
on a destroyed component, during a root teardown or after the owner ended. A package that promises a
close hook promises something it cannot keep.

### A close veto: `CanClose(reason)`

Rejected. The strongest alternative to the back policy. An unscoped veto lets a view survive a code
close with its services already gone. A policy enum plus `IViewBack` covers the real need (the back
key may mean something to a view) and leaves every other close unconditional.

### A mandatory base class

Rejected. A view that has to derive from a package class cannot derive from the project's own. The
hooks are interfaces on the view component, and the base class is optional.

### One scope for the whole life of a view

Rejected. A view that stays open while it is covered would keep its animators and timers running under
a fullscreen screen. The visible scope is the span that stops when the view stops being seen.

### A hard dependency on a general lifetime library for the scope

Rejected. The package must stand alone, and the scope it needs is a small object: a token, a list of
entries and one end routine. A dependency would also bind the package's release cycle to another
project's.

### User-owned scopes

Rejected. A scope that user code can end or reuse can end while the view is still open, or be reused
for a second view, and the guarantee "ends exactly once, on every path" would no longer hold.

## See also

- [View authoring](../../View-Authoring.md)
- [Navigation and back](../../Navigation-and-Back.md)
- [Concepts](../../Concepts.md)
- [Architecture](../Architecture.md)
