# Addressables Usage

Core plus the Addressables integration: the views are not prefab references in the catalog, they are loaded by
address. Needs the Addressables package (`com.unity.addressables`; the sample's assembly compiles only when it is
installed), the uGUI package (`com.unity.ugui`, which includes TextMeshPro) and UniTask.

## Setup

1. Import TextMeshPro's Essential Resources once: `Window > TextMeshPro > Import TMP Essential Resources`.
   The texts stay blank until then.
2. Make the two prefabs addressable, with exactly these addresses: `Prefabs/Gallery` as `uidirector-samples/gallery`
   and `Prefabs/Photo` as `uidirector-samples/photo`. Either way works:
   - Menu: `Tools/UIDirector/Samples/Make Addressables Usage Prefabs Addressable`. It creates the Addressables
     settings if the project has none, and only when you click it; nothing runs on import. It puts both prefabs in
     the default group and sets the two addresses.
   - By hand: open `Window > Asset Management > Addressables > Groups`, create the settings if asked, drag
     `Prefabs/Gallery` and `Prefabs/Photo` into a group, and set the two addresses.
3. Open the scene `AddressablesUsage.unity` from the folder the Package Manager imported the sample into:
   `Assets/Samples/UIDirector/<version>/Addressables Usage/`. The scene has no Event System on purpose: the right
   input module depends on your project's input handling, so the bootstrap creates one when Play starts.
4. Press Play. Optionally open `Window > UIDirector` to watch the stacks, the shield and the queues live (it shows
   something in Play Mode only).

In the editor the default play mode script, "Use Asset Database", loads the prefabs directly, so no build step is
needed. A player build needs the Addressables content built first.

The view ids in this sample (`gallery`, `photo`) differ from the ids in the other samples, so importing several
samples into one project never makes `Tools/UIDirector/Validate Catalogs` report a duplicate.

## What to do

The catalog rows carry an address instead of a prefab. `gallery` is a screen that keeps its instance, with the
address `uidirector-samples/gallery`; `photo` is a popup that closes on a tap on the backdrop, with the address
`uidirector-samples/photo`. The scene's root has "Initialize On Awake" off and an `AddressablesViewProvider`
component on its own GameObject; the root's asset provider field stays empty, so that component loads and releases
every view.

- **Play** opens the gallery on its own (`source=boot`). The status line first shows nothing, then reads "Photo
  popup preloaded." once the preload below finished. The gallery has three photo buttons, "Trim kept views" and the
  status line.
- **What happens when the addresses are missing**: the load of the gallery fails and the navigator turns the
  request away with `NotShown` and the reason `LoadFailed` (after the 15 second load timeout, `LoadTimedOut`). The
  bootstrap then logs one warning that starts with `The gallery did not open (LoadFailed): ` and the reason's
  detail, followed by "Mark Prefabs/Gallery and Prefabs/Photo as addressable, as the sample README describes, and
  create the Addressables settings if the project has none." The navigator also logs its own error with the
  diagnostic ID `UIDIRECTOR104`. Fix the addresses, then press Play again.
- **Sunrise**, **Harbor**, **Glacier** each open the photo popup with that word as its title and a "Close"
  button (`source=gallery.photo`). While the gallery is visible the photo asset is already loaded, so the first
  photo opens without a load wait. "Close", or a tap on the backdrop, closes the popup and the gallery is
  usable again. If the popup cannot open, the status line reads `Photo '<title>' did not open: <reason>.`
- **Trim kept views** calls `Trim()` and sets the status "Trimmed: hidden kept instances were released." Trim
  frees kept instances that are not on screen. The gallery is open, so it stays. Nothing else in this sample is
  kept (the photo popup is destroyed when it closes), so you see only the status line change; the call matters in
  a project with several kept screens.
- **A photo address that is missing while the gallery address is right**: the gallery opens, the status reads
  "Photo popup could not be preloaded, see the console.", and a photo button sets the status to
  `Photo '<title>' did not open: LoadFailed.`

## What to look at

- The preload: [GalleryView](GalleryView.cs) asks `PreloadAsync` for the photo view from `OnVisible`, on the
  visible scope. The asset stays loaded for as long as the gallery is on screen, and the lease ends when the gallery
  is covered by another screen or closed. A photo popup does not cover the gallery, so the lease stays while the
  popups open and close.
- Trim: the same file, the "Trim kept views" handler.
- Catalog rows with an address and no prefab: select `AddressablesUsageCatalog.asset` and read the `gallery` and
  `photo` rows.
- The Hierarchy: the root made with `GameObject > UI > UIDirector Root` has "Initialize On Awake" off and carries
  the `AddressablesViewProvider`; the `Bootstrap` object holds `AddressablesBootstrap`. The prefabs are in
  `Prefabs/`: Gallery and Photo.
- `Window > UIDirector`: the gallery in `screens` and a photo popup in `popups` while it is open.
- [AddressablesBootstrap](AddressablesBootstrap.cs) - the root initialised in `Awake` with an empty setup, the
  gallery opened with the root as the owner, and the warning that names the setup step when the result carries a
  reject reason.
- [GalleryView](GalleryView.cs) - the preload on the visible scope, photo popups owned by the open scope, the
  `Rejected` check on the result and `Trim`.
- [PhotoView](PhotoView.cs) - a popup with typed arguments that closes on a button.
- [RemoteViews](RemoteViews.cs) - the two keys and the argument struct.
- [MarkSampleAddressables](Editor/MarkSampleAddressables.cs) - the menu helper from step 2, editor only.
- [SampleEventSystem](Support/SampleEventSystem.cs) - the event system creation.
