# Assets and Addressables

[Back to index](index.md)

A catalog row names its view in one of two ways: a prefab reference that is resident while the
catalog is loaded, or an asset key that a provider resolves when the view opens. This page is
about the second way: loading views by address with the Addressables integration, loading them
ahead of time, releasing them, what happens when a load fails, and what a provider of your own
has to do. The examples are the files of the Addressables Usage sample.

---

## Open a view by address

Four steps. The core carries no Addressables type; the key is a plain string.

1. Install `com.unity.addressables`. The integration compiles itself (see
   [The integration turns on by itself](#the-integration-turns-on-by-itself)).
2. Add an `AddressablesViewProvider` component to the same GameObject as the `ViewRoot`.
3. In the catalog, leave a row's `Prefab` empty and write the prefab's Addressables address in
   `Asset Key`.
4. Mark that prefab addressable with exactly that address.

Open the view as you open any other. The keys of the sample are plain keys; nothing in them says the
rows have no prefab:

<!-- source: Samples~/AddressablesUsage/RemoteViews.cs -->
```csharp
[ViewKeys]
public static class RemoteViews
{
    public static readonly ViewKey<NoArgs, NoResult> Gallery = ViewKey.Create("gallery");
    public static readonly ViewKey<PhotoArgs, NoResult> Photo = ViewKey.Create<PhotoArgs>("photo");
}
```

The root is initialised with an empty setup. Its `AssetProvider` stays empty, so the root finds the
`AddressablesViewProvider` component on its own GameObject:

<!-- source: Samples~/AddressablesUsage/AddressablesBootstrap.cs -->
```csharp
// AssetProvider stays empty, so the AddressablesViewProvider on the root's GameObject is used.
_viewRoot.Initialize(new ViewRootSetup());
```

In the sample, the `gallery` row has the address `uidirector-samples/gallery` and is a screen that
keeps its instance, and the `photo` row has `uidirector-samples/photo` and is a popup that closes on a
tap on the backdrop. Pressing Play opens the gallery on its own (`source=boot`).

In the editor the default Addressables play mode script, "Use Asset Database", loads the prefabs
directly, so no content build is needed to try it. A player build needs the Addressables content
built first.

---

## Rows with a prefab and rows with an asset key

A project can mix both in one catalog.

| | Row with a `Prefab` | Row with an `Asset Key` |
|---|---|---|
| Resident | The prefab, while its catalog is loaded | Nothing until the view opens |
| Opening | `Object.Instantiate` in the call that opens it, under the inactive staging object, so `Awake` has not run | The root's `IViewProvider` loads and instantiates it, asynchronously |
| Needs a provider | No | Yes. Without one the open ends as `RejectReason.LoadFailed` with [UIDIRECTOR120](Troubleshooting.md#uidirector120) |
| Load wait | None | Yes: the request is in `Loading` until the provider returns |
| Closing | The instance is destroyed, or parked for `KeepInstance` | The instance is handed back to the provider's `Release`, or parked for `KeepInstance` (the provider's handle then stays alive) |
| Preload | `PreloadAsync` returns `true` at once and loads nothing | `PreloadAsync` asks the provider to keep the asset loaded |

When a row has both a prefab and an asset key, the prefab wins and the key is ignored. The row's
`Id` and its `Asset Key` are separate on purpose: code and remote data keep naming the id, and
renaming an address changes one catalog row, which the validator reports. See
[Catalogs and keys](Catalogs-and-Keys.md#a-catalog-row) for every field of a row.

---

## The provider seam

The root resolves asset keys through one `IViewProvider`:

<!-- signature -->
```csharp
public interface IViewProvider
{
    UniTask<GameObject> AcquireAsync(string assetKey, Transform inactiveParent, CancellationToken ct);
    void Release(GameObject instance);
    UniTask<IDisposable> PreloadAsync(string assetKey, CancellationToken ct);
}
```

The root takes its provider when it initialises, in this order:

1. `ViewRootSetup.AssetProvider`, when it is set.
2. An `IViewProvider` component on the same GameObject as the `ViewRoot`.
3. None. A row with an asset key then ends as `LoadFailed` with
   [UIDIRECTOR120](Troubleshooting.md#uidirector120), and `PreloadAsync` returns `false`.

<!-- signature -->
```csharp
public IViewProvider AssetProvider;
```

Because the provider is read once in `Initialize`, a component that is added to the root's
GameObject after the root has initialised is not seen. With `Initialize On Awake` on, the root
initialises in `Awake`, so the component must already be on the object, as a prefab or scene
setup has it. With `Initialize On Awake` off, set `AssetProvider` yourself or add the component
before you call `Initialize` ([Catalogs and keys](Catalogs-and-Keys.md#give-the-catalogs-to-the-root)).
A second `Initialize` throws.

To add the provider from code, turn `Initialize On Awake` off on the root, create the component, and pass
it in the setup before anything reads `Navigator` (its first access initialises the root with default
settings, which has no provider unless one sits on the root's GameObject):
`_viewRoot.Initialize(new ViewRootSetup { AssetProvider = _viewRoot.gameObject.AddComponent<AddressablesViewProvider>() });`.
The component may sit on any GameObject that lives as long as the root; it does not have to be on the
root's. The root never destroys it. A root that initialised in `Awake` has already read its provider, so
adding the component afterwards and calling `Initialize` throws.

A direct-prefab row never touches the provider.

---

## The integration turns on by itself

The Addressables integration is a separate assembly, `Ecanakli.UIDirector.Addressables`. It
declares a version define for `com.unity.addressables` and requires it, so it compiles when the
Addressables package is installed and is absent when it is not. There is no menu to run and no
scripting define to set. The package does not list Addressables as a dependency; a project that
does not use it never compiles any Addressables type. The editor validator reads the same define,
so its Addressables checks run only when the package is installed.

The core has no reference to the integration. If you remove Addressables, the rows that carry an
asset key stay in your catalogs and fail until a provider is available; the validator warns
([UIDIRECTOR120](Troubleshooting.md#uidirector120)).

---

## The AddressablesViewProvider component

`AddressablesViewProvider` is a `MonoBehaviour` that implements `IViewProvider`. Put it beside the
`ViewRoot`; it needs no settings.

- **Acquire.** It loads the prefab asset with `Addressables.LoadAssetAsync` and instantiates it itself
  with `Object.Instantiate` under the inactive parent, then returns the instance still inactive. It
  instantiates only when the call is still wanted, the provider is alive and the parent still exists;
  otherwise it releases the asset and no view code runs. It keeps the asset handle in a table keyed by
  the instance id and is the only code that releases it. View code never sees a handle. The instance is
  an ordinary object, not an Addressables-tracked instance, so `Addressables.ReleaseInstance` on it
  returns `false`; the asset stays loaded, and Addressables tracks its handle, while the instance lives.
- **Release.** It destroys the instance with `Object.Destroy` (the destroy happens at the end of the
  frame, and the provider is meant for Play Mode) and releases the asset handle that the instance came from. Every release is guarded by
  `IsValid()`, because a scene unload can invalidate a handle by itself. A failure while releasing is
  logged, never thrown. Releasing by instance id means an instance that was destroyed from outside
  still releases its handle.
- **Cancel and late results.** When the token is cancelled before the load completes, the call
  ends at once with `OperationCanceledException`, and the load's single completion callback releases
  the asset that arrives later without instantiating it. The load timeout takes the same path.
- **Failed loads.** A load that fails or produces nothing releases its handle and throws
  `InvalidOperationException` with a message that names the key; the inner exception is the one
  Addressables reported. The request then ends as `LoadFailed`.
- **A destroyed parent.** If the inactive parent is destroyed before the load completes, the asset is
  released, nothing is instantiated, and the task faults with `InvalidOperationException` that names the
  key. A parent that is already destroyed when the call is made fails the same way, and nothing is loaded.
- **Preload.** It loads the prefab asset with `LoadAssetAsync` without instantiating it, and returns
  a lease. The first `Dispose` of the lease releases the asset; later calls do nothing. A lease and
  the instances made from the same key are independent reference counts, so the order in which
  they are released does not matter.
- **Destroy.** When the component is destroyed it destroys every instance and releases its asset handle, then releases every lease.
  Later calls throw `ObjectDisposedException` or do nothing, and a load still in flight releases its
  late result.
- **Usage errors** throw at the call: a null or empty key throws `ArgumentException`, a null
  `inactiveParent` throws `ArgumentNullException`, a destroyed provider throws
  `ObjectDisposedException`, and a load that Addressables cannot start throws `InvalidOperationException`
  naming the key. A token that is already cancelled returns a cancelled task instead.

Call it from the main thread only; the root does.

---

## Mark the prefab addressable

The address is the row's `Asset Key`. The validator accepts an address, an Addressables GUID or a
label as a key, and the provider passes the key to Addressables unchanged; use the address so the
row reads clearly. Mark the prefab either way:

- **By hand.** Open `Window > Asset Management > Addressables > Groups`, create the settings if
  asked, drag the prefab into a group, and set its address to the row's `Asset Key`.
- **From an editor script.** The sample ships a menu helper,
  `Tools/UIDirector/Samples/Make Addressables Usage Prefabs Addressable`. It runs only when you click
  it, never on import. The lines that matter are:

<!-- source: Samples~/AddressablesUsage/Editor/MarkSampleAddressables.cs -->
```csharp
var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
entry.SetAddress(address);
settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
```

The addresses it uses are constants of that file:

<!-- source: Samples~/AddressablesUsage/Editor/MarkSampleAddressables.cs -->
```csharp
private const string GalleryAddress = "uidirector-samples/gallery";
private const string PhotoAddress = "uidirector-samples/photo";
```

The prefab itself is an ordinary view prefab: a `ViewHost` on the root and a view component
([View authoring](View-Authoring.md#prefab-structure)). A view script that is referenced only from a
prefab inside an Addressables bundle can be removed by managed code stripping in a build; see
[Limitations](Limitations.md).

---

## Preload with a lease

A view that loads on demand makes the first open wait for the asset. `INavigator.PreloadAsync` loads
the asset ahead of time and keeps it loaded for as long as an owner lives:

<!-- signature -->
```csharp
UniTask<bool> PreloadAsync(string viewId, CancellationToken owner, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0);
```

The sample's gallery preloads the photo popup while it is visible, with the visible scope as owner,
so the lease ends when the gallery is covered by another screen or closed:

<!-- source: Samples~/AddressablesUsage/GalleryView.cs -->
```csharp
// The lease lives on the visible scope: it is released when this screen is covered or closed.
protected override void OnVisible(ViewScope visible)
{
    visible.Run(PreloadAsync);
}

private async UniTask PreloadAsync(CancellationToken ct)
{
    // With the photo asset held, the first photo below opens without a load wait.
    var loaded = await Navigator.PreloadAsync(RemoteViews.Photo.Id, ct);
    _status.text = loaded ? "Photo popup preloaded." : "Photo popup could not be preloaded, see the console.";
}
```

What it does:

- **It loads the asset, not an instance.** It never creates a view instance. A later open still
  instantiates, but it does not wait for a download or a bundle load, because the asset is held.
- **It returns `true` once the asset is loaded and held.** It returns `false` when the id is
  unknown ([UIDIRECTOR101](Troubleshooting.md#uidirector101)), the row has an asset key and the
  root has no provider ([UIDIRECTOR120](Troubleshooting.md#uidirector120)), the owner ended
  before or during the load, the root is gone, or the load failed. For a direct-prefab row it
  returns `true` at once.
- **The owner ends the lease, and only the owner.** A lease lives until its owner token ends, also after
  `PreloadAsync` has returned `true`. Neither `Trim()` nor a low-memory event releases it; only destroying the root or the provider component ends it earlier.
  When the owner token ends, the lease is disposed and the asset is released. A photo popup does not cover the gallery, so in the sample the lease stays while popups open
  and close. Ending the root releases every lease.
- **Preloads are not bounded by the load timeout.** The timeout applies to opening a view. A
  preload ends with its owner, or when the provider finishes or fails.
- **A failed preload** routes the provider's exception to the root's error handler and returns
  `false`; the id [UIDIRECTOR104](Troubleshooting.md#uidirector104) belongs to opening a view.
  In the sample, a photo address that is missing while the gallery address is right leaves the
  gallery open with the status "Photo popup could not be preloaded, see the console.", and a photo
  button then sets the status to `Photo '<title>' did not open: LoadFailed.`

Preloading is a main-thread call and throws off the main thread ([Threading](Threading.md)).
Several preloads of one id are independent leases.

---

## Keep and trim

A row's `Retention` decides what a close does to the instance of an asset-key row.

- `ReleaseOnClose` (the default) releases the instance to the provider at close. The next open loads
  and instantiates again.
- `KeepInstance` parks one idle instance per id, hidden and deactivated, and reuses it on the next
  open without injecting again. The instance and its asset handle stay alive while it is parked, so
  the asset stays loaded. There is no budget and no timer. See
  [View authoring](View-Authoring.md#kept-instances).

`INavigator.Trim()` releases every idle kept instance. It never touches a request in flight or a
preload lease: a lease lives until its owner token ends, also after `PreloadAsync` has returned. The root
calls `Trim()` on `Application.lowMemory`, so a low-memory event frees kept instances and leaves preloaded
assets loaded. The sample's gallery has a button that calls it:

<!-- source: Samples~/AddressablesUsage/GalleryView.cs -->
```csharp
// Frees kept instances that are not on screen; this screen is open, so it stays.
private void TrimKept()
{
    Navigator.Trim();
    _status.text = "Trimmed: hidden kept instances were released.";
}
```

The gallery is open, so it stays. Nothing else in the sample is kept, so you see only the status line
change; the call matters in a project with several kept screens. `Trim` called off the main thread is
posted to it.

To release preloads on low memory, end their owner from your own `Application.lowMemory` handler. Pass the
token of a `CancellationTokenSource` you hold as the owner of those `PreloadAsync` calls, cancel the source
in the handler, and create a new source for later preloads. Cancelling the source disposes its leases and
releases their assets.

---

## What is released when

| Moment | What is released |
|---|---|
| A view with a `ReleaseOnClose` asset-key row closes | Its instance, through the provider's `Release`, before `Closed` is published |
| A view with a `KeepInstance` row closes | Nothing; the instance is parked |
| `Trim()` or `Application.lowMemory` | Every idle kept instance |
| A catalog is removed | The kept instances of its rows. A view of a removed row that closes later is released, not kept |
| An instance is destroyed from outside | Its asset handle, by instance id; it is never kept |
| The owner of a preload ends, or the root is destroyed | The lease, so the asset |
| A load is cancelled (withdrawn, owner ended, root destroyed) or times out | The late result, by the provider when it arrives |
| The root is destroyed | Open instances, kept instances and leases |
| The `AddressablesViewProvider` component is destroyed | Every instance and its asset handle, then every lease |

Removing a catalog leaves the open views of its rows open; they end when their own owner ends.

---

## The load timeout and the load indicator

Both are Inspector fields on the `ViewRoot`, in unscaled seconds on the root's clock:

| Field | Default | Minimum | What it does |
|---|---|---|---|
| `Load Timeout Seconds` | 15 | 0.1 | The longest a view may take to load. Then the token given to the provider is cancelled and the request ends as `NotShown` with `RejectReason.LoadTimedOut` ([UIDIRECTOR105](Troubleshooting.md#uidirector105)). |
| `Indicator Delay Seconds` | 0.4 | 0 | How long a load must run before the load indicator is shown. |
| `Load Indicator` | none | | An optional GameObject. It is activated when a load has run longer than the indicator delay, and deactivated when the loads end. |

The defaults come from the constants `DefaultLoadTimeoutSeconds` (15) and
`DefaultLoadIndicatorDelaySeconds` (0.4). The timeout counts from the moment the layer's runner starts
the load, not from the call, so time a request spends waiting behind another one is not part of it.
The indicator shows only while a load is running on a layer that shields input, so a load on a
layer without that rule shows no indicator. The clock advances by at most 0.1 s per frame, so after a
long hitch or a suspension the timeout and the delay are later than wall-clock time.

A short timeout is a good way to see the failure path; a long one hides a stalled download. If a
load legitimately takes longer, raise the field or preload the view before it is needed.

---

## When a load fails

A load that fails ends the request as not shown with `RejectReason.LoadFailed`. The package logs
[UIDIRECTOR104](Troubleshooting.md#uidirector104) as an error, puts the exception message in
`Detail`, and routes the exception itself to the root's error handler
(`ViewErrorSource.Provider`), or to `Debug.LogException` when none is set. For the Addressables
provider the message always names the key: `Addressables failed to load '<key>'.` for a failed load,
and `The parent for '<key>' was destroyed ...` when the staging parent is gone.

Common causes: the prefab is not marked addressable or the address differs from the row's key, the
Addressables settings do not exist, or the content is not built or not reachable in the player.
A key that Addressables cannot resolve fails at once; a load that never finishes ends as
`LoadTimedOut` after the load timeout.

The code that opened the view reads the failure from the result. The sample reports the setup
step that is usually missing:

<!-- source: Samples~/AddressablesUsage/AddressablesBootstrap.cs -->
```csharp
var result = await navigator.ShowAsync(
    RemoteViews.Gallery,
    navigator.RootToken,
    new OpenOptions { Source = "boot" });

// A gallery that was shown and later closed is not a failure.
if (result.Rejected == RejectReason.None)
    return;

Debug.LogWarning(
    $"The gallery did not open ({result.Rejected}): {result.Detail}. " +
    "Mark Prefabs/Gallery and Prefabs/Photo as addressable, as the sample README describes, " +
    "and create the Addressables settings if the project has none.");
```

When the addresses are missing in the sample, the gallery's open ends as `NotShown` with `LoadFailed`,
the bootstrap logs one warning that starts with `The gallery did not open (LoadFailed): `, and the
navigator logs its own error with `UIDIRECTOR104`. Fix the addresses and press Play again.

For a popup that fails to open from a view, check `Rejected` the same way. The gallery sets the status
`Photo '<title>' did not open: <reason>.` when `Rejected` is not `None`. See
[Opening and results](Opening-and-Results.md#reject-reasons).

---

## The validator's UIDIRECTOR124 warning

`Tools/UIDirector/Validate Catalogs` checks every row that has an asset key and no prefab. With
Addressables installed it looks the key up in the project's Addressables settings. When no entry
has that address, GUID or label, it reports
[UIDIRECTOR124](Troubleshooting.md#uidirector124) as a warning. The check runs in the editor only,
never at run time, and a batch run does not fail on it. When the key matches an address or a GUID,
the validator also checks the prefab it finds like any other row; a key that matches a label is
accepted without a prefab check.

Fix it by marking the prefab addressable with that address, or by correcting the key. **Ignore it when
the key is valid for something the validator cannot see:**

- the root uses a provider of your own, whose keys are not Addressables keys;
- the key is served by content built outside this project, which its settings do not list.

With Addressables not installed, the same rows report
[UIDIRECTOR120](Troubleshooting.md#uidirector120) as a warning: the key only loads when the root has a
provider of your own.

---

## Write a provider of your own

Implement `IViewProvider` and pass it in `ViewRootSetup.AssetProvider`. The package relies on this
contract:

1. **`AcquireAsync` returns an inactive instance under the inactive parent.** The instance must be
   a child of `inactiveParent`, so `Awake` and `OnEnable` have not run when the package injects
   and prepares it. Its root needs a `ViewHost` and a view component for the key's types
   ([UIDIRECTOR103](Troubleshooting.md#uidirector103), [UIDIRECTOR102](Troubleshooting.md#uidirector102)).
   Return nothing or throw to fail the load; both end the request as `LoadFailed`, and a thrown
   message becomes the `Detail`. An instance that is already destroyed when the package prepares it
   fails the request as `LoadFailed` too.
2. **Respect the token.** It is cancelled by the load timeout, by a withdrawn request, by an owner
   that ended and by a destroyed root. Stop waiting at once, complete with
   `OperationCanceledException`, and release whatever you loaded, also when the load completes
   later. If a result does arrive after the token was cancelled, the stage hands it back to
   `Release`.
3. **`Release` releases exactly what `AcquireAsync` returned, and decides what happens to the
   instance.** Destroy it, or return it to a pool. When the package lets go of a provider's instance, it
   deactivates the instance if it is still active, calls `Release`, and does nothing else to it, so a
   pooling provider always receives it inactive.
   `Release` must not throw, and it must be safe on a Unity object that was already destroyed, because
   an instance destroyed from outside is still released. Key your bookkeeping by instance id, not by a
   reference you test with `null`. Doing nothing for a null or an unknown instance is correct. An
   exception from `Release` does not stop the package: the instance still counts as released, and the
   exception is routed to the root's error handler as `ViewErrorSource.Provider` with the view id
   (or logged with `Debug.LogException` when no handler is set).
4. **`PreloadAsync` loads without instantiating and returns a lease.** The asset stays loaded until the
   lease is disposed. `Dispose` must be safe to call twice. Throw or return `null` to fail; the
   preload then returns `false`. A lease and the instances of the same key must not depend on each
   other's release order.
5. **Complete on the main thread.** The package does not switch threads after an await
   ([Threading](Threading.md)).
6. **Own your lifetime.** The root reads the provider once and never disposes it. Release what you
   still hold when the provider goes away, as the Addressables provider does in `OnDestroy`.

Every release the package makes goes through `Release`, which is why the table in
[What is released when](#what-is-released-when) names it so often.

No sample shows a custom provider; the contract above comes from the interface and from how the
stage and the Addressables provider use it.

---

## Traps

- **An asset key and no provider.** The open fails as `LoadFailed` with
  [UIDIRECTOR120](Troubleshooting.md#uidirector120). Add the component, pass a provider, or fill in the
  row's `Prefab`.
- **A component added after `Initialize`.** The root does not see it. Add it before the root initialises.
- **An address that differs from the key by a character.** Addresses are compared exactly. The
  validator warns with [UIDIRECTOR124](Troubleshooting.md#uidirector124); the player fails with
  [UIDIRECTOR104](Troubleshooting.md#uidirector104).
- **Content that is not built.** "Use Asset Database" hides the problem in the editor; a player build
  fails to load until the Addressables content is built.
- **Releasing an instance yourself.** Never destroy a provider's instance by hand or call
  `Addressables.ReleaseInstance` on it (it returns `false` for the Addressables provider's instances);
  the provider owns the handle and the package calls `Release`.
- **Expecting a lease to keep an instance.** A lease keeps the asset loaded, not a view instance;
  keeping an instance is `KeepInstance`.
- **Forgetting that Trim spares leases and requests in flight.** It frees idle kept instances only. Low memory does not end a preload lease; only its owner token does, so end that owner from your own `Application.lowMemory` handler (see [Keep and trim](#keep-and-trim)).
- **Expecting the timeout to apply to a preload.** It does not.

---

## See also

- [Catalogs and keys](Catalogs-and-Keys.md): rows, `Asset Key`, `Retention` and the validator
- [View authoring](View-Authoring.md): kept instances and the prefab structure
- [Opening and results](Opening-and-Results.md): the result of a failed load
- [Threading](Threading.md): a custom provider completes on the main thread
- [Limitations](Limitations.md): the timeouts and what the package does not do
- [Troubleshooting](Troubleshooting.md#uidirector104): UIDIRECTOR104, UIDIRECTOR105, UIDIRECTOR120 and UIDIRECTOR124
- [Architecture](Design/Architecture.md#14-the-asset-provider-seam): the provider seam in detail
- [API reference](API-Reference.md): `IViewProvider`, `ViewRootSetup`, `AddressablesViewProvider`
