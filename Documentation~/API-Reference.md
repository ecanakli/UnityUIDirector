# API reference

[Back to index](index.md)

Every public type of the package, with its namespace, its assembly and its public members. The guides
explain what the calls do and when to use them; this page is the list of names and shapes, written as
they are declared in the source. Each type has one line on what it is for, one block with its public
declarations and no bodies, and a link to the guide that teaches it.

The page covers 60 public types: 57 in the core assembly and one in each integration assembly. The
editor tools (the UIDirector window, Validate Catalogs, the integration menu items) are internal and
have no scripting API.

---

## How to read this page

- **Blocks show declarations, not bodies.** Attributes that tell the compiler or the Inspector
  something (`[Flags]`, `[RequireComponent]`, `[AttributeUsage]`) are kept; the `[SerializeField]`
  fields of the components are private and are not API. Their Inspector names are in the guides.
- **Caller-information parameters are declared on purpose and are never passed.** The methods that
  register work or open a view end with `[CallerMemberName] string member = null, [CallerLineNumber] int line = 0`.
  The compiler fills them in. `HoldInput` and `ViewQueue.Hold` show their call site (member and line)
  in UIDIRECTOR110 and UIDIRECTOR111, the UIDirector window and `Describe`; `ShowAsync` and `Open` put
  the calling member on the shield holders they take; `AddObserver`, `AddCatalog` and `PreloadAsync`
  take them for future use and do not show them in this version. The methods that have them are
  `ShowAsync`, `Open`, `HoldInput`, `AddObserver`, `AddCatalog` and `PreloadAsync` on `INavigator`, the
  `ShowAsync` and `Open` shortcuts in `NavigatorExtensions`, and `Hold` on `ViewQueue`.
- **Main thread.** Unless a line says otherwise, a member must be called on Unity's main thread. The
  calls that throw off the main thread, the calls that are posted to it, and the calls that are not
  checked at all are listed on [Threading](Threading.md). The ones that may be called from any thread
  are `ViewRegistration.Remove`, `INavigator.CloseAll`, `INavigator.Trim` and `ViewQueue.Clear`.
- **Nothing here throws for what happens to a view.** `ShowAsync` completes with a `ViewResult<TResult>`
  that says how the request ended, and `Open` returns a `ViewHandle`; a refusal, a failed load and an
  owner that ended are all data. What a member throws for a usage error is listed with the type. See
  [Opening and results](Opening-and-Results.md).
