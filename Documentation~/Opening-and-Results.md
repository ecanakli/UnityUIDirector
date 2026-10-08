# Opening and results

[Back to index](index.md)

This page is about the code that asks for a view and learns how it went: `Open` against `ShowAsync`,
the options of a call, the handle you get back, reading a result (the value, the close reason, the
reject reason), who owns an open and what happens when the owner ends, and closing many views at
once. The model is in [Concepts](Concepts.md); the keys you pass are in
[Catalogs and keys](Catalogs-and-Keys.md).

---

## Open a view, or open it and wait

Every call takes a key, the arguments (not for a key without arguments), an **owner** token and
optional `OpenOptions`. There are two ways to call:

| You want | Call | Returns |
|---|---|---|
| To start a view and carry on | `Open(key, owner, options)` | A `ViewHandle`, at once |
| To start a view and wait for how it ended | `await ShowAsync(key, args, owner, options)` | A `ViewResult<TResult>` when the request ends |

`Open` suits a button that opens a screen. The Basic Usage sample's Home screen opens the library from
a button and does not care when it closes. The root owns the new screen, so it outlives the Home
screen that holds the button:

<!-- source: Samples~/BasicUsage/HomeView.cs -->
```csharp
// The owner is the root: a screen opened from a button outlives the screen that holds the button.
private void OpenLibrary() =>
    Navigator.Open(AppViews.Library, Navigator.RootToken, new OpenOptions { Source = "home.library" });
```

`ShowAsync` suits a question: open a popup, wait, read the answer. The settings screen asks for a
confirmation, and the answer decides what happens next:

<!-- source: Samples~/BasicUsage/SettingsView.cs -->
```csharp
private async UniTask ConfirmResetAsync(CancellationToken ct)
{
    // The owner is this screen's scope token: the popup cannot outlive the screen.
    var answer = await Navigator.ShowAsync(
        AppViews.Confirm,
        new ConfirmArgs("Reset settings", "All settings return to their defaults."),
        ct,
        new OpenOptions { Source = "settings.reset", RejectWhenBusy = true });

    // Refused at once instead of waiting behind a view that is still opening.
    if (answer.Rejected == RejectReason.Busy)
    {
        _status.text = "Busy, try again.";
        return;
    }

    // Back, backdrop and a closed screen all end with no value, so this line also guards the screen.
    if (!answer.HasValue || !answer.Value)
        return;

    _settings.ResetToDefaults();
    _status.text = "Settings were reset.";

    // The toast outlives this screen, so the root owns it.
    Navigator.Open(
        AppViews.Toast,
        new ToastArgs("Settings were reset."),
        Navigator.RootToken,
        new OpenOptions { Source = "settings.reset" });
}
```

Both forms accept the same keys. For a key without arguments (`NoArgs`) the extension methods
`Open(key, owner)` and `ShowAsync(key, owner)` fill in the empty arguments.

Both forms decide **at the call**, synchronously, whether the request is accepted. The call itself
never waits and never throws for anything that happens to the view. The only exceptions are usage
errors: a call off the main thread throws `InvalidOperationException`, and a null navigator in an
extension method throws `ArgumentNullException`. See [Threading](Threading.md).

---

## OpenOptions

`OpenOptions` is an optional struct. Leave it out and every field is its default.

<!-- signature -->
```csharp
public struct OpenOptions
{
    public string Source;
    public bool RejectWhenBusy;
    public int RequestId;
}
```

| Field | Default | What it does |
|---|---|---|
| `Source` | `null` | A free tag that travels with the request's events and diagnostics. Use one string per call site, such as `"home.library"`, so a log line or an analytics event names the code that asked. It has no effect on behavior. See [Events and analytics](Events-and-Analytics.md). |
| `RejectWhenBusy` | `false` | Refuse the call at once with `RejectReason.Busy` when the navigator is busy (an operation runs or pends on any layer, or an input hold exists) instead of queuing behind it. Use it in tap handlers. |
| `RequestId` | `0` | `0` allocates a fresh request id. A non-zero value must come from `INavigator.ReserveRequestId()`, so one id can follow a request from before it reaches the navigator. A queue does this for you; ordinary code leaves it at `0`. |

