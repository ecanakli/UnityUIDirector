# Catalogs and keys

[Back to index](index.md)

A view prefab becomes openable when a catalog row describes it and a key names that row. This page
covers the whole path: creating a `ViewCatalog` asset and its rows (every field of a row), writing
typed keys, giving the catalogs to the root, adding and removing a catalog at run time, keeping ids
unique, and checking everything with the validator before you press Play. The model behind it is in
[Concepts](Concepts.md).

---

## The shortest path

1. Create a catalog: `Assets > Create > UIDirector/View Catalog`.
2. Select it, add a row to the list and fill it: an `Id`, the `Prefab`, and the `Layer Id` it opens on
   (the default is `screens`). The prefab root needs a `ViewHost` and a component that implements
   `IView<TArgs, TResult>`; see [View authoring](View-Authoring.md).
3. Drag the catalog into the `Catalogs` list of the `ViewRoot` in your scene.
4. Write a key for the row, in a static class marked `[ViewKeys]`:

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

5. Run `Tools/UIDirector/Validate Catalogs`. A clean run logs
   `UIDirector catalog validation PASSED: 0 error(s), 0 warning(s), 1 catalog(s).` (the last number is
   how many catalogs the project holds)

The key `Home` now opens the row whose `Id` is `home`; see [Opening and results](Opening-and-Results.md).
Ids are plain strings and are compared exactly, so `Home` and `home` are two ids.

---

## Make a view and its row

