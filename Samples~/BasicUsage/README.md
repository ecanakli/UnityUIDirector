# Basic Usage

Core only: no DOTween, no Zenject, no queue. Needs the uGUI package (`com.unity.ugui`, which includes TextMeshPro)
and UniTask.

## Setup

1. Import TextMeshPro's Essential Resources once: `Window > TextMeshPro > Import TMP Essential Resources`.
   The texts stay blank until then.
2. Open the scene `BasicUsage.unity` from the folder the Package Manager imported the sample into:
   `Assets/Samples/UIDirector/<version>/Basic Usage/`. The scene has no Event System on purpose: the right input
   module depends on your project's input handling, so the bootstrap creates one when Play starts.
3. Press Play. Optionally open `Window > UIDirector` to watch the stacks and the input shield live (it shows
   something in Play Mode only). Click the Game view once so it has keyboard focus: device back is the Escape key
   in the editor.

The view ids in this sample (`home`, `library`, `item-detail`, `settings`, `confirm`, `rename`, `saving`, `toast`)
differ from the ids in the Queue Usage sample, so importing both samples into one project never makes
`Tools/UIDirector/Validate Catalogs` report a duplicate.

## What to do

Every request writes `[views]` lines to the Console (see "What to look at"). The lines named below are the ones
that matter for each action.

- **Play** opens Home on its own: `[views] Requested #1 view=home layer=screens source=boot`, then `Shown`, then
  `Settled`. Home has two buttons, "Library" and "Settings". The bootstrap owns Home with the root's token, so it
  ends only when the root does.
- **Library** (on Home) opens the library screen. The label reads "3 items", written when the screen becomes
  visible. Home is still in the stack under it. Source tag: `home.library`.
- **Settings** (on Home) opens the settings screen the same way. Source tag: `home.settings`.
- **Getting started** and **Release notes** (on Library) each open the item detail screen with that item's title
  (`library.item`). Detail has three buttons: "Rename", "Back to the library" and "Home".
- **Archive** (on Library) opens the same detail screen with the title "Archive". Archive cannot be renamed, see
  **Rename** below.
- **Back** (on Library) calls `Back()` from code. Library closes with `reason=Back` and Home is revealed again,
  without its open code running a second time.
- **Rename** (on an item detail) opens the rename popup, owned by that detail screen's open, with the current
  title in a text field and the buttons "Cancel" and "Save". For Getting started and Release notes the popup
  appears. For Archive the popup declines inside its open code: nothing is shown, the screen stays as it is, and
  the Console logs `Requested` and then
  `[views] NotShown #n view=rename layer=popups source=item.rename rejected=Declined detail=This item cannot be renamed.`
- **Rename > type a new name > Save** closes the popup (`reason=Completed`). A moment later, once the popup is
  gone, the detail screen's title changes: the screen subscribed to the library's `Renamed` event on its open
  scope, and the event fired when the popup's answer reached the screen. The buttons on the Library screen keep
  their fixed labels; open the item again and the detail screen shows the new title, because the item list kept it.
- **Rename > Save with an empty field** keeps the popup open and shows "Enter a name first." Nothing is renamed.
- **Rename > Cancel** closes the popup without a value (`reason=Dismissed`). The title does not change.
- **Rename > type something, then Escape**: the popup asks its own back hook first, so it does not close. A hint,
  "Press back again to discard the change.", appears. A second Escape closes the popup with `reason=Back` and the
  title does not change. Escape without having typed anything closes the popup at once. Typing and then restoring
  the original text still counts as an edit.
- **Back to the library** (on an item detail) calls `CloseTo` for the library: the detail screen closes with
  `reason=Code` and the library is revealed.
- **Home** (on an item detail) opens `home` while Home is already in the stack. That is a return, not a new
  screen: no `Requested` line for Home, Home's open code does not run again, and everything above it closes with
  `reason=Navigated` (the detail screen with its hide transition, the library at once). Only Home is left.
- **Reset settings** (on Settings) opens the confirm popup "Reset settings" / "All settings return to their
  defaults." with "No" and "Yes". "No" closes it with a false answer and nothing else happens. "Yes" closes it,
  then the Console shows `[settings] reset to defaults`, the Settings label reads "Settings were reset." and a
  toast with the same text slides in at the top, on its own layer, and leaves by itself after 2 seconds
  (`Closed ... view=toast layer=toasts ... reason=Dismissed`). A toast never blocks a tap: the layer has no input
  shield and no back.
- **Reset settings, double click**: the first click opens the confirm popup at once and raises the input shield
  in the same call, so the second click lands on the shield. One popup opens and no extra line is logged.
