# View authoring

[Back to index](index.md)

This page covers the component on a view prefab: the two ways to write it, every hook the package calls and when, refusing an open, completing with a result, tying listeners and tasks to a scope so nothing has to be removed on close, kept instances, and what the prefab must look like. How a caller opens a view and reads its result is [Opening and results](Opening-and-Results.md).

---

## Write a view on the base class

Derive from `ViewBehaviour`, implement `OnOpen`, and bind to the scope you are given:

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

The component sits on the prefab root, on the same object as the `ViewHost`. Pick the base class that matches the key the view is opened with:

| Base class | Key | `OnOpen` receives |
|---|---|---|
| `ViewBehaviour` | `ViewKey<NoArgs, NoResult>` | `NoArgs args, ViewScope open` |
| `ViewBehaviour<TArgs>` | `ViewKey<TArgs, NoResult>` | `TArgs args, ViewScope open` |
| `ViewBehaviour<TArgs, TResult>` | `ViewKey<TArgs, TResult>` | `TArgs args, ViewScope open` |

The plain `ViewBehaviour` has no arguments and no result, so `OnOpen` takes `NoArgs` and ignores it (`HomeView` in the Basic Usage sample):

<!-- source: Samples~/BasicUsage/HomeView.cs -->
```csharp
protected override void OnOpen(NoArgs args, ViewScope open)
{
    open.Bind(_library.onClick, OpenLibrary);
    open.Bind(_settings.onClick, OpenSettings);
}
```

The key's argument and result types must equal the base class type arguments. A mismatch ends the open as not shown with [UIDIRECTOR102](Troubleshooting.md#uidirector102). Keys are covered in [Catalogs and keys](Catalogs-and-Keys.md).

What the base class gives you:

| Member | Meaning |
|---|---|
| `OnOpen(TArgs args, ViewScope open)` | Required. Runs once per open. |
| `OnVisible(ViewScope visible)` | Optional override. Runs each time the view comes on screen. |
| `OnBack()` | Optional override. Asked when Back targets the view and its row says `HandledByView`. Returns `false` by default. |
| `Complete(TResult value)` | Closes the view with a value. |
| `Close()` | Closes the view without a value. |
| `Decline(string reason = null)` | Inside `OnOpen` only: refuses the open. |
| `Navigator` | The `INavigator` of the root that opened the view. `null` before the first open. |
| `OpenScope` | The scope of the current open. `null` before the first open. |

## Write a view without the base class

A view that already derives from another class implements the interfaces. `IView<TArgs, TResult>` is required, `IViewVisible` and `IViewBack` are optional:

<!-- signature -->
```csharp
public interface IView<TArgs, TResult>
{
    void OnOpen(TArgs args, ViewContext<TResult> context);
}

public interface IViewVisible
{
    void OnVisible(ViewScope visible);
}

public interface IViewBack
{
    bool OnBack();
}
```

`ViewContext<TResult>` is what one open hands to the view: `Navigator`, `Handle`, `Open` (the open scope), `Complete(value)`, `Close()` and `Decline(reason)`. Keep the reference if the view completes later:

<!-- source: Samples~/BasicUsage/RenameView.cs -->
```csharp
public sealed class RenameView : MonoBehaviour, IView<RenameArgs, string>, IViewBack, IServiceReceiver
{
    [SerializeField] private TMP_InputField _input;
    [SerializeField] private Button _save;
    [SerializeField] private Button _cancel;
    [SerializeField] private TMP_Text _hint;

    private ViewContext<string> _context;
    private IItemLibrary _library;
    private bool _edited;
    private bool _discardHintShown;
```

<!-- source: Samples~/BasicUsage/RenameView.cs -->
```csharp
public void OnOpen(RenameArgs args, ViewContext<string> context)
{
    _context = context;

    // Decline is the right call inside OnOpen: nothing is shown and the caller sees Declined.
    if (!_library.CanRename(args.ItemId))
    {
        context.Decline("This item cannot be renamed.");
        return;
    }

    // A reused instance must not carry state from the previous open.
    _edited = false;
    _discardHintShown = false;
    _hint.text = string.Empty;
    _input.SetTextWithoutNotify(args.CurrentTitle);

    context.Open.Bind(_input.onValueChanged, MarkEdited);
    context.Open.Bind(_save.onClick, Save);
    context.Open.Bind(_cancel.onClick, Cancel);
}
```

