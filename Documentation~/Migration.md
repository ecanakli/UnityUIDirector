# Migration

[Back to index](index.md)

This page is for a project that already has a UI manager of its own and wants to move to UIDirector
without a rewrite weekend. It shows what each old habit becomes, then moves one view at a time while
the old manager keeps running next to the new root. At the end of every step the project still
builds and plays.

The "before" code on this page is generic and short; it stands for whatever your manager looks like.
Every "after" block is copied verbatim from the Basic Usage and Zenject Usage samples, so it compiles
as shown.

---

## The old UI in one picture

A hand-written manager usually has the same five parts. This one is typical:

<!-- illustrative: before -->
```csharp
public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [SerializeField] private GameObject[] _screens;
    private readonly List<GameObject> _stack = new List<GameObject>();

    private void Awake() => Instance = this;

    public void Push(int index)
    {
        if (_stack.Count > 0) _stack[^1].SetActive(false);
        _screens[index].SetActive(true);
        _stack.Add(_screens[index]);
    }

    public void Pop()
    {
        _stack[^1].SetActive(false);
        _stack.RemoveAt(_stack.Count - 1);
        _stack[^1].SetActive(true);
    }

    public void ShowPopup(string name, Action<bool> onAnswer)
    {
        var popup = Instantiate(Resources.Load<PopupView>("Popups/" + name), transform);
        popup.Init(onAnswer);
    }
}
```

and a screen that uses it:

<!-- illustrative: before -->
```csharp
public class SettingsScreen : MonoBehaviour
{
    [SerializeField] private Button _reset;

    private void OnEnable() => _reset.onClick.AddListener(OnReset);
    private void OnDisable() => _reset.onClick.RemoveListener(OnReset);

    private void OnReset() =>
        UIManager.Instance.ShowPopup("Confirm", yes => { if (yes) ResetSettings(); });
}
```

Screens are switched with `SetActive`, a list is the stack, a popup is found by name, the manager is
reached through a static field, and the answer comes back in a callback. The sections below replace
each of these.

---

## What replaces what

