# FAQ

[Back to index](index.md)

Short answers. Each one links to the page that explains it in full.

---

## Scope and setup

### Does it work with UI Toolkit?

No. UIDirector builds its layers, input shield and backdrops from uGUI objects under a root canvas,
and a view is a prefab with a `Canvas` on its root. UI Toolkit is not supported, and neither is a
world-space root canvas. See [Limitations](Limitations.md#setup) and [Root canvas](Root-Canvas.md).

### What does it need?

UniTask and the uGUI package (`com.unity.ugui`). The package returns `UniTask`, not `Task`.
Addressables, DOTween and Zenject are optional and covered below. See
[Threading](Threading.md#unitask-usage).

### One root or several?

One root per application is the documented setup. A second `ViewRoot` works as an independent
system with its own layers, input shield, back rule and queues, and nothing coordinates two roots:
`Back()` on one knows nothing about the other, and each root numbers its own sorting band from its
own `Base Sorting Order`, so two roots on one sorting layer need bases that keep the bands apart.
See [Root canvas](Root-Canvas.md#the-sorting-band) and [Limitations](Limitations.md#setup).

### How do I get the navigator, and why does `Initialize` throw?

The navigator is `ViewRoot.Navigator`. A root with `Initialize On Awake` on initializes itself in its
own `Awake` with default settings, and `Initialize` runs once: a second call throws "The root is
already initialised". Reading `ViewRoot.Navigator` on a root that is not initialized also
initializes it with defaults. So when you need a `ViewRootSetup`, turn `Initialize On Awake` off and
call `Initialize` before anything reads the navigator, as the Basic Usage bootstrap does. The package's
own components do not read it early: before the root is initialized, `BackInput` ignores the back key,
and a `ViewButton` with the Back or Open View action logs a warning and does nothing. See
[Root canvas](Root-Canvas.md#initialize-with-viewrootsetup).

---

## Scenes

### What happens to the UI when the scene changes, and what about additive scenes?

The package never loads a scene and closes nothing when a scene changes. A view lives as long as its
owner token. The root decides the rest:

- A root without `Persist Across Scenes` is destroyed with its scene. Every view under it ends
  without a [UIDIRECTOR107](Troubleshooting.md#uidirector107) line, and `RootToken` is cancelled.
- A root with it survives the load, and its views stay open until their owners end them. Keep one
  `EventSystem` alive across the change.
- A view opened by code in a scene that can go away should be owned by a token that ends with that
  scene, for example the `destroyCancellationToken` of a `MonoBehaviour` in it. A view that must
  survive should be owned by `INavigator.RootToken`.

For additive scenes, put the root in the scene that stays loaded. Unloading the scene that holds the
root tears the UI down with no `UIDIRECTOR107` line (see
[Diagnostics](Diagnostics.md#a-scene-unload-or-a-root-destruction-logs-no-uidirector107)). Views
opened from an additive scene are owned by a token of an object in that scene, so they end when it
unloads. Scene-placed view instances cannot be adopted; a view is always a prefab instance created by
the package. See
[Input and platform](Input-and-Platform.md#scenes-and-persist-across-scenes),
[Root canvas](Root-Canvas.md#persist-across-scenes) and [Limitations](Limitations.md#navigation).

---

## Opening views

### How do I pass data in and get a result out?

The key carries both types: `ViewKey<TArgs, TResult>`. You pass the arguments to `ShowAsync` and read
the result from what it returns. The Basic Usage settings screen asks a question and waits:

<!-- source: Samples~/BasicUsage/SettingsView.cs -->
```csharp
var answer = await Navigator.ShowAsync(
    AppViews.Confirm,
    new ConfirmArgs("Reset settings", "All settings return to their defaults."),
    ct,
    new OpenOptions { Source = "settings.reset", RejectWhenBusy = true });
```

The view answers by calling `Complete` with a value:

<!-- source: Samples~/BasicUsage/ConfirmView.cs -->
```csharp
private void Accept()
{
    Complete(true);
}
```

`ShowAsync` always completes with a `ViewResult` and never throws for anything that happens to the
view. Read the value only after `HasValue` is true: Back, a backdrop tap, a closed screen and an
ended owner all end with no value, and `Value` is then `default`, which for a `bool` looks like
"no". See [Opening and results](Opening-and-Results.md#reading-a-result),
[View authoring](View-Authoring.md#complete-with-a-result) and
[Popups and backdrop](Popups-and-Backdrop.md).

### Can I open a view from code that is not a MonoBehaviour?

Yes. `INavigator` is an interface, not a singleton. Hand it to the class that needs it, and open with
the root's token as the owner when the view should live as long as the UI. The Zenject sample's start
class is a plain C# class:

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

Without a container, build the class where you already hold the root, as the Queue Usage bootstrap
does:

<!-- source: Samples~/QueueUsage/QueueBootstrap.cs -->
```csharp
var onboarding = new OnboardingState();
var notices = new StartupNotices(_navigator, onboarding);
```

Call it on the main thread. See [Opening and results](Opening-and-Results.md), [Zenject](Zenject.md)
and [Threading](Threading.md).

### My await returned with no value. Why?

Usually the view ended some other way than `Complete`. Check `Reason` and `Rejected` on the result.
The common cause for a popup is `ClosedByNavigation`: when a screen opens on a history layer, popups
that were accepted **earlier** than that screen are snapped away with `CloseReason.Navigated`. A popup
that the new screen opens itself, or that is requested after the screen, stays. The rule runs on an
open only, never on a pop, `Back()` or `CloseTo`. See
[Navigation and back](Navigation-and-Back.md#closedbynavigation).

### What happens to open views when I remove a catalog?

They stay open. Removing a catalog, by removing its registration or by ending its owner, makes the
ids unknown to new opens and releases kept instances, but a view that is already open finishes
normally. It ends with the catalog only when it was opened with the same owner. See
[Catalogs and keys](Catalogs-and-Keys.md#add-and-remove-a-catalog-at-run-time).

---

## Back

### How does back work on Android?

`BackInput` polls the Escape key and calls `Back()` on its root's navigator. Android's back button is
expected to arrive as Escape. The package does not manage `Input.backButtonLeavesApp` and has no code
for the predictive back gesture. In the editor, back is Escape, and the Game view needs focus.

When `Back()` returns `false` (nothing handled it, for example on the bottom screen) `BackInput`
raises `Unhandled` and your game decides, for example by asking to quit. The Basic Usage bootstrap
listens to it:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
// No removal needed: the bootstrap and the root live and die together.
_backInput.Unhandled.AddListener(OnBackUnhandled);
```

See [Input and platform](Input-and-Platform.md#the-back-button),
[Navigation and back](Navigation-and-Back.md#backinput) and
[Limitations](Limitations.md#setup).

---

## Beside existing UI

### Can I use it next to UI I already have?

Yes, with two things to arrange. First, **sorting**: the package keeps its views in one band of
sorting orders from `Base Sorting Order` up to the input shield. Your own canvas should sort below
the band (it stays under the views) or above the shield (it covers everything); a value in between
interleaves with the views. Second, **back**: `BackInput` does not ask other UI first. A project that
has an older popup system replaces `BackInput` with a small router that asks the older UI, and calls
`Back()` only when it did not use the press; the Basic Usage sample ships one. Never keep both on
the root. See [Navigation and back](Navigation-and-Back.md#a-project-that-still-has-an-older-ui),
[Root canvas](Root-Canvas.md#the-sorting-band) and [Migration](Migration.md).

---

## When something does not work

### Why does my view get no clicks?

Check these in order:

1. **There is no `EventSystem`, or its input module does not match the project's input handling.**
   The package logs [UIDIRECTOR116](Troubleshooting.md#uidirector116) once when the root initializes.
   See [Input and platform](Input-and-Platform.md#give-the-ui-an-eventsystem).
2. **The input shield is up.** A load, a transition, a close or a `HoldInput` call raises it, and
   everything under it gets no pointer input. In `Window > UIDirector` the status line reads `Busy`
   and the Shield holders section lists each holder with its age and call site. A hold that nothing
   ends is reported after 30 seconds as [UIDIRECTOR110](Troubleshooting.md#uidirector110). See
   [Input and platform](Input-and-Platform.md#when-it-is-up).
3. **The view is not interactive.** A view is interactive only when it is fully `Open`, not covered,
   not closing, and not below the top view of a modal layer. The package disables the raycasters of
   every other view. See [View authoring](View-Authoring.md#prefab-structure).
4. **Something else catches the pointer first:** a full-screen graphic with `Raycast Target` in your
   own prefab, or a canvas that sorts itself above the shield, such as the open list of a dropdown
   ([Troubleshooting](Troubleshooting.md#a-dropdown-list-stays-above-everything)).

### Why does a tap on the backdrop do nothing?

The backdrop closes the top popup only when that popup's row has `Close On Backdrop` set, only when
the popup is fully open, and not while the input shield is up. It closes the top view of the layer
only, never one below. Without the flag the tap is swallowed. See
[Popups and backdrop](Popups-and-Backdrop.md#close-on-backdrop-tap).

### Why does a popup draw under a screen?

Layers do not interleave: a popup's sorting order is above every screen's, however many views are
open. So look for one of these:

- **The popup's row is on the `screens` layer.** `screens` is the default `Layer Id` of a new row.
  A popup left there is a screen: it joins the history, and a screen opened later draws over it.
  Set the row's `Layer Id` to `popups`.
- **A canvas outside the package sorts above the band,** such as one of your own, a second root with
  a higher base, or a dropdown list.
- **Content inside the view sorts itself.** A nested canvas in a view should use the view's sorting
  order plus 1 to 4.

See [Root canvas](Root-Canvas.md#the-sorting-band), [Popups and backdrop](Popups-and-Backdrop.md)
and [Catalogs and keys](Catalogs-and-Keys.md).

### Why did a transition snap, and what is UIDIRECTOR106?

A show or hide transition that does not finish within the transition timeout (3 seconds by default)
is cancelled, the view is snapped to its end pose, and the package logs
[UIDIRECTOR106](Troubleshooting.md#uidirector106). The request is not lost. The usual cause is a
custom transition that animates on scaled time while `Time.timeScale` is 0; the built-in
transitions use unscaled time. A frame rate below 30 fps can also cause it: the built-in transitions
advance at most 1/30 s per frame and the timeout clock at most 0.1 s, so below 30 fps a long transition
takes longer than its duration and can run into the timeout (raise `Transition Timeout Seconds` or
shorten it). Navigation also snaps a view on purpose, with no animation, when it
closes popups that are older than a screen that opens. See [Transitions](Transitions.md#the-timeout),
[Limitations](Limitations.md#timeouts-defaults-and-the-frame-clock) and [DOTween](DOTween.md).

### How do I hide the UI during a cutscene?

Add a `CanvasGroup` to the root's object, set it to alpha 0 and not interactable, and take a
`HoldInput` while it is hidden; remove the hold and restore the group afterwards. Views keep their
state and are drawn again at once. Do not disable the root `Canvas` (nested view canvases keep
drawing) and do not deactivate the root or a layer object (that ends every view with
[UIDIRECTOR107](Troubleshooting.md#uidirector107)). Back still withdraws a request that is waiting or
loading on a layer that takes back, and a hold older than 30 seconds logs [UIDIRECTOR110](Troubleshooting.md#uidirector110). The
code and every caveat are on
[Hide the whole UI for a moment](Root-Canvas.md#hide-the-whole-ui-for-a-moment).

### Why does keyboard or gamepad navigation reach a view that is covered?

uGUI navigation starts from the selected object and, with `Automatic` navigation, searches every
`Selectable` in the scene. The package clears the selection when a view loses input but does not
restrict moves from the top view. Set the `Navigation` of the `Selectable` components in views you
drive with a keyboard, gamepad or TV remote to `Explicit`, so moves never leave the view, or give the
row `When Covered` = `Deactivate`, which unregisters the selectables of a screen while another screen hides it. A view that stays visible
under a popup is not deactivated, so for it `Explicit` navigation is the fix. See
[Limitations](Limitations.md#setup).

### Why is nothing logged in my release build?

Diagnostic lines are written in the editor and in development builds only. A release build logs no
`UIDIRECTOR` line. The outcome still reaches your code in `ViewResult.Rejected` and `Detail`, and
exceptions from your code reach the error handler in every build. See [Diagnostics](Diagnostics.md).

---

## Threads and async

### Can I call it from another thread or from a task?

Opening, registering and queueing must be on the main thread and throw anywhere else, with no
change made. A few stop-type calls and owner cancellations are posted to the main thread and take
effect up to one frame later. `await ShowAsync(...)` resumes on the main thread, because the
package completes the task there, so you do not need `UniTask.SwitchToMainThread()` after it. Several
calls, such as `ViewHandle.Close()` and the queries, are not checked at all and must not be made from
another thread. See [Threading](Threading.md).

---

## Optional integrations

### Do I need Addressables?

No. A catalog row with a `Prefab` reference needs no provider; the prefab is instantiated directly.
A row with an asset key is resolved by a view provider, and the package ships one for Addressables
that compiles only when the Addressables package is installed. Without a provider, a row with an
asset key fails with [UIDIRECTOR120](Troubleshooting.md#uidirector120). See
[Assets and Addressables](Assets-and-Addressables.md).

### Do I need dependency injection?

No. Injection is one delegate, `ViewRootSetup.Inject`, that runs on each new view instance while it
is inactive, before its `Awake`; the Basic Usage sample hands its services to views with it. A
Zenject or Extenject integration is included. It turns on by itself with Extenject 9.0.0 or newer
installed as a package; with Zenject copied under `Assets/`, choose `Tools/UIDirector/Zenject Integration`
once (see [Zenject](Zenject.md#turn-the-integration-on)). Another container is a few lines of your
own code. See [Zenject](Zenject.md) and
[Root canvas](Root-Canvas.md#initialize-with-viewrootsetup).

### Do I need DOTween?

No. The built-in transitions (fade, scale, slide) use no tween library. `TweenTransition` is an
optional integration that compiles only when DOTween is present. See
[Transitions](Transitions.md) and [DOTween](DOTween.md).

---

## Cost

### Does it allocate?

Nothing is allocated per frame while the UI is idle, nor when one event is dispatched to observers or
during a frame of a timed wait; a warm open and close of a plain screen stays under 8192 bytes.
See [Performance](Performance.md).

## See also

- [Concepts](Concepts.md): the model behind every answer
- [Troubleshooting](Troubleshooting.md): one section per diagnostic id, and symptoms without one
- [Limitations](Limitations.md): what the package does not do
- [Diagnostics](Diagnostics.md): the console, the window, the error handler and the validator
- [Migration](Migration.md): moving from ad-hoc screen scripts
- [API reference](API-Reference.md)
