# Popups and backdrop

[Back to index](index.md)

A popup is a view on the `popups` layer that opens over a screen and usually answers a question. This
page shows how to open one and await its answer, what the shared backdrop does, how a tap on it
closes a popup and what the awaiter sees, why a double click lands on the input shield, how
`RejectWhenBusy` refuses a request while anything is running, and how a confirm popup completes
with `true` or `false`.

The examples come from the Basic Usage sample: the settings screen opens a confirm popup before it
resets the settings.

---

## Open a popup and await its answer

A screen opens a popup with `ShowAsync` and reads the result. The click handler starts the work on
the screen's open scope, so the work ends with the screen:

<!-- source: Samples~/BasicUsage/SettingsView.cs -->
```csharp
private void OnResetClicked()
{
    OpenScope.Run(ConfirmResetAsync);
}
```

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
```

`ShowAsync` never throws for anything that happens to the view. It completes with a
`ViewResult<bool>` that says what became of the request:

| Field | Meaning |
|---|---|
| `HasValue` | `true` only when the popup closed with `Complete(value)` |
| `Value` | The answer; `default` when `HasValue` is `false` |
| `WasShown` | `true` when the popup reached the open state before it ended |
| `Reason` | How it closed: `Completed`, `Dismissed`, `Back`, `Backdrop`, `Code`, `Navigated`, `OwnerEnded`, `Destroyed`; `NotShown` when it never appeared |
| `Rejected` | Why it never appeared, for example `Busy`, `Duplicate`, `Declined`; `None` when it was shown |

The owner token is the second-to-last argument. It decides the life of the popup, not of the
await. When the owner ends, the popup closes with `OwnerEnded`; the awaiter still completes, with a
result. Here the owner is the settings screen's scope token, so the popup closes if the screen does.
Pass `Navigator.RootToken` for a question no screen owns, such as the quit confirm in
[Navigation and back](Navigation-and-Back.md#backinput). The full contract of `ShowAsync`, `Open`,
owners and results is in [Opening and results](Opening-and-Results.md).

### How a popup can end, and what the awaiter sees

| Cause | `Reason` | `HasValue` |
|---|---|---|
| The view calls `Complete(value)` | `Completed` | `true` |
| The view calls `Close()` | `Dismissed` | `false` |
| `Back()` | `Back` | `false` |
| A tap on the backdrop | `Backdrop` | `false` |
| `ViewHandle.Close()`, `CloseAll(ViewLayers.Popups)` | `Code` | `false` |
| A screen opened after the popup | `Navigated` | `false` |
| The owner token ended | `OwnerEnded` | `false` |
| The root was destroyed | `Destroyed` | `false` |

The line `if (!answer.HasValue || !answer.Value) return;` is the safe reading of every row except the
first: anything that is not an explicit `true` counts as "no". Code that must tell the causes apart
reads `answer.Reason`.

A popup that never appears has `WasShown == false`, `Reason == NotShown` and a `Rejected` value; the
values are listed in [Opening and results](Opening-and-Results.md#reject-reasons). `UnknownKey`,
`TypeMismatch`, `WrongLayer`, `LoadFailed` and `Faulted` are setup mistakes: fix the key, the row
(its layer), the prefab or asset provider, or the view's `OnOpen`. The others (for example `Busy`,
`Duplicate`, `Declined`, `OwnerEnded`, `Cancelled`) are events of normal running that the calling
code handles. The view itself can refuse inside `OnOpen`
with `Decline(reason)`; the caller then sees `Declined` and `Detail` carries the text. Calling `Complete` or `Close` inside `OnOpen` does not refuse the open: the
view opens and closes at once ([UIDIRECTOR118](Troubleshooting.md#uidirector118)).

---

## A confirm popup that completes with true or false

`ConfirmView` is a popup written on the base class, with typed arguments and a `bool` result:

<!-- source: Samples~/BasicUsage/ConfirmView.cs -->
```csharp
public sealed class ConfirmView : ViewBehaviour<ConfirmArgs, bool>
{
    [SerializeField] private TMP_Text _title;
    [SerializeField] private TMP_Text _message;
    [SerializeField] private Button _accept;
    [SerializeField] private Button _decline;

    protected override void OnOpen(ConfirmArgs args, ViewScope open)
    {
        _title.text = args.Title;
        _message.text = args.Message;

        // The package removes both listeners when this open ends.
        open.Bind(_accept.onClick, Accept);
        open.Bind(_decline.onClick, Refuse);
    }

