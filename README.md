# UIDirector

Runs your Screens, Popups & Overlays. One root, one way back, one queue.

UIDirector is a navigation layer for uGUI. Views are prefabs under one persistent root, opened by typed keys that carry their argument and result types, and `ShowAsync` completes with a result that says how the view ended instead of throwing. Screens keep a history stack, popups share a backdrop, system overlays block input, one back rule decides what a back press does, and an optional priority queue presents notices one at a time.

## Requirements

| | |
|---|---|
| Unity | 6000.0 or newer. |
| [UniTask](https://github.com/Cysharp/UniTask) | Required. Install it before this package; a package installed from a git URL cannot declare it as a dependency. |
| `com.unity.ugui` 2.0.0 | Required. It is declared as a dependency, so a git install brings it in when your manifest does not list it. It includes TextMesh Pro, which the samples use. |
| Addressables (`com.unity.addressables`) | Optional, for views loaded by address. |
| DOTween | Optional, for `TweenTransition` and your own transitions on DOTween. |
| Zenject or Extenject | Optional, for injecting services into views. |
| Input System (`com.unity.inputsystem`) | Optional. `BackInput` reads the back key from it when it is installed and active, and from the legacy input manager otherwise. |

## Install

Install UniTask first. In the Package Manager open the **+** menu, choose **Add package from git URL**
(newer editors label it **Install package from git URL**) and enter:

```
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
```

UniTask documents its other install routes in its own README. Then add UIDirector the same way:

```
https://github.com/ecanakli/UnityUIDirector.git#v0.1.1
```

Or add both lines to the `dependencies` of `Packages/manifest.json` (the order does not matter):

```json
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
"com.ecanakli.uidirector": "https://github.com/ecanakli/UnityUIDirector.git#v0.1.1"
```

Always install a tag. Without `#v0.1.1` the install tracks the default branch.

### Then, depending on your setup

- **Your code is in assembly definitions.** Reference `Ecanakli.UIDirector` and `UniTask`. Add
  `Ecanakli.UIDirector.Addressables` to use `AddressablesViewProvider` from code,
  `Ecanakli.UIDirector.DOTween` (with `DOTween.dll` and the define constraint, see
  [DOTween](Documentation~/DOTween.md)) to use `TweenTransition`, and
  `Ecanakli.UIDirector.DependencyInjection.Zenject` plus `Zenject` for `NavigatorInstaller`. Every type
  is in the namespace `Ecanakli.UIDirector` except `NavigatorInstaller`
  (`Ecanakli.UIDirector.DependencyInjection`). Code outside assembly definitions needs no reference.
- **Addressables.** The integration compiles by itself when `com.unity.addressables` is installed.
  There is nothing to switch on. See [Assets and Addressables](Documentation~/Assets-and-Addressables.md).
- **DOTween.** The integration compiles when the `DOTWEEN` define is present, which DOTween's own setup
  panel sets, or when a UPM package named `com.demigiant.dotween` is installed. If neither is true but
  the DOTween assembly is in the project, choose `Tools/UIDirector/DOTween Integration` once. See
  [DOTween](Documentation~/DOTween.md).
- **Zenject under `Assets/`.** Choose `Tools/UIDirector/Zenject Integration` once; it sets a scripting
  define on every build target, so commit `ProjectSettings` afterwards. **Extenject as a UPM package,
  9.0.0 or newer:** the integration turns itself on. See [Zenject](Documentation~/Zenject.md).

## Getting started in 5 minutes

Six steps. Every block of code below is copied from the Basic Usage sample (see
[Samples](#samples)), which compiles. The code is the scenario of its Settings screen: it asks
"Reset settings" ("All settings return to their defaults.") in a confirm popup and acts on the answer.

The blocks leave out the `using` lines. Add the ones a file needs: `Ecanakli.UIDirector` for every
package type, `Cysharp.Threading.Tasks` where `UniTask` or `Forget` appears, `System.Threading` for
`CancellationToken`, `TMPro` for `TMP_Text`, and `UnityEngine` and `UnityEngine.UI` for `SerializeField`
and `Button`. The sample's own files sit in the namespace `Ecanakli.UIDirector.Samples.BasicUsage`, which
already reaches `Ecanakli.UIDirector`; your files need the `using` line. The argument types the blocks
pass around (`ConfirmArgs`, `ItemArgs`, `RenameArgs`, `ToastArgs`) are plain structs you write yourself.
In the sample they are in `ViewArgs.cs`:

<!-- source: Samples~/BasicUsage/ViewArgs.cs -->
```csharp
/// <summary>What the confirm popup shows.</summary>
public readonly struct ConfirmArgs
{
    public readonly string Title;
    public readonly string Message;

    public ConfirmArgs(string title, string message)
    {
        Title = title;
        Message = message;
    }
}

/// <summary>Which item the detail screen shows.</summary>
public readonly struct ItemArgs
{
    public readonly string ItemId;

    public ItemArgs(string itemId)
    {
        ItemId = itemId;
    }
}

/// <summary>The item to rename and its current title.</summary>
public readonly struct RenameArgs
{
    public readonly string ItemId;
    public readonly string CurrentTitle;

    public RenameArgs(string itemId, string currentTitle)
    {
        ItemId = itemId;
        CurrentTitle = currentTitle;
    }
}

/// <summary>The one line a toast shows.</summary>
public readonly struct ToastArgs
{
    public readonly string Message;

    public ToastArgs(string message)
    {
        Message = message;
    }
}
```

### 1. Create the root

Choose `GameObject > UI > UIDirector Root`. It creates a canvas with a `ViewRoot` and a `BackInput`,
and an `EventSystem` when the scene has none. Without an `EventSystem` views show but receive no
clicks ([UIDIRECTOR116](Documentation~/Troubleshooting.md#uidirector116)). With `Initialize On Awake`
left on, the root initializes itself in `Awake` with the three default layers and `ViewRoot.Navigator`
is ready from then on. The Basic Usage sample turns `Initialize On Awake` off and initializes the root
in code, because its toast row sits on a layer that only code can declare (step 5). Details are in
[Root canvas](Documentation~/Root-Canvas.md).

### 2. Describe the views and name them

Create a catalog with `Assets > Create > UIDirector/View Catalog`, add one row per view prefab (an id,
the prefab, the layer it opens on) and drag the catalog into the `Catalogs` list of the root. A view
prefab has a `ViewHost` and one view component on its root. Then give every row a typed key, in one
static class marked `[ViewKeys]`:

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

`Confirm` takes a `ConfirmArgs` and answers with a `bool`; `Home` takes nothing and answers nothing.
The compiler checks what you pass in and what you read out. Run `Tools/UIDirector/Validate Catalogs`
to check keys and rows against each other before you press Play. See
[Catalogs and keys](Documentation~/Catalogs-and-Keys.md).

### 3. Write the confirm popup

A view derives from `ViewBehaviour<TArgs, TResult>`. `OnOpen` receives the arguments and a scope; what
you bind to the scope is removed when the open ends, so there is no `OnDisable` and no
`RemoveListener`. `Complete(value)` closes the popup and answers:

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

The popup's row sits on the built-in `popups` layer: modal, with a shared backdrop, and it takes back.
See [View authoring](Documentation~/View-Authoring.md) and
[Popups and backdrop](Documentation~/Popups-and-Backdrop.md).

### 4. Open it from a screen and read the answer

`ShowAsync` opens a view and completes with a `ViewResult<bool>`. It does not throw for anything that
happens to the view: a back press, a tap on the backdrop, a refusal and an ended owner all arrive as a
result without a value. The token passed with every open is its **owner**; when it is cancelled the view
ends. Here the owner is the token of the screen's open scope, so the popup cannot outlive the screen:

The Reset button starts the check in the screen's open scope, which runs it with the scope's token:

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

Always check `HasValue` before you read `Value`: for a `bool` result the default is `false`, which looks
like an answer. The last call is the other way to open a view: `Open` returns a handle at once and does
not wait. The root owns the toast because it must outlive the screen that asked for it. More in
[Opening and results](Documentation~/Opening-and-Results.md).

### 5. Initialize the root and open the first screen

The toast row of the catalog sits on a layer named `toasts`. The three default layers do not include
it, so the sample declares it in code:

<!-- source: Samples~/BasicUsage/AppLayers.cs -->
```csharp
// Above the system layer, no history, no back and no input shield: a toast never blocks a tap.
public static readonly ViewLayer Toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);
```

A layer declared in code reaches the root through `ViewRootSetup.ExtraLayers`, which only `Initialize`
accepts. That is why the sample turns `Initialize On Awake` off and initializes the root in its own
`Awake`. Turn it off on your root too before you call `Initialize`: a root that already initialized
itself throws `InvalidOperationException` on a second initialization. Without this call the `toast` row is refused at registration
([UIDIRECTOR109](Documentation~/Troubleshooting.md#uidirector109)) and opening the toast ends as not
shown. The other `ViewRootSetup` members are covered in
[Add a layer of your own](Documentation~/Root-Canvas.md#add-a-layer-of-your-own):

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

Then open the first screen. The root's token as owner means the screen ends only when the root does:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
private void Start()
{
    // Owned by the root, so Home ends only when the root does.
    _navigator.Open(AppViews.Home, _navigator.RootToken, new OpenOptions { Source = "boot" });
}
```

`_navigator` is `ViewRoot.Navigator`. A screen opens on the `screens` layer, which is a stack and the
back history; opening an id that is already in the stack returns to it. See
[Navigation and back](Documentation~/Navigation-and-Back.md).

### 6. Route back

`Back()` is the single back rule. It returns `true` when something handled the press and `false` when
nothing did:

<!-- source: Samples~/BasicUsage/LibraryView.cs -->
```csharp
// The other screens use a ViewButton set to Back; this one shows the call.
private void GoBack() => Navigator.Back();
```

`BackInput`, which the root menu added, calls `Back()` on the Escape key once the root is initialized. On the bottom screen `Back()`
returns `false` and closes nothing; `BackInput` then raises `Unhandled`, and the game decides. The sample
listens and asks to quit with the same confirm popup:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
// No removal needed: the bootstrap and the root live and die together.
_backInput.Unhandled.AddListener(OnBackUnhandled);
```

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
```

A yes then calls `Application.Quit()`, and in the editor it stops Play Mode.

Press Play and open `Window > UIDirector` to watch the layers, the requests and the input shield live.
To see every request in the Console, add an observer: the sample's `ViewEventLog` prints one `[views]`
line per event (see [Events and analytics](Documentation~/Events-and-Analytics.md)). A message
tagged `UIDIRECTOR1xx` is explained in [Troubleshooting](Documentation~/Troubleshooting.md).

## Samples

Import them from the Package Manager: select **UIDirector**, open the **Samples** tab and press
**Import**. Each sample has its own README with the setup steps and a list of things to try. Every
sample scene has no Event System on purpose, because the right input module depends on your project's
input handling; the sample creates one when Play starts. Basic Usage, Queue Usage, Addressables Usage,
DOTween Usage and Zenject Usage all use TextMesh Pro, so import its Essential Resources once with
`Window > TextMeshPro > Import TMP Essential Resources`; the texts stay blank until then.

| Sample | Needs | Shows |
|---|---|---|
| [Basic Usage](Samples~/BasicUsage/README.md) | The core only, TextMesh Pro Essential Resources | A confirm popup with a result, three screens with back, a system overlay, a custom layer and transition, events and errors |
| [Queue Usage](Samples~/QueueUsage/README.md) | The core only, TextMesh Pro Essential Resources | A priority queue for popups: startup notices, a gate that denies, a hold while a flow runs, a feature catalog added at run time |
| [Addressables Usage](Samples~/AddressablesUsage/README.md) | `com.unity.addressables`, and the two prefabs marked addressable (a menu item does it) | Views loaded by address, a preload lease and trimming kept instances |
| [DOTween Usage](Samples~/DOTweenUsage/README.md) | DOTween, with the integration active | `TweenTransition` on a popup and a custom transition built on DOTween |
| [Zenject Usage](Samples~/ZenjectUsage/README.md) | Zenject or Extenject with the integration active, and a Scene Context you add yourself | The navigator bound in a SceneContext and views that receive injections |

## Documentation

The same grouping is on the [documentation home](Documentation~/index.md).

**Start here**

- [Concepts](Documentation~/Concepts.md): the root, layers, keys, requests, outcomes, owners and scopes
- [Root canvas](Documentation~/Root-Canvas.md): the root, its fields, `ViewRootSetup`, the sorting band, your own layers
- [Catalogs and keys](Documentation~/Catalogs-and-Keys.md): rows, typed keys, adding a catalog at run time, the validator
- [View authoring](Documentation~/View-Authoring.md): writing a view, its hooks, scopes, kept instances
- [Opening and results](Documentation~/Opening-and-Results.md): `Open` and `ShowAsync`, handles, results, owners, `CloseAll`
- [FAQ](Documentation~/FAQ.md): short answers to the first questions

**Guides**

- [Navigation and back](Documentation~/Navigation-and-Back.md): the history stack, `CloseTo`, `Back()`, `BackPolicy`, `BackInput`
- [Popups and backdrop](Documentation~/Popups-and-Backdrop.md): modal popups, the backdrop, the input shield, `RejectWhenBusy`
- [System overlays](Documentation~/System-Overlays.md): blocking overlays around a piece of work
- [Transitions](Documentation~/Transitions.md): the built-in transitions and writing your own
- [Queue](Documentation~/Queue.md): priorities, gates, holds and a feature that adds its own catalog
- [Events and analytics](Documentation~/Events-and-Analytics.md): the observer interface and every event
- [Input and platform](Documentation~/Input-and-Platform.md): the EventSystem, the shield, double taps, `HoldInput`, the back button
- [Migration](Documentation~/Migration.md): moving a project that has a UI manager of its own, one view at a time

**Integrations**

- [DOTween](Documentation~/DOTween.md): `TweenTransition` and kill-safe custom transitions
- [Assets and Addressables](Documentation~/Assets-and-Addressables.md): views by address, preload, keep and trim, your own provider
- [Zenject](Documentation~/Zenject.md): `NavigatorInstaller` and injection into views

**Reference**

- [API reference](Documentation~/API-Reference.md): every public type and member as signatures
- [Diagnostics](Documentation~/Diagnostics.md): console lines, the UIDirector window, the read-out, the error handler, the validator
- [Troubleshooting](Documentation~/Troubleshooting.md): one section per `UIDIRECTOR1xx` id, then symptoms
- [Performance](Documentation~/Performance.md): what is guaranteed about cost and which test holds it
- [Threading](Documentation~/Threading.md): what throws off the main thread and what is posted to it
- [Limitations](Documentation~/Limitations.md): what the package does not do and its provisional defaults

**Design**

- [Architecture](Documentation~/Design/Architecture.md): why the package is shaped this way
- [ADR-001 Outcomes as data](Documentation~/Design/Decisions/ADR-001-Outcomes-As-Data.md)
- [ADR-002 Strict serialization](Documentation~/Design/Decisions/ADR-002-Strict-Serialization.md)
- [ADR-003 Begin-only hooks and scopes](Documentation~/Design/Decisions/ADR-003-Begin-Only-Hooks-And-Scopes.md)
- [ADR-004 The queue sits above the navigator](Documentation~/Design/Decisions/ADR-004-Queue-Above-The-Navigator.md)
- [ADR-005 Token owners](Documentation~/Design/Decisions/ADR-005-Token-Owners.md)

For AI tools, [llms.txt](llms.txt) holds a summary, the rules that bite and a one-line description of
every page in reading order, and [llms-full.txt](llms-full.txt) is the whole documentation in one file.

## Status and limits

- Version 0.1.1. See the [changelog](CHANGELOG.md).
- Main thread only. Opening, registering and queueing throw on another thread; a few stop-type calls are
  posted to the main thread. See [Threading](Documentation~/Threading.md).
- uGUI only, with a `Screen Space - Overlay` or `Screen Space - Camera` root. World-space UI and UI
  Toolkit are not supported. One root per application is the documented setup.
- Diagnostics have stable ids, `UIDIRECTOR101` to `UIDIRECTOR126`. Ids 101 to 120 and 126 are logged at
  run time in the editor and in development builds. Ids 121 to 124 come only from the catalog validator,
  and 125 from the validator and from the root when a catalog with a row whose id is empty or only whitespace is registered (that row is refused). Each
  has a section in [Troubleshooting](Documentation~/Troubleshooting.md).
- What the package deliberately leaves to the game is listed in
  [Limitations](Documentation~/Limitations.md#not-in-010).

## License

MIT. See [LICENSE.md](LICENSE.md).