| Old habit | In UIDirector | Read |
|---|---|---|
| Screens switched with `SetActive` | Each screen is a prefab view on the `screens` layer. The package creates it, hides what it covers and releases it | [Concepts](Concepts.md) |
| A list used as the stack, a `Pop` method | The `screens` layer is the stack and the back history. Opening an id that is already in it returns to it | [Navigation and back](Navigation-and-Back.md) |
| One `Back` button handler per screen, or one `Update` that reads the key | One `Back()` rule on the navigator, and `BackInput` to call it | [Navigation and back](Navigation-and-Back.md#back) |
| A popup instantiated from `Resources` by name | A catalog row with an id, and a typed key `ViewKey<TArgs, TResult>` in code | [Catalogs and keys](Catalogs-and-Keys.md) |
| An `Action<bool>` callback for the answer | `await ShowAsync(...)` returns a `ViewResult` with the value, or the reason there is none | [Opening and results](Opening-and-Results.md) |
| `Init(args)` on the popup | The key's argument type, read in `OnOpen` | [View authoring](View-Authoring.md) |
| `AddListener` in `OnEnable`, `RemoveListener` in `OnDisable` | `open.Bind(...)` on the scope; the package removes it | [View authoring](View-Authoring.md#tie-listeners-and-tasks-to-a-scope) |
| `UIManager.Instance` reached from everywhere | `INavigator` is an interface: views get it as `Navigator`, other classes receive it | [Opening and results](Opening-and-Results.md) |
| A `bool _busy` or a transparent panel that eats taps | The input shield, raised inside the `Open` call, and `RejectWhenBusy` | [Popups and backdrop](Popups-and-Backdrop.md#the-input-shield) |
| A loading panel switched on and off by hand | A view on the `system` layer closed in a `finally`, or the root's `Load Indicator` | [System overlays](System-Overlays.md) |
| `canvas.sortingOrder = 100` on each popup | One sorting band per root, computed from the layer and the slot | [Root canvas](Root-Canvas.md#the-sorting-band) |
| Fade coroutines on `Time.deltaTime` | A `ViewTransition` on unscaled time | [Transitions](Transitions.md) |
| A static event or a log line for analytics | An observer with an owner token | [Events and analytics](Events-and-Analytics.md) |
| A "show these in turn" list for notices | The priority queue | [Queue](Queue.md) |
| A popup that must not outlive its screen, closed by hand | The owner token of the open | [Opening and results](Opening-and-Results.md#owners-and-what-happens-when-one-ends) |

---

## Before you start

- The package needs UniTask and `com.unity.ugui`. Install steps are in the README.
- List what the old UI does: every screen, every popup with the data it takes and the answer it gives,
  the places that read the back key, and every canvas with its sorting order. The list is your
  migration order; the steps below move from the edges (a popup that only asks a question) toward
  the screens that other code calls.
- Calls into the package run on the main thread. Old code that opens UI from a worker thread or
  from a thread-pool continuation must hop to the main thread first; see [Threading](Threading.md).

---

## Step 1: put a root beside the old canvas

Choose `GameObject > UI > UIDirector Root`. It adds a new outermost object with its own canvas, and it
adds an `EventSystem` only when the scene has none. A project that already has a working UI has one,
so nothing changes there: keep exactly one active `EventSystem`
([Input and platform](Input-and-Platform.md#give-the-ui-an-eventsystem)).

The old canvas is not touched. Its manager keeps working, and nothing the package shows is a child
of it.

To pass code-only settings, turn `Initialize On Awake` off on the root and initialize it from code,
before anything reads `ViewRoot.Navigator`:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
_viewRoot.Initialize(new ViewRootSetup
{
    Inject = InjectServices,
    ErrorHandler = OnViewError,
    ExtraLayers = new[] { AppLayers.Toasts }
});
_navigator = _viewRoot.Navigator;
```

You can leave `Initialize On Awake` on and use the defaults until you need a setting. Every field of
the root is on [Root canvas](Root-Canvas.md). If your old UI is drawn by its own camera, the
camera variant `UIDirector Root (Camera)` creates a camera of its own beside the root; see the
render-mode table on the same page before you choose.

The menu also adds a `BackInput` to the root, and it reads the back key every frame. It does nothing
until the root is initialized. As soon as the first package view is open (step 3), one press closes it
and is also handled by the old manager's own back code. If the old UI reads the back key, set up the router of
[step 7](#step-7-route-the-back-key-through-one-place) now, before step 3. It does not depend on
steps 3 to 6.

Press Play. Nothing visible changed, and the old UI still works.

---

## Step 2: give the two UIs separate sorting bands

Both UIs draw on the screen at once, so decide who is above whom now, before the first view moves.
The package puts every view on a nested canvas with a sorting order computed from the root's
`Base Sorting Order`:

```text
order = Base Sorting Order + layer order x 1000 + slot x 10
```

The first popup is at base + 1010, the input shield at base + 3000 with the three default layers.
An older canvas should sit outside that band:

| The old canvas | Give it | Effect |
|---|---|---|
| Should stay under the package's views (a HUD, a background) | A sorting order below `Base Sorting Order` | Every package view covers it |
| Should cover everything (an old full-screen popup system you keep for now) | A sorting order above the shield | It draws above the views and above the shield, so it is not blocked by it |
| Somewhere in between | Do not | It interleaves with the package's screens and popups |

Orders compare only between overlay canvases, or between canvases drawn by the same camera on one
sorting layer. An overlay canvas always draws above camera canvases, and canvases on different cameras
follow camera depth first. Give the old canvas the same render mode as the root, and for a camera root
draw it with the root's UI Camera, before you set orders. A camera root takes the sorting layer of its
own canvas, so put the old canvas on that sorting layer when you compare their orders. How an overlay
root and a camera root differ is on [Root canvas](Root-Canvas.md#overlay-roots-and-camera-roots).

If the old UI contains a popup system you will keep until the end, sorting it above the shield is the
simplest arrangement: a migrated screen never ends up over it. The trade-off is that the shield does
not block it, so a tap on an old button still works while a package transition runs. Decide per case
whether that matters.

---

## Step 3: turn one popup into a view

Start with a popup that asks a question and has no children of its own. Three things are needed.

**The prefab.** The root of the prefab gets a `ViewHost` (Unity adds the `Canvas` and
`GraphicRaycaster` it requires) and one component that implements the view contract. Remove any
`CanvasScaler` from the popup's root: the project's root canvas scales, and the view is a nested
canvas. The rest of the layout is yours. The rules are on
[View authoring](View-Authoring.md#prefab-structure).

**The view class.** `Init(args)` becomes `OnOpen(args, open)`, and the listeners move onto the scope,
so the `OnEnable` and `OnDisable` pair disappears:

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

The two type arguments, `ConfirmArgs` and `bool`, replace the `Init` parameter and the
`Action<bool>`. `Complete(value)` is the callback call.

**The row and the key.** Create a catalog with `Assets > Create > UIDirector/View Catalog`, add a row
with an `Id`, the prefab, and `Layer Id` set to `popups` (a new row defaults to `screens`), and
assign the catalog to the root's `Catalogs` list. In code, the id gets a typed name:

<!-- source: Samples~/BasicUsage/AppViews.cs -->
```csharp
[ViewKeys]
public static class AppViews
{
    public static readonly ViewKey<NoArgs, NoResult> Home = ViewKey.Create("home");
    public static readonly ViewKey<NoArgs, NoResult> Library = ViewKey.Create("library");
    public static readonly ViewKey<ItemArgs, NoResult> ItemDetail = ViewKey.Create<ItemArgs>("item-detail");
    public static readonly ViewKey<NoArgs, NoResult> Settings = ViewKey.Create("settings");
    public static readonly ViewKey<ConfirmArgs, bool> Confirm = ViewKey.Create<ConfirmArgs, bool>("confirm");
    public static readonly ViewKey<RenameArgs, string> Rename = ViewKey.Create<RenameArgs, string>("rename");
    public static readonly ViewKey<NoArgs, NoResult> Saving = ViewKey.Create("saving");
    public static readonly ViewKey<ToastArgs, NoResult> Toast = ViewKey.Create<ToastArgs>("toast");
}
```

Run `Tools/UIDirector/Validate Catalogs`. It reports a key with no row, a prefab with no matching
view component and a prefab without a `ViewHost` before you press Play
([Catalogs and keys](Catalogs-and-Keys.md#validate)).

A popup the old code found by name with `Resources.Load` is now a direct prefab reference in a row.
A row with a `Prefab` keeps the prefab resident while its catalog is loaded, which `Resources.Load`
did not. For a large number of popups, use an `Asset Key` with a view provider so nothing is resident
until it opens ([Assets and Addressables](Assets-and-Addressables.md)), or split the rows into
catalogs that you add at run time ([Catalogs and keys](Catalogs-and-Keys.md#add-and-remove-a-catalog-at-run-time)).

---

## Step 4: open it and read the answer

The call site changes from a callback to an `await`. The old line

<!-- illustrative: before -->
```csharp
UIManager.Instance.ShowPopup("Confirm", yes => { if (yes) ResetSettings(); });
```

becomes the call below. Its third argument is the owner, here the token of the screen that asks, so
the popup cannot outlive that screen. The old manager had no such rule and left the cleanup to each
callback:

<!-- source: Samples~/BasicUsage/SettingsView.cs -->
```csharp
var answer = await Navigator.ShowAsync(
    AppViews.Confirm,
    new ConfirmArgs("Reset settings", "All settings return to their defaults."),
    ct,
    new OpenOptions { Source = "settings.reset", RejectWhenBusy = true });
```

Then read the outcome. A callback only ran when the popup answered. An `await` also resumes when the
popup closes some other way: Back, a backdrop tap, a closed screen, an owner that ended. That is
why the value has a flag:

<!-- source: Samples~/BasicUsage/SettingsView.cs -->
```csharp
// Back, backdrop and a closed screen all end with no value, so this line also guards the screen.
if (!answer.HasValue || !answer.Value)
    return;
```

Where the old callback ran only on "yes", this line returns on every ending that is not an explicit
"yes". Code that must tell the causes apart reads `answer.Reason` and `answer.Rejected`. The full
list is on [Opening and results](Opening-and-Results.md#reading-a-result).

`RejectWhenBusy` is the replacement for a hand-made `_busy` flag: it refuses the call while the
navigator is busy. Leave it out where waiting is fine.

A double tap needs no code of yours. The first tap raises the input shield inside the call, so the
second tap lands on the shield and not on the button
([Popups and backdrop](Popups-and-Backdrop.md#the-input-shield)).

---

## Step 5: move the screens

A screen moves the same way: a prefab, a view class, a row on the `screens` layer, a key. What changes
is how it is reached. The stack and `Push` become `Open`:

<!-- source: Samples~/BasicUsage/LibraryView.cs -->
```csharp
private void OpenItem(string itemId) =>
    Navigator.Open(AppViews.ItemDetail, new ItemArgs(itemId), Navigator.RootToken,
        new OpenOptions { Source = "library.item" });
```

`Open` returns at once and does not wait; use `ShowAsync` when the caller needs the outcome. The
`Source` string names the call site in events and diagnostics. The owner is the root because a screen
opened from a button should outlive the screen that holds the button.

The list-as-stack code becomes the layer. The screen below is covered and kept loaded, not destroyed,
and `Pop` becomes `Back()` or a jump to a known screen:

<!-- source: Samples~/BasicUsage/ItemDetailView.cs -->
```csharp
// Home is already in the stack: this returns to it, and Home's OnOpen does not run again.
private void GoHome() =>
    Navigator.Open(AppViews.Home, Navigator.RootToken, new OpenOptions { Source = "item.home" });

// Pops everything above the library.
private void GoToLibrary() => Navigator.CloseTo(AppViews.Library);
```

Two differences from a hand-written stack are worth knowing before you port code that depended on
them:

- **`OnOpen` runs once per open, not once per visit.** A screen that was covered and is revealed again
  gets `OnVisible`, not `OnOpen`. Code that refreshed a screen in `OnEnable` goes in `OnVisible`:

  <!-- source: Samples~/BasicUsage/LibraryView.cs -->
  ```csharp
  // Runs on the first show and again each time this screen is revealed.
  protected override void OnVisible(ViewScope visible) => _count.text = $"{_library.Count} items";
  ```

- **Opening an id that is already in the stack returns to it** instead of pushing a copy. A flow that
  deliberately stacks two instances of one screen sets `Allow Multiple` on its row.

The details, with a table of what runs and what does not, are on
[Navigation and back](Navigation-and-Back.md#return-to-a-screen-that-is-already-open).

A screen that takes its data from a static field of the old manager now takes it from its key's
argument type (`ViewKey<ItemArgs, NoResult>` above). That is the end of `Init(args)` for screens too.

Popups that are open when a screen opens are closed by the screen change; this is the
`ClosedByNavigation` rule of the `popups` layer, and it does not touch old popups that are not views of
the package. See [Navigation and back](Navigation-and-Back.md#closedbynavigation).

---

## Step 6: stop reaching for a singleton

`UIManager.Instance` was a global. `INavigator` is an interface with one instance per root, and it is
handed to whoever needs it.

- **A view** gets it as the `Navigator` property of `ViewBehaviour`. The snippets above use it.
- **A plain class** receives it where it is built. The Zenject sample's start class is an ordinary C#
  class; any other way of passing a constructor argument works the same:

  <!-- source: Samples~/ZenjectUsage/AppStart.cs -->
  ```csharp
  private readonly INavigator _navigator;

  public AppStart(INavigator navigator)
  {
      _navigator = navigator;
  }

  public void Initialize()
  {
      // Owned by the root, so the scoreboard ends only when the root does.
      _navigator.Open(ZenjectViews.Scoreboard, _navigator.RootToken, new OpenOptions { Source = "boot" });
  }
  ```

- **Services a view needs** (settings, a store) used to come from other singletons. The root's
  `Inject` delegate runs on every new view instance while it is still inactive, before its `Awake`.
  It is the one seam for handing services in, whether you use a container or not:

  <!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
  ```csharp
  // Runs on every new view instance while it is still inactive.
  private void InjectServices(GameObject instance)
  {
      instance.GetComponentsInChildren(true, _receivers);
      foreach (var receiver in _receivers)
          receiver.Receive(_services);

      _receivers.Clear();
  }
  ```

  A container integration is on [Zenject](Zenject.md).

- **Anything that logged or sent analytics from inside the old manager** becomes an observer. It sees
  every request, shown view, close and refusal in one place, and ends with its owner token:

  <!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
  ```csharp
  // The observer ends with the root: RootToken is cancelled when the root is destroyed.
  _navigator.AddObserver(new ViewEventLog(), _navigator.RootToken);
  ```

While old and new code coexist, the simplest bridge is to keep the old manager's public methods and
make them forward. `Show("Settings")` calls `Open` for an id that has moved and does what it always
did for one that has not. A method that took an `Action<bool>` awaits `ShowAsync` and calls the
callback when `HasValue` is true. Callers do not change until you are ready to change them, and
the forwarders are the list of what is left.

---

## Step 7: route the back key through one place

The old UI and the package each have their own idea of what back means. While both are on screen, one
object must decide the order, or one key press is handled twice.

`BackInput` does not ask other UI first. A project with an older popup system replaces it on the root
with a small router that asks the older UI, and calls `Back()` only when the older UI did not use the
press. The Basic Usage sample ships that router, `HardwareBackRouter`, and its handler is:

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

In your project `TryClose()` is a call into the old manager that returns `true` when it closed
something. The rules, and the complete recipe, are on
[Navigation and back](Navigation-and-Back.md#a-project-that-still-has-an-older-ui):

- Never keep both `BackInput` and the router on the root.
- Order follows the stacking. If the older UI is drawn below the package's layers, call `Back()`
  first and ask the older UI when `Back()` returns `false`.
- The quit logic stays in the router's `Unhandled` event, the way `BackInput.Unhandled` serves it.

When the last old popup is gone, delete the router and put `BackInput` back on the root.

If a flow of the old UI is on screen and the package has queued notices (a daily reward, a news
popup), the queue does not know about it. Take a hold for as long as the old flow runs, with the flow's
owner token, so queued items wait:

<!-- source: Samples~/QueueUsage/TutorialFlow.cs -->
```csharp
// Queued items wait; the flow's own opens below are not affected by the hold.
var hold = _queue.Hold("tutorial", ct);
try
{
    for (var step = 1; step <= Tips.Length; step++)
    {
        var result = await _navigator.ShowAsync(QueueViews.TutorialTip,
            new TipArgs(Tips[step - 1], step, Tips.Length), ct,
            new OpenOptions { Source = "tutorial" });

        // Not shown, backed out, replaced or owner ended: the player left the tutorial.
        if (result.Reason != CloseReason.Dismissed && result.Reason != CloseReason.Completed)
            return;
    }
}
finally
{
    // Ending the hold lets the queue continue; while the tutorial ran, the gate denied notices below priority 100.
    hold.Remove();
    _onboarding.IsRunning = false;
    IsRunning = false;
}
```

The hold is removed in a `finally`, so no way out leaves the queue held. [Queue](Queue.md#holds) has
the rules.

---

## Step 8: retire the old manager

A view is migrated when no code calls the old manager for it. When the last screen and popup have
moved:

1. Search for the manager's type name and its static field. Every remaining hit is a forwarder or a
   caller you have not changed.
2. Delete the router and put `BackInput` back on the root.
3. Delete the old canvas, or keep it for content that is not a screen (a HUD) with a sorting order
   below the band.
4. Run `Tools/UIDirector/Validate Catalogs` and open `Window > UIDirector` in Play Mode to see the
   layers, requests and shield holders live ([Diagnostics](Diagnostics.md)).

---

## Running both side by side: what to expect

| Area | Behaviour while both exist |
|---|---|
| Drawing | The package draws only its own views, on the root's canvas, and covers only its own views. Old content stays visible until the old manager hides it |
| Input | The input shield blocks the package's views and old canvases that sort below it. A canvas sorted above the shield is not blocked |
| Back | `Back()` knows only package views. The router in step 7 is what joins the two |
| `ClosedByNavigation` | Applies to package popups only |
| The queue | Presents package views only. Hold it while an old flow runs |
| `Time.timeScale` 0 | Transitions, timeouts and the backdrop run on unscaled time. An old tween on scaled time still stops, as it did before |
| Scene changes | The root has no scene-change rule of its own. A root without `Persist Across Scenes` is destroyed with its scene and ends every view on it; turn it on when the old manager survives scene loads. An old manager that tears down on a scene change keeps doing so; give package views an owner that ends where you want them to end ([Limitations](Limitations.md#navigation)) |
| Two canvases, one `EventSystem` | One `EventSystem` serves both |

---

## Traps

- **A second `EventSystem`.** The menu adds one only when the scene has none. If the old UI creates its
  own at run time, the project can end up with two.
- **An old canvas sorted inside the band.** It interleaves with screens and popups. Put it below
  `Base Sorting Order` or above the shield.
- **A popup row left on `screens`.** `screens` is the default `Layer Id`. A popup left there is a
  screen: it joins the history and a later screen draws over it. Set `popups`.
- **Treating `Open` as "it is open".** `Open` accepts the request and returns. A request can still be
  refused, fail to load or decline. When a later line depends on the outcome, use `ShowAsync`.
- **Reading `Value` without `HasValue`.** For a `bool` the default is `false`, which looks like an
  answer of "no".
- **Opening UI from a worker thread.** The call throws. Hop to the main thread first
  ([Threading](Threading.md)).
- **Both `BackInput` and a router on the root.** One press is handled twice.
- **Calling `SetActive(false)` or `Destroy` on a package view.** One frame later the request
  ends with `CloseReason.Destroyed` (or `NotShown` with `Rejected` of `Cancelled` if the view had not been
  shown yet) and logs [UIDIRECTOR107](Troubleshooting.md#uidirector107).
  The package releases the instance instead of keeping it, so a `KeepInstance` row builds a new one on
  its next open. Close through `Complete`, `Close`, the handle or Back.
- **Keeping a view's own `OnDisable` cleanup.** Listeners bound on the scope are removed by the
  package. A hand-written `RemoveListener` is harmless and not needed.

## See also

- [Concepts](Concepts.md): the model behind the steps
- [Navigation and back](Navigation-and-Back.md): the history stack, `Back()` and the older-UI recipe
- [Opening and results](Opening-and-Results.md): `Open`, `ShowAsync`, owners and results
- [Catalogs and keys](Catalogs-and-Keys.md): rows, keys and the validator
- [View authoring](View-Authoring.md): hooks, scopes and prefab structure
- [Root canvas](Root-Canvas.md): the sorting band and render modes
- [Popups and backdrop](Popups-and-Backdrop.md)
- [Queue](Queue.md)
- [FAQ](FAQ.md)
- [Limitations](Limitations.md)
- [Troubleshooting](Troubleshooting.md)
