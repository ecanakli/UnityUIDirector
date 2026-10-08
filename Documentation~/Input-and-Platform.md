# Input and platform

[Back to index](index.md)

This page covers what the UI needs from the platform and how the package treats input: the
EventSystem and the input module, what the input shield blocks and when it is up, double taps and
several fingers, blocking input from code with `HoldInput`, time scale 0, the Android back button,
scenes, and the Enter Play Mode options.

---

## Give the UI an EventSystem

uGUI delivers no click or touch without an `EventSystem` and an input module in the scene. The package
does not create one. When a root initializes and the scene has no `EventSystem`, it logs
[UIDIRECTOR116](Troubleshooting.md#uidirector116) once. Views are still shown, and they get no input.

Three ways to have one:

- **The create-root menu.** `GameObject > UI > UIDirector Root` adds an `EventSystem` when the scene has
  none ([Root canvas](Root-Canvas.md#create-a-root)).
- **`GameObject > UI > Event System`,** if you built the root by hand.
- **Code,** when the scene is assembled at start-up. Create it before the root initializes, so the check
  finds it. The Basic Usage bootstrap does this in the first line of its `Awake`:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
// Before the root initialises, so the root finds an EventSystem.
SampleEventSystem.EnsureExists();
```

### Input System or the legacy input module

The input module has to match the project's Active Input Handling (`Project Settings > Player`). A
scene that bakes in one module breaks in a project with the other setting: the module is a missing
script, or it throws when it reads input. That is why the samples ship scenes without an EventSystem
and create it at start:

<!-- source: Samples~/BasicUsage/Support/SampleEventSystem.cs -->
```csharp
        public static void EnsureExists()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            eventSystem.AddComponent<InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<StandaloneInputModule>();
#endif
        }
```

| Active Input Handling | Defines Unity sets | Module created |
|---|---|---|
| Input System Package (New) | `ENABLE_INPUT_SYSTEM` | `InputSystemUIInputModule` |
| Both | `ENABLE_INPUT_SYSTEM` and `ENABLE_LEGACY_INPUT_MANAGER` | `InputSystemUIInputModule` |
| Input Manager (Old), or no Input System package | `ENABLE_LEGACY_INPUT_MANAGER` | `StandaloneInputModule` |

`UIDIRECTOR_SAMPLES_INPUT_SYSTEM` is a define that the sample's assembly definition sets through
`versionDefines` when `com.unity.inputsystem` is installed. Your own assembly definition needs both: a
reference to `Unity.InputSystem` to use its types, and the same `versionDefines` entry so the guarded
code still compiles in a project without the package. The create-root menu makes the
same choice with the package's own define.

`FindAnyObjectByType<EventSystem>()` finds active objects only. An EventSystem that is deactivated does
not count as present.

---

## What the input shield blocks

The input shield is a counted, full-screen blocker above every layer. It is one graphic with an empty
mesh on its own canvas, always active, and only its `Raycast Target` switches. Because it never
activates or deactivates, it blocks in the same frame in which it is raised, inside the `Open` call.

What it blocks:

- **Pointer input to everything the package shows.** Mouse clicks and touches raycast to the shield
  first, so no button under it receives them. This is uGUI's own raycasting; it needs no code in your
  view.
- **The backdrop.** A tap on a backdrop while the shield is up does nothing, so a tap cannot close a
  popup in the middle of a transition ([Popups and backdrop](Popups-and-Backdrop.md)).
- **Submit and Cancel on the selected control.** When the shield goes up, the package clears the
  EventSystem's selection unless it is selecting right now, because those keys go to the selection and
  would pass the shield.
- **The back key.** `Back()` is swallowed while the shield is up
  ([Navigation and back](Navigation-and-Back.md#resolution-order)).

What it does not block:

- **Code.** Calling `Open`, `ShowAsync` or `ViewHandle.Close()` still works; the shield is not a lock
  on the navigator. `Back()` is the exception, see above.
- **A canvas that sorts itself above the shield,** such as the open list of a TextMesh Pro dropdown, or
  interface the package does not own with a higher sorting order
  ([Root canvas](Root-Canvas.md#the-sorting-band)).
- **A press that began before the shield was raised and ends after it was lowered,** which is delivered
  if the same control is still under the pointer.

### When it is up

The shield holds a list of reasons, and it is up while the list is not empty. Each reason ends on every
path, including a failed request and a destroyed root.

| Reason | Taken | Released |
|---|---|---|
| An open, a load or another operation on a layer with `ShieldsInput` | In the `Open` or `ShowAsync` call that is accepted | When the open has finished, unless a close is pending |
| A close | When the close is decided | When the view is gone |
| A pop-to or close-all | When it is accepted | When it ends |
| `HoldInput` | In the call | When its owner ends, or on `Remove()` |

The three default layers have `ShieldsInput`. A layer without it, such as the sample's `toasts` layer,
never takes the shield. `INavigator.IsBusy` is true while an operation runs or waits, or an input hold
exists.

Operations are bounded: a load by the load timeout (15 s by default) and a transition by the transition
timeout (3 s by default), so a view that never finishes does not keep the UI blocked for good. A hold
from `HoldInput` has no such bound; it is reported after 30 seconds ([below](#block-input-yourself-with-holdinput)).

The Basic Usage sample shows the shield in a number of places. Open `Window > UIDirector` in Play Mode
and look at `Shield holders` during a transition: each holder is listed with its kind, its request, its
age and the call site.

---

## Double taps and several fingers

**A double tap on a button that opens a view opens it once.** The first tap calls `Open` or `ShowAsync`,
which raises the shield in the same call. The second tap raycasts to the shield and not to the button.
One popup opens and nothing is logged. `SettingsView` in the Basic Usage sample is the example; the
walk-through with the code is on [Popups and backdrop](Popups-and-Backdrop.md#the-input-shield).

Two more guards stand behind the shield:

- An id that is already open or in flight on a layer that is not a history layer is refused as
  `RejectReason.Duplicate`, unless the row sets `Allow Multiple`. On a history layer the second open
  returns to the screen that exists.
- A handler that can fire many times can ask not to wait: `OpenOptions.RejectWhenBusy` refuses the call
  while `IsBusy` is true ([Popups and backdrop](Popups-and-Backdrop.md#rejectwhenbusy)).

**A layer without `ShieldsInput` has no shield guard.** Taps on a button that opens a toast reach the
button every time, and only the duplicate rule limits them.

**Several fingers.** The shield is a raycast target, so it blocks a second finger exactly as it blocks
the first: each pointer is raycast on its own and lands on the shield once it is up. The case that
matters is two fingers released in the same frame, where the first one's click raises the shield. The
second pointer is then blocked and its control gets no click.

The package does not serialize presses. Two fingers that press two different buttons of one view while
nothing raises the shield between them are two ordinary clicks.

---

## Block input yourself with HoldInput

`INavigator.HoldInput` raises the shield without a view, for as long as you need it:

<!-- signature -->
```csharp
ViewRegistration HoldInput(
    string reason,
    CancellationToken owner,
    [CallerMemberName] string member = null,
    [CallerLineNumber] int line = 0);
```

Use it when work must not be interrupted by a tap and no overlay is wanted, for example while code
finishes a short animation of your own. `reason` is a short text for the Console and the editor window.
`member` and `line` are filled in by the compiler; do not pass them.

- **The owner ends the hold.** The shield goes down when `owner` is cancelled or when you call `Remove()`
  on the returned `ViewRegistration`. `Remove()` is safe to call twice. Pass a token that ends with the
  work, so the hold needs no line on the way out.
- **It counts.** The shield stays up until every reason, from views and from holds, has ended.
- **It makes `Back()` return `true` without closing a shown view,** and `IsBusy` true. A request still
  waiting or loading on a layer that takes back is withdrawn first
  ([Navigation and back](Navigation-and-Back.md#resolution-order)).
- **A hold older than 30 seconds is reported once** with its reason and call site
  ([UIDIRECTOR110](Troubleshooting.md#uidirector110)). The hold keeps blocking until it ends.
- **A token that is already cancelled, or a destroyed root, returns an inactive registration** and
  raises nothing. A null reason throws `ArgumentNullException`. Off the main thread it throws
  `InvalidOperationException` ([Threading](Threading.md)).

For an overlay that tells the player something is happening, open a view on the `system` layer
instead ([System overlays](System-Overlays.md)).

---

## Time scale 0

The UI keeps working while `Time.timeScale` is 0, for example under a pause menu. Everything the
package times runs on unscaled time:

- The root advances its clock in `Update` with `Time.unscaledDeltaTime`, at most 0.1 s per frame. The
  load timeout, the transition timeout, the load indicator delay and the 30-second hold warning count
  on that clock.
- The built-in transitions (fade, scale and slide) and the backdrop fade run on unscaled time, advancing
  by at most a thirtieth of a second per frame, so below 30 frames per second they run longer than
  their duration.
- The queue's delays are unscaled.

Your own code has to follow. A transition of your own must animate on unscaled time, or it never
finishes at time scale 0 and the transition timeout snaps it
([UIDIRECTOR106](Troubleshooting.md#uidirector106)); see [Transitions](Transitions.md). The same holds
for a DOTween transition ([DOTween](DOTween.md)). A delay inside a view uses the unscaled delay type, as
the sample's toast does:

<!-- source: Samples~/BasicUsage/ToastView.cs -->
```csharp
// Unscaled time, so a paused game (timeScale 0) still dismisses the toast.
await UniTask.Delay(ShowTime, DelayType.UnscaledDeltaTime, cancellationToken: ct);
```

A default `UniTask.Delay` runs on scaled time and never returns while the game is paused. Animators,
particle systems and tweens inside a view follow Unity's rules: they stand still at time scale 0 unless
they are set to unscaled time.

---

## The back button

`BackInput` polls the Escape key once per frame and calls `Back()` on its root's navigator. Where
Unity reports Android's back button as the Escape key, one component serves both (see
[On a device](#on-a-device)). The
create-root menu adds it to the root. What it does with the answer is on [Navigation and back](Navigation-and-Back.md#backinput):
when `Back()` returns `false`, it raises `Unhandled` and the game decides.

It reads the key from the Input System when that package is installed and active, and from the legacy
input manager otherwise. When neither is enabled it does nothing.

- **With the Input System,** `BackInput` owns an `InputAction` bound to `<Keyboard>/escape` and checks
  it with `WasPressedThisFrame` in `Update`. The action is created when the component is enabled and
  disposed when it is destroyed. It binds every keyboard, including one that appears later, and a press
  whose down and up arrive in the same input update still counts once. A key already held when the
  component is enabled does not fire. When the Input System has no keyboard device, it does nothing.
- **With the legacy input manager,** it checks `Input.GetKeyDown(KeyCode.Escape)` in `Update`.
- **Before the root is initialised,** a press does nothing: reading the navigator then would initialise
  the root with default settings. A `ViewButton` set to `Back` or `OpenView` under a root that is not
  initialised does nothing either, and logs a warning.

The Basic Usage bootstrap listens to `Unhandled` to ask whether to quit:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
// No removal needed: the bootstrap and the root live and die together.
_backInput.Unhandled.AddListener(OnBackUnhandled);
```

A project that needs a different key, or that must ask another UI first, replaces `BackInput`. The sample
ships `HardwareBackRouter` for that. It reads the key the same way as `BackInput`: with the Input System
through an `InputAction` it owns, otherwise through the legacy input manager.

<!-- source: Samples~/BasicUsage/HardwareBackRouter.cs -->
```csharp
#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        private void OnEnable()
        {
            // An owned action binds every keyboard and keeps a press whose down and up land in one input update.
            if (_backAction == null)
                _backAction = new InputAction("Sample Back", InputActionType.Button, "<Keyboard>/escape");

            _backAction.Enable();
        }

        private void OnDisable()
        {
            if (_backAction != null)
                _backAction.Disable();
        }

        private void OnDestroy()
        {
            if (_backAction != null)
            {
                _backAction.Dispose();
                _backAction = null;
            }
        }
#endif

        private void Update()
        {
#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            if (_backAction != null && _backAction.WasPressedThisFrame())
                HandleBack();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
                HandleBack();
#endif
        }
```

Put it on the root instead of `BackInput`, never both: with both, one press is handled twice. The
router's `HandleBack`, and why the older UI is asked first, are on
[Navigation and back](Navigation-and-Back.md#a-project-that-still-has-an-older-ui).

### On a device

The package polls Escape and nothing else. It does not manage `Input.backButtonLeavesApp`, it offers no
predictive back animation, and it has no code for Android's predictive back gesture or its setting.

Whether the back button arrives as the Escape key depends on the device and on Unity's input
handling; the package only sees the key. In the editor, back is the Escape key, and the Game view needs
focus to receive it.

---

## Scenes and Persist Across Scenes

The package never loads a scene and closes nothing when a scene changes. What happens to the UI follows
from the root:

- **A root without `Persist Across Scenes` is destroyed with its scene.** Every view under it ends
  silently and `RootToken` is cancelled.
- **A root with it survives the load.** Its outermost object is kept, and with the camera variant the
  UI camera is kept too. Views stay open until their owners end them.
- **The EventSystem is not kept for you.** A persisted root needs exactly one EventSystem at all times:
  put it under the persisted object, or create it from the start-up code. See
  [Root canvas](Root-Canvas.md#persist-across-scenes).
- **Own a view with `RootToken`** when it must outlive the scene that opened it, and with a token of the
  scene when it must not ([Concepts](Concepts.md)).

---

## Enter Play Mode options

Unity can enter Play Mode without a domain reload, which keeps static fields between runs. The runtime
code of the package keeps no mutable static state that a new session could trip over: the three default
layers and every key are immutable values, the one static collection is a cache of text suffixes for
log messages, and the one static flag, which records that the application is quitting, is reset by a
hook that runs when Play Mode starts. Entering Play Mode with domain reload disabled needs no reset
call from you.

Check your own code instead. A `static` field that holds a
service, an event handler or a cancellation source survives into the next Play Mode session.

---

## Traps

- **No EventSystem, or the wrong module for the input handling.** Views draw and do not react
  ([UIDIRECTOR116](Troubleshooting.md#uidirector116)).
- **A scene that bakes in an input module.** It breaks in a project with the other input handling. Create
  the EventSystem in code, as the samples do.
- **Both `BackInput` and your own router on the root.** One press is handled twice.
- **A hold that nothing ends.** The UI stays blocked and, after 30 seconds, logs
  [UIDIRECTOR110](Troubleshooting.md#uidirector110).
- **Scaled time inside a view or a transition at time scale 0.** The code never continues; a transition
  is snapped after the timeout.
- **A persisted root with no EventSystem in the next scene.** Views react to nothing.

## See also

- [Root canvas](Root-Canvas.md)
- [Navigation and back](Navigation-and-Back.md)
- [Popups and backdrop](Popups-and-Backdrop.md)
- [System overlays](System-Overlays.md)
- [Transitions](Transitions.md)
- [Threading](Threading.md)
- [Limitations](Limitations.md)
- [Troubleshooting](Troubleshooting.md)
- [Architecture: the input shield](Design/Architecture.md#7-the-input-shield)
