# Root canvas

[Back to index](index.md)

This page is about the object everything else hangs on: the `ViewRoot` and the canvas under it. It
shows how to create a root, what every Inspector field does, how to initialize the root from code
with `ViewRootSetup`, how an overlay root differs from a camera root, how the sorting band is
numbered, how to add a layer of your own, what the Prefab Mode UI environment is for, and how to hide
the whole UI for a moment.

The examples come from the Basic Usage sample. Its root was made with the menu below, then
`Initialize On Awake` was turned off and the bootstrap initializes the root in code.

---

## Create a root

Choose `GameObject > UI > UIDirector Root`. In the active scene it creates one outermost object,
`UIDirector Root`, on the UI layer, with:

- a `Canvas` in `Screen Space - Overlay` mode;
- a `CanvasScaler` set to `Scale With Screen Size`, reference resolution 1080 x 1920, match 0.5;
- a `ViewRoot`;
- a `BackInput`, which turns the Escape key into `Back()`. Android's back button is expected to arrive
  as Escape ([Input and platform](Input-and-Platform.md#the-back-button)).

If the scene has no EventSystem, the menu also creates a separate outermost `EventSystem` object with
the input module that fits your project's input handling. It is not under the root, so Persist Across
Scenes does not keep it.

The root is built inactive and activated last, so its `Awake` sees the final render mode and camera.
The creation can be undone in one step. The canvas is yours: render mode, scaler, reference
resolution, sorting layer and shader channels are ordinary `Canvas` settings that you change as in any
uGUI project.

Then assign your catalogs in the `Catalogs` list ([Catalogs and keys](Catalogs-and-Keys.md)). With
`Initialize On Awake` left on, the root initializes itself in its `Awake` with default settings, and
`ViewRoot.Navigator` is ready from then on. Turn it off when you want to initialize the root from code.

### The camera variant

Choose `GameObject > UI > UIDirector Root (Camera)` when the UI must be drawn by its own camera. It
creates an outermost object `UIDirector` that holds two siblings:

```text
UIDirector                 outermost object, UI layer
  UIDirector Root          Canvas (Screen Space - Camera), CanvasScaler, ViewRoot, BackInput
  UI Camera                orthographic, clears depth only, draws the UI layer only
```

The camera clears depth only, so the scene stays visible under the UI. Its depth is set once, at
creation, to one above the highest camera depth in the loaded scenes. The new camera's own default of 0
counts, so the depth is at least 1 and the UI draws after the scene's cameras. The canvas takes the camera as its render camera, with a plane distance of 100.

The camera sits beside the root and not under it on purpose. A `Screen Space - Camera` canvas that is a
child of its own camera is placed at the camera's position and draws nothing.

---

## Initialize with ViewRootSetup

Numbers and references are Inspector fields. Code-only settings go through `ViewRootSetup`, passed to
`ViewRoot.Initialize` once, before anything reads `ViewRoot.Navigator`:

<!-- signature -->
```csharp
public void Initialize(ViewRootSetup setup);

public sealed class ViewRootSetup
{
    public Action<GameObject> Inject;
    public IViewProvider AssetProvider;
    public IReadOnlyList<ViewLayer> ExtraLayers;
    public ViewErrorHandler ErrorHandler;
}
```

The Basic Usage bootstrap keeps the root in a serialized field, turns `Initialize On Awake` off in the
Inspector, and initializes the root in its own `Awake`:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
// "Initialize On Awake" is off on this root; the bootstrap initialises it in Awake.
[SerializeField] private ViewRoot _viewRoot;
```

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

| Member | What it is | If left null |
|---|---|---|
| `Inject` | Runs on each new view instance while it is inactive, before its `Awake`. The seam for a container or for handing services to views. The sample's `InjectServices` gives its services to every component that asks. [Zenject](Zenject.md) shows a container as the injector | No injection |
| `AssetProvider` | Resolves catalog rows that carry an asset key ([Assets and Addressables](Assets-and-Addressables.md)) | An `IViewProvider` component on the root's object, if any; otherwise rows with an asset key fail with [UIDIRECTOR120](Troubleshooting.md#uidirector120) |
| `ExtraLayers` | Layers declared in code, added to the three defaults. See [Add a layer of your own](#add-a-layer-of-your-own) | The three default layers |
| `ErrorHandler` | Receives exceptions from your code and from providers, with the view id, the request id and the failing step | The package logs them with `Debug.LogException` |

The Addressables sample passes an empty setup, so the provider component on the root's object is used:

<!-- source: Samples~/AddressablesUsage/AddressablesBootstrap.cs -->
```csharp
// AssetProvider stays empty, so the AddressablesViewProvider on the root's GameObject is used.
_viewRoot.Initialize(new ViewRootSetup());
```

What `Initialize` does and does not allow:

- **It runs once.** A second call throws `InvalidOperationException` ("The root is already
  initialised"). It also throws when the root was already initialized by `Initialize On Awake`, or by a
  read of `ViewRoot.Navigator`, and when the root was destroyed. A null setup throws
  `ArgumentNullException`.
- **Reading `ViewRoot.Navigator` on a root that is not initialized initializes it with default
  settings.** So code that needs a `ViewRootSetup` must call `Initialize` before any read. A
  destroyed root that was never initialized throws on that read.
- **Components that read the root early do not initialize it.** Before the root is initialized,
  `BackInput` ignores the back key, and a `ViewButton` with the Back or Open View action logs a warning
  and does nothing. Apart from `Initialize On Awake`, only your own read of `ViewRoot.Navigator` can initialize the root
  with defaults.
- **Both of the above are why `Initialize On Awake` must be off** when you call `Initialize`: with the
  setting on, the root initializes itself in `Awake`, and whether your call comes first depends on which
  `Awake` runs first.
- **Another script's `Awake` can initialize the root first.** With `Initialize On Awake` on, a script
  that reads `ViewRoot.Navigator` in its own `Awake` before the root's `Awake` has run initializes the
  root with default settings. The root's own `Awake` then sees an initialized root and skips its
  initialization without an error, and a later `Initialize(setup)` throws. Unity does not order the
  `Awake` methods of different objects, so do not rely on it. Initialize from one bootstrap with the flag
  off, as above, or read `Navigator` in `Start`: every `Awake` of the objects in a scene has run before
  the first `Start`, so with the flag on the root is already initialized by then.
- **It warns once when the scene has no EventSystem** ([UIDIRECTOR116](Troubleshooting.md#uidirector116)).
  Create the EventSystem before the call, as the sample does with `SampleEventSystem.EnsureExists()`.
- **A failure inside `Initialize` leaves nothing behind.** The layer list is validated before anything is
  built, and if a later step fails, the objects built so far are destroyed and the exception is
  rethrown.
- **A root can be initialized while it is inactive.** A dependency injection container may create the
  root inactive, call `Initialize` and activate it afterwards. Teardown happens in `OnDestroy`, which Unity
  sends only to an object that has been active; a root that is destroyed without ever becoming active
  never tears down ([Limitations](Limitations.md#lifetime-and-cleanup)).
- `ViewRoot.IsInitialized` is true once `Initialize` has completed, also after the root was destroyed.

---

## Every field of ViewRoot

`ViewRoot` requires a `Canvas` on the same object and allows one `ViewRoot` per object. Its
Inspector fields:

| Field | Default | What it does |
|---|---|---|
| `Catalogs` | empty | `ViewCatalog` assets registered, in list order, when the root initializes. Add more later with `INavigator.AddCatalog`. Removing a catalog that was added that way leaves its open views open; they end only when their owner ends |
| `Base Sorting Order` | 0 | Sorting order where the package's band starts. Clamped to the 16-bit range in the Inspector. See [The sorting band](#the-sorting-band) |
| `Persist Across Scenes` | off | Keeps the root's outermost object across scene loads. See [Persist Across Scenes](#persist-across-scenes) |
| `Initialize On Awake` | on | Initializes the root with default settings in `Awake`. Turn it off to call `Initialize` with a `ViewRootSetup` |
| `Load Timeout Seconds` | 15 | Unscaled seconds a view may take to load. Then the open ends as not shown with `RejectReason.LoadTimedOut` ([UIDIRECTOR105](Troubleshooting.md#uidirector105)). Minimum 0.1 |
| `Transition Timeout Seconds` | 3 | Unscaled seconds a transition may run. Then it is snapped to its end pose ([UIDIRECTOR106](Troubleshooting.md#uidirector106)). Minimum 0.1 |
| `Indicator Delay Seconds` | 0.4 | How long a load on a layer that shields input runs before the `Load Indicator` is shown. Minimum 0 |
| `Waiting Cap` | 4 | Requests one layer may hold waiting. The next is refused as `Busy` ([UIDIRECTOR112](Troubleshooting.md#uidirector112)). Minimum 1 |
| `Load Indicator` | none | An object the package activates while a load runs longer than the delay, and deactivates when no load is running |
| `Backdrop Color` | black, 60 percent opacity | Colour of the generated backdrop behind modal layers. Unused when a backdrop prefab is set |
| `Backdrop Prefab` | none | Instantiated behind each modal layer instead of the generated backdrop |

The Inspector keeps the minimums; a value below its minimum is raised when the field changes. The
timeouts and their clock are explained on
[Limitations](Limitations.md#timeouts-defaults-and-the-frame-clock).

**The sorting layer is not a field of `ViewRoot`.** It is the `Sorting Layer` of the root's `Canvas`, and
the package reads it every time it places a view, the shield, a backdrop or the load indicator. A
project that changes it at run time moves the whole band.

Details that the table cannot hold:

- **Load Indicator.** Start the object inactive. The package activates it when a load runs longer
  than the delay and deactivates it when no load is running. The indicator shows only for a
  load on a layer that shields input, which is the case for the three default layers. It gets its own
  canvas one order above the shield, and no raycaster, so it takes no input. A canvas that you put on
  it is reused: the package turns on its Override Sorting and sets its sorting layer and order as it
  would for a canvas it adds.
- **Backdrop Prefab.** The package instantiates it once for each modal layer under that layer, stretches
  it over the layer, and adds the `Canvas`, `GraphicRaycaster`, `CanvasGroup` and backdrop component it
  lacks. Give it a graphic with `Raycast Target` on, so a tap reaches it. A backdrop tap, its fade and
  the rule that a tap while the shield is up does nothing are on
  [Popups and backdrop](Popups-and-Backdrop.md).
- **Timeouts and the clock.** The root advances its clock in `Update` by the unscaled frame time, at
  most 0.1 s per frame, so a long hitch counts as one short step. Built-in transitions and the backdrop
  fade advance at most 1/30 s per frame, so below 30 fps they take longer than their duration, and a
  long one can be snapped by the transition timeout
  ([Limitations](Limitations.md#timeouts-defaults-and-the-frame-clock)).

---

## Overlay roots and camera roots

| | Overlay | Camera |
|---|---|---|
| Menu | `UIDirector Root` | `UIDirector Root (Camera)` |
| Canvas render mode | `Screen Space - Overlay` | `Screen Space - Camera`, render camera is the `UI Camera` |
| Outermost object | The root itself | The `UIDirector` holder with the root and the camera as siblings |
| Sorting layer | None: an overlay canvas has no sorting layer, and `Base Sorting Order` places the band | The `Sorting Layer` of the root canvas, plus `Base Sorting Order` |
| What the camera draws | No camera | The UI layer only |
| Content must be on | Any layer | The UI layer (layer 5): an object on another layer is culled |
| `Persist Across Scenes` | Keeps the root | Keeps the holder, so the camera survives with the root |
| If the camera is destroyed | Not applicable | Views may not render; logged as [UIDIRECTOR114](Troubleshooting.md#uidirector114) when the next view loads, once for each stretch of time the camera is missing |

Pick the overlay root unless the UI needs a camera of its own. Four rules for the camera root:

- **Keep the camera beside the root, never under it or under its canvas.**
- **Keep the camera alive as long as the root.** The holder layout does that; with `Persist Across
  Scenes` the camera persists with the root. A camera that lives elsewhere in the scene does not.
- **Put every object of a view on the UI layer,** including objects you create inside a view at run
  time. The camera's culling mask is the UI layer only. See [View authoring](View-Authoring.md).
- **Reassign `Canvas.worldCamera` if you replace the camera.** Unity reads a camera-mode canvas
  whose camera is gone as an overlay canvas; the package remembers the mode the root started with
  and warns with [UIDIRECTOR114](Troubleshooting.md#uidirector114) when the next view loads, once for
  each stretch of time the camera is missing.

World-space canvases are not supported ([Limitations](Limitations.md)).

---

## Persist Across Scenes

`Persist Across Scenes` calls `DontDestroyOnLoad` on the outermost object of the hierarchy the root
belongs to, in the root's `Awake`. It does not depend on `Initialize On Awake`. With the menu layouts
that is the root itself (overlay) or the `UIDirector` holder (camera), so the camera survives with the
root. If you parent the root under a larger object of your own, that whole object persists.

What it does not do:

- **It does not move an EventSystem.** The package keeps no EventSystem of its own. If the scene that
  loaded first owned the only one, the next scene has none and views stop receiving input. Keep exactly
  one EventSystem active at any time: put it under the persisted object, or create it from the code that
  starts the persisted root.
- **It does not load or unload scenes, and it closes nothing on a scene change.** A view lives as long
  as its owner token. Own long-lived views with `INavigator.RootToken`
  ([Limitations](Limitations.md)).
- **It does not stop a second root.** The root is not a singleton. Loading the scene that contains a
  persisted root again creates another root next to the first. Guard that in your own start-up code.

Without the setting a scene change destroys the root with its scene. Every view it holds ends, and the
awaiters complete: shown views with `CloseReason.Destroyed`, the others as not shown with
`RejectReason.RootGone`. A root that is destroyed this way logs nothing and `RootToken` is cancelled.

---

## The sorting band

Every view is a nested canvas with `Override Sorting` on. The root gives each one a sorting order
from a fixed formula, so the order depends only on the layer and on the view's slot:

```text
order = Base Sorting Order + layer order x 1000 + slot x 10
```

`slot` runs from 1 to 99 on each layer. A new view gets the highest live slot of its layer plus one,
and keeps it while it lives. The other objects sit relative to the layers:

| Object | Sorting order | With base 0 and the sample's `toasts` layer |
|---|---|---|
| A view on layer `L`, slot `s` | base + `L` order x 1000 + `s` x 10 | First popup: 1010. Second popup: 1020. First toast: 3010 |
| Reserved for canvases you nest in a view (you set them) | The view's order + 1 to + 4 | 1011 to 1014 for the first popup |
| The backdrop of a modal layer | The top on-screen view's order - 5 | 1005 under the first popup |
| The input shield | base + (highest layer order + 1) x 1000 | 4000. Without `toasts`: 3000 |
| The load indicator | The shield's order + 1, on its own canvas | 4001 |

Consequences:

- **Layers never interleave,** as long as every layer has its own order and keeps to its 99 slots. A
  popup is above every screen and a toast above every popup, however many views are open.
- **The shield and the indicator are above everything the package shows.** They sit above the highest
  layer, so a layer you add moves them up.
- **A view owns the four orders above its own.** Put particle systems or canvases that belong to a view
  on `ViewHost.SortingOrder + 1` to `+ 4`.
- **Your own canvases need a place in the band.** A HUD canvas that should always be under the views
  sorts below `Base Sorting Order`; one that should cover everything sorts above the shield. A value in
  between interleaves with views. A canvas that sorts itself above the shield, such as the open list of
  a TextMesh Pro dropdown, is not blocked by the shield
  ([Popups and backdrop](Popups-and-Backdrop.md#the-input-shield)).
- **Two roots on one sorting layer need bases that keep their bands apart.** Nothing coordinates them.

### Canvas.sortingOrder is 16-bit and wraps silently

Unity stores `Canvas.sortingOrder` as a signed 16-bit value. A larger number does not throw: it wraps
to a negative one, and a view would draw under everything. The package therefore never assigns a number
outside the range. Before it accepts a request it checks that the view's order, with its four nested
orders, stays below 29999, that the backdrop's order stays in the 16-bit range, and that the shield
stays below 29999. A request that does not fit ends as not shown with `RejectReason.Busy` and logs
[UIDIRECTOR115](Troubleshooting.md#uidirector115), an error.

The causes are a `Base Sorting Order` near the top of the range, a layer of your own with a very high
order (each layer takes 1000 orders), or 99 live views on one layer. Slots come back only when the views
on top of a layer end. The Inspector clamps the base to the 16-bit range, which is not enough to stay
inside the band: keep the base low.

---

## Add a layer of your own

A layer is an immutable value: an id, an order and a set of rules. Declare it as a `static readonly`
field so that every place that needs it reads the same value. The Basic Usage sample declares a layer
for toasts above the system layer, with no rules, so a toast neither blocks a tap nor takes back:

<!-- source: Samples~/BasicUsage/AppLayers.cs -->
```csharp
// Above the system layer, no history, no back and no input shield: a toast never blocks a tap.
public static readonly ViewLayer Toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);
```

Hand it to the root in `ViewRootSetup.ExtraLayers` (the `Initialize` call above does), and name its id
in the `Layer Id` field of a catalog row. The root merges your layers with the three defaults and sorts
them by order; layers with the same order keep the order you gave them.

- **The order decides where it draws.** A layer with order 3 is drawn above `system` (2) and, through
  the formula above, gets the orders from 3000 up.
- **The rules decide how it behaves.** `ViewLayerRules.None` is a plain layer: it holds no shield,
  takes no back and covers nothing. Combine the flags of `ViewLayerRules` for more; they are explained
  on [Concepts](Concepts.md#one-root-layers-with-rules).
- **The validator finds it.** `Tools/UIDirector/Validate Catalogs` reads every static `ViewLayer`
  field of the assemblies that reference the package, so a row naming `toasts` is accepted. A layer
  that is not in a static field is unknown to it, and its rows are reported as
  [UIDIRECTOR109](Troubleshooting.md#uidirector109).
- **Give each layer an order of its own.** Two layers with the same order get the same sorting orders
  and their views interleave.
- **Ids are unique.** An extra layer whose id is already used, including `screens`, `popups` and
  `system`, or the same layer listed twice, makes `Initialize` throw `ArgumentException` with the id in
  its message. The check runs before anything is built, so nothing is left under the root. A `null` entry
  is skipped.
- **A row can name only layers the root has.** On a root without the layer the row is refused when its
  catalog is registered ([UIDIRECTOR109](Troubleshooting.md#uidirector109)), and opening that id ends as
  not shown with `RejectReason.UnknownKey` ([UIDIRECTOR101](Troubleshooting.md#uidirector101)).

The view on that layer is an ordinary view. The sample's toast closes itself after two seconds, on
unscaled time:

<!-- source: Samples~/BasicUsage/ToastView.cs -->
```csharp
private async UniTask CloseAfterDelay(CancellationToken ct)
{
    // Unscaled time, so a paused game (timeScale 0) still dismisses the toast.
    await UniTask.Delay(ShowTime, DelayType.UnscaledDeltaTime, cancellationToken: ct);
    Close();
}
```

Because the layer has no `ShieldsInput`, opening a toast never raises the input shield, and the screen
under it stays usable. Because it has no `TakesBack`, a toast never swallows a back press. The full
rules of the other layers are on [Popups and backdrop](Popups-and-Backdrop.md) and
[System overlays](System-Overlays.md).

---

## The Prefab Mode UI environment

A view prefab has a `Canvas` on its root (`ViewHost` requires it), and no `CanvasScaler`: the scaler is
on the root canvas. Opened in Prefab Mode, the prefab then has no scaler above it and opens at the wrong
size. Unity lets a project set a UI environment scene for Prefab Mode, and the package can write one
that matches your root.

Choose `Tools > UIDirector > Create Prefab UI Environment`. With a root selected (or one of its
children), the environment copies that root's canvas settings (render mode, plane distance, sorting
layer and order, pixel perfect, shader channels) and its `CanvasScaler`. With nothing selected it writes
an overlay canvas with a default scaler. The menu then asks where to save the scene and whether to
assign it as the project's Prefab Mode UI environment.

- The first time you create a root in an editor session, the package offers to create the environment
  once, when no UI environment is assigned. It does not offer in batch mode and never saves your open
  scene.
- The scene must be saved under `Assets/` and end in `.unity`. It cannot be created in Play Mode, over
  an open scene, or while an untitled scene is open: save that scene first.
- A camera is not copied. The environment scene has none, so a camera-mode canvas renders as an
  overlay canvas there.

With the environment assigned, Prefab Mode places a nested-canvas view prefab under the environment's
canvas and lays it out in the scaler's reference units. With a 1080 x 1920 scaler the view fills the
scaled canvas at the Scene view's aspect; without an environment Unity's default canvas has no scaler and
the same view is laid out in Scene view pixels.

---

## Hide the whole UI for a moment

To hide every view for a cutscene or a screenshot without ending any of them, add a `CanvasGroup` to the
root's object, set it to alpha 0 and not interactable, and hold input while it is hidden. Undo all of it
afterwards. The `rootCanvas`, `navigator` and `owner` below are the root's `Canvas`, its navigator and the
token that owns the hold:

<!-- source: Tests/Runtime/Stage/HideUiRecipeTests.cs -->
```csharp
var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
group.alpha = 0f;
group.interactable = false;
var hold = navigator.HoldInput("cutscene", owner);
```

And later:

<!-- source: Tests/Runtime/Stage/HideUiRecipeTests.cs -->
```csharp
hold.Remove();
group.alpha = 1f;
group.interactable = true;
```

To restore exactly, remember what was there. If the root already had a `CanvasGroup`, read its `alpha`
and `interactable` before you change them and write the same values back, instead of 1 and true. If you
added the group yourself, either keep it and reset it to alpha 1 and interactable true, as the recipe
does, or `Destroy` it when the hide ends.

Each part has a job. Alpha 0 on the root group hides every uGUI graphic under the root: the views, the
backdrops, the input shield, and the load indicator when it is a child of the root (an indicator placed
elsewhere in the scene keeps drawing). The group reaches the view canvases because a nested
canvas that does not set `ignoreParentGroups` inherits the alpha of its parents. `interactable = false`
stops keyboard and gamepad navigation, because `Selectable.IsInteractable` walks the groups above a
selectable across nested canvases. The hold raises the input shield, which takes pointer input. Views that
are opened, closed or restored while the UI is hidden behave as usual and are drawn again when the group
is restored. Keep the group and reuse it; take a new hold for each hide.

What to avoid and what to expect:

- **Do not disable the root `Canvas` component.** Each view is a nested canvas with its own sorting, and a
  disabled parent canvas does not stop it from drawing, and it keeps its own
  raycaster. The same goes for `blocksRaycasts = false` on the root group, which
  does not reach the nested canvases. The shield from `HoldInput` is what blocks input.
- **Do not deactivate the root object or a layer object.** That ends every view under it with
  [UIDIRECTOR107](Troubleshooting.md#uidirector107).
- **A view `CanvasGroup` with `ignoreParentGroups` set ignores the root group.** That view stays visible.
  Clear the flag on views that should hide.
- **Back is swallowed while the hold is up, except that it still withdraws a request that is waiting or loading.**
  While the hold is up, `Back()` closes no shown view and returns true, so `BackInput.Unhandled` is not
  raised. It does end the newest request that is
  still waiting or loading on a layer that takes back, unless its row's `Back` is `Ignore`, as not shown
  with `RejectReason.Cancelled`.
  To ignore the back key altogether during the hide, set `BackInput.enabled = false` and enable it again
  afterwards. A disabled `BackInput` does not read the key at all (with the Input System package its
  action is disabled too). Disable the component, not the object it sits on: with the menu layout that
  object is the root.
- **`ViewButton` clicks are blocked while the hold is up.** A pointer press reaches the input shield
  before it reaches the button, so a `ViewButton` with any action does not fire. `interactable = false`
  on the root group covers keyboard and gamepad submit.
- **A hold older than 30 seconds logs [UIDIRECTOR110](Troubleshooting.md#uidirector110).** Remove it when
  the hide ends.
- **Hidden views still run.** `Update`, animators and transitions keep running under alpha 0, so the cost
  is not zero. Particle systems and other renderers that are not uGUI graphics ignore the group and keep
  drawing: stop or disable them for the hide. For a long hide, close the views instead.
- **The group is yours to restore.** When `owner` ends, the hold is removed but the group stays at alpha 0
  and not interactable, so the UI stays hidden. Restore the group in the same place that ends the owner,
  or use an owner that lives as long as the code that restores it.

---

## Traps

- **`Initialize On Awake` on while `Initialize` is called from code.** The second initialization
  throws. Turn the setting off.
- **A read of `ViewRoot.Navigator` before `Initialize`.** It initializes the root with defaults and the
  later `Initialize` throws.
- **A camera root whose objects are not on the UI layer.** The views exist but nothing is drawn.
- **A `UI Camera` placed under the root.** The canvas draws nothing.
- **A persisted root and no EventSystem in the next scene.** Views are drawn and do not react.
- **A very high `Base Sorting Order` or layer order.** Requests are refused with
  [UIDIRECTOR115](Troubleshooting.md#uidirector115); the sorting order would otherwise wrap.
- **A Load Indicator left active in the scene.** It shows from the start until a load that outlasts the
  delay ends, and is then switched off.
- **Deactivating the root object or a layer object.** It ends every view under it. Hide the whole UI with
  the group and hold in [Hide the whole UI for a moment](#hide-the-whole-ui-for-a-moment) instead.
- **Disabling the root `Canvas` to hide the UI.** Nested view canvases keep drawing.

## See also

- [Concepts](Concepts.md)
- [Catalogs and keys](Catalogs-and-Keys.md)
- [Popups and backdrop](Popups-and-Backdrop.md)
- [System overlays](System-Overlays.md)
- [View authoring](View-Authoring.md)
- [Input and platform](Input-and-Platform.md)
- [Zenject](Zenject.md)
- [Assets and Addressables](Assets-and-Addressables.md)
- [Limitations](Limitations.md)
- [Troubleshooting](Troubleshooting.md)
- [Architecture: the root and its layers](Design/Architecture.md#2-the-root-and-its-layers)
