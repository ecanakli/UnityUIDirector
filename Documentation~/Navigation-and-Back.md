# Navigation and back

[Back to index](index.md)

Screens live on a history layer: a stack that is also the back history. This page covers opening a
screen forward, returning to a screen that is already open, `CloseTo`, the layer rule
`ClosedByNavigation`, `Back()` and the order in which it decides, the `BackPolicy` values, the
`BackInput` component, and what to do in a project that still has an older UI. Popups and overlays
have their own pages: [Popups and backdrop](Popups-and-Backdrop.md) and
[System overlays](System-Overlays.md).

The examples come from the Basic Usage sample. Its catalog has `home`, `library`, `item-detail` and
`settings` on the `screens` layer, so they stack up in the order they are opened.

---

## Open a screen forward

Open the next screen with `Open` and let the root own it:

<!-- source: Samples~/BasicUsage/LibraryView.cs -->
```csharp
private void OpenItem(string itemId) =>
    Navigator.Open(AppViews.ItemDetail, new ItemArgs(itemId), Navigator.RootToken,
        new OpenOptions { Source = "library.item" });
```

`Open` returns a `ViewHandle` at once and does not wait. Use `ShowAsync` when the caller needs the
outcome; see [Opening and results](Opening-and-Results.md). The third argument is the owner token.
`Navigator.RootToken` means "as long as the UI exists", so the detail screen is not tied to the
library screen that asked for it and stays open until something closes it.

What happens on the `screens` layer:

1. The request is accepted at the call, in the order calls arrive, and takes the input shield. Input
   is blocked from that moment, in the same call.
2. The view loads, its `OnOpen` runs, and the show transition plays.
3. The screen below is covered: it is hidden once the new screen is fully open, but it stays loaded
   with its instance and its open scope. Only its visible scope ends.

The layer's rules are `History`, `TakesBack` and `ShieldsInput`. A history layer covers what is below
a view by default, so no screen needs to hide the previous one itself.

---

## Return to a screen that is already open

Opening an id that is already in the stack does not create a second screen. It returns to the one that
exists. `ItemDetailView` does this in two ways:

<!-- source: Samples~/BasicUsage/ItemDetailView.cs -->
```csharp
// Home is already in the stack: this returns to it, and Home's OnOpen does not run again.
private void GoHome() =>
    Navigator.Open(AppViews.Home, Navigator.RootToken, new OpenOptions { Source = "item.home" });

// Pops everything above the library.
private void GoToLibrary() => Navigator.CloseTo(AppViews.Library);
```

What runs and what does not, when you open an id that is already on a history layer and its key takes
no arguments (`NoArgs`):

| | Behaviour |
|---|---|
| New request | None. `Open` returns the existing handle with the existing request id; `ShowAsync` joins the existing request and completes with its outcome. No `Requested` event is published for the second call. |
| `OnOpen` of the target | Does not run again. The open scope is the same one; nothing is rebound. |
| `OnVisible` of the target | Runs again once the target is revealed, with a new visible scope. |
| Screens above the target | Popped. The top one plays its hide transition, the rest are snapped away. Each ends with `CloseReason.Navigated`. |
| Requests above the target that have not started | Withdrawn at the call as not shown with `RejectReason.Cancelled`. They are not shown and then popped. |
| The shield | Up from the call until the last popped view has ended. Each popped view holds it until it ends. |
| `ClosedByNavigation` | Does not run. A return is not an open. |
| The target is already on top | Nothing happens: no transition, no pop. |
| The target is still loading | The caller joins it. A second load is not started. |
| The owner token you pass | Ignored. The existing request keeps its own owner, and the call does not bind yours. |

**The views a pop removes stop counting as open at the call.** A view that a return, `CloseTo`,
`CloseAll` or a reopen in place is about to remove does not count as already open for a new `Open`
(the duplicate check and the return-to-existing check). `Open(Shop)`, `CloseTo(Home)`, `Open(Shop)`
therefore ends with a second `Shop` that opens after the pop. It does not join the first one, which
is on its way out, and a non-history layer does not refuse it as `Duplicate`. `IsOpen` and
`TryGetOpen` are different: they keep reporting a view until its request has ended, so a closing view
still has a handle. If the target of the pop ends before the pop runs, the views above it are still
popped.