- **Diagnostic ids are not public constants.** A warning or an error in the console carries an id such
  as UIDIRECTOR101; the ids are listed under [Errors and diagnostics](#errors-and-diagnostics).

### Assemblies and namespaces

| Assembly | Compiled | Namespace of its public types |
|---|---|---|
| `Ecanakli.UIDirector` | Always | `Ecanakli.UIDirector` |
| `Ecanakli.UIDirector.Addressables` | When `com.unity.addressables` is installed (define `ECANAKLI_UIDIRECTOR_ADDRESSABLES`, set by the assembly) | `Ecanakli.UIDirector` |
| `Ecanakli.UIDirector.DOTween` | When DOTween is installed (`DOTWEEN`, or `ECANAKLI_UIDIRECTOR_DOTWEEN` for a UPM package named `com.demigiant.dotween` or set by `Tools/UIDirector/DOTween Integration`) | `Ecanakli.UIDirector` |
| `Ecanakli.UIDirector.DependencyInjection.Zenject` | When `ECANAKLI_UIDIRECTOR_DI_ZENJECT` is defined: set by the assembly for Extenject 9.0.0+ as a package, or by `Tools/UIDirector/Zenject Integration` for Zenject under Assets/ | `Ecanakli.UIDirector.DependencyInjection` |

One `using Ecanakli.UIDirector;` reaches everything that user code calls, including the Addressables
and DOTween types. An installer adds `using Ecanakli.UIDirector.DependencyInjection;`. How each
integration turns on is explained on [Assets and Addressables](Assets-and-Addressables.md#the-integration-turns-on-by-itself),
[DOTween](DOTween.md#turn-the-integration-on) and [Zenject](Zenject.md#turn-the-integration-on).

### Types from other libraries in the signatures

Snippets on these pages leave out their `using` lines; the sample files they are copied from under
`Samples~/` carry them in full. This table names the namespace of each outside type in the signatures
and of the ones the snippets use most.

| Type | Namespace | Library |
|---|---|---|
| `UniTask`, `UniTask<T>` | `Cysharp.Threading.Tasks` | UniTask |
| `CancellationToken` | `System.Threading` | .NET |
| `IDisposable`, `Action`, `Action<T>`, `Func<T, TResult>`, `Func<T1, T2, TResult>`, `Exception`, `Attribute`, `AttributeUsage`, `AttributeTargets`, `FlagsAttribute`, `Serializable` | `System` | .NET |
| `IReadOnlyList<T>` | `System.Collections.Generic` | .NET |
| `StringBuilder` | `System.Text` | .NET |
| `CallerMemberName`, `CallerLineNumber` | `System.Runtime.CompilerServices` | .NET |
| `MonoBehaviour`, `ScriptableObject`, `GameObject`, `Transform`, `Canvas`, `RectTransform`, `CanvasGroup`, `CreateAssetMenu`, `Object`, `RequireComponent`, `DisallowMultipleComponent`, `SerializeField`, `Min`, `Vector3`, `Debug` | `UnityEngine` | Unity |
| `Button`, `GraphicRaycaster` | `UnityEngine.UI` | Unity UI (`com.unity.ugui`) |
| `UnityEvent`, `UnityEvent<T>`, `UnityAction`, `UnityAction<T>` | `UnityEngine.Events` | Unity |
| `EventSystem`, `StandaloneInputModule` | `UnityEngine.EventSystems` | Unity UI (`com.unity.ugui`) |
| `InputSystemUIInputModule` | `UnityEngine.InputSystem.UI` | Input System |
| `TMP_Text` | `TMPro` | TextMesh Pro (in `com.unity.ugui`) |
| `DOGetter<T>`, `DOSetter<T>` | `DG.Tweening.Core` | DOTween |
| `DOTween`, `Sequence`, `Ease` and the `DOScale`-style extension methods | `DG.Tweening` | DOTween |
| `DiContainer`, `MonoInstaller`, `InjectAttribute` (`[Inject]`) | `Zenject` | Zenject or Extenject |

### Types on this page

| Group | Types |
|---|---|
| [Root and setup](#root-and-setup) | `ViewRoot`, `ViewRootSetup`, `BackInput`, `SafeAreaFitter`, `ViewButton`, `ViewButtonAction` |
| [Navigator and handles](#navigator-and-handles) | `INavigator`, `NavigatorExtensions`, `ViewHandle`, `ViewRegistration`, `OpenOptions` |
| [Keys and catalogs](#keys-and-catalogs) | `ViewKey<TArgs, TResult>`, `ViewKey`, `NoArgs`, `NoResult`, `ViewKeysAttribute`, `ViewCatalog`, `ViewEntry`, `ViewRetention`, `BackPolicy`, `CoverPolicy`, `CoveredMode`, `ViewLayer`, `ViewLayers`, `ViewLayerRules` |
| [Views and contexts](#views-and-contexts) | `ViewHost`, `IView<TArgs, TResult>`, `IViewVisible`, `IViewBack`, `ViewBehaviour<TArgs, TResult>`, `ViewBehaviour<TArgs>`, `ViewBehaviour`, `ViewContext<TResult>` |
| [Scopes](#scopes) | `ViewScope` |
| [Results and reasons](#results-and-reasons) | `ViewResult<TResult>`, `ViewResult`, `ViewClosed`, `ViewPhase`, `CloseReason`, `RejectReason` |
| [Events and observers](#events-and-observers) | `IViewObserver`, `ViewEvent`, `ViewEventKind` |
| [Queue and gates](#queue-and-gates) | `ViewQueue`, `QueueOptions`, `IViewGate`, `GateRequest`, `GateDecision` |
| [Transitions](#transitions) | `ViewTransition`, `FadeTransition`, `ScaleTransition`, `SlideTransition`, `SlideEdge` |
| [Errors and diagnostics](#errors-and-diagnostics) | `ViewErrorHandler`, `ViewErrorContext`, `ViewErrorSource`, the diagnostic ids |
| [Asset providers](#asset-providers) | `IViewProvider` |
| [Addressables](#addressables) | `AddressablesViewProvider` |
| [DOTween](#dotween) | `TweenTransition` |
| [Zenject](#zenject) | `NavigatorInstaller` |

---

## Root and setup

### `ViewRoot`

The UI root: a component on your canvas that builds the layers, the input shield, the backdrops and
the staging area, and owns the navigator. Taught in [Root canvas](Root-Canvas.md) (every Inspector
field is in [Every field of ViewRoot](Root-Canvas.md#every-field-of-viewroot)) and
[Concepts](Concepts.md#one-root-layers-with-rules).

- `Navigator` initialises the root with default settings on first access when it was not initialised
  yet, and throws `InvalidOperationException` when the root was destroyed before that.
- `Initialize` throws `ArgumentNullException` for a null setup, `InvalidOperationException` when
  the root is already initialised or destroyed, and `ArgumentException` (parameter name `setup`) when
  `ExtraLayers` repeats a default layer or another extra layer by id; nothing is built in that case.
  Call it at most once, before the first access to `Navigator`.
- A root that is not initialised yet is left alone by `BackInput` and `ViewButton`. It is initialised only by `Initialize On Awake`, by your call to `Initialize` (or `NavigatorInstaller.Install`), or by your first read of `Navigator`.
- The root calls `Trim()` on `Application.lowMemory`.

<!-- signature -->
```csharp
[DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
public sealed class ViewRoot : MonoBehaviour
{
    public bool IsInitialized { get; }
    public INavigator Navigator { get; }
    public void Initialize(ViewRootSetup setup);
}
```

### `ViewRootSetup`

The settings that only code can give: injection, a provider, extra layers and an error handler. It is
the argument of `ViewRoot.Initialize`. Taught in [Root canvas](Root-Canvas.md#initialize-with-viewrootsetup).

<!-- signature -->
```csharp
public sealed class ViewRootSetup
{
    public Action<GameObject> Inject;
    public IViewProvider AssetProvider;
    public IReadOnlyList<ViewLayer> ExtraLayers;
    public ViewErrorHandler ErrorHandler;
}
```

| Field | Meaning |
|---|---|
| `Inject` | Runs on each new instance while it is inactive, before `Awake`. Null skips injection. An exception from it ends the request as not shown with `RejectReason.LoadFailed` and reaches the error handler as `ViewErrorSource.Inject`. |
| `AssetProvider` | Resolves rows that carry an asset key. Null uses an `IViewProvider` component on the root, if there is one. |
| `ExtraLayers` | Layers declared in code, added to the three default layers. Null entries are skipped; an id that repeats a default layer or another entry makes `Initialize` throw. |
| `ErrorHandler` | Receives exceptions from user code and providers. Null logs them with `Debug.LogException`. |

### `BackInput`

An optional component that polls the back key (Escape, the Android back button) and calls `Back()`
on its root's navigator. Taught in [Navigation and back](Navigation-and-Back.md#backinput).

`Unhandled` is raised when `Back()` returned false, so that the game decides what a back press
means, for example by asking to quit. The component finds its root on its own object, on a parent,
or in its Inspector field. While that root is not initialised the component ignores the key: it calls
nothing, raises nothing and does not initialise the root. With the Input System package it reads
Escape through an `InputAction`; otherwise it uses the legacy input manager.

<!-- signature -->
```csharp
[DisallowMultipleComponent]
public sealed class BackInput : MonoBehaviour
{
    public UnityEvent Unhandled { get; }
}
```

### `SafeAreaFitter`

An optional component on a `RectTransform` that fits the anchors to `Screen.safeArea` and refits when
the safe area or the screen size changes. It assumes the parent rect covers the whole screen; offsets
stay as authored, and an edge that is turned off keeps its anchor at the screen edge. It has no
public members; its four edge toggles are Inspector fields. Its cost is on
[Performance](Performance.md).

<!-- signature -->
```csharp
[RequireComponent(typeof(RectTransform))]
public sealed class SafeAreaFitter : MonoBehaviour
{
}
```

### `ViewButton` and `ViewButtonAction`

`ViewButton` is the Inspector entry point on a `Button`: a click closes its view, calls `Back()`, or
opens a view by id without arguments. It never throws from a click. `ViewButtonAction` is the
choice. Taught in [Navigation and back](Navigation-and-Back.md#back).

<!-- signature -->
```csharp
public enum ViewButtonAction
{
    CloseView,  // closes the view the button belongs to, as the user dismissing it
    Back,       // calls Back on the navigator of the root above the button
    OpenView    // opens the view with the given id, owned by the root
}

[RequireComponent(typeof(Button))]
public sealed class ViewButton : MonoBehaviour
{
}
```

`ViewButton` has no public members; the action and the view id are Inspector fields. `OpenView`
opens a row whose view takes no arguments and returns no value. A blank view id, or a click on a
`Back` or `OpenView` button before its root is initialised, logs a warning and does nothing.

---

## Navigator and handles

### `INavigator`

Opens, closes and queries views on one root. `ViewRoot.Navigator` returns it; the Zenject integration
binds it. Taught in [Opening and results](Opening-and-Results.md), with `Back` and `CloseTo` in
[Navigation and back](Navigation-and-Back.md) and `HoldInput` in
[Input and platform](Input-and-Platform.md#block-input-yourself-with-holdinput).

- `ShowAsync` and `Open` never throw for anything that happens to the view. Off the main thread they
  throw `InvalidOperationException`; see [Threading](Threading.md).
- `HoldInput`, `AddObserver`, `AddCatalog`, `PreloadAsync` and `Describe` throw `ArgumentNullException`
  for a null reason, observer, catalog, view id or output.
- `Back` and `CloseTo` return false and do nothing off the main thread.
- `HoldInput`, `AddObserver` and `AddCatalog` return a `ViewRegistration` that ends with the owner;
  after the root was destroyed they return an empty registration.
- `RootToken` is cancelled when the root is destroyed.

<!-- signature -->
```csharp
public interface INavigator
{
    UniTask<ViewResult<TResult>> ShowAsync<TArgs, TResult>(
        ViewKey<TArgs, TResult> key,
        TArgs args,
        CancellationToken owner,
        OpenOptions options = default,
        [CallerMemberName] string member = null,
        [CallerLineNumber] int line = 0);

    ViewHandle Open<TArgs, TResult>(
        ViewKey<TArgs, TResult> key,
        TArgs args,
        CancellationToken owner,
        OpenOptions options = default,
        [CallerMemberName] string member = null,
        [CallerLineNumber] int line = 0);

    bool Back();
    bool CloseTo(string viewId);
    void CloseAll(ViewLayer layer);

    bool IsOpen(string viewId);
    bool TryGetOpen(string viewId, out ViewHandle handle);
    bool TryGetTop(ViewLayer layer, out ViewHandle handle);
    bool IsBusy { get; }
    bool IsLayerClear(ViewLayer layer);
    CancellationToken RootToken { get; }

    ViewRegistration HoldInput(
        string reason,
        CancellationToken owner,
        [CallerMemberName] string member = null,
        [CallerLineNumber] int line = 0);

    ViewRegistration AddObserver(
        IViewObserver observer,
        CancellationToken owner,
        [CallerMemberName] string member = null,
        [CallerLineNumber] int line = 0);

    ViewRegistration AddCatalog(
        ViewCatalog catalog,
        CancellationToken owner,
        [CallerMemberName] string member = null,
        [CallerLineNumber] int line = 0);

    UniTask<bool> PreloadAsync(
        string viewId,
        CancellationToken owner,
        [CallerMemberName] string member = null,
        [CallerLineNumber] int line = 0);

    void Trim();
    int ReserveRequestId();
    void Describe(StringBuilder output);
}
```

| Member | Meaning |
|---|---|
| `ShowAsync` | Opens a view and completes with its outcome. |
| `Open` | Opens a view without waiting for it; the handle follows the request. |
| `Back` | The single back rule. False means nothing handled it and the game decides. |
| `CloseTo` | Pops everything above the given screen. False when it is not in a history layer. |
| `CloseAll` | Closes every view and withdraws every pending request on the layer. |
| `IsOpen`, `TryGetOpen`, `TryGetTop` | True or a handle while a request for the id is open or in flight, or the top request of the layer. |
| `IsBusy` | True while an operation runs or an input hold exists. |
| `IsLayerClear` | True when the layer has no request, nothing modal is above it and the navigator is not busy. |
| `RootToken` | Cancelled when the root is destroyed; the explicit "as long as the UI exists" owner. |
| `HoldInput` | Keeps the input shield up until the owner ends or the registration is removed. |
| `AddObserver` | Delivers every view event to the observer until the owner ends or the registration is removed. |
| `AddCatalog` | Registers the catalog rows until the owner ends or the registration is removed. |
| `PreloadAsync` | Keeps the view's asset loaded until the owner ends. False when the id is unknown or the load failed. |
| `Trim` | Releases every idle kept instance. |
| `ReserveRequestId` | Reserves an id so that a caller can publish events for a request before it reaches the navigator. |
| `Describe` | Writes the layers, requests, shield holders and kept instances as text; see [Diagnostics](Diagnostics.md#a-text-read-out-from-code). |

### `NavigatorExtensions`

Shortcuts on `INavigator` for keys without arguments and for keys instead of ids. Taught in
[Opening and results](Opening-and-Results.md#open-a-view-or-open-it-and-wait) and
[Navigation and back](Navigation-and-Back.md#closeto). Each throws `ArgumentNullException` for a null
navigator.

<!-- signature -->
```csharp
public static class NavigatorExtensions
{
    public static UniTask<ViewResult<TResult>> ShowAsync<TResult>(
        this INavigator navigator,
        ViewKey<NoArgs, TResult> key,
        CancellationToken owner,
        OpenOptions options = default,
        [CallerMemberName] string member = null,
        [CallerLineNumber] int line = 0);

    public static ViewHandle Open<TResult>(
        this INavigator navigator,
        ViewKey<NoArgs, TResult> key,
        CancellationToken owner,
        OpenOptions options = default,
        [CallerMemberName] string member = null,
        [CallerLineNumber] int line = 0);

    public static bool CloseTo<TArgs, TResult>(this INavigator navigator, ViewKey<TArgs, TResult> key);
    public static bool IsOpen<TArgs, TResult>(this INavigator navigator, ViewKey<TArgs, TResult> key);
}
```

### `ViewHandle`

A reference to one request. It is a struct; the default value is an empty handle that reports
`ViewPhase.Ended`. Once the request has ended, the handle is inert. Taught in
[Opening and results](Opening-and-Results.md#the-handle).

- `Close` never throws. A view that has started to show (from `Opening` on) closes with
  `CloseReason.Code`; a request still waiting or loading is withdrawn and ends as `NotShown` with
  `RejectReason.Cancelled`.
- `WaitForCloseAsync` completes when the request ends, for any number of callers. It throws only
  when `waiter` is cancelled; a cancelled waiter is removed from the request at once. See
  [System overlays](System-Overlays.md#wait-for-it-to-be-gone).
- `TryGetInstance` gives the live instance while the view is opening, open or closing.

<!-- signature -->
```csharp
public readonly struct ViewHandle
{
    public int RequestId { get; }
    public string ViewId { get; }
    public ViewPhase Phase { get; }
    public bool IsAlive { get; }

    public void Close();
    public UniTask<ViewClosed> WaitForCloseAsync(CancellationToken waiter);
    public bool TryGetInstance(out GameObject instance);
}
```

### `ViewRegistration`

A registration that ends with its owner; `Remove` ends it earlier. `HoldInput`, `AddCatalog`,
`AddObserver` and the queue's `Hold`, `AddGate` and `AddObserver` return one. The default value is an
inactive registration. Taught in
[Opening and results](Opening-and-Results.md#owners-and-what-happens-when-one-ends).

`Remove` is safe to call twice, from any thread, and never throws. Off the main thread it is posted,
and `IsActive` stays true until the main thread runs it.

<!-- signature -->
```csharp
public readonly struct ViewRegistration
{
    public bool IsActive { get; }
    public void Remove();
}
```

### `OpenOptions`

Per-call options of an open request, the optional argument of `ShowAsync` and `Open`. Taught in
[Opening and results](Opening-and-Results.md#openoptions).

<!-- signature -->
```csharp
public struct OpenOptions
{
    public string Source;
    public bool RejectWhenBusy;
    public int RequestId;
}
```

| Field | Meaning |
|---|---|
| `Source` | A free tag carried by events and diagnostics. |
| `RejectWhenBusy` | Refuse with `RejectReason.Busy` instead of waiting when the navigator is busy. |
| `RequestId` | Zero allocates an id; otherwise an id from `INavigator.ReserveRequestId`. |

---

## Keys and catalogs

### `ViewKey<TArgs, TResult>` and `ViewKey`

The typed identity of a view: a string id plus the argument and result types. `ViewKey` holds the
factory shortcuts. Keep keys in a static class marked `[ViewKeys]`. Taught in
[Catalogs and keys](Catalogs-and-Keys.md#keys). The constructor and the factories throw
`ArgumentNullException` for a null id and `ArgumentException` for an empty or whitespace-only id.
An id with spaces around other characters is valid and is not trimmed.

<!-- signature -->
```csharp
public readonly struct ViewKey<TArgs, TResult>
{
    public ViewKey(string id);
    public string Id { get; }
}

public static class ViewKey
{
    public static ViewKey<NoArgs, NoResult> Create(string id);
    public static ViewKey<TArgs, NoResult> Create<TArgs>(string id);
    public static ViewKey<TArgs, TResult> Create<TArgs, TResult>(string id);
}
```

### `NoArgs` and `NoResult`

The argument type of a view that takes no arguments, and the result type of a view that returns no
value. Taught in [Concepts](Concepts.md#views-rows-and-keys).

<!-- signature -->
```csharp
public readonly struct NoArgs
{
}

public readonly struct NoResult
{
}
```

### `ViewKeysAttribute`

Marks a static class whose key fields the editor validator checks against the catalogs. It changes
nothing at run time. Taught in [Catalogs and keys](Catalogs-and-Keys.md#keys); the C# name is
`ViewKeys` in brackets.

<!-- signature -->
```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class ViewKeysAttribute : Attribute
{
}
```

### `ViewCatalog`

A `ScriptableObject` asset that holds the rows of views; several catalogs may be registered with one
root. Create it with `Assets > Create > UIDirector/View Catalog`. Taught in
[Catalogs and keys](Catalogs-and-Keys.md#a-catalog-row).

<!-- signature -->
```csharp
[CreateAssetMenu(menuName = "UIDirector/View Catalog")]
public sealed class ViewCatalog : ScriptableObject
{
    public IReadOnlyList<ViewEntry> Entries { get; }
}
```

### `ViewEntry`

One catalog row, read-only in code: an id, a prefab source and the view's policy. It is edited in the
Inspector only. Taught in [Catalogs and keys](Catalogs-and-Keys.md#a-catalog-row).

<!-- signature -->
```csharp
[Serializable]
public sealed class ViewEntry
{
    public string Id { get; }
    public GameObject Prefab { get; }
    public string AssetKey { get; }
    public string LayerId { get; }
    public ViewRetention Retention { get; }
    public BackPolicy Back { get; }
    public bool CloseOnBackdrop { get; }
    public CoverPolicy Cover { get; }
    public CoveredMode WhenCovered { get; }
    public bool AllowMultiple { get; }
}
```

| Property | Meaning |
|---|---|
| `Id` | The id that code and remote data use to name the view. |
| `Prefab` | A direct prefab reference, resident while the catalog is loaded. |
| `AssetKey` | The asset key used when `Prefab` is null, resolved by the root's view provider. |
| `LayerId` | The id of the layer the view opens on. |
| `Retention` | What happens to the instance when the view closes. |
| `Back` | What `Back()` does when this view is the back target. |
| `CloseOnBackdrop` | True when a tap on the backdrop closes this view. |
| `Cover` | Whether the view hides what is below it once it is fully open. |
| `WhenCovered` | How the view is hidden while another view covers it. |
| `AllowMultiple` | True when more than one instance of this id may exist at once. |

### `ViewRetention`

What happens to the instance when its view closes. Taught in
[View authoring](View-Authoring.md#kept-instances).

<!-- signature -->
```csharp
public enum ViewRetention
{
    ReleaseOnClose,  // the instance is released when the view closes
    KeepInstance     // the instance is kept hidden and reused by the next open of the same id
}
```

### `BackPolicy`

What `Back()` does when the view is the back target. Taught in
[Navigation and back](Navigation-and-Back.md#backpolicy).

<!-- signature -->
```csharp
public enum BackPolicy
{
    LayerDefault,   // follow the layer: close on a layer that takes back
    Close,          // close with CloseReason.Back; the bottom view of a history layer stays and Back returns false
    Ignore,         // swallow the back request and keep the view open
    HandledByView   // ask the view through IViewBack; when it declines, follow the layer
}
```

### `CoverPolicy`

Whether the view hides what is below it once it is fully open. Taught in the `Cover` row of
[Catalogs and keys](Catalogs-and-Keys.md#a-catalog-row).

<!-- signature -->
```csharp
public enum CoverPolicy
{
    LayerDefault,  // a history layer covers, other layers do not
    Covers,        // everything below is hidden once the view is fully open
    Transparent    // views below stay visible
}
```

### `CoveredMode`

How a view is hidden while it is covered. Taught in the `When Covered` row of
[Catalogs and keys](Catalogs-and-Keys.md#a-catalog-row) and in
[View authoring](View-Authoring.md#lifecycle-what-runs-when).

<!-- signature -->
```csharp
public enum CoveredMode
{
    DisableCanvas,  // every canvas under the view is disabled
    Deactivate      // the view object is deactivated
}
```

### `ViewLayer`

An ordered band of the root with one set of rules. It is immutable. The constructor throws
`ArgumentNullException` for a null id and `ArgumentException` for an empty one. Taught in
[Concepts](Concepts.md#one-root-layers-with-rules) and
[Root canvas](Root-Canvas.md#add-a-layer-of-your-own).

<!-- signature -->
```csharp
public sealed class ViewLayer
{
    public ViewLayer(string id, int order, ViewLayerRules rules);
    public string Id { get; }
    public int Order { get; }
    public ViewLayerRules Rules { get; }
}
```

### `ViewLayers`

The three layers every root has. Taught in [Concepts](Concepts.md#one-root-layers-with-rules).

<!-- signature -->
```csharp
public static class ViewLayers
{
    public static readonly ViewLayer Screens;  // id "screens", order 0: History | TakesBack | ShieldsInput
    public static readonly ViewLayer Popups;   // id "popups", order 1: Modal | TakesBack | ShieldsInput | ClosedByNavigation
    public static readonly ViewLayer System;   // id "system", order 2: Modal | ShieldsInput
}
```

### `ViewLayerRules`

The flags a layer applies to the views opened on it. Taught in
[Concepts](Concepts.md#one-root-layers-with-rules).

<!-- signature -->
```csharp
[Flags]
public enum ViewLayerRules
{
    None = 0,                // a plain layer that shields nothing and takes no back
    History = 1,             // the list is a stack and the back history
    Modal = 2,               // a backdrop under the top view; everything below it is non-interactive
    TakesBack = 4,           // Back may close the top view
    ShieldsInput = 8,        // opens and closes on this layer hold the input shield
    ClosedByNavigation = 16  // closed when a view opens on a history layer
}
```

---

## Views and contexts

### `ViewHost`

Required on every view prefab root; it connects the view object to its request, together with a
`Canvas` and a `GraphicRaycaster` on the same object. An optional transition component is assigned in
its Inspector. Taught in [View authoring](View-Authoring.md#prefab-structure).

`Close` closes the view without a value, as the user dismissing it (`CloseReason.Dismissed`); it
never throws and is meant for Inspector events. `SortingOrder` is the sorting order of the view's own
canvas, for particles and sub-canvases inside the view.

<!-- signature -->
```csharp
[DisallowMultipleComponent, RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
public sealed class ViewHost : MonoBehaviour
{
    public bool IsOpen { get; }
    public ViewHandle Handle { get; }
    public int SortingOrder { get; }
    public void Close();
}
```

### `IView<TArgs, TResult>`

The contract a view component implements. It is called once per open, before the first visible frame.
Taught in [View authoring](View-Authoring.md#write-a-view-without-the-base-class).

<!-- signature -->
```csharp
public interface IView<TArgs, TResult>
{
    void OnOpen(TArgs args, ViewContext<TResult> context);
}
```

### `IViewVisible` and `IViewBack`

Optional hooks on the same component as `IView`. `OnVisible` is called each time the view comes on
screen, with the scope of that span. `OnBack` is asked when `Back()` targets a view whose policy is
`HandledByView`; true means handled, false applies the layer's rule. Taught in
[View authoring](View-Authoring.md#lifecycle-what-runs-when) and
[Navigation and back](Navigation-and-Back.md#handledbyview).

<!-- signature -->
```csharp
public interface IViewVisible
{
    void OnVisible(ViewScope visible);
}

public interface IViewBack
{
    bool OnBack();
}
```

### `ViewBehaviour<TArgs, TResult>`, `ViewBehaviour<TArgs>` and `ViewBehaviour`

The optional base classes for a view, built only on the view interfaces. The three differ in what
they fix: both type arguments, only the arguments (the result is `NoResult`), or neither (`NoArgs`
and `NoResult`). All hooks are protected and the interface implementations are explicit. Taught in
[View authoring](View-Authoring.md#write-a-view-on-the-base-class).

- `Navigator` and `OpenScope` are null before the first open.
- `Complete` closes the view with a value and `Close` closes it without one; the first close cause
  wins. `Decline` is valid inside `OnOpen` only and ends the request before anything is shown
  ([View authoring](View-Authoring.md#refuse-an-open-with-decline)).

<!-- signature -->
```csharp
public abstract class ViewBehaviour<TArgs, TResult> : MonoBehaviour, IView<TArgs, TResult>, IViewVisible, IViewBack
{
    protected INavigator Navigator { get; }
    protected ViewScope OpenScope { get; }

    protected abstract void OnOpen(TArgs args, ViewScope open);
    protected virtual void OnVisible(ViewScope visible);
    protected virtual bool OnBack();

    protected void Complete(TResult value);
    protected void Close();
    protected void Decline(string reason = null);
}

public abstract class ViewBehaviour<TArgs> : ViewBehaviour<TArgs, NoResult>
{
}

public abstract class ViewBehaviour : ViewBehaviour<NoArgs, NoResult>
{
}
```

### `ViewContext<TResult>`

What one open hands to a view that does not use the base class: the navigator, the handle, the open
scope and the ways to end the request. Taught in
[View authoring](View-Authoring.md#write-a-view-without-the-base-class).

<!-- signature -->
```csharp
public sealed class ViewContext<TResult>
{
    public INavigator Navigator { get; }
    public ViewHandle Handle { get; }
    public ViewScope Open { get; }

    public void Complete(TResult value);
    public void Close();
    public void Decline(string reason = null);
}
```

---

## Scopes

### `ViewScope`

Owns the bindings and tasks of one open, or of one visible span. The package ends it, so view code has
no close-side line; the cleanups run newest first. A stale reference to an ended scope is inert:
`Bind`, `Subscribe` and `Run` do nothing on it, and `Add` and `OnEnd` act at once. Every method throws
`ArgumentNullException` for a null event, handler, delegate or disposable; a `state` may be null. Cancellation of a task started with `Run` is swallowed
and other errors are routed to the error handler. Not safe from other threads. Taught in
[View authoring](View-Authoring.md#tie-listeners-and-tasks-to-a-scope) and
[Concepts](Concepts.md#scopes-end-what-a-view-started).

<!-- signature -->
```csharp
public sealed class ViewScope
{
    public CancellationToken Token { get; }
    public bool IsEnded { get; }

    public void Bind(UnityEvent source, UnityAction handler);
    public void Bind<T>(UnityEvent<T> source, UnityAction<T> handler);
    public void Subscribe(Action<Action> add, Action<Action> remove, Action handler);
    public void Subscribe<T>(Action<Action<T>> add, Action<Action<T>> remove, Action<T> handler);
    public void Add(IDisposable disposable);
    public void OnEnd(Action action);
    public void OnEnd<TState>(TState state, Action<TState> action);
    public void Run(Func<CancellationToken, UniTask> work);
    public void Run<TState>(TState state, Func<TState, CancellationToken, UniTask> work);
}
```

| Member | Meaning |
|---|---|
| `Token` | Cancelled when the scope ends. Valid, and already cancelled, when read after the end. |
| `IsEnded` | True once the scope has ended. |
| `Bind` | Adds the handler to the event now and removes it when the scope ends. A duplicate pair is ignored. |
| `Subscribe` | Subscribes through the given add and remove delegates; the remove runs when the scope ends. |
| `Add` | Disposes the object when the scope ends. An ended scope disposes it at once. |
| `OnEnd` | Runs the action when the scope ends. The `TState` overload does not allocate a closure. |
| `Run` | Starts the work with the scope token. The `TState` overload passes a state without a closure. |

---

## Results and reasons

### `ViewResult<TResult>` and `ViewResult`

The outcome of one request. `HasValue` is true only when the view closed with `Complete(value)`;
`Value` is the default value otherwise. `ViewResult` holds factories for fakes and tests. Taught in
[Opening and results](Opening-and-Results.md#reading-a-result) and
[Faking a result in tests](Opening-and-Results.md#faking-a-result-in-tests).

- `Closed` is for a view that was shown and closed without a value; it throws `ArgumentException` for
  `CloseReason.NotShown`. `Closed(CloseReason.Completed)` is allowed: it is what a `ShowAsync` joined
  with other argument or result types returns.
- `NotShown` throws `ArgumentException` for `RejectReason.None`.
- `default(ViewResult<T>)` is not an outcome the navigator returns.

<!-- signature -->
```csharp
public readonly struct ViewResult<TResult>
{
    public int RequestId { get; }
    public bool WasShown { get; }
    public CloseReason Reason { get; }
    public RejectReason Rejected { get; }
    public string Detail { get; }
    public bool HasValue { get; }
    public TResult Value { get; }
    public bool TryGetValue(out TResult value);
}

public static class ViewResult
{
    public static ViewResult<TResult> Completed<TResult>(TResult value);
    public static ViewResult<TResult> Closed<TResult>(CloseReason reason);
    public static ViewResult<TResult> NotShown<TResult>(RejectReason reason, string detail = null);
}
```

| Member | Meaning |
|---|---|
| `RequestId` | The id of the request that produced the outcome. Zero on the factory results. |
| `WasShown` | True when the show transition started before the request ended. |
| `Reason` | How the view closed; `CloseReason.NotShown` when it was never shown. |
| `Rejected` | Why the request ended without being shown; `RejectReason.None` when it was shown. |
| `Detail` | Optional text with more detail. |
| `TryGetValue` | Returns `HasValue` and writes the value to the out parameter. |

### `ViewClosed`

The untyped outcome that `ViewHandle.WaitForCloseAsync` returns. It has the same
`WasShown`, `Reason`, `Rejected` and `Detail` as `ViewResult<TResult>`, without a value. Taught in
[Opening and results](Opening-and-Results.md#joining-an-open-view).

<!-- signature -->
```csharp
public readonly struct ViewClosed
{
    public bool WasShown { get; }
    public CloseReason Reason { get; }
    public RejectReason Rejected { get; }
    public string Detail { get; }
}
```

### `ViewPhase`

Where a request is in its life. A request passes through the phases in this order, each at most once.
Taught in [Concepts](Concepts.md#a-request-start-to-finish).

<!-- signature -->
```csharp
public enum ViewPhase
{
    Waiting,  // accepted and waiting for its layer to reach it
    Loading,  // the instance is being acquired and prepared
    Opening,  // the show transition is running
    Open,     // fully open
    Closing,  // the hide transition is running
    Ended     // the request has ended; its handle is inert
}
```

### `CloseReason`

How a shown view closed, or `NotShown`. Taught in
[Opening and results](Opening-and-Results.md#close-reasons).

<!-- signature -->
```csharp
public enum CloseReason
{
    NotShown,   // the request ended before the view was shown
    Completed,  // the view closed itself with a value
    Dismissed,  // the view closed itself without a value
    Back,       // Back closed the view
    Backdrop,   // a tap on the backdrop closed the view
    Code,       // code closed it through a handle, CloseTo or CloseAll
    Navigated,  // a screen change closed it, or it was reopened in place
    OwnerEnded, // the owner token of the request ended
    Destroyed   // destroyed or deactivated outside the package, or the root was destroyed
}
```

### `RejectReason`

Why a request ended without being shown. Taught in
[Opening and results](Opening-and-Results.md#reject-reasons).

<!-- signature -->
```csharp
public enum RejectReason
{
    None,          // the view was shown, so nothing was rejected
    UnknownKey,    // no catalog row has the requested id
    TypeMismatch,  // the prefab has no view for the key's types, or no view host
    Duplicate,     // the id is already in flight or open on a layer that does not allow it
    Busy,          // the navigator was busy and the call refused to wait, or a limit was reached
    Denied,        // a gate denied the request
    LoadFailed,    // the asset could not be loaded
    LoadTimedOut,  // the asset did not load within the load timeout
    Cancelled,     // the request was withdrawn before it was shown
    Declined,      // the view declined to open in its open hook
    Faulted,       // the view threw in its open hook
    OwnerEnded,    // the owner token ended before the view was shown
    RootGone,      // the root no longer exists
    WrongLayer     // a queue presented a key whose row is on another layer
}
```

---

## Events and observers

### `IViewObserver`

Receives every view event of a navigator. Add it with `INavigator.AddObserver` and an owner token.
`OnViewEvent` is called once per event, on the main thread; an exception thrown in it is routed to the
error handler and does not stop other observers. Taught in
[Events and analytics](Events-and-Analytics.md#the-observer-interface).

<!-- signature -->
```csharp
public interface IViewObserver
{
    void OnViewEvent(in ViewEvent viewEvent);
}
```

### `ViewEvent`

One fact about one request, published after the state it describes is in place. Taught in
[Events and analytics](Events-and-Analytics.md#the-fields-of-an-event).

<!-- signature -->
```csharp
public readonly struct ViewEvent
{
    public ViewEventKind Kind { get; }
    public int RequestId { get; }
    public string ViewId { get; }
    public string LayerId { get; }
    public string Source { get; }
    public int Priority { get; }
    public CloseReason Reason { get; }
    public RejectReason Rejected { get; }
    public string Detail { get; }
    public float SecondsWaiting { get; }
    public float SecondsOpen { get; }
}
```

| Property | Meaning |
|---|---|
| `Kind` | The kind of fact. |
| `RequestId`, `ViewId`, `LayerId` | The request, the view and its layer. Zero or null for `Settled`. |
| `Source` | The free tag the caller put in the open options or the queue options. |
| `Priority` | The queue priority of the item; zero for requests that did not come from a queue. |
| `Reason` | How the view closed. Set on `Closed` and `NotShown`; `CloseReason.NotShown` on every other kind. |
| `Rejected` | Why the request was not shown. Set on `NotShown`. |
| `Detail` | Optional text with more detail. |
| `SecondsWaiting` | Seconds from the navigator's acceptance to `Shown`, without time in a queue before it or while the app was suspended. Set on `Shown`. |
| `SecondsOpen` | Seconds from `Shown` to `Closed`, excluding time the app was suspended. Set on `Closed`. |

### `ViewEventKind`

The kind of fact a view event reports. Taught in
[Events and analytics](Events-and-Analytics.md#every-event-kind).

<!-- signature -->
```csharp
public enum ViewEventKind
{
    Enqueued,  // a queue accepted the item; published by the queue only
    Requested, // the navigator accepted the request
    Shown,     // the show transition ended and the view is open
    Closed,    // the view ended after being shown; its instance was released, or parked for reuse when the row says KeepInstance
    NotShown,  // the request ended without being shown
    Settled    // the navigator went from busy to settled; the request id is zero
}
```

---

## Queue and gates

### `ViewQueue`

Presents waiting requests on one layer, one at a time, by priority. It uses only `INavigator`, so it
is built in your code, not by the root. Create it on the main thread with a lifetime token; the queue
ends when the token ends or when the navigator's root is destroyed (`INavigator.RootToken`),
whichever comes first. Taught in [Queue](Queue.md).

- `EnqueueAsync` completes with the outcome, also when the request ends in the queue without being
  shown. `Enqueue` returns the request id that its events carry.
- `Hold`, `AddGate` and `AddObserver` return a `ViewRegistration` that ends with the owner. `Hold`
  records its caller's member and line for the long-hold warning
  ([UIDIRECTOR111](Troubleshooting.md#uidirector111)) and the window.
- `Clear` ends every waiting item as not shown with `RejectReason.Cancelled`; an item already
  presented is not touched. It is safe from any thread.
- When the lifetime token ends, waiting items end as not shown with `RejectReason.Cancelled`. When the
  root is destroyed, they end with `RejectReason.RootGone`, also when the root token is the lifetime.
  Later `Enqueue` and `EnqueueAsync` calls end the same way, and a queue built after the root is gone
  is inert from the start. An item that is already presented belongs to the navigator and ends there.
- An item whose presentation faults ends as not shown with `RejectReason.Faulted`.
- A key whose row is on another layer than the queue's is turned away when it is presented, as not
  shown with `RejectReason.WrongLayer` and [UIDIRECTOR126](Troubleshooting.md#uidirector126).
- `Enqueue`, `EnqueueAsync`, `Hold`, `AddGate`, `AddObserver` and the constructor throw
  `InvalidOperationException` off the main thread; the constructor does so while its lifetime token and the navigator's root are
  still alive, because it then registers an observer on the navigator; a queue built after either has ended is inert and registers nothing.
- The constructor throws `ArgumentNullException` for a null navigator or layer, and `ArgumentException`
  (parameter name `layer`) when the navigator is the root's own and has no layer with that id; other
  `INavigator` implementations are not checked. `Enqueue` and `EnqueueAsync` throw for a default key;
  `Hold`, `AddGate`, `AddObserver` and `Describe` for a null argument.

<!-- signature -->
```csharp
public sealed class ViewQueue
{
    public ViewQueue(INavigator navigator, ViewLayer layer, CancellationToken lifetime, string name = null);

    public int Count { get; }
    public bool IsHeld { get; }

    public UniTask<ViewResult<TResult>> EnqueueAsync<TArgs, TResult>(
        ViewKey<TArgs, TResult> key,
        TArgs args,
        CancellationToken owner,
        QueueOptions options = default);

    public int Enqueue<TArgs, TResult>(
        ViewKey<TArgs, TResult> key,
        TArgs args,
        CancellationToken owner,
        QueueOptions options = default);

    public ViewRegistration Hold(
        string reason,
        CancellationToken owner,
        [CallerMemberName] string member = null,
        [CallerLineNumber] int line = 0);

    public ViewRegistration AddGate(IViewGate gate, CancellationToken owner, int order = 0);
    public ViewRegistration AddObserver(IViewObserver observer, CancellationToken owner);

    public void Clear();
    public void Describe(StringBuilder output);
}
```

| Member | Meaning |
|---|---|
| `Count` | The number of waiting items; the item being presented is not counted. |
| `IsHeld` | True while at least one hold exists. Waiting items are not presented then. |
| `Hold` | Keeps waiting items from being presented until the owner ends or the registration is removed. Holds are counted, and each one is listed with its call site. |
| `AddGate` | Asks the gate about every item at enqueue and before presentation, in ascending `order`, until the owner ends. |
| `AddObserver` | Delivers this queue's `Enqueued` and `NotShown` events to the observer until the owner ends. |
| `Describe` | Writes the queue's state, holds, gates, presenting item and waiting items as text. |

### `QueueOptions`

Per-item options of a queued request. Taught in [Queue](Queue.md#queueoptions).

<!-- signature -->
```csharp
public struct QueueOptions
{
    public int Priority;
    public string Source;
}
```

`Priority`: higher is presented first; equal priorities keep their enqueue order. `Source`: a free tag
carried by events and gate requests.

### `IViewGate`

A synchronous rule on a queue, asked at enqueue and again right before presentation. A gate that
throws counts as a denial and is reported as [UIDIRECTOR119](Troubleshooting.md#uidirector119). Taught
in [Queue](Queue.md#gates).

<!-- signature -->
```csharp
public interface IViewGate
{
    GateDecision Evaluate(in GateRequest request);
}
```

### `GateRequest`

What a gate is asked about: one queued request, at enqueue or right before it is presented. Taught in
[Queue](Queue.md#gates).

<!-- signature -->
```csharp
public readonly struct GateRequest
{
    public int RequestId { get; }
    public string ViewId { get; }
    public ViewLayer Layer { get; }
    public int Priority { get; }
    public string Source { get; }
    public bool AtPresentation { get; }
}
```

`AtPresentation` is false at enqueue and true right before the item is presented. `Source` may be
null.

### `GateDecision`

A gate's answer: `Allow` lets the request pass this gate, `Deny` ends it as not shown with the reason
as its detail. `default(GateDecision)` is `Allow`, so an unassigned `GateDecision` field allows.
Taught in [Queue](Queue.md#gates).

<!-- signature -->
```csharp
public readonly struct GateDecision
{
    public static GateDecision Allow { get; }
    public static GateDecision Deny(string reason);
    public bool IsAllowed { get; }
    public string Reason { get; }
}
```

---

## Transitions

### `ViewTransition`

The base of every transition: a component on the view prefab, referenced by its `ViewHost`. A custom
transition uses unscaled time, stops when the token is cancelled, keeps `Snap` synchronous,
idempotent and non-throwing, never completes a running play's task from inside `Snap`, and never calls
the navigator. An exception from `PlayAsync` or `Snap` is reported to the error handler as
`ViewErrorSource.Transition`. Taught in
[Transitions](Transitions.md#write-a-custom-transition).

<!-- signature -->
```csharp
public abstract class ViewTransition : MonoBehaviour
{
    public abstract UniTask PlayAsync(bool show, CancellationToken ct);
    public abstract void Snap(bool shown);
}
```

`PlayAsync` animates to the shown or hidden pose on unscaled time, starting from the opposite end
pose, and stops when `ct` is cancelled. `Snap` writes the end pose now.

### `FadeTransition`, `ScaleTransition` and `SlideTransition`

The built-in transitions. `FadeTransition` fades the `CanvasGroup` alpha (shown is 1, hidden is 0).
`ScaleTransition` scales a target `RectTransform`, with an optional fade (shown is scale 1, hidden is
`Hidden Scale`). `SlideTransition` moves a target's `anchoredPosition` by the layer
rect's size in canvas units past an edge. All three run on unscaled time. Their duration, easing,
target and edge are Inspector fields; they add no public members of their own. Taught in
[Transitions](Transitions.md#put-a-built-in-transition-on-a-view).

<!-- signature -->
```csharp
[RequireComponent(typeof(CanvasGroup))]
public sealed class FadeTransition : ViewTransition
{
    public override UniTask PlayAsync(bool show, CancellationToken ct);
    public override void Snap(bool shown);
}

public sealed class ScaleTransition : ViewTransition
{
    public override UniTask PlayAsync(bool show, CancellationToken ct);
    public override void Snap(bool shown);
}

public sealed class SlideTransition : ViewTransition
{
    public override UniTask PlayAsync(bool show, CancellationToken ct);
    public override void Snap(bool shown);
}
```

### `SlideEdge`

The layer edge a `SlideTransition` slides in from and out to. Taught in the `SlideTransition` row of
[Transitions](Transitions.md#put-a-built-in-transition-on-a-view).

<!-- signature -->
```csharp
public enum SlideEdge
{
    Left,    // hidden one layer width to the left
    Right,   // hidden one layer width to the right
    Top,     // hidden one layer height above
    Bottom   // hidden one layer height below
}
```

---

## Errors and diagnostics

### `ViewErrorHandler`

Receives every exception thrown by user code that the package isolated: a view hook, a scope cleanup,
a scope task, a transition, a provider, the inject step, an observer or a gate. Set it in
`ViewRootSetup.ErrorHandler`; without one the package logs with `Debug.LogException`. Taught in
[Diagnostics](Diagnostics.md#the-error-handler).

<!-- signature -->
```csharp
public delegate void ViewErrorHandler(Exception exception, in ViewErrorContext context);
```

### `ViewErrorContext`

Where an error was raised. `ViewId` is null and `RequestId` is zero when none applies; `Target` is the
Unity object involved, for example the view instance, or null. An error from `ViewTransition.Snap`
carries the view id and the request id. An error from a provider's `Release` carries the view id; its
request id is zero when the instance was kept or the load result arrived after the request was
cancelled. Taught in
[Diagnostics](Diagnostics.md#the-error-handler).

<!-- signature -->
```csharp
public readonly struct ViewErrorContext
{
    public ViewErrorSource Source { get; }
    public string ViewId { get; }
    public int RequestId { get; }
    public UnityEngine.Object Target { get; }
}
```

### `ViewErrorSource`

Which kind of user callback or package step an error came from. Taught in
[Diagnostics](Diagnostics.md#the-error-handler).

<!-- signature -->
```csharp
public enum ViewErrorSource
{
    ViewHook,      // a hook on the view component threw
    ScopeCleanup,  // a scope cleanup threw while a scope ended
    ScopeTask,     // a task started with a scope threw
    Transition,    // a show or hide transition threw, or its Snap threw
    Provider,      // the provider threw (also in Release), a preload failed, or binding or placing an instance threw
    Inject,        // ViewRootSetup.Inject threw on a new instance; the request ends as not shown with LoadFailed
    Observer,      // an observer threw while an event was dispatched
    Gate           // a queue gate threw
}
```

### Diagnostic ids

Warnings and errors that the package logs carry a stable id. Each id has its own section in
[Troubleshooting](Troubleshooting.md); the console line ends with its anchor as text,
`(see Troubleshooting#uidirector1xx)`. How the lines are produced,
which builds log them, and the editor window are on [Diagnostics](Diagnostics.md). The ids from
UIDIRECTOR121 to UIDIRECTOR124 are reported by the editor validator only; UIDIRECTOR125 is reported by
the validator and by the root when it registers a catalog; UIDIRECTOR126 is reported at run time.

| Id | Meaning |
|---|---|
| [UIDIRECTOR101](Troubleshooting.md#uidirector101) | Unknown view id: no catalog row has the requested id. |
| [UIDIRECTOR102](Troubleshooting.md#uidirector102) | The prefab has no view for the key's argument and result types. |
| [UIDIRECTOR103](Troubleshooting.md#uidirector103) | The prefab root has no `ViewHost`. |
| [UIDIRECTOR104](Troubleshooting.md#uidirector104) | Load failed: the view could not be created. |
| [UIDIRECTOR105](Troubleshooting.md#uidirector105) | Load timed out. |
| [UIDIRECTOR106](Troubleshooting.md#uidirector106) | A transition timed out and was snapped to its end. |
| [UIDIRECTOR107](Troubleshooting.md#uidirector107) | An open view was destroyed or deactivated by code outside the package; its instance is released, also when the row keeps instances. |
| [UIDIRECTOR108](Troubleshooting.md#uidirector108) | Duplicate view id: a second row with the same id was refused. |
| [UIDIRECTOR109](Troubleshooting.md#uidirector109) | A row names a layer that does not exist. |
| [UIDIRECTOR110](Troubleshooting.md#uidirector110) | An input hold has been active for more than 30 seconds. |
| [UIDIRECTOR111](Troubleshooting.md#uidirector111) | A queue hold has been active for more than 60 seconds; the message names the call that took it. |
| [UIDIRECTOR112](Troubleshooting.md#uidirector112) | A layer reached its waiting cap; the new request was refused. |
| [UIDIRECTOR113](Troubleshooting.md#uidirector113) | An owner token, or a queue's lifetime or root token, was cancelled off the main thread; it was handled on the main thread. |
| [UIDIRECTOR114](Troubleshooting.md#uidirector114) | The root canvas is in Screen Space - Camera mode but has no camera. |
| [UIDIRECTOR115](Troubleshooting.md#uidirector115) | The sorting band is exhausted; the request was refused. |
| [UIDIRECTOR116](Troubleshooting.md#uidirector116) | There is no EventSystem in the scene. |
| [UIDIRECTOR117](Troubleshooting.md#uidirector117) | `OnOpen` of a view threw an exception. |
| [UIDIRECTOR118](Troubleshooting.md#uidirector118) | `Complete` or `Close` was called inside `OnOpen`. |
| [UIDIRECTOR119](Troubleshooting.md#uidirector119) | A gate threw an exception; the request was treated as denied. |
| [UIDIRECTOR120](Troubleshooting.md#uidirector120) | A row has an asset key, but the root has no view provider. |
| [UIDIRECTOR121](Troubleshooting.md#uidirector121) | A catalog row has neither a prefab nor an asset key. |
| [UIDIRECTOR122](Troubleshooting.md#uidirector122) | A row uses `BackPolicy.Close` on a layer that does not take back. |
| [UIDIRECTOR123](Troubleshooting.md#uidirector123) | A view prefab contains a dropdown. |
| [UIDIRECTOR124](Troubleshooting.md#uidirector124) | An asset key matches no Addressables address, GUID or label. |
| [UIDIRECTOR125](Troubleshooting.md#uidirector125) | A catalog row has an empty or whitespace-only id; the row is refused. |
| [UIDIRECTOR126](Troubleshooting.md#uidirector126) | A queue presented a key whose row is on another layer; the request was refused. |

---

## Asset providers

### `IViewProvider`

Creates and releases instances for rows that carry an asset key. The root uses the provider in
`ViewRootSetup.AssetProvider`, or an `IViewProvider` component on its own object. The Addressables
integration is one implementation; a project can write its own. Taught in
[Assets and Addressables](Assets-and-Addressables.md#write-a-provider-of-your-own).

- `AcquireAsync` returns an inactive instance under the given inactive parent. On cancel it releases
  whatever it loaded, also when the load completes later.
- `Release` releases exactly what `AcquireAsync` returned. It never throws and is safe on a
  destroyed instance. The package deactivates the instance first, so a pooling provider receives it
  inactive. A provider that throws anyway is reported to the error handler as `ViewErrorSource.Provider`
  with the view id; the instance still counts as released.
- `PreloadAsync` keeps the asset loaded until the returned lease is disposed.

<!-- signature -->
```csharp
public interface IViewProvider
{
    UniTask<GameObject> AcquireAsync(string assetKey, Transform inactiveParent, CancellationToken ct);
    void Release(GameObject instance);
    UniTask<IDisposable> PreloadAsync(string assetKey, CancellationToken ct);
}
```

---

## Addressables

Assembly `Ecanakli.UIDirector.Addressables`, compiled when `com.unity.addressables` is installed.

### `AddressablesViewProvider`

An `IViewProvider` that resolves asset keys through Addressables. Add it as a component next to the
`ViewRoot`, or pass it in `ViewRootSetup.AssetProvider`. It owns every handle it creates and is the
only caller that releases them, so view code never sees a handle. Call it from the main thread only.
Taught in [Assets and Addressables](Assets-and-Addressables.md#the-addressablesviewprovider-component).

- `AcquireAsync` and `PreloadAsync` throw `ObjectDisposedException` when the provider was destroyed,
  `ArgumentException` for a null or empty key, and `InvalidOperationException` when the load failed or
  produced nothing (the message names the key). They report a cancelled token as a cancelled task.
- `AcquireAsync` loads the prefab asset and instantiates it itself under the inactive parent, only
  while the call is still wanted and the parent still exists. It throws `ArgumentNullException` for a
  null parent; a parent that is destroyed at the call or during the load faults the task with
  `InvalidOperationException`, and nothing is instantiated.
- The instances are ordinary instantiated objects, not Addressables-tracked instances: the provider
  holds the asset handle while the instance lives, and `Addressables.ReleaseInstance` on one returns
  false. Hand an instance back with `Release`, which destroys it with `Object.Destroy` (Play Mode) and
  releases the handle.
- `Release` does nothing for null, for an unknown instance or for one that was already released.
- The lease that `PreloadAsync` returns releases the asset on its first `Dispose`; later calls do
  nothing.

<!-- signature -->
```csharp
public sealed class AddressablesViewProvider : MonoBehaviour, IViewProvider
{
    public UniTask<GameObject> AcquireAsync(string assetKey, Transform inactiveParent, CancellationToken ct);
    public void Release(GameObject instance);
    public UniTask<IDisposable> PreloadAsync(string assetKey, CancellationToken ct);
}
```

---

## DOTween

Assembly `Ecanakli.UIDirector.DOTween`, compiled when DOTween is installed.

### `TweenTransition`

A fade, scale and offset transition driven by DOTween on unscaled time. Shown is alpha 1, scale 1 and
the authored position; hidden is alpha 0, the hidden scale and that position moved by a fraction of
the layer rect. Each part can be turned off, and the duration and the DOTween ease are Inspector
fields. It adds no public members to `ViewTransition`. A kill that the component makes itself
(`Snap`, a newer play, `OnDestroy`) resumes the awaiting play on the next player-loop step, never inside
that call. Taught in [DOTween](DOTween.md#tweentransition).

<!-- signature -->
```csharp
public sealed class TweenTransition : ViewTransition
{
    public override UniTask PlayAsync(bool show, CancellationToken ct);
    public override void Snap(bool shown);
}
```

---

## Zenject

Assembly `Ecanakli.UIDirector.DependencyInjection.Zenject`, namespace
`Ecanakli.UIDirector.DependencyInjection`, compiled while `ECANAKLI_UIDIRECTOR_DI_ZENJECT` is defined (see [Zenject](Zenject.md#turn-the-integration-on)).

### `NavigatorInstaller`

Connects a `ViewRoot` to a Zenject container: the container injects every view instance, and
`INavigator` and `ViewRoot` can be injected. Call `Install` from `InstallBindings`, with
"Initialize On Awake" turned off on the root. Taught in
[Zenject](Zenject.md#wire-the-root-in-an-installer).

- `Install` throws `ArgumentNullException` when the container, the root or (in the three-argument
  overload) the setup is null, `InvalidOperationException` when the root was already initialised, and passes on the `ArgumentException` (parameter name `setup`) that `ViewRoot.Initialize` throws when `ExtraLayers` repeats a layer id.
- The three-argument overload takes a `ViewRootSetup` for `ExtraLayers`, `ErrorHandler` and
  `AssetProvider`. The installer sets `Inject` to the container itself, so a setup whose `Inject` is not
  null makes `Install` throw `ArgumentException`. The setup is copied and never changed.
- While the container validates, `Install` only checks its arguments and binds, and leaves the root
  untouched ([While the container validates](Zenject.md#while-the-container-validates)).

<!-- signature -->
```csharp
public static class NavigatorInstaller
{
    public static void Install(DiContainer container, ViewRoot root);
    public static void Install(DiContainer container, ViewRoot root, ViewRootSetup setup);
}
```

---

## See also

- [Concepts](Concepts.md): the model behind these types.
- [Troubleshooting](Troubleshooting.md): one section per diagnostic id.
- [Threading](Threading.md): which members may be called from which thread.
- [Limitations](Limitations.md): what the package does not do.