Every call also has two optional trailing parameters, `member` and `line`. The compiler fills them
with the calling method and line. The calling method names the request's shield hold in `Describe`
and the UIDirector window; the 30-second warning,
[UIDIRECTOR110](Troubleshooting.md#uidirector110), is for `HoldInput`. Do not pass them.

`RejectWhenBusy` is a per-call choice. Without it, a second open waits in its layer, in the order
requests were accepted, up to the waiting cap (4 per layer by default; the next request ends as
`Busy`, [UIDIRECTOR112](Troubleshooting.md#uidirector112)). A double tap on a button that opens a
popup therefore needs either `RejectWhenBusy`, or a layer that refuses the second request as a
duplicate, which a non-history layer does unless the row sets `Allow Multiple`.

---

## What happens at the call

The navigator runs these checks in order. The first one that fails ends the request as not shown:

1. The root was destroyed: `RootGone`.
2. The owner token is already cancelled: `OwnerEnded`.
3. No catalog row has the id: `UnknownKey` ([UIDIRECTOR101](Troubleshooting.md#uidirector101)).
4. The row names a layer the root does not have: `UnknownKey` ([UIDIRECTOR109](Troubleshooting.md#uidirector109)).
5. A [queue](Queue.md) presented the request and the row is on another layer than the queue's:
   `WrongLayer` ([UIDIRECTOR126](Troubleshooting.md#uidirector126)). Nothing is placed and the shield
   is not taken.
6. The id is already open or in flight on a layer that is not a history layer, and the row does not
   allow multiple instances: `Duplicate`.
7. The call set `RejectWhenBusy` and the navigator is busy: `Busy`.
8. The layer is a history layer, the id is already in its stack, the row does not set
   `Allow Multiple` and the key has no arguments: not refused and not new. You join the request that exists (see [Joining](#joining-an-open-view)).
9. The layer already holds the waiting cap of requests that have not started: `Busy`
   ([UIDIRECTOR112](Troubleshooting.md#uidirector112)).
10. The layer's sorting band has no free slot: `Busy` ([UIDIRECTOR115](Troubleshooting.md#uidirector115)).

"Already open" in checks 6 and 8 means a request whose end is not decided yet. A request whose close
or withdrawal is already latched does not count, and neither does a request that a newer reopen is
replacing. That includes the views a `CloseAll`, `CloseTo`, a return to an earlier screen or a reopen
in place is about to remove: a new open of their id is a new request that runs after the operation.

A request that passes is added to its layer in `Waiting`. If the layer shields input, the input
shield goes up in the same call. That is why a second tap in the same frame lands on the shield and
not on the button.

A refused request still has a request id and publishes a `NotShown` event at the call. `ShowAsync`
returns a result that says why. `Open` returns an **empty handle**, described below.

---

## The handle

`Open` returns a `ViewHandle`, a small read-only struct that refers to one request. It is cheap to
copy and keep. It is **not awaitable**.

| Member | Meaning |
|---|---|
| `RequestId` | The id of the request; `0` for an empty handle. |
| `ViewId` | The id of the requested view; `null` for an empty handle. |
| `Phase` | `Waiting`, `Loading`, `Opening`, `Open`, `Closing` or `Ended`. `Ended` for an empty handle. |
| `IsAlive` | `true` until the request has ended. |
| `Close()` | Closes the view, or withdraws the request if it has not started. Never throws. |
| `WaitForCloseAsync(CancellationToken waiter)` | Completes with a `ViewClosed` when the request ends. |
| `TryGetInstance(out GameObject)` | The live instance while the view is opening, open or closing. |

After the request ends the handle is **inert**: `Phase` stays `Ended`, `Close()` does nothing, and
`WaitForCloseAsync` returns the stored outcome at once. A handle is therefore safe to keep in a field;
check `IsAlive` before you rely on it.

`INavigator` also finds handles by id: `IsOpen(viewId)` is `true` while a request for the id is open
or in flight, `TryGetOpen(viewId, out handle)` gives its handle, and `TryGetTop(layer, out handle)`
gives the top request of a layer. They report a request until it has ended, including a view that a
`CloseTo` or `CloseAll` is closing, so a closing view still has a handle. They are not the duplicate
check: a new `Open` of that id already starts a new request, as
[What happens at the call](#what-happens-at-the-call) describes.

The sample's save button keeps a handle for a system overlay, does the work, and closes the overlay in
a `finally`, so no path leaves it up. Then it waits for the overlay's hide transition:

<!-- source: Samples~/BasicUsage/SettingsView.cs -->
```csharp
private async UniTask SaveAsync(CancellationToken ct)
{
    // The root owns the overlay, so the finally below is what guarantees it never stays up.
    var overlay = Navigator.Open(AppViews.Saving, Navigator.RootToken, new OpenOptions { Source = "settings.save" });

    try
    {
        await _settings.SaveAsync(ct);
    }
    finally
    {
        overlay.Close();
    }

    // Wait for the hide transition, so the status appears once the overlay is gone.
    var closed = await overlay.WaitForCloseAsync(ct);
    _status.text = closed.Reason == CloseReason.Code ? "Saved." : "Saved. The overlay ended early.";
}
```

**What `Close()` does depends on where the request is.** Before it started (`Waiting` or `Loading`)
the request is withdrawn at once and ends as not shown with `RejectReason.Cancelled`. From `Opening`
on, the view closes with `CloseReason.Code` and plays its hide transition; during `Opening` that
happens when the show transition has finished, because a started step is never interrupted. The first
cause that ends a request wins, so `Close()` after the view already closed itself changes nothing.

**`WaitForCloseAsync` has its own token.** Any number of callers can wait, and each call gets its own
completion. The call throws `OperationCanceledException` only when `waiter` is cancelled, and that
leaves the view alone. The owner of the request never cancels a waiter; see
[Owners](#owners-and-what-happens-when-one-ends).

**The empty handle.** A refused `Open` returns `default(ViewHandle)`. Its `Close()` does nothing and
its `WaitForCloseAsync` completes at once with `NotShown`, `RejectReason.Cancelled` and the detail
"The handle is empty." It does not say why the request was refused. When you need the reason, use
`ShowAsync`, or read the `NotShown` event ([Events and analytics](Events-and-Analytics.md)).

The view itself reaches its own request through `ViewContext.Handle`, and ends it with
`ViewContext.Complete(value)` or `ViewContext.Close()`; see [View authoring](View-Authoring.md).

---

## Reading a result

`ShowAsync` completes with a `ViewResult<TResult>`. It never throws for what happens to the view.

| Member | Meaning |
|---|---|
| `HasValue` | `true` only when the view was shown and closed itself with `Complete(value)`. |
| `Value` | The value; the default value when `HasValue` is `false`. |
| `TryGetValue(out TResult)` | Returns `HasValue` and writes the value. |
| `WasShown` | The view entered its show transition before the request ended. |
| `Reason` | A `CloseReason`: how a shown view ended, or `NotShown`. |
| `Rejected` | A `RejectReason`: why a request ended without being shown; `None` when it was shown. |
| `Detail` | Optional text with more about the outcome. |
| `RequestId` | The id of the request, the same id the events carry. |

**Read the answer with `TryGetValue`** when all you need is "did the user answer, and with what". The
rename popup's caller does exactly that:

<!-- source: Samples~/BasicUsage/ItemDetailView.cs -->
```csharp
// No value: the player went back, or the item cannot be renamed.
if (!result.TryGetValue(out var title)) return;
```

`HasValue` is `false` for every ending that is not `Complete(value)`: the popup was dismissed, the
back key closed it, the owner ended, it was refused. A close with `Complete(value)` after the view
was shown is the only way to get a value. For a view with no result (`NoResult`) `HasValue` is
`false` unless the view called `Complete`, so test `Reason` there.

### Close reasons

A view that was shown reports one of these in `Reason`. The first cause that ends a view is latched;
later causes are ignored.

| `CloseReason` | Cause |
|---|---|
| `Completed` | The view called `ViewContext.Complete(value)`. `HasValue` is `true`. |
| `Dismissed` | The view called `ViewContext.Close()`, or `ViewHost.Close()` ran from an Inspector event, such as a `ViewButton`. |
| `Back` | `Back()` closed it. |
| `Backdrop` | A tap on the backdrop of a row with `Close On Backdrop`. |
| `Code` | `ViewHandle.Close()`, `CloseTo` or `CloseAll`. |
| `Navigated` | A screen change ended it, or it was reopened in place. A request that was not shown yet reports `NotShown` and `Cancelled` instead. |
| `OwnerEnded` | The owner token ended while it was shown. |
| `Destroyed` | The instance was destroyed or deactivated outside the package, or the root was destroyed. |
| `NotShown` | The request never reached the screen; read `Rejected`. |

### Reject reasons

A request that never reached its show transition always reports `CloseReason.NotShown` and one of
these in `Rejected`:

| `RejectReason` | When |
|---|---|
| `UnknownKey` | No row has the id ([UIDIRECTOR101](Troubleshooting.md#uidirector101)), or the row names an unknown layer ([UIDIRECTOR109](Troubleshooting.md#uidirector109)). |
| `TypeMismatch` | The prefab root has no component for the key's types ([UIDIRECTOR102](Troubleshooting.md#uidirector102)) or no `ViewHost` ([UIDIRECTOR103](Troubleshooting.md#uidirector103)). |
| `Duplicate` | The id is already open or in flight on a layer that does not allow it. |
| `Busy` | `RejectWhenBusy` and the navigator was busy, the waiting cap was reached ([UIDIRECTOR112](Troubleshooting.md#uidirector112)), or the sorting band is full ([UIDIRECTOR115](Troubleshooting.md#uidirector115)). |
| `Denied` | A queue gate denied the request. See [Queue](Queue.md). |
| `LoadFailed` | The asset could not be loaded ([UIDIRECTOR104](Troubleshooting.md#uidirector104), [UIDIRECTOR120](Troubleshooting.md#uidirector120)). |
| `LoadTimedOut` | The load took longer than the load timeout ([UIDIRECTOR105](Troubleshooting.md#uidirector105)). |
| `Cancelled` | The request was withdrawn before it was shown: `ViewHandle.Close()`, `Back()`, `CloseAll`, a screen opening on a history layer (`ClosedByNavigation`), a `CloseTo`, a return or a reopen in place that popped past it while it had not started, a newer reopen that took its place, or the instance was lost. |
| `Declined` | The view called `Decline` in `OnOpen`. `Detail` carries its reason text. |
| `Faulted` | `OnOpen` threw ([UIDIRECTOR117](Troubleshooting.md#uidirector117)). |
| `OwnerEnded` | The owner ended, or was already cancelled, before the view was shown. |
| `RootGone` | The root no longer exists. A [queue](Queue.md) ends its waiting items this way when its navigator's root is destroyed. |
| `WrongLayer` | A queue presented the request, but the row is on another layer than the queue's ([UIDIRECTOR126](Troubleshooting.md#uidirector126)). See [Queue](Queue.md). |

`WaitForCloseAsync` completes with a `ViewClosed` (`WasShown`, `Reason`, `Rejected`, `Detail`).
Observers get the same facts in a `ViewEvent` whose `Kind` is `Closed` or `NotShown`. No arguments and
no value travel with either.

### Joining an open view

On a history layer, opening an id that is already in the stack with a key **without arguments** does
not create a second view, unless the row sets `Allow Multiple`. `ShowAsync` joins the existing request: `OnOpen` does not run again, you
complete when that request ends, and any view above it is popped with `CloseReason.Navigated`. `Open`
returns the existing handle. A key **with arguments** reopens the screen in place with the new
arguments instead; see [Navigation and back](Navigation-and-Back.md).

A joined `ShowAsync` that was made with other argument or result types gets the same outcome but not
the value: `HasValue` is `false`, and a view that completed with a value reports `Reason` as
`Completed` to such a joiner.

**A join does not bind the owner you pass.** The request keeps the owner it was opened with. If your
token ends, nothing happens to the request or to your wait: the `await` keeps waiting until the
request ends, and it completes with that request's own outcome and request id.

### Stopping the wait at your own token

`ShowAsync` never throws and has no wait token. To make an `await` stop when a token of your own ends,
wrap the call with UniTask's `AttachExternalCancellation` and `SuppressCancellationThrow`:

<!-- source: Tests/Editor/Navigation/NavigationHistoryTests.cs -->
```csharp
private static async UniTask<(bool IsCanceled, ViewResult<NoResult> Result)> ShowUntilStopped(
    UniTask<ViewResult<NoResult>> show,
    CancellationToken stop)
{
    return await show.AttachExternalCancellation(stop).SuppressCancellationThrow();
}
```

Call it as `ShowUntilStopped(navigator.ShowAsync(key, owner), stop)`. When `stop` ends, `IsCanceled` is
`true`, your code carries on, and a request you joined goes on without you. Other joiners and the
view are not affected.

Two traps:

- `IsCanceled` is `true` whenever your token ended, and the result is then `default`, not an outcome. If the same
  token is also the owner of a call that started a new request, the view ended too, as
  `CloseReason.OwnerEnded` or `RejectReason.OwnerEnded`, and the wrapped `await` reports a stop and not
  that result.
- If you need the outcome as well as the stop, pass a separate token as `stop` and a different token
  as the owner.

### Faking a result in tests

`ViewResult.Completed(value)`, `ViewResult.Closed<TResult>(reason)` and
`ViewResult.NotShown<TResult>(reason, detail)` are public factories, so a game can fake an
`INavigator` and return any outcome without a test assembly from the package.

Two combinations are refused with an `ArgumentException`, because they do not describe an outcome:

- `Closed<TResult>(CloseReason.NotShown)`: a closed result was shown. Use `NotShown` for a request
  that was not shown.
- `NotShown<TResult>(RejectReason.None, ...)`: a request that was not shown always has a reject
  reason.

`Closed<TResult>(CloseReason.Completed)` is allowed. It is a view that completed without a value,
which is what a joiner with other argument or result types receives; use `Completed(value)` for a view
that completed with a value. `default(ViewResult<T>)` is not an outcome the navigator returns.

---

## Owners and what happens when one ends

Every open takes a `CancellationToken`, the **owner**. It decides how long the request lives. The
sample passes two kinds; the table adds the third. Each is a deliberate answer to "what should this
view not outlive?":

| Owner | Where in the sample | Meaning |
|---|---|---|
| `Navigator.RootToken` | `HomeView`, the toast and the saving overlay | The view ends when the root is destroyed, and not before. |
| `ct`, the token of the screen's scope | The confirm popup in `SettingsView` | The popup ends with the screen that asked for it. |
| `CancellationToken.None` | Not used in the sample | Nothing ends it until someone closes it, the instance is lost, or the root is destroyed (in practice the same as `RootToken`). |

Two cases do not behave as the table suggests:

- **A call that joins an existing request ignores its owner.** A key without arguments that returns to
  a screen already on a history layer binds nothing: the request keeps its own owner. See
  [Joining](#joining-an-open-view).
- **A screen's own scope token is a poor owner for a view that replaces or pops that screen.** A
  reopen in place replaces the screen it reopens, and pops the screens above it. A token that ends
  with one of those screens ends the new request. Pass `Navigator.RootToken` there; see
  [Navigation and back](Navigation-and-Back.md#return-to-a-screen-that-is-already-open).

When the owner is cancelled:

| State of the request | What happens | Result |
|---|---|---|
| Not shown yet (`Waiting`, `Loading`) | Withdrawn at once; a running load is cancelled | `NotShown`, `RejectReason.OwnerEnded` |
| Shown (`Opening`, `Open`, `Closing`) | The view is **snapped** to its hidden pose, with no hide transition, and finished | `CloseReason.OwnerEnded` |
| Already cancelled at the call | Refused before anything is touched | `NotShown`, `RejectReason.OwnerEnded` |
| An outcome was already latched | The latched outcome stands | The latched reason |

Ending by owner is a **terminate**: it cuts a running step short and does not play the hide
transition. The two other terminate causes behave the same way: the instance being destroyed or
deactivated from outside the package, and the root being destroyed. A transition that exceeds the
transition timeout is cut and snapped to its end pose too ([UIDIRECTOR106](Troubleshooting.md#uidirector106)).
Every other close waits for the running step. `ViewHandle.Close()`, `Back()`, a backdrop tap,
`ViewContext.Close()` and `Complete` play the hide transition. `CloseTo` and `CloseAll` animate only
the topmost view and snap the rest. A screen change snaps the popups it ends
(`CloseReason.Navigated`).

**The owner never cancels an awaiter.** `ShowAsync` has no cancellation parameter, and when the owner
ends the awaiting code resumes with a result, not an exception: `CloseReason.OwnerEnded` for a shown
view, `RejectReason.OwnerEnded` for a request that was not shown yet. That
matters in `ConfirmResetAsync` above. Its owner is the screen's scope token; if the screen closes
first, the popup ends with `OwnerEnded`, the `await` returns, and `HasValue` is `false`. The line
`if (!answer.HasValue || !answer.Value) return;` is what stops the method from touching a screen that
is gone. After any `await` on a view that someone else can end, check the result before you use state
that the owner's end may have cleaned up.

**The await always completes, also after the owner is destroyed.** The method that awaits `ShowAsync`
resumes and the code after the `await` runs, even when the owner (for example the screen that opened
the popup) was destroyed first. A call that joined an existing request is the exception: its owner is
not bound, so it resumes only when that request ends, with that request's outcome (see
[Joining](#joining-an-open-view)). Otherwise, what it receives depends on the request's state when the
owner ended:

- The request was shown: `Reason` is `CloseReason.OwnerEnded`.
- The request was not shown yet, or the token was already cancelled at the call: `Rejected` is
  `RejectReason.OwnerEnded`.
- An outcome was latched before the owner ended (the user answered, the view was closed, the root was
  destroyed): that reason is reported instead, so `OwnerEnded` is not the only value to expect.

In every case code after the `await` runs on the main thread with no exception. Guard any access to a
destroyed object there: test the result first, as `ConfirmResetAsync` does, or check a Unity object
against `null` before you use it.

An owner token can be cancelled from any thread. The package handles it on the main thread and logs
[UIDIRECTOR113](Troubleshooting.md#uidirector113) when the cancellation arrived elsewhere. See
[Threading](Threading.md).

The same ending also happens to everything the view started. The view's scopes end with its request:
the bindings, tasks and cleanup actions it put on the open scope or the visible scope. The saving
overlay starts its spinner on the visible scope, so the spinner stops whenever the overlay is hidden,
closed or terminated, with no close line in the view:

<!-- source: Samples~/BasicUsage/SavingOverlay.cs -->
```csharp
protected override void OnVisible(ViewScope visible)
{
    // Per-span work goes on the visible scope, so the spinner stops whenever the overlay is hidden or closed.
    visible.Run(SpinAsync);
}

private async UniTask SpinAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        // Unscaled time keeps the spinner turning while the game is paused.
        _spinner.Rotate(0f, 0f, -DegreesPerSecond * Time.unscaledDeltaTime);

        // A cancelled yield throws, and the scope swallows it as the normal end of the loop.
        await UniTask.Yield(ct);
    }
}
```

**`ViewContext.Open` stays readable after the request ended.** A view that kept its
`ViewContext` can still read `context.Open` later. It returns the scope that ended with the request,
never `null`, and registering on an ended scope does the cleanup at once and adds nothing. A late
`Complete`, `Close` or `Decline` on the context does nothing and does not throw; the first outcome
stays.

---

## Closing many views

Closing one view is `handle.Close()`, or the view closing itself. Two calls close several.

### CloseTo

`INavigator.CloseTo(viewId)` and the extension `CloseTo(key)` pop every view above a screen on a
history layer and keep the screen. The item detail screen of the sample uses it to return to the
library:

<!-- source: Samples~/BasicUsage/ItemDetailView.cs -->
```csharp
// Pops everything above the library.
private void GoToLibrary() => Navigator.CloseTo(AppViews.Library);
```

- It returns `true` when a request with that id, whose close is not already decided, is in a history
  layer, also when no view sits above it and nothing needs to close. It returns `false` when the id
  is not there, the root is gone, or the call is off the main thread.
- The views above the target close with `CloseReason.Code`. The top one plays its hide transition,
  the ones between it and the target are snapped.
- The views to close are latched at the call, so a new `Open` of their ids starts a new request that
  runs after the pop. Requests above the target that have not started (`Waiting` or `Loading`) are
  withdrawn at the call as not shown with `RejectReason.Cancelled`. If the target ends before the pop
  runs, the views above it are still closed.
- Returning to the screen by opening its key without arguments pops the same views with
  `CloseReason.Navigated` instead of `Code`. Neither touches the popups layer. `ClosedByNavigation`
  runs only when a new screen opens or a screen is reopened in place with arguments. See
  [Navigation and back](Navigation-and-Back.md).

### CloseAll

`INavigator.CloseAll(layer)` empties one layer: it closes every view on it and withdraws every
request that is still waiting.

- **It withdraws waiting requests at once.** Requests in `Waiting` or `Loading` end immediately as
  not shown with `RejectReason.Cancelled`, and a running load is cancelled. They do not wait for the
  views that are on screen.
- What is on screen closes as one operation with `CloseReason.Code`. The topmost view plays its hide
  transition; every other view is snapped away. On a layer with `ShieldsInput` the input shield stays up from the call until the last view has closed.
- The views on screen are latched at the call. A new `Open` of one of their ids is a new request, not
  a join and not a `Duplicate`.
- Requests accepted after the call are not part of it. They open as usual once `CloseAll` has run.
- The operation runs in order on the layer, so a view that is still opening finishes its open first.
- On the wrong thread the call is posted to the main thread instead of throwing. An unknown layer
  does nothing.

Use the layer constants (`ViewLayers.Popups`) or your own `ViewLayer` value; layers are matched by id.

---

## Traps

- **An empty handle is not an error.** A refused `Open` hands back `default`. Test `IsAlive`, not
  `null`; the handle is a struct.
- **Do not treat `Open` as "it opened".** The request is only accepted. It can still be withdrawn,
  fail to load or decline. Read the outcome through `ShowAsync`, `WaitForCloseAsync` or an observer.
- **`HasValue` false is normal.** A popup that closes with Back, a backdrop tap or a dismiss carries no
  value. Decide what "no answer" means before you write the line that reads `Value`.
- **A terminate does not animate.** An owner ending, a destroyed view or a destroyed root snaps the
  view away. If a hide animation matters, close with `handle.Close()` or the view's own `Complete`
  before the owner ends.
- **A close during `Opening` is delayed.** It runs after the show transition, so a fast
  `Close()` is not instant. This is deliberate: a started step is never interrupted.
- **`CloseAll` is not synchronous.** The call returns at once, the waiting requests end at once, and
  the views on screen close over the following frames.
- **`RejectWhenBusy` is not a debounce.** It refuses while any layer is busy, including a transition on
  another layer. Use it where a refused tap is acceptable, as the sample does for the reset button.
- **A request owned by a screen's token ends with the screen.** That is the intent for a popup; for a
  toast or an overlay that must outlive the screen, pass `RootToken`, as the sample does.
- **A join is not bound to your token.** Opening a screen that is already in a history stack, with a
  key without arguments, waits for the existing request however your token ends. Wrap the `await` as
  shown in [Stopping the wait at your own token](#stopping-the-wait-at-your-own-token) when the
  caller must be able to leave.

---

## See also

- [Concepts](Concepts.md): requests, outcomes, owners and scopes in one picture
- [Catalogs and keys](Catalogs-and-Keys.md): the rows and keys a call names
- [Navigation and back](Navigation-and-Back.md): history stacks, `Back()`, returning to a screen
- [Popups and backdrop](Popups-and-Backdrop.md): modal popups, `Close On Backdrop`
- [System overlays](System-Overlays.md): blocking overlays like the saving overlay
- [View authoring](View-Authoring.md): `Complete`, `Close`, `Decline` and the scopes inside a view
- [Queue](Queue.md): presenting requests one at a time by priority
- [Events and analytics](Events-and-Analytics.md): observing `Requested`, `Shown`, `Closed`, `NotShown`
- [Threading](Threading.md): which calls are main-thread only
- [Troubleshooting](Troubleshooting.md): the diagnostics this page links
- [API reference](API-Reference.md): `INavigator`, `ViewHandle`, `ViewResult`, `OpenOptions`
- [ADR-001](Design/Decisions/ADR-001-Outcomes-As-Data.md) and
  [ADR-005](Design/Decisions/ADR-005-Token-Owners.md): why outcomes are data and owners are tokens