Because `OnOpen` does not run again, anything that must be fresh each time the screen comes back
belongs in `OnVisible`. `LibraryView` refreshes its count there:

<!-- source: Samples~/BasicUsage/LibraryView.cs -->
```csharp
// Runs on the first show and again each time this screen is revealed.
protected override void OnVisible(ViewScope visible) => _count.text = $"{_library.Count} items";
```

**A key with arguments reopens in place.** When the key carries arguments, for example
`ViewKey<ItemArgs, NoResult>`, opening an id that is in the stack reuses the instance instead. The
old request ends with `Navigated`, a new request takes its list position and slot, `OnOpen` runs
again with the new arguments on a fresh open scope, and the screen snaps to shown with no transition.
Screens above it are popped first. Back from the reopened screen goes to the screen below it; the
earlier arguments are not kept.

When a screen sits above the one being reopened, the order is:

1. At the call, the views above are latched to close, and requests above that have not started are
   withdrawn as not shown with `RejectReason.Cancelled`.
2. The old request is not latched. It stays where it is, covered and not interactive, and gets no
   `OnVisible`, while the screens above are popped. `Describe` marks it as `being replaced`.
3. When the runner reaches the new open, the old request ends with `Navigated` and the new one takes
   its place.

If the new request is withdrawn before its open runs (Back withdraws it, or its owner ends), the old
screen stays: it is open, visible and interactive again, and a new visible scope begins for it. The
screens above it stay popped. A `CloseAll` of the layer closes the old screen too, with `Code`, and
the new request ends as not shown with `RejectReason.Cancelled`. Destroying the root ends both. If the
old request itself ends during the wait, it ends with its own reason and not `Navigated`: a close through its handle ends it with
`Code` and the new request still takes over the instance; its owner ending or the loss of its
instance ends it with `OwnerEnded` or `Destroyed`, and the new request opens on a fresh instance.

Reopening again while the first reopen still waits for its open ends the first one as not shown with
`RejectReason.Cancelled`. The newest request takes over the original screen, and that screen stays
open if the newest request ends before its open runs.