    private void Accept()
    {
        Complete(true);
    }

    private void Refuse()
    {
        Complete(false);
    }
}
```

The button handlers call `Complete`, which closes the popup with a value. The first close cause
wins: a second click, a press of back, or a tap on the backdrop after `Complete` changes nothing.
Both listeners are bound on the open scope, so there is no `OnDisable` and no unsubscribe line.

The key ties the types together, so the call site is checked by the compiler: `AppViews.Confirm` is a
`ViewKey<ConfirmArgs, bool>`, and `ShowAsync` returns `ViewResult<bool>`. See
[Catalogs and keys](Catalogs-and-Keys.md).

Two things make a confirm popup safe to read with a single line:

- Pressing back closes it through the layer rule with `Back`, and a tap on the backdrop closes it with
  `Backdrop` when its row allows that. Neither has a value, so both read as "no".
- When the screen that owns it goes away, the popup ends with `OwnerEnded` and the same line returns
  before anything is changed.

---

## The backdrop

Every modal layer has one shared backdrop. `popups` and `system` are modal; a layer you add with
`ViewLayerRules.Modal` gets one too.

- It sits directly under the layer's top view that is on screen, and shows while that view is not
  hidden. With two popups stacked, there is still one backdrop; it jumps up under the newest.
- It fades in and out over 0.2 seconds of unscaled time, so it works while the game is paused. Below
  30 fps one frame advances at most 1/30 s, so the fade then takes longer ([Transitions](Transitions.md)).
  It stops taking pointer input at once when it hides, and its canvas is turned off when the fade ends. An idle
  backdrop costs nothing.
- Everything below the layer's top view is not interactive: the screens underneath cannot be clicked
  even where the backdrop is transparent. They stay visible, because a popup does not cover what is
  below it.
- Its look is the root's backdrop colour (black at 60 percent opacity by default) or a backdrop
  prefab you assign on the root. See [Root canvas](Root-Canvas.md).

The `screens` layer is not modal and has no backdrop.

---

## Close on backdrop tap

A tap on the backdrop closes the top popup when that popup's catalog row has `Close On Backdrop`
set. The view ends with `CloseReason.Backdrop` and no value. In the Basic Usage catalog, the
`confirm` row has the flag and the `rename` row does not.

- The tap reads the row of the top view only. With `p1` (flag set) under `p2` (flag not set), a tap
  closes nothing.
- A tap closes only the top view of the layer, never one below it.
- Without the flag the tap is swallowed. It does not reach the screen below, and nothing closes.
- The tap is an ordinary pointer click on the backdrop, delivered by the scene's `EventSystem` and
  its input module, whichever input system the project uses
  ([Input and platform](Input-and-Platform.md#give-the-ui-an-eventsystem)).
- The popup must be fully open. A tap while it is opening or closing does nothing, and a tap while the
  input shield is up does nothing.

A popup that closes by backdrop tap plays its hide transition like any other close.

---

## The input shield

The input shield is a counted, full-screen input blocker above every layer. A request takes a hold on
it when the request is accepted on a layer with `ShieldsInput` (`screens`, `popups` and `system` have
it), and gives it back when the open has finished. A close holds it again from the moment the close
is decided until the view is gone.

The hold is taken inside the `Open` or `ShowAsync` call, before the popup has loaded or drawn
anything. That is what protects a double click. The first click on "Reset" calls `ShowAsync`, which
raises the shield in the same call. The second click of the double click reaches the shield and not
the button. One popup opens, and the second click does nothing and logs nothing.

If a second request for the same popup id still gets through, for example from code, it is refused
as `RejectReason.Duplicate`: an id already open or in flight on a layer that is not a history layer is
not accepted again, unless the row sets `AllowMultiple`.

Things the shield does not cover:

- A control that was pressed before the shield was raised and released after it was lowered can still
  receive the release if the pointer is still over it.
- A canvas that sorts itself above the shield, such as the open list of a TextMesh Pro dropdown,
  is not blocked. The validator warns about dropdowns inside views
  ([UIDIRECTOR123](Troubleshooting.md#uidirector123)); see also
  [Troubleshooting](Troubleshooting.md#a-dropdown-list-stays-above-everything).

Code can raise the shield on its own with `INavigator.HoldInput`; see
[Input and platform](Input-and-Platform.md). A hold older than 30 seconds is reported once
([UIDIRECTOR110](Troubleshooting.md#uidirector110)).

---

## RejectWhenBusy

Each layer runs one operation at a time. Without the option, a request waits only behind operations
on its own layer; on an idle layer it starts at once. `OpenOptions.RejectWhenBusy` refuses the call
whenever `INavigator.IsBusy` is `true`, on any layer, even when the request would not have waited.
The request ends as not shown with `RejectReason.Busy` and `Detail` reads "The navigator is busy and
the call asked not to wait". `SettingsView` sets it in
the options of the confirm call shown at the top of this page (`RejectWhenBusy = true`) and reads the
refusal:

<!-- source: Samples~/BasicUsage/SettingsView.cs -->
```csharp
// Refused at once instead of waiting behind a view that is still opening.
if (answer.Rejected == RejectReason.Busy)
{
    _status.text = "Busy, try again.";
    return;
}
```

`INavigator.IsBusy` is `true` while any operation runs or waits, on any layer, or while an input hold
exists. In the sample, a toast sliding in or out makes the navigator busy, so a click on "Reset" in
that short window is refused and the label reads "Busy, try again." Without the flag the popup would
open right away, because the toast runs on its own layer. While nothing is moving, the popup opens as
usual.

- **When to use it.** On handlers that a player can trigger repeatedly: buttons, shortcuts, gestures.
  A refused request costs nothing. Without the flag, a request behind work on its own layer waits,
  and its popup appears after the player has moved on.
- **What it does not do.** It does not refuse a request that would be a duplicate or an unknown id;
  those reasons are checked first and win.
- **Without it,** a request behind work on the same layer waits in `Waiting`, already holding the
  shield. A layer keeps at most four waiting requests by default; the next is refused as `Busy` and logged as
  [UIDIRECTOR112](Troubleshooting.md#uidirector112). A back press withdraws the newest waiting request
  first ([Navigation and back](Navigation-and-Back.md#resolution-order)).
- `Rejected == Busy` also comes from the waiting cap and from a full sorting band. Read `Detail` to
  tell them apart.

Notices that may wait their turn are better served by the queue, which orders them by priority; see
[Queue](Queue.md). A queue serves one layer: a queued key whose row is on another layer is refused as
`RejectReason.WrongLayer` and logged as [UIDIRECTOR126](Troubleshooting.md#uidirector126).

---

## Popups and screen changes

- **A screen that opens later closes the popup.** The `popups` layer has `ClosedByNavigation`: popups
  accepted earlier than the new screen are snapped away with `Navigated` and no hide transition. A
  popup that the new screen opens itself survives. See
  [Navigation and back](Navigation-and-Back.md#closedbynavigation) and [Limitations](Limitations.md).
- **Back closes the popup first.** Back goes to the top popup before any screen and closes it with
  `CloseReason.Back`. A popup whose row says `HandledByView` is asked first, as `RenameView` does.
- **A system overlay is above the popup.** A popup opened while an overlay is up draws below it and
  cannot be clicked until the overlay is gone. See [System overlays](System-Overlays.md).
- **Hide transitions.** `Complete`, `Close`, back, a backdrop tap and a handle close all play the
  popup's hide transition. See [Transitions](Transitions.md).

## Traps

- **A popup whose id is already open is refused.** Opening the confirm twice gives `Duplicate`, not a
  second popup. For two instances at once, set `AllowMultiple` on the row.
- **Do not read `answer.Value` without `HasValue`.** It is `default` for every close that is not
  `Complete`. For `bool` that is `false`, which looks like an answer.
- **Do not own a popup with a token that outlives the question** unless that is intended. A popup
  owned by `RootToken` does not close when the screen below it closes or is popped; it can then sit
  over a different screen. Back, a backdrop tap, a handle close or a newly opened screen still close it.
- **Do not close inside `OnOpen`.** Use `Decline`.
- **Do not hold a `ViewHandle` as a lock.** It is inert once the request ended, and it is not
  awaitable; use `ShowAsync` or `WaitForCloseAsync`.

## See also

- [Opening and results](Opening-and-Results.md)
- [Navigation and back](Navigation-and-Back.md)
- [System overlays](System-Overlays.md)
- [View authoring](View-Authoring.md)
- [Catalogs and keys](Catalogs-and-Keys.md)
- [Transitions](Transitions.md)
- [Queue](Queue.md)
- [Input and platform](Input-and-Platform.md)
- [Root canvas](Root-Canvas.md)
- [Troubleshooting](Troubleshooting.md)
- [Architecture: projection and the input shield](Design/Architecture.md#7-the-input-shield)