- **Reset settings while a toast is sliding in or out** is the case the "Busy, try again." text is for. The call
  asks not to wait, the navigator is busy with the toast's transition, and the request is refused at once:
  `[views] NotShown #n view=confirm layer=popups source=settings.reset rejected=Busy detail=The navigator is busy and the call asked not to wait.`
  No popup opens and the Settings label reads "Busy, try again." The window for this is short (the toast's slide
  in right after "Yes", and its slide out 2 seconds later). While the toast sits still, Reset opens the popup as
  usual.
- **Save** (on Settings) opens the "Saving..." overlay on the system layer and keeps it for 1.5 seconds
  (unscaled time) while the fake save runs. The overlay blocks input and swallows back for that time: Escape does
  nothing, it neither closes anything nor opens the quit confirm. After 1.5 seconds the overlay closes
  (`Closed ... view=saving layer=system ... reason=Code`) and only once it is gone the Settings label reads
  "Saved." Source tag: `settings.save`.
- **Back** (on Settings, a Back button with no code behind it) calls `Back()`: Settings closes with
  `reason=Back` and Home is revealed. The label is cleared the next time Settings opens.
- **Escape on a screen above Home** (Library, an item detail, Settings) does the same as the Back button of that
  screen. With a popup open, Escape goes to the popup first (the confirm popup closes with `reason=Back` and
  counts as "No"). Escape while a view is still opening is swallowed.
- **Escape on Home**: nothing in the stack handles it, so `Back()` returns false and the bootstrap's handler
  opens the "Quit" / "Close the application?" confirm (`source=back.root`). "Yes" calls `Application.Quit()` and,
  in the editor, leaves Play Mode. "No" or Escape closes it and keeps the app running.

## What to look at

- The Console. Each line is `[views] <Kind> #<request> view=<id> layer=<layer> source=<tag>`. `Shown` adds
  `waited=`, `Closed` adds `reason=` and `open=`, `NotShown` adds `rejected=` and `detail=`, and `Settled` (the
  navigator went from busy to idle) has no request and no view. The `source` tags (`home.library`, `item.rename`,
  `settings.reset`, `back.root` and so on) say which line of code asked. The log is [ViewEventLog](ViewEventLog.cs),
  added in [AppBootstrap](AppBootstrap.cs) with the root's token as its owner.
- `Window > UIDirector`: one section per layer (`screens`, `popups`, `system`, `toasts`) with its requests and
  phases, `Shield holders (n)` (open it during a transition or while the popup opens), and the newest events.
  Home, Library and an item detail stack up in `screens`; the confirm and rename popups appear in `popups`; the
  saving overlay in `system`; the toast in `toasts`.
- The Hierarchy: the root made with `GameObject > UI > UIDirector Root` has "Initialize On Awake" off and carries
  `BackInput`; the `Bootstrap` object holds `AppBootstrap`. The prefabs are in `Prefabs/` (Home, Library,
  ItemDetail, Settings, Confirm, Rename, Saving, Toast) and `BasicUsageCatalog.asset` lists them with their layers
  and policies.
- [AppViews](AppViews.cs) - every key of the sample: the id, the argument type and the result type in one place.
- [AppBootstrap](AppBootstrap.cs) - the setup in `Awake` (event system, services, `Initialize` with the inject
  seam, the error handler and the extra layer), Home opened in `Start`, and the quit confirm behind the unhandled
  back press.
- [AppLayers](AppLayers.cs) - the `toasts` layer: above the system layer, no history, no back, no input shield.
- [HomeView](HomeView.cs), [LibraryView](LibraryView.cs), [ItemDetailView](ItemDetailView.cs) - screens that open
  other screens with the root as the owner, return to a screen already in the stack, and subscribe to a C# event on
  the open scope.
- [ConfirmView](ConfirmView.cs) - a popup with typed arguments and a bool result, written on the base class.
- [RenameView](RenameView.cs) - the same kind of popup written on the interfaces only (`IView` and `IViewBack`),
  with `Decline` inside the open code and a back hook.
- [SettingsView](SettingsView.cs) - a popup awaited with `ShowAsync` (with `RejectWhenBusy` and the `Rejected` check), a toast opened with the root as the owner, and a system overlay closed in a `finally`.
- [SavingOverlay](SavingOverlay.cs) and [ToastView](ToastView.cs) - an overlay with work on the visible scope, and
  a view that closes itself after a delay.
- [PopTransition](PopTransition.cs) - a custom transition on unscaled time, the example for writing your own.
- [HardwareBackRouter](HardwareBackRouter.cs) with [LegacyPopup](Support/LegacyPopup.cs) - the recipe for a
  project that still has an older UI above the package: ask it first, then call `Back()`. It replaces `BackInput`
  and is not wired into the scene.
- [ViewArgs](ViewArgs.cs), [AppServices](AppServices.cs), [SettingsStore](Support/SettingsStore.cs),
  [ItemLibrary](Support/ItemLibrary.cs), [SampleEventSystem](Support/SampleEventSystem.cs) - the stand-ins for
  your own code: argument structs, the services views receive, a fake settings store and item list, and the
  event system creation.
