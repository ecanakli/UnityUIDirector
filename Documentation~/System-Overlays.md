# System overlays

[Back to index](index.md)

The `system` layer is for blocking overlays: a "Saving..." screen, a "Connecting..." spinner, a
"please wait" while something that cannot be interrupted runs. This page shows how to open one around
a piece of work, what it blocks, how back behaves while it is up, how to close it through its handle,
and how to wait until it is gone with `WaitForCloseAsync`.

The examples come from the Basic Usage sample: the settings screen shows a saving overlay while a
fake save runs.

---

## Show an overlay around work

Open the overlay, do the work, close the overlay in a `finally`:

<!-- source: Samples~/BasicUsage/SettingsView.cs -->
```csharp
private void OnSaveClicked()
{
    OpenScope.Run(SaveAsync);
}
```

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

The overlay view is an ordinary view. `SavingOverlay` sets a label in `OnOpen` and spins an indicator
on the visible scope:

<!-- source: Samples~/BasicUsage/SavingOverlay.cs -->
```csharp
public sealed class SavingOverlay : ViewBehaviour
{
    private const float DegreesPerSecond = 360f;

    [SerializeField] private TMP_Text _label;
    [SerializeField] private RectTransform _spinner;

    protected override void OnOpen(NoArgs args, ViewScope open)
    {
        _label.text = "Saving...";
    }

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
}
```

Its catalog row names the `system` layer and nothing else is special: no code in the view says that
it blocks. The layer does that.

---

## What the layer does

The `system` layer has two rules, `Modal` and `ShieldsInput`. It is the highest of the three default
layers, so it is drawn above screens and popups.

| It is | Which means |
|---|---|
| `ShieldsInput` | Opening and closing the overlay hold the input shield, so nothing under it can be clicked while it appears or leaves. |
| `Modal` | The layer has a backdrop under its top view, and every view below, popups and screens, is not interactive while the overlay is on screen. |
| Not `TakesBack` | `Back()` never closes a `system` view. It is swallowed. |
| Not `History` | There is no stack. Two overlays with different ids can be up together, and neither covers the other. |
| Not `ClosedByNavigation` | Opening a screen does not close it. |

What this gives you:

- **Input is blocked.** The player cannot click the screens or popups below. Code can still open and
  close views under the overlay; the overlay blocks the player and not the navigator.
- **A screen change leaves it alone.** A screen that opens under the overlay snaps the older popups
  away with `Navigated`, and the overlay stays.
- **A popup opened while the overlay is up draws below it.** The popup layer is under the system
  layer, and it is not interactive until the overlay is gone.
- **The overlay dims what is below** with the layer's backdrop. The backdrop colour is the root's
  setting ([Root canvas](Root-Canvas.md)). Leave `Close On Backdrop` off on an overlay row; the flag
  works on any modal layer, and a blocking overlay should not end because someone tapped the dim.
  With the flag off a tap on the dim is swallowed: it closes nothing and does not reach the views below.