Catalogs, rows, view prefabs and the root's `Catalogs` list are authored in the editor, in the
Inspector. Nothing here is built from code (see [Limitations](Limitations.md#setup)). The whole path for
one new view:

1. **Build the prefab.** Make a UI object (it has a `RectTransform`) and save it as a prefab. On its
   root add:
   - a `ViewHost`; Unity adds the `Canvas` and `GraphicRaycaster` it requires. Do not add a
     `CanvasScaler`.
   - your view component, on the same object: a class derived from `ViewBehaviour`,
     `ViewBehaviour<TArgs>` or `ViewBehaviour<TArgs, TResult>`, or one that implements
     `IView<TArgs, TResult>` ([View authoring](View-Authoring.md)). Assign its serialized fields (buttons,
     texts) in the prefab.
   - optionally a transition (`FadeTransition`, `ScaleTransition` or `SlideTransition`) dragged into the
     `Transition` field of the `ViewHost`. A view without one shows and hides instantly. See
     [Transitions](Transitions.md#put-a-built-in-transition-on-a-view).
2. **Create or pick a catalog.** `Assets > Create > UIDirector/View Catalog` makes a `ViewCatalog`
   asset. Select it and press `+` on its list to add a row.
3. **Fill the row.** `Id` (unique, matches the key), `Prefab` (drag the prefab) or `Asset Key` (when the
   prefab loads on demand), `Layer Id` (`screens`, `popups`, `system` or a layer you declared), then
   the policies you need: `Retention`, `Back`, `Close On Backdrop`, `Cover`, `When Covered`,
   `Allow Multiple`. The defaults suit a plain screen. Every field is described in
   [A catalog row](#a-catalog-row).
4. **Give the catalog to the root.** Select the `ViewRoot` in the scene (create one with
   `GameObject > UI > UIDirector Root`), add an element to its `Catalogs` list and drag the catalog in.
   A catalog that is not in that list, and was not passed to `AddCatalog`, opens nothing.
5. **Write the key** in a `[ViewKeys]` class, with the same id and the argument and result types of the
   view component (see [Keys](#keys)).
6. **Validate.** `Tools/UIDirector/Validate Catalogs`, or the `Validate` button under the rows of the
   catalog. A missing `ViewHost`, a view component that does not match the key and an unknown layer are
   reported before you press Play. For a row with an `Asset Key`, the prefab checks run only when
   Addressables is installed and the key resolves to an address.

---

## A catalog row

A `ViewCatalog` is a list of `ViewEntry` rows. The Inspector draws the list with the default
Inspector, followed by a validation block (see [Validate](#validate)). Rows are authored in the
Inspector; `ViewEntry` has no public constructor and a catalog's row list cannot be filled from code, so a catalog that opens anything is an asset.

| Field | Default | What it does |
|---|---|---|
| `Id` | empty | The name code and remote data use for this view. Must be set and unique across every catalog on the root. An id that is empty or only whitespace is [UIDIRECTOR125](Troubleshooting.md#uidirector125): the validator reports it and the root refuses the row. An id with spaces around other characters is valid and is not trimmed. |
| `Prefab` | none | A direct reference to the view prefab. The prefab stays resident while its catalog is loaded; opening instantiates it at once, under an inactive parent so `Awake` has not run yet. Its root needs a `ViewHost` ([UIDIRECTOR103](Troubleshooting.md#uidirector103)). |
| `Asset Key` | empty | Used only when `Prefab` is empty. A string the root's view provider resolves, so nothing is resident until the view opens. With neither prefab nor key the row is [UIDIRECTOR121](Troubleshooting.md#uidirector121). With a key and no provider the open fails with `RejectReason.LoadFailed` ([UIDIRECTOR120](Troubleshooting.md#uidirector120)). See [Assets and Addressables](Assets-and-Addressables.md). |
| `Layer Id` | `screens` | The id of the layer the view opens on: `screens`, `popups`, `system`, or a layer you declared. A row naming a layer the root does not have is refused at registration ([UIDIRECTOR109](Troubleshooting.md#uidirector109)). |
| `Retention` | `ReleaseOnClose` | What happens to the instance when the view closes. `ReleaseOnClose` releases it. `KeepInstance` parks one idle instance per id, hidden and deactivated, and reuses it on the next open without injecting again. The view's `OnOpen` runs again on every open either way. |
| `Back` | `LayerDefault` | What `Back()` does when this view is the back target. `LayerDefault` follows the layer, `Close` closes the view with `CloseReason.Back`, `Ignore` swallows back and keeps the view open, `HandledByView` asks the view's `IViewBack.OnBack`; when it returns `false` the layer's rule applies, as with `LayerDefault`. `Close` on a layer without `TakesBack` is [UIDIRECTOR122](Troubleshooting.md#uidirector122). See [Navigation and back](Navigation-and-Back.md). |
| `Close On Backdrop` | off | On a modal layer, a tap on the backdrop closes this view with `CloseReason.Backdrop`, when the view is fully open. A tap while the input shield is up does nothing. See [Popups and backdrop](Popups-and-Backdrop.md). |
| `Cover` | `LayerDefault` | Whether the view hides what is below it once it is fully open. `LayerDefault` covers on a history layer and does not elsewhere; `Covers` always hides what is below; `Transparent` never does. |
| `When Covered` | `DisableCanvas` | How a covered view is hidden. `DisableCanvas` turns off every enabled canvas under the view and turns only those back on at reveal; the view keeps running. `Deactivate` deactivates the view object, which stops its `Update`, animators and particles, at the cost of a rebuild when it returns. |
| `Allow Multiple` | off | More than one instance of this id may exist at once. Without it a second open of an id that is already open or in flight is refused with `RejectReason.Duplicate` on a non-history layer, or returns to the existing screen on a history layer (and reopens it in place when the key has arguments). |

Some notes on the fields people ask about:

- **`Prefab` wins over `Asset Key`.** When both are set the direct reference is used and the key is
  ignored. Use the key alone when you want the asset to load on demand.
- **The id and the asset key are separate on purpose.** Code and remote data name the id; the key may
  change when an address is renamed, and only the row changes.
- **`KeepInstance` keeps one idle instance per id and nothing more.** There is no budget and no
  timer. `INavigator.Trim()` releases every idle kept instance, and the root calls it on
  `Application.lowMemory`. A deep stack of heavy screens stays resident, so keep stacks shallow with
  `CloseTo`.
- **`Cover` and `When Covered` are independent.** One says whether the view hides what is below it,
  the other says how a view that is hidden is switched off.

---

## Keys

A key is the compile-time half of a row: the id plus the type of the arguments the view takes and the
type of the value it answers with.

<!-- signature -->
```csharp
public readonly struct ViewKey<TArgs, TResult>
{
    public ViewKey(string id);
    public string Id { get; }
}
```

You rarely call the constructor. `ViewKey.Create` has three forms:

| Call | Key type | Use it for |
|---|---|---|
| `ViewKey.Create("home")` | `ViewKey<NoArgs, NoResult>` | A view with no arguments and no answer |
| `ViewKey.Create<ItemArgs>("item-detail")` | `ViewKey<ItemArgs, NoResult>` | A view that takes arguments and answers nothing |
| `ViewKey.Create<ConfirmArgs, bool>("confirm")` | `ViewKey<ConfirmArgs, bool>` | A view that takes arguments and answers with a value |

A null id throws `ArgumentNullException`, and an id that is empty or only whitespace throws
`ArgumentException`, at the point the key is created. An id with spaces around other characters is
accepted as written and compared exactly. In a static field the exception surfaces as a
`TypeInitializationException` when the class is first used, and the class's other keys fail with it.
The validator cannot read any key of such a class and skips them all, so fix a blank id first.

**What the types do.**

- The call site is checked by the compiler: `Navigator.ShowAsync(AppViews.Confirm, new ConfirmArgs(...), ...)`
  takes a `ConfirmArgs` and completes with a `ViewResult<bool>`.
- At open time the package looks for a component on the prefab root that implements
  `IView<TArgs, TResult>` for exactly the key's types. If there is none the request ends as not shown
  with `RejectReason.TypeMismatch` ([UIDIRECTOR102](Troubleshooting.md#uidirector102)). The key and the
  view must agree on both types, including `NoArgs` and `NoResult`.
- Use `NoArgs` and `NoResult` for "nothing". They are empty structs defined by the package.
- Argument and result types can be any type. Small `readonly struct` types, as in the sample's
  `ViewArgs.cs`, make the arguments immutable and allocate nothing. Under IL2CPP each struct
  combination adds its own copy of the generic code, so keep the number of distinct struct types
  modest, or use classes where code size matters.
- On a history layer the type of the arguments matters to the stack: a key without arguments
  (`NoArgs`) returns to an existing screen of that id, and a key with arguments reopens that screen in
  place with the new arguments. See [Navigation and back](Navigation-and-Back.md).

**Shortcuts.** For a key without arguments, `INavigator` has extension methods that fill in the empty
arguments: `ShowAsync(key, owner)` and `Open(key, owner)`. `CloseTo(key)` and `IsOpen(key)` take any
key and read its id.

**Where keys live.** Put the keys of one area in one static class and mark it `[ViewKeys]`, as
`AppViews` does above. The mark costs nothing at run time. It tells the validator to read every
static `ViewKey` field of that class, public or not, and report any key with no row
([UIDIRECTOR101](Troubleshooting.md#uidirector101)) or whose prefab has no matching view
([UIDIRECTOR102](Troubleshooting.md#uidirector102)). A feature that ships its own catalog ships its
own keys class, apart from the app's:

<!-- source: Samples~/QueueUsage/NewsViews.cs -->
```csharp
[ViewKeys]
public static class NewsViews
{
    public static readonly ViewKey<NoticeArgs, NoResult> News = ViewKey.Create<NoticeArgs>("news");
}
```

A key that is not in a `[ViewKeys]` class still works, but nothing checks it before you press Play.
Classes in test assemblies (assemblies that reference NUnit) are skipped, because test fixtures have
no catalog rows.

---

## Give the catalogs to the root

The `ViewRoot` Inspector has a `Catalogs` list. Every catalog in it is registered when the root
initialises, in list order. Catalogs are Inspector data, so they are not part of `ViewRootSetup`;
`ViewRootSetup` carries the code-only settings (`Inject`, `AssetProvider`, `ExtraLayers`,
`ErrorHandler`).

A row can only name a layer that exists on the root. The three built-in layers always exist. A layer
of your own is declared in code, as the Basic Usage sample does for its toast layer:

<!-- source: Samples~/BasicUsage/AppLayers.cs -->
```csharp
// Above the system layer, no history, no back and no input shield: a toast never blocks a tap.
public static readonly ViewLayer Toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);
```

and passed to the root in `ExtraLayers`, with the rest of the code-only setup. Turn off
`Initialize On Awake` on the `ViewRoot`, then call `Initialize` once, before anything reads
`ViewRoot.Navigator`. With the flag on, the root initialises itself with default settings and a second
`Initialize` throws `InvalidOperationException`:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
_viewRoot.Initialize(new ViewRootSetup
{
    Inject = InjectServices,
    ErrorHandler = OnViewError,
    ExtraLayers = new[] { AppLayers.Toasts }
});
```

Rows then set `Layer Id` to `toasts`. The root merges extra layers with the three defaults and sorts
them by order; layers with an equal order keep the order you gave. An extra layer whose id repeats
the id of another layer makes `Initialize` throw an `ArgumentException`.

Declare each extra layer as a `static` field of type `ViewLayer`, as above. The validator finds
layers by reading those fields; a layer built inline and never stored in a static field is unknown to
it, and every row that names it is reported as [UIDIRECTOR109](Troubleshooting.md#uidirector109)
although it works at run time.

---

## Add and remove a catalog at run time

A feature that is not always present can bring its own catalog and register it while the app runs.
`INavigator.AddCatalog(catalog, owner)` registers every row of the catalog until `owner` ends or the
returned `ViewRegistration` is removed. The owner is the same kind of token as the owner of a view
(see [Concepts](Concepts.md#every-open-has-an-owner)), so the registration needs no removal line.

`NewsFeature` in the Queue Usage sample keeps one token source for everything the feature adds. It is
linked to the root's token, so the feature ends with the root, or earlier on `Remove`:

<!-- source: Samples~/QueueUsage/NewsFeature.cs -->
```csharp
_owner = CancellationTokenSource.CreateLinkedTokenSource(_navigator.RootToken);
_navigator.AddCatalog(_catalog, _owner.Token);
```

Taking the feature away is ending that owner:

<!-- source: Samples~/QueueUsage/NewsFeature.cs -->
```csharp
_owner.Cancel();
_owner.Dispose();
_owner = null;
```

What each call does:

- **Registration.** Every acceptable row is registered. The checks run in this order, and a refused
  row claims no id: a row whose id is empty or only whitespace is refused with
  [UIDIRECTOR125](Troubleshooting.md#uidirector125), a row that names an unknown layer with
  [UIDIRECTOR109](Troubleshooting.md#uidirector109), and a row whose id is already registered with
  [UIDIRECTOR108](Troubleshooting.md#uidirector108). All three are logged as errors, and the other
  rows of the same catalog still register.
- **Bad calls.** A null catalog throws `ArgumentNullException`. A call off the main thread throws
  `InvalidOperationException`. When the root is gone or the owner is already cancelled the call
  registers nothing and returns an inactive registration (`IsActive` is `false`).
- **Removal.** `registration.Remove()` and the owner ending do the same thing, and either is safe to
  repeat. The ids of the catalog become unknown to new opens (`RejectReason.UnknownKey`) and are free
  again, so the same rows can be registered later without [UIDIRECTOR108](Troubleshooting.md#uidirector108).
- **Views that are already open keep their row.** Removing a catalog does not close them; they
  finish normally. A kept instance of a removed row is released, and a view of a removed row that
  closes later is released instead of kept.
- **An open view ends with the catalog only when it shares the owner.** In the sample the news popup
  is opened with the same owner as the catalog, so ending the owner removes the catalog and also ends
  the popup: a shown popup closes with `CloseReason.OwnerEnded` and a waiting one is withdrawn.

---

## Ids are unique across all catalogs

All catalogs registered on one root share one id space. Ids are compared exactly, including case.

- If two rows share an id, the **first one wins**. At run time the second row is refused and logged as
  [UIDIRECTOR108](Troubleshooting.md#uidirector108); the first row opens normally. The order is the
  `Catalogs` list order, then the order of `AddCatalog` calls, then the order of rows in a catalog.
- Registering the same catalog twice refuses every row of the second registration.
- The validator treats every `ViewCatalog` in the project as one id space, so it reports a clash even
  between two catalogs that no root registers together. Give each feature its own id prefix or pick
  ids that cannot meet. The Basic Usage and Queue Usage samples use disjoint ids for this reason.
- A row refused for an unknown layer ([UIDIRECTOR109](Troubleshooting.md#uidirector109)) or an empty
  id claims no id, in the root and in the validator. A later row with the same id on a known layer is
  the one that registers, and it is not reported as a duplicate.

---

## Validate

`Tools/UIDirector/Validate Catalogs` checks every `ViewCatalog` in the project and every static class
marked `[ViewKeys]`. It logs one line per finding, with the id first and the Troubleshooting anchor
last, then a summary:

```text
UIDIRECTOR108: Row 3 ('item') in catalog 'Ui' repeats the id first used by Row 0 ('item') in catalog 'Core'; the second row is refused. (see Troubleshooting#uidirector108)
UIDirector catalog validation FAILED: 1 error(s), 0 warning(s), 2 catalog(s).
```

The same checks run in the catalog's Inspector. The block under the rows lists the findings of that
catalog; the `Validate` button runs it again and the `?` next to a finding opens its section of the
documentation. The Inspector validates every catalog in the project, so a clash with another catalog
shows up on the catalog that holds the second row.

| Id | Severity | Finding |
|---|---|---|
| [UIDIRECTOR101](Troubleshooting.md#uidirector101) | Error | A key field of a `[ViewKeys]` class has no catalog row the root registers; when rows with that id exist but all name an unknown layer, the message names the first of them |
| [UIDIRECTOR102](Troubleshooting.md#uidirector102) | Error | The prefab root has no component implementing `IView<TArgs, TResult>` for the key |
| [UIDIRECTOR103](Troubleshooting.md#uidirector103) | Error | The prefab root has no `ViewHost` |
| [UIDIRECTOR108](Troubleshooting.md#uidirector108) | Error | Two rows on known layers share an id; the first wins |
| [UIDIRECTOR109](Troubleshooting.md#uidirector109) | Error | A row names a layer that does not exist |
| [UIDIRECTOR120](Troubleshooting.md#uidirector120) | Warning | A row has an asset key and Addressables is not installed |
| [UIDIRECTOR121](Troubleshooting.md#uidirector121) | Error | A row has neither a prefab nor an asset key |
| [UIDIRECTOR122](Troubleshooting.md#uidirector122) | Warning | `Back` is `Close` on a layer that does not take back |
| [UIDIRECTOR123](Troubleshooting.md#uidirector123) | Warning | The prefab contains a dropdown |
| [UIDIRECTOR124](Troubleshooting.md#uidirector124) | Warning | The asset key matches no Addressables address, GUID or label |
| [UIDIRECTOR125](Troubleshooting.md#uidirector125) | Error | A row has an empty or whitespace-only id; the root reports it too when it refuses the row |

In a build pipeline run the validator in batch mode with
`-executeMethod Ecanakli.UIDirector.EditorTools.CatalogValidation.RunFromCommandLine`. It exits with
code 1 when any error exists and 0 otherwise; warnings never fail a run.

---

## Traps

- **A key is not checked until it is in a `[ViewKeys]` class.** A mistyped id in an unmarked class
  fails at the first open with `RejectReason.UnknownKey` ([UIDIRECTOR101](Troubleshooting.md#uidirector101)).
- **The key's types must match the view's.** The compiler cannot see the prefab. A `ViewKey<NoArgs, NoResult>`
  for a view that implements `IView<ItemArgs, NoResult>` fails at open time, not at compile time. The
  validator finds it before you press Play.
- **The view component sits on the prefab root,** next to the `ViewHost`. A component on a child
  object is not found.
- **A catalog that is not on the root opens nothing.** Check the `Catalogs` list, or call `AddCatalog`
  before the first open of its ids.
- **A refused row is silent in the game, loud in the log.**
  [UIDIRECTOR108](Troubleshooting.md#uidirector108),
  [UIDIRECTOR109](Troubleshooting.md#uidirector109) and
  [UIDIRECTOR125](Troubleshooting.md#uidirector125) are logged in the editor and in development
  builds only; a release build refuses the row without a message. Run the validator before you ship.
- **The validator is not run for you.** Nothing checks catalogs at build time unless your pipeline
  calls the batch entry point.

---

## See also

- [Concepts](Concepts.md): rows, keys, layers and owners in one picture
- [Opening and results](Opening-and-Results.md): opening a key and reading its outcome
- [View authoring](View-Authoring.md): the component a row's prefab needs
- [Navigation and back](Navigation-and-Back.md): `Back`, `Cover` and the history stack in use
- [Assets and Addressables](Assets-and-Addressables.md): rows with an asset key, retention and preload
- [Queue](Queue.md): the feature that brings its own catalog also queues a popup
- [Troubleshooting](Troubleshooting.md): `UIDIRECTOR101` to `UIDIRECTOR126`
- [API reference](API-Reference.md): `ViewCatalog`, `ViewEntry`, `ViewKey`, `ViewLayer`