The package finds the hooks by interface on the prefab root. `IViewVisible` and `IViewBack` must be implemented by the same component as `IView`, not by a second component. A view on a child object is not found ([UIDIRECTOR102](Troubleshooting.md#uidirector102)).

`RenameView` also implements `IServiceReceiver`, a stand-in of the sample for however your project hands services to a view. It is not part of the package. Injection through the root is covered in [Zenject](Zenject.md).

---

## Lifecycle: what runs when

For a view whose row has the default retention (`ReleaseOnClose`), one open runs in this order:

1. **Created inactive.** The instance is created under the root's inactive staging object. The inject delegate of `ViewRootSetup` runs on it while it is inactive, so injection happens before `Awake`.
2. **Placed.** The package deactivates the instance, parents it to its layer and activates it. It does not switch any canvas or raycaster: `Awake` and `OnEnable` run now, without arguments, on an object whose canvases and raycasters are as you authored them, and a canvas or raycaster that they or `OnOpen` turn on or off stays that way. The transition's hidden state and the own canvas's sorting are applied after `OnOpen`. Do not read arguments or `Navigator` here.
3. **`OnOpen(args, context)`.** Once per open, before the first visible frame. This is where the view reads its arguments and binds.
4. **`OnVisible(visible)`.** The first visual state is applied and the visible scope begins. The show transition plays right after, so the hook runs at the start of the show, not at its end.
5. **Open.** The view is interactive when nothing covers it.
6. **Covered and revealed.** When another view covers it, its visible scope ends. When it is revealed, a new visible scope begins and `OnVisible` runs again. `OnOpen` does not.
7. **`OnBack()`.** Only when Back targets this view, the view is `Open`, and its catalog row sets `BackPolicy.HandledByView`.
8. **Closed.** The first close cause is latched. The views this one covered are revealed and get `OnVisible` at once, then the hide transition plays. At the end, in this order: the visible scope ends, the open scope ends, the instance is released or kept, the event is published and the awaiters complete. When a request ends without a hide (owner ended, closed by navigation, instance lost), the revealed views get `OnVisible` after the release and before the event.

| Hook | Interface | Runs | Arguments |
|---|---|---|---|
| `OnOpen` | `IView<TArgs, TResult>` | Once per open | The arguments of this open and the context |
| `OnVisible` | `IViewVisible` | Each time the view comes on screen and is not covered | The visible scope of that span |
| `OnBack` | `IViewBack` | When Back targets the view and the row says `HandledByView` | None. Returns `true` when handled |

There is no `OnClose`, `OnHide` or `OnCovered`, by design. Anything that has to end is registered on a scope and the package ends it; see [ADR-003](Design/Decisions/ADR-003-Begin-Only-Hooks-And-Scopes.md).

The Unity messages behave as follows. `Awake` runs once per instance. `OnEnable` and `OnDisable` run whenever the package activates or deactivates the instance: when it places it, when it parks it for reuse, and when a covered view uses `CoveredMode.Deactivate`. `OnDestroy` runs when the instance is released. Do not put cleanup of listeners in `OnDisable`; use the scope.

`OnVisible` is where per-span work starts, such as a spinner. It runs again on every reveal, which is why a screen can refresh itself there:

<!-- source: Samples~/BasicUsage/LibraryView.cs -->
```csharp
// Runs on the first show and again each time this screen is revealed.
protected override void OnVisible(ViewScope visible) => _count.text = $"{_library.Count} items";
```

**Errors in hooks.** An exception in `OnOpen` ends the request as not shown with `RejectReason.Faulted` and reports [UIDIRECTOR117](Troubleshooting.md#uidirector117). An exception in `OnVisible` or `OnBack` goes to the root's error handler and the view stays open; a throwing `OnBack` counts as not handled.

**Never deactivate or destroy a view yourself.** Calling `Destroy` or `SetActive(false)` on a view, on one of its parents, or on its layer ends the request with `CloseReason.Destroyed` one frame later and reports [UIDIRECTOR107](Troubleshooting.md#uidirector107). This holds for a `KeepInstance` row too: the instance is released, not kept, and the next open creates a new one. Close a view through `Complete`, `Close`, `ViewHost.Close()`, the handle or Back.

---

## Refuse an open with Decline

When a view finds in `OnOpen` that it should not be shown, call `Decline`. Nothing is shown, no transition plays, the caller receives a result with `RejectReason.Declined` and your reason text, and the scope ends so anything bound before the call is removed:

<!-- source: Samples~/BasicUsage/RenameView.cs -->
```csharp
// Decline is the right call inside OnOpen: nothing is shown and the caller sees Declined.
if (!_library.CanRename(args.ItemId))
{
    context.Decline("This item cannot be renamed.");
    return;
}
```

`Decline` only latches the refusal. It does not stop the method, so write the `return` yourself. Outside `OnOpen` the call is ignored. A decline needs no diagnostic; it is the expected way to say no.

Do not use `Complete` or `Close` for this. Inside `OnOpen` they are latched, the view opens and then closes at once, and the package reports [UIDIRECTOR118](Troubleshooting.md#uidirector118).

---

## Complete with a result

`Complete(value)` closes the view and hands the value to whoever awaits it:

<!-- source: Samples~/BasicUsage/ConfirmView.cs -->
```csharp
private void Accept()
{
    Complete(true);
}

private void Refuse()
{
    Complete(false);
}
```

The caller reads the outcome from the `ViewResult`. `ItemDetailView` opens the rename popup with its open scope's token as the owner, and applies the title only when the popup completed with one:

<!-- source: Samples~/BasicUsage/ItemDetailView.cs -->
```csharp
private void OnRenameClicked() => OpenScope.Run(RenameAsync);

private async UniTask RenameAsync(CancellationToken ct)
{
    // The owner is this open's token: the popup cannot outlive this screen.
    var result = await Navigator.ShowAsync(
        AppViews.Rename,
        new RenameArgs(_itemId, _library.GetTitle(_itemId)),
        ct,
        new OpenOptions { Source = "item.rename" });

    // No value: the player went back, or the item cannot be renamed.
    if (!result.TryGetValue(out var title)) return;

    _library.Rename(_itemId, title);
}
```

| Call | `CloseReason` | `HasValue` |
|---|---|---|
| `Complete(value)` | `Completed` | `true` |
| `Close()` | `Dismissed` | `false` |

- **The first cause wins.** Complete, Close, Back, a backdrop tap, a handle close or the owner ending: the first one is latched and later ones are ignored. After `Complete` returns the view is still on screen, playing its hide transition, so do not touch it afterwards.
- **A close during the open is kept.** A `Complete` or `Close` that arrives while the view is still opening runs after the show finishes.
- **A value is a message, not a delivery.** It is held in memory and is lost if the process dies. Do not perform a one-time side effect, such as a purchase, from the awaiting code alone. See [Opening and results](Opening-and-Results.md).

---

## Tie listeners and tasks to a scope

A scope owns bindings and tasks for one span of a view's life. You register the cleanup in the same call that starts the work, so there is no removal line to write and none to forget. The package ends the scope exactly once on every path: closed, withdrawn before it was shown, declined, owner ended, instance lost, root destroyed.

| Scope | Created | Ends | Reached through |
|---|---|---|---|
| Open scope | When the request is accepted | When the request ends | `ViewContext.Open`, the `open` parameter, `OpenScope` |
| Visible scope | Each time the view comes on screen and is not covered | When the view is covered, or when the request ends | The `visible` parameter of `OnVisible` |

Put long-lived listening on the open scope and per-span work on the visible scope. A view that a screen covers keeps its open scope and loses its visible scope. To decide where a piece of work goes: work that should stop while another view covers this one, such as a refresh timer, belongs to the visible scope and is started in `OnVisible`, so it starts again on every reveal; work that runs for as long as the view is open, such as a listener for a model change, belongs to the open scope. By default a popup over a screen does not cover it, so the screen's visible scope keeps running under a popup (a row's `Cover` policy can change that).

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

**Buttons and other `UnityEvent`s.** `Bind` adds the listener now and removes it when the scope ends. `ConfirmView` above binds both buttons this way.

**Events that are not `UnityEvent`.** `Subscribe` takes an add delegate, a remove delegate and the handler. The package calls add now and remove at the end:

<!-- source: Samples~/BasicUsage/ItemDetailView.cs -->
```csharp
// The package removes the C# event handler when this open ends.
open.Subscribe<string>(handler => _library.Renamed += handler, handler => _library.Renamed -= handler, OnRenamed);

// A reused instance must not show the previous item for a frame.
open.OnEnd(() => _title.text = string.Empty);
```

For a plain `Action` event use the non-generic form, which takes the same three arguments without a type argument: `open.Subscribe(handler => _session.SignedOut += handler, handler => _session.SignedOut -= handler, OnSignedOut)`, where `OnSignedOut` is a `void` method with no parameters.

`OnEnd` runs an action at the end, here to clear the title of a reusable instance. `Add(IDisposable)` disposes an object at the end.

**Tasks.** `Run` starts an async method with the scope's token. When the scope ends the token is cancelled. Any `OperationCanceledException` the work throws is swallowed, whichever token it came from; any other exception goes to the root's error handler as a scope task error. The toast closes itself after two seconds, and the task disappears if the toast is closed or replaced first:

<!-- source: Samples~/BasicUsage/ToastView.cs -->
```csharp
protected override void OnOpen(ToastArgs args, ViewScope open)
{
    _message.text = args.Message;

    // The scope cancels this task if the toast is closed or replaced first.
    open.Run(CloseAfterDelay);
}

private async UniTask CloseAfterDelay(CancellationToken ct)
{
    // Unscaled time, so a paused game (timeScale 0) still dismisses the toast.
    await UniTask.Delay(ShowTime, DelayType.UnscaledDeltaTime, cancellationToken: ct);
    Close();
}
```

Work that belongs to one span goes on the visible scope. The spinner of the saving overlay stops whenever the overlay is hidden or closed:

<!-- source: Samples~/BasicUsage/SavingOverlay.cs -->
```csharp
protected override void OnVisible(ViewScope visible)
{
    // Per-span work goes on the visible scope, so the spinner stops whenever the overlay is hidden or closed.
    visible.Run(SpinAsync);
}
```

<!-- source: Samples~/BasicUsage/SavingOverlay.cs -->
```csharp
private async UniTask SpinAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        // Unscaled time keeps the spinner turning while the game is paused.
        _spinner.Rotate(0f, 0f, -DegreesPerSecond * Time.unscaledDeltaTime);

        // A cancelled yield throws, and the scope swallows it as the normal end of the loop.
        await UniTask.Yield(ct);
    }
}
```

**The scope's token is an owner.** The rename call in `ItemDetailView` passes the token it received from `OpenScope.Run` as the owner of the popup, so the popup cannot outlive the screen that opened it. A request that should outlive the view uses `Navigator.RootToken` instead.

**Rules of a scope.**

- `Token` is created on first read, cancelled when the scope ends and never disposed, so a late holder stays valid. A token read after the end is already cancelled.
- The end routine cancels the token first, then runs the entries newest first. Each entry is isolated: a throw is reported and the rest still run.
- Registering on an ended scope cleans up at once: the listener is not added, the disposable is disposed, the action runs, the task does not start. A scope reference kept after the view closed is inert.
- Binding the same event and handler twice on one scope adds one listener.
- An `OnEnd` action runs inside the end routine. Calling the navigator from it is legal.
- Do not call `AddListener` yourself on an instance that is kept for reuse. The listener would be added again on every open.

---

## Kept instances

A catalog row decides what happens to the instance when its view closes (`ViewRetention`):

| Retention | On close | Next open of the same id |
|---|---|---|
| `ReleaseOnClose` (default) | The instance is released | A new instance is created: injection, `Awake`, `OnEnable` and `OnOpen` all run |
| `KeepInstance` | The instance is parked, deactivated, under the staging object | The same instance is placed again |

What runs again on a reopen of a kept instance: `OnEnable` (it was deactivated while parked), `OnOpen` with the new arguments and a fresh open scope, and `OnVisible` with a fresh visible scope. What does not run again: `Awake` and the root's inject delegate. The instance is not created again either, so its fields keep their values.

That is why `OnOpen` must reset the state a previous open left behind:

<!-- source: Samples~/BasicUsage/RenameView.cs -->
```csharp
// A reused instance must not carry state from the previous open.
_edited = false;
_discardHintShown = false;
_hint.text = string.Empty;
_input.SetTextWithoutNotify(args.CurrentTitle);
```

Bindings made on the open scope were removed when the previous open ended, so they are added once per open and a click fires once.

When the package parks an instance it gives back only the canvases and raycasters it switched off itself, so a canvas or raycaster your prefab or your code keeps disabled stays disabled.

Limits of retention:

- One idle instance per id and no budget. A second concurrent instance of a row with `AllowMultiple` is released when it closes if the slot is taken.
- `INavigator.Trim()` releases every idle kept instance, and the root calls it on `Application.lowMemory`. A request in flight is never touched.
- An idle kept instance that is destroyed from outside is dropped from the cache.
- Removing a catalog releases the kept instances of its rows.

On a history layer, opening a screen that is already in the stack returns to it instead of creating it again. A key without arguments does not run `OnOpen` again. A key with arguments runs `OnOpen` again with the new arguments on a fresh open scope, without a transition. See [Navigation and back](Navigation-and-Back.md).

---

## Prefab structure

The steps from an empty prefab to an openable view, including the catalog row and the root's `Catalogs` list, are in [Make a view and its row](Catalogs-and-Keys.md#make-a-view-and-its-row). A view prefab follows these rules. `Tools > UIDirector > Validate Catalogs` checks rules 1, 2 and 8; rule 2 only for keys declared in a class marked `[ViewKeys]`.

1. **`ViewHost` on the root.** It requires a `Canvas` and a `GraphicRaycaster`, which Unity adds with it. Without it the open ends with [UIDIRECTOR103](Troubleshooting.md#uidirector103).
2. **The view component on the same object.** One component implements `IView<TArgs, TResult>` for the key and the optional hooks. Otherwise: [UIDIRECTOR102](Troubleshooting.md#uidirector102).
3. **Do not set the root canvas's sorting.** The package sets `overrideSorting`, the sorting order and the sorting layer of the view's own canvas when it places the view. A view owns the four orders above its own, so a nested canvas that overrides sorting uses `ViewHost.SortingOrder` plus 1 to 4.
4. **No `CanvasScaler` on the view root.** The project's root canvas scales; the view is a nested canvas. In Prefab Mode a nested canvas has no scaler, so `Tools > UIDirector > Create Prefab UI Environment` writes a scene with a canvas and scaler to edit prefabs in.
5. **The canvases under a covered view are disabled.** While a view is covered, the package disables every enabled `Canvas` under it, including its own, nested ones and those of inactive children, and turns on again on reveal only the ones it disabled. A canvas that was already disabled is left alone, and one your code turns on while the view is covered stays on. The list is collected when the view is covered, so canvases created at run time are included. Disabling a canvas stops drawing and input but not scripts: `Update`, animators and particle systems keep running. A row with `CoveredMode.Deactivate` deactivates the view object instead, which stops them at the cost of a rebuild when the view returns.
6. **The raycasters of a non-interactive view are disabled.** A view that is not `Open`, is covered, is closing, or sits under a modal layer has every enabled `GraphicRaycaster` under it disabled, and the package turns on again only those when the view becomes interactive. A raycaster your code disables stays disabled. A `CanvasGroup` is not used to block input, so do not depend on its `interactable` or `blocksRaycasts` for this. If the selected object of the event system is inside the view when it loses input, the selection is cleared. Keyboard and gamepad navigation can still move the selection into such a view; see [Keyboard, gamepad and remote navigation](#keyboard-gamepad-and-remote-navigation).
7. **Content is on the UI layer when the root draws through a camera.** A root created with `GameObject > UI > UIDirector Root (Camera)` has a UI camera whose culling mask is the UI layer only. A view whose objects are on another layer is culled and does not appear. Put every object of the prefab on the UI layer, and put objects that you create or instantiate inside the view at run time on it too. A root canvas in `Screen Space - Overlay` mode has no camera to cull. See [Root canvas](Root-Canvas.md).
8. **No dropdowns that you cannot close.** An open dropdown draws above the package's sorting band and is not blocked by the input shield. The validator warns with [UIDIRECTOR123](Troubleshooting.md#uidirector123).

### Keyboard, gamepad and remote navigation

A non-interactive view (covered, opening, closing, or under a modal layer) takes no pointer input, but uGUI navigation does not use the raycaster. Navigation starts from the selected object and moves to a neighbouring `Selectable`. The package clears the selection when a view loses input, so a move starts from the view that has focus and can reach a `Selectable` of a view below it: with automatic navigation, uGUI searches every `Selectable` in the scene. The package does not set `CanvasGroup.interactable` or change `Selectable.navigation` for you, because both change state the project owns (a `CanvasGroup` change re-tints every `Selectable` under it).

On views used with a keyboard, a gamepad or a TV remote:

- Set the `Navigation` of the `Selectable`s in the view to `Explicit`, so a move never leaves the view.
- For a view that must be unreachable while another view covers it, set `When Covered` on its own row to `Deactivate`. A deactivated view unregisters its `Selectable`s. This does not apply to a screen under a modal popup: that screen is not covered, only non-interactive, so only `Explicit` navigation in the popup keeps a move inside it. See [Catalogs and keys](Catalogs-and-Keys.md#a-catalog-row).

---

## Traps

- **Reading arguments in `Awake` or `OnEnable`.** They run before `OnOpen`. Read arguments and `Navigator` in `OnOpen`.
- **A hook on a second component.** `IViewVisible` and `IViewBack` are found on the component that implements `IView`.
- **State that survives a kept instance.** Reset it in `OnOpen`, or register the reset with `OnEnd`.
- **`Complete` or `Close` inside `OnOpen`.** The view opens and closes. Use `Decline`.
- **A hook for `OnBack` without a row policy.** `OnBack` is asked only for a row with `BackPolicy.HandledByView`. With another policy it is never called. When it returns `false`, the layer's rule applies as it does for `LayerDefault`.
- **Expecting a view to be switched off at placement.** Awake, OnEnable and OnOpen see your canvases and raycasters as authored. Do not rely on them being disabled there.
- **Hand-written cleanup.** `AddListener` plus a matching `RemoveListener` in `OnDisable` is the pattern the scope replaces.

## See also

- [Opening and results](Opening-and-Results.md): opening a view and reading its `ViewResult`
- [Navigation and back](Navigation-and-Back.md): `BackPolicy`, `OnBack` and returning to a screen
- [Catalogs and keys](Catalogs-and-Keys.md): rows, retention, cover and back policies
- [Transitions](Transitions.md): the transition referenced by `ViewHost`
- [Popups and backdrop](Popups-and-Backdrop.md): modal layers and the shared backdrop
- [Root canvas](Root-Canvas.md): render mode, the UI camera and the sorting band
- [Zenject](Zenject.md): injection into views before `Awake`
- [Troubleshooting](Troubleshooting.md): [UIDIRECTOR102](Troubleshooting.md#uidirector102), [UIDIRECTOR103](Troubleshooting.md#uidirector103), [UIDIRECTOR107](Troubleshooting.md#uidirector107), [UIDIRECTOR117](Troubleshooting.md#uidirector117), [UIDIRECTOR118](Troubleshooting.md#uidirector118)
- [Architecture: scopes](Design/Architecture.md#9-scopes) and [ADR-003](Design/Decisions/ADR-003-Begin-Only-Hooks-And-Scopes.md): why hooks only begin things