A view that must not block input, such as a toast, does not belong on `system`. Declare a layer for
it with `ViewLayerRules.None` through `ViewRootSetup.ExtraLayers`, as the Basic Usage sample does for
its `toasts` layer ([Navigation and back](Navigation-and-Back.md#closedbynavigation)).

---

## Back while an overlay is up

`Back()` on a `system` overlay returns `true` and closes nothing, and `BackInput.Unhandled` is not
raised. That is what makes an overlay blocking: a press of Escape does not close the screens or the
popup under it and does not open a quit confirm. One exception is described below the table.

| Row policy of the overlay | What `Back()` does |
|---|---|
| `LayerDefault`, `Close`, `Ignore` | Swallowed. The view is not asked and not closed. |
| `HandledByView` | The view is asked through `IViewBack.OnBack()`. The press is swallowed whatever the answer; a `false` does not close the overlay, because the layer does not take back. |

Two more cases swallow back: the overlay is still opening or closing, and the input shield is up. In
both, `Back()` returns `true`.

Everything below the overlay, the popup and the screen under it, stays open and is not closed by back
until the overlay is gone.

One exception: a screen or popup that is still waiting or loading below the overlay is withdrawn by
back (`Rejected` is `Cancelled`), because withdrawing a pending request comes before everything else
(see [Navigation and back](Navigation-and-Back.md#resolution-order)). This matters when an overlay
covers the load of the next screen. Set that row's `Back` to `Ignore` if it must not be cancelled while
the overlay is up.

Two traps:

- `BackPolicy.Close` on a `system` row has no effect. The catalog validator reports it as
  [UIDIRECTOR122](Troubleshooting.md#uidirector122).
- If the overlay must be dismissable by the player, it needs its own button that completes or closes
  the view, or it belongs on a layer that takes back, such as `popups`. See
  [Back does nothing while a system overlay is open](Troubleshooting.md#back-does-nothing-while-a-system-overlay-is-open).

---

## Close it through its handle

`Open` returns a `ViewHandle`. `handle.Close()` closes the overlay, or withdraws the request if the
overlay has not been shown yet. `SettingsView` calls it in a `finally`, so a thrown exception or a
cancelled save still takes the overlay down.

- **Reason.** A close through the handle ends with `CloseReason.Code`.
- **It never throws,** can be called any number of times, and does nothing after the request ended.
  The first close cause wins.
- **Closing right after opening still shows the overlay once.** A close that is decided while the
  overlay is opening is carried out after the show transition. A very short job therefore shows the
  overlay for its show and hide transitions. If the overlay is still loading or waiting when the job
  ends, the request is withdrawn and the overlay never appears, `WasShown` is `false` and `Rejected`
  is `Cancelled`.
- **A second `Open` of an id that is up is refused.** While the first overlay has no close cause, the
  id is open on a layer that is not a history layer, so the call gives `RejectReason.Duplicate` and
  returns an empty handle. `IsAlive` is `false` on it, `Close()` does nothing and `WaitForCloseAsync`
  completes at once. The first overlay stays up. Do not assume the handle you got is the one that
  owns the overlay.
- **After `Close()`, a new `Open` of the id is accepted,** even while the first overlay plays its hide
  transition. Two overlays with that id then exist for a moment, and the new handle is alive. This
  happens when one job closes the overlay and the next job opens it right away.

### Who owns the overlay

The sample passes `Navigator.RootToken` as owner. The overlay then ends only when something closes it
or the root is destroyed, which is why the `finally` is the guarantee. Two other choices exist:

- Pass a token that ends with the work, such as the open scope token of the view whose life matches
  the work. Or pass the token of your own `CancellationTokenSource` that the routine cancels in a
  `finally`; the package never cancels it for you, so that `finally` is a line you write. Either way
  the close reason is `OwnerEnded` and the overlay is snapped away with no hide transition.
- Pass the screen's scope token. The overlay then closes with `OwnerEnded` when the screen closes.

An overlay that stays up with nobody to close it blocks the whole interface. That is the main risk of
the layer; the `Window > UIDirector` view and `INavigator.Describe` list the open views and the
shield holders when you need to find the one that stayed.

---

## Wait for it to be gone

`WaitForCloseAsync` completes when the request ends:

<!-- signature -->
```csharp
public UniTask<ViewClosed> WaitForCloseAsync(CancellationToken waiter);
```

It is on `ViewHandle`, because an `Open` call has no await of its own. In `SaveAsync`, calling
`overlay.Close()` only starts the hide transition. Awaiting the handle holds the code until the
overlay is gone, so the status text appears after the overlay left and not under it:

<!-- source: Samples~/BasicUsage/SettingsView.cs -->
```csharp
// Wait for the hide transition, so the status appears once the overlay is gone.
var closed = await overlay.WaitForCloseAsync(ct);
_status.text = closed.Reason == CloseReason.Code ? "Saved." : "Saved. The overlay ended early.";
```

`ViewClosed` carries the same facts as a `ViewResult` without a value: `WasShown`, `Reason`,
`Rejected` and `Detail`.

- Any number of callers can wait on one handle. A call after the request ended returns the stored
  outcome at once.
- It throws `OperationCanceledException` only when the token you pass is cancelled. The token you
  pass cancels your wait and never the overlay.
- The reason tells you who ended it. `Code` is a close by code of an overlay that was shown.
  `NotShown` with `Rejected` of `Cancelled` is your own close that withdrew the overlay before it
  appeared. `Destroyed` means the root or the instance went away. `OwnerEnded` is possible only for an
  overlay owned by a token other than `RootToken`. Anything but `Code` is what the sample's text
  "The overlay ended early." reports.

When the overlay itself produces an answer, for example a "connection lost, retry" overlay with a
button, open it with `ShowAsync` and read the `ViewResult`; back stays swallowed, so the view needs
its own button. See [Opening and results](Opening-and-Results.md).

When no overlay is needed and input only has to be blocked for a while, `INavigator.HoldInput` does
that without a view; see [Input and platform](Input-and-Platform.md).

## Traps

- **An overlay that nobody closes locks the interface.** Close it in a `finally`, or give it an
  owner that ends with the work.
- **Back does nothing while it is up.** That is the design; it is not a bug in `BackInput`.
- **A second overlay with the same id is refused** while the first has no close cause. Check
  `IsAlive` on the handle when two callers can ask for the same overlay.
- **Per-span work goes on the visible scope.** `SavingOverlay` starts its spinner there, so it stops
  when the overlay is hidden or closed and nothing needs an unsubscribe.
- **A popup opened while an overlay is up opens below it** and cannot be used until the overlay is
  gone. Open it after the overlay has closed, for example after `await overlay.WaitForCloseAsync(ct)`.

## See also

- [Navigation and back](Navigation-and-Back.md)
- [Popups and backdrop](Popups-and-Backdrop.md)
- [Opening and results](Opening-and-Results.md)
- [View authoring](View-Authoring.md)
- [Input and platform](Input-and-Platform.md)
- [Root canvas](Root-Canvas.md)
- [Troubleshooting](Troubleshooting.md)
- [Architecture: back resolution](Design/Architecture.md#8-back-resolution)