**Pass `Navigator.RootToken` as the owner of a reopen.** A token that ends with the screen the reopen
replaces or pops, such as that screen's open-scope token, ends the new request as not shown with
`RejectReason.OwnerEnded`, so the new arguments never appear. See
[Owners](Opening-and-Results.md#owners-and-what-happens-when-one-ends).

**A row with `AllowMultiple` is the exception.** It stacks a second instance instead of returning to
the first. See [Catalogs and keys](Catalogs-and-Keys.md).

---

## CloseTo

`CloseTo` pops everything above a screen and keeps the screen:

<!-- signature -->
```csharp
bool CloseTo(string viewId);
public static bool CloseTo<TArgs, TResult>(this INavigator navigator, ViewKey<TArgs, TResult> key);
```

The first form is on `INavigator`, the second is an extension that takes the key. `ItemDetailView`
calls it in the snippet above with `AppViews.Library`.

- Every popped screen ends with `CloseReason.Code`. Their `ShowAsync` awaiters complete with that
  reason and no value.
- The views to pop are latched at the call, so a new `Open` of their ids does not join them (see
  [the table above](#return-to-a-screen-that-is-already-open)). Requests above the target that have
  not started are withdrawn at the call with `RejectReason.Cancelled`. A reopen in place that has not
  run yet is one of them; when it already holds the screen's instance, that screen is removed without
  a hide transition.
- The top screen plays its hide transition and every screen between it and the target is snapped
  away. The input shield stays up from the call until the last of them has ended.
- It returns `true` when a request with that id, whose close is not already decided, is in a history
  layer, also when that screen is already the top and nothing is popped. It returns `false` when the
  id is unknown or is not on a history layer. A popup id always gives `false`.
- Like `Back()`, it returns `false` and does nothing when called off the main thread
  ([Threading](Threading.md)).

Use `CloseTo` when you want to leave a flow and you know where it started. Use an `Open` of the same
id when you also want the call to read as "go to this screen". For a key without arguments the two
do the same pop and differ in the close reason: `Navigated` for the return through `Open`, `Code`
for `CloseTo`.

---

## ClosedByNavigation

`ClosedByNavigation` is a layer rule. A view on a layer that has it is closed when a view opens on a
history layer. The default `popups` layer has it; `screens` and `system` do not.

| Layer | Order | Rules |
|---|---|---|
| `screens` | 0 | `History`, `TakesBack`, `ShieldsInput` |
| `popups` | 1 | `Modal`, `TakesBack`, `ShieldsInput`, `ClosedByNavigation` |
| `system` | 2 | `Modal`, `ShieldsInput` |

The exact rule:

- It runs when a screen is opened on a history layer: after that screen's `OnOpen` has returned and
  before its show transition starts. A reopen in place counts as an open.
- It closes only the views that were accepted earlier than the screen that was opened. A popup that
  the new screen opens itself, even inside its own `OnOpen`, was accepted later and survives.
- It does not run on a pop, on `Back()`, on `CloseTo`, or on a return to a screen that is already
  open.
- It does not run when the screen fails to open: a decline, an exception in `OnOpen`, a failed load
  and a type mismatch all leave the popups alone.
- The views are closed by snap, with no hide transition, and end with `CloseReason.Navigated`. A
  popup that was still loading or waiting is withdrawn as not shown with `RejectReason.Cancelled`.
- Views on layers without the rule, `system` and any layer you add without it, are never touched.

A popup that is open when the player taps a button that opens another screen therefore disappears
with that screen change, and the code that awaited it resumes with `Navigated` and no value. That is
the usual reason an await returns without an answer; see [Popups and backdrop](Popups-and-Backdrop.md).

To keep a view across screen changes, put it on a layer without the rule. The Basic Usage sample's
`toasts` layer is declared with `ViewLayerRules.None`, so a toast neither blocks input nor closes
when a screen opens; layers are added through `ViewRootSetup.ExtraLayers`
([Root canvas](Root-Canvas.md)).

---

## Back()

`INavigator.Back()` is the single back rule. It returns `true` when something handled the press and
`false` when nothing did, so the game decides:

<!-- source: Samples~/BasicUsage/LibraryView.cs -->
```csharp
// The other screens use a ViewButton set to Back; this one shows the call.
private void GoBack() => Navigator.Back();
```

`ViewButton` is the no-code entry point: put it on a button and set its action to `Back`, `CloseView`
or `OpenView`. `Back` calls `Back()` on the navigator of the root above the button. A click on a
`Back` or `OpenView` button before that root is initialised logs a warning and does nothing; the
click does not initialise the root. `CloseView` does not use the root and is not affected.

### Resolution order

`Back()` checks these conditions in order and stops at the first that matches:

| # | Condition | Result |
|---|---|---|
| 1 | The root was destroyed, or the call is off the main thread | `false` |
| 2 | A request is still `Waiting` or `Loading` on a layer that takes back, and its row policy is not `Ignore` | The newest such request, by the order it was accepted in, is withdrawn as not shown with `RejectReason.Cancelled`. `true` |
| 3 | The input shield is up | Swallowed. `true` |
| 4 | Going from the highest layer down, the first layer that is `Modal` or `TakesBack` and has a view on screen. Its top view is the target | See below |
| 5 | Nothing matched | `false` |

Step 2 comes before step 3 on purpose. A request that is still loading holds the shield, so without
the order a back press could never cancel a screen that is slow to load.

For the target in step 4:

- A target that is still opening or already closing swallows the press. Back waits for it, so a
  double press closes one screen, not two. `true`.
- On a layer that does not take back, which is the `system` layer: a row with `HandledByView` asks
  the view, and every other policy swallows. `true` either way. See
  [System overlays](System-Overlays.md).
- `BackPolicy.Ignore`: swallowed. `true`.
- `BackPolicy.Close` and `LayerDefault` on a layer that takes back: if the target is the bottom view
  of a history layer, the result is `false`, because the bottom view is the root of the history and
  the game decides. Any other target closes with `CloseReason.Back` and the result is `true`.
- `BackPolicy.HandledByView`: the view is asked. `true` from the view ends it. `false` continues as
  `LayerDefault` does.

A layer that is neither modal nor takes back is skipped, so a plain layer such as `toasts` never
swallows a press. Popups are above screens, so a press closes the open popup before it touches any
screen. The only popup of its layer is closed too, although it is the bottom view of that layer; the
root rule applies to history layers only.

### The bottom screen

On `home`, the bottom screen of the stack, `Back()` returns `false` and leaves the screen open. That
is the cue for a quit confirm or a "press again to exit" rule. Handle it where the press arrives; see
[BackInput](#backinput) below.

---

## BackPolicy

`BackPolicy` is a field of every catalog row. It decides what `Back()` does when that row's view is
the target. Set it in the row's `Back` field in the catalog Inspector; in the Basic Usage catalog the
`rename` row has `Back = HandledByView`. See [Catalogs and keys](Catalogs-and-Keys.md).

| Value | Effect |
|---|---|
| `LayerDefault` | Follow the layer: close on a layer that takes back. |
| `Close` | Close the view with `CloseReason.Back`. On the bottom view of a history layer it still returns `false`. |
| `Ignore` | Swallow the press and keep the view open. A pending request with this policy is not withdrawn by Back either. |
| `HandledByView` | Ask the view through `IViewBack.OnBack()` first. |

`BackPolicy` is a policy and not a veto. A view can ask to handle a back press, but code that closes
a view is never stopped: `ViewHandle.Close()`, `CloseTo`, `CloseAll`, and the owner token ending all
close the view whatever its policy says.

### HandledByView

A row with `HandledByView` calls `IViewBack.OnBack()` on the view when it is the target. Return
`true` when the view handled the press and `false` to let the layer's rule apply, exactly as with
`LayerDefault`. `RenameView` in the Basic Usage sample asks the player to confirm a discard:

<!-- source: Samples~/BasicUsage/RenameView.cs -->
```csharp
public bool OnBack()
{
    if (!_edited || _discardHintShown)
        return false;

    _discardHintShown = true;
    _hint.text = "Press back again to discard the change.";
    return true;
}
```

The first press after an edit shows the hint and returns `true`, so the popup stays open. The second
press returns `false` and the popup closes with `CloseReason.Back`. A press with no edit returns
`false` at once.

<!-- signature -->
```csharp
public interface IViewBack
{
    bool OnBack();
}
```

A view that derives from `ViewBehaviour` overrides `protected virtual bool OnBack()` instead; the
base class already implements `IViewBack`. A view written on the interfaces implements `IViewBack`
itself, as `RenameView` does.

Traps:

- The package asks only when the row says `HandledByView`. A view that implements `IViewBack` on a
  row with another policy is never asked.
- A hook that throws is routed to the error handler and counts as not handled.
- On a layer that does not take back the answer is swallowed in both cases. `BackPolicy.Close` on
  such a layer has no effect and the catalog validator reports it as
  [UIDIRECTOR122](Troubleshooting.md#uidirector122).

---

## BackInput

`BackInput` is an optional component. It polls the back key and calls `Back()` on its root's
navigator. When `Back()` returns `false` it raises its `Unhandled` event.

- The `GameObject > UI > UIDirector Root` menu adds it to the root. Without the menu, add it to the
  root object or to a child, or set its `Root` field. With no `ViewRoot` found it logs a warning and
  raises `Unhandled`.
- It does nothing until its root is initialised: no `Back()`, no `Unhandled`, and the press does not
  initialise the root. Initialise the root from your bootstrap before the first frame in which a
  press can arrive.
- It reads the Escape key and nothing else; [Input and platform](Input-and-Platform.md) covers
  Android's back button and the other platform notes. It uses the Input System package when that
  package is installed and active, through its own `InputAction` on every keyboard, so a press whose
  down and up land in one input update still counts. Otherwise it uses the legacy input manager, and
  it does nothing when neither is enabled.
- One per object. An exception thrown by `Back()` or by an `Unhandled` listener is logged and does
  not propagate.

`Unhandled` is how the game answers a back press on the bottom screen. The Basic Usage bootstrap
listens to it:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
// No removal needed: the bootstrap and the root live and die together.
_backInput.Unhandled.AddListener(OnBackUnhandled);
```

and asks to quit with a confirm popup:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
        private void OnBackUnhandled()
        {
            // A Unity event cannot await; the quit answer is read inside.
            ConfirmQuitAsync().Forget();
        }

        private async UniTaskVoid ConfirmQuitAsync()
        {
            var answer = await _navigator.ShowAsync(
                AppViews.Confirm,
                new ConfirmArgs("Quit", "Close the application?"),
                _navigator.RootToken,
                new OpenOptions { Source = "back.root" });

            if (!answer.HasValue || !answer.Value)
                return;

            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
```

The owner is the root token because no screen owns this question. Pressing Escape again while the
confirm is open closes it with `Back`, which has no value, so the app keeps running.

`Unhandled` is raised only when `Back()` returns `false`. It is not raised while an overlay is up,
while the shield is up, or while the back target is in its show or hide transition, because `Back()`
returns `true` in those cases ([System overlays](System-Overlays.md)).

---

## A project that still has an older UI

A project that is moving to UIDirector often keeps screens and popups from another UI system that the
package does not run. A back press must reach that UI first, and only then the navigator.
`BackInput` has no hook for asking another UI before it calls `Back()`.

Replace it. Remove `BackInput` from the root and put your own component there, one that polls the
key, asks the older UI, and calls `Back()` when the older UI did not use the press. The Basic Usage
sample ships that component as `HardwareBackRouter`. Its handler is:

<!-- source: Samples~/BasicUsage/HardwareBackRouter.cs -->
```csharp
public void HandleBack()
{
    // The older UI sits above the package's layers, so it answers first.
    if (_legacyPopup.TryClose())
        return;

    if (!_root.Navigator.Back())
        _unhandled.Invoke();
}
```

`HardwareBackRouter.cs` holds the whole component: the serialized root, a serialized reference to the
stand-in `LegacyPopup` (`Samples~/BasicUsage/Support/LegacyPopup.cs`), its own `Unhandled` event, and
an `Update` that polls Escape the way `BackInput` does.
In your project, replace `LegacyPopup.TryClose()` with a call into your older UI that returns `true`
when it closed something. `HardwareBackRouter` is not wired into the sample scene; the scene uses
`BackInput`.

- **Never both.** `BackInput` does not ask the older UI, so with both on the root one press is handled
  twice: a press that closed an older popup also closes the top package view, or raises
  `BackInput.Unhandled` on the bottom screen.
- **Order follows the stacking.** The snippet asks the older UI first because it is drawn above the
  package's layers. If the older UI is drawn below, call `Back()` first and ask the older UI when
  `Back()` returns `false`.
- **Quit logic stays in the router.** Its `Unhandled` event plays the role of `BackInput.Unhandled`.
- **Main thread only.** Call `Back()` from `Update` or a Unity event, not from a worker thread.

---

## Traps

- **`OnOpen` does not run again on a return.** Work that must repeat when the screen comes back goes
  in `OnVisible`.
- **A popup disappears when a screen opens.** `ClosedByNavigation` snaps popups accepted earlier; the
  await returns `Navigated` with no value.
- **`Back()` on the bottom screen returns `false`.** Nothing closes. Decide in `Unhandled`.
- **Back is swallowed while the shield is up and while the back target is in its show or hide
  transition.** A press during a hide does nothing, and a press during a hold made with `HoldInput`
  returns `true`. A transition on a plain layer such as `toasts` does not affect back. See
  [Input and platform](Input-and-Platform.md).
- **Back cancels a screen that is still loading.** The newest waiting or loading request is withdrawn
  before anything else is considered.
- **`Close` on the bottom screen does nothing.** The policy keeps the root open. Handle the press in
  `Unhandled`.
- **A return ignores your owner token.** `Open` or `ShowAsync` that returns to a screen already in the
  stack joins its request and does not bind the token you pass. To stop waiting at a token of your
  own, see [Opening and results](Opening-and-Results.md#stopping-the-wait-at-your-own-token).
- **A reopen needs an owner that outlives the screens it pops.** Pass `Navigator.RootToken`, not the
  open-scope token of the screen being replaced or popped.
- **`IsOpen` is not the duplicate check.** It stays `true` for a view that a pop is closing, while a
  new `Open` of the same id already starts a new request.

## See also

- [Opening and results](Opening-and-Results.md)
- [Popups and backdrop](Popups-and-Backdrop.md)
- [System overlays](System-Overlays.md)
- [Catalogs and keys](Catalogs-and-Keys.md)
- [View authoring](View-Authoring.md)
- [Input and platform](Input-and-Platform.md)
- [Root canvas](Root-Canvas.md)
- [Limitations](Limitations.md)
- [Troubleshooting](Troubleshooting.md)
- [Architecture: back resolution](Design/Architecture.md#8-back-resolution)
