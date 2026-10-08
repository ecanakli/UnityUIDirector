# Performance

[Back to index](index.md)

This page says what the package guarantees about cost, which test holds each guarantee, what opening
a view costs and how to keep that cost out of a frame the player notices, and what the figures do
not cover.

**Read this first.** Every figure and every "allocates nothing" on this page comes from a test in
the package's `Tests/` folder. The tests measure allocation, not time, so no duration is stated
anywhere on this page. To get a duration for your own project, profile it; the last section says how.

---

## What the package guarantees

| Situation | Guarantee | Held by |
|---|---|---|
| An idle root: nothing open, or a screen open and nothing happening | One cheap call per frame, no allocation | `IdleFrame_ScreenOpenWithBackInputAndFitter_AllocatesNothing` |
| `Back()` with nothing to close, and `Back()` swallowed by an input hold | No allocation | `Back_NothingToDo_AllocatesNothing`, `Back_SwallowedByInputHold_AllocatesNothing` |
| `IsOpen`, `IsBusy`, `IsLayerClear` | No allocation | `IsOpen_OpenScreen_AllocatesNothing`, `IsBusy_Settled_AllocatesNothing`, `IsLayerClear_PopupsOverOpenScreen_AllocatesNothing` |
| One event dispatched to four observers | No allocation | `Publish_OneEventToFourObserversWhenWarm_AllocatesNothing` |
| A frame of a timed wait (a load that has not finished, under the load timeout and the indicator delay) | No allocation | `TimedWaitFrame_LoadPendingUnderTimeoutAndIndicatorDelay_AllocatesNothing` |
| A frame of a running `FadeTransition` | No allocation | `TransitionFrame_FadeRunningWhenWarm_AllocatesNothing` |
| One warm open and close of a plain screen | Under 8192 bytes | `OpenAndClose_PlainScreenWhenWarm_StaysUnderByteBudget` |
| The editor read-out of navigator state | No allocation once warm | `CaptureState_AfterOneWarmCall_AllocatesNothing` and its sibling in `NavigatorReadOutTests` |

Every row except the last is in `Tests/Runtime/Performance`, in the `Ecanakli.UIDirector.PlayModeTests`
assembly. The last is an edit mode test in `Tests/EditorTools`.

"Warm" means the same kind of call has run before on that root, so that lists, pools, cached
delegates and the JIT have reached their steady state. The first call of anything may allocate once.

### How the tests measure

The tests use two instruments. A short synchronous call is wrapped in Unity's
`Is.Not.AllocatingGCMemory()` constraint, which fails when the call allocates:

<!-- source: Tests/Runtime/Performance/IdleAllocationTests.cs -->
```csharp
TestDelegate back = () => { navigator.Back(); };

Assert.That(back, Is.Not.AllocatingGCMemory());
LogAssert.NoUnexpectedReceived();
```

A frame is measured with a recorder of the `GC.Alloc` marker. The test inserts two markers into the
player loop, one at the start of the Update phase and one after `ScriptRunBehaviourUpdate`, and
counts allocation samples between them over 30 frames. That span covers every `MonoBehaviour.Update`
(the root's clock tick, `BackInput`) and UniTask's Update runners (where the built-in transitions
step). It leaves out coroutines, so the test runner's own frame work is not counted. The scene's
`EventSystem` is switched off during the measurement, because it is not the package's cost.

Each test also checks that the measured code really did the work: the screen is open, the request is
still waiting, every observer received the event. A test that measured nothing would fail on those
checks instead of passing.

---

## Idle

An idle root costs one call per frame. `ViewRoot.Update` advances the navigator's clock by the
frame's unscaled delta (at most 0.1 s) and runs the timers that became due; with no timer pending
that is a loop over an empty list. Three more things can run, and only when you use them:

- `BackInput`, when it is on the root, reads one key. It allocates nothing.
- `SafeAreaFitter`, when one is registered with the root, adds one comparison of the safe area and
  the screen size per frame for all fitters together. With none registered the comparison does not
  run. The fitter declares no `Update` of its own, so a registered fitter costs nothing per frame. A
  fitter with no `ViewRoot` above it is checked by one player-loop item that compares the safe area
  each frame and removes itself when the fitter is disabled; enabling it again in the same frame
  reuses the item.
- A queue with nothing waiting and no hold schedules no timer. Each queue hold keeps one 60-second
  warning timer running until it ends.

An idle backdrop runs no animation and its canvas is turned off. The input shield is one graphic
with an empty mesh that stays active, so raising it blocks input in the same frame without a canvas
rebuild. A root with nothing open therefore does the clock tick and nothing else.

The idle test opens a screen, adds `BackInput` and a `SafeAreaFitter`, waits five frames, and counts
`GC.Alloc` samples over 30 frames. The count must be zero.

## Queries

The calls a game makes often are free of allocation once warm: `Back()` when there is nothing to
close, `Back()` while the input shield swallows it, `IsOpen`, `IsBusy` and `IsLayerClear`. A button
handler or an `Update` can call them without producing garbage.

## Events and timed waits

Dispatching an event to observers allocates nothing. The test builds the observer list the
navigator publishes through, adds four observers, publishes a closed event after a warm-up dispatch,
and requires zero allocation and four deliveries:

<!-- source: Tests/Runtime/Performance/OperationAllocationTests.cs -->
```csharp
TestDelegate publish = () => { observerList.Publish(in viewEvent); };

Assert.That(publish, Is.Not.AllocatingGCMemory());
Assert.That(counters, Has.All.Matches<CountingObserver>(counter => counter.Count == 2), "the measured dispatch reached all four observers");
```

`ViewEvent` is a struct passed with `in`, so the payload is not boxed and carries no view
arguments. An observer you write is your cost: the sample's `ViewEventLog` builds a string per event
because it prints one.

A load that has not finished is a timed wait: the load timeout and the indicator delay are both
pending while the root ticks its clock. A test holds a request in that state with a provider that
never completes, and counts zero allocations over 30 frames with the clock advancing and both
timers still pending. The same holds for a frame of a running `FadeTransition`, measured over 10
frames inside a one-second fade after one full warm show and hide. The other built-in transitions
and your own are not covered by an allocation test.

---

## Open and close

Opening is not a hot path and the package does not claim zero allocation for it. An open allocates
a request record with its awaiter lists, a context, an open scope (its token source
is created on first use), a visible scope for each span on screen, a cancellation source for each
load or transition step and a completion source for each awaiter. The delegates that the package's
own steps use are cached.

The test holds that total under a budget. It opens a plain screen, closes it in the same call, and
repeats; each cycle also creates and destroys the instance, because the row releases it on close:

<!-- source: Tests/Runtime/Performance/OperationAllocationTests.cs -->
```csharp
private bool OpenAndClose(ViewKey<NoArgs, NoResult> key)
{
    var handle = _world.Navigator.Open(key, _world.Owner);
    handle.Close();
    return !handle.IsAlive;
}
```

The budget is one constant:

<!-- source: Tests/Runtime/Performance/OperationAllocationTests.cs -->
```csharp
private const long OpenCloseBudgetBytes = 8192;
```

**A warm open and close of a plain screen stays under 8192 bytes.** The number is a ceiling, not a
measurement. The test prints the bytes and the allocation count it saw to the console. The bytes of a cycle are read from the profiler's
`GC Allocated In Frame` counter as the lowest open-and-close frame over 20 rounds minus the lowest
idle frame, after five warm-up cycles. The lowest value drops editor spikes. A known 32 KiB
allocation in a third frame of each round proves that the counter reads the frame it is asked
about; if it does not, the test is skipped as inconclusive, not passed.

What the budget covers and what it does not:

- It covers a plain prefab with no transition, one observer, a direct prefab reference.
- It does not cover a transition, your `OnOpen`, an Addressables load, a queue, or the cost of the
  Unity objects in your prefab. A view with a transition adds the transition's frames; a view that
  binds many listeners adds what each listener costs.
- A refused request still builds its `Detail` text in every build, because the result carries it.
  The diagnostic line is compiled out of release builds, but the message string is built by the
  caller either way. These are failure paths.

---

## Synchronous Instantiate

A catalog row with a `Prefab` reference is instantiated by `Object.Instantiate` in the step that
loads the view. The prefab is already in memory, so nothing is loaded and nothing is awaited: the
step starts inside the `Open` or `ShowAsync` call when the layer is idle, and when the layer reaches
the request otherwise. The instance is created under an inactive parent so that `Awake` does not run
yet, then placed under its layer and activated. `Awake`, `OnEnable`, the inject delegate and the
first canvas build of the whole prefab all happen in that one frame.

For a small prefab this is unremarkable. For a prefab with hundreds of graphics it is a visible
hitch on the frame of the first open. The package measures no duration for it (see below). It calls
the synchronous `Object.Instantiate` and does not use Unity's asynchronous instantiation, so it
cannot spread the work over frames. What you can do:

| Measure | What it does |
|---|---|
| Set the row to `KeepInstance` | The first open creates the instance. When the view closes it is parked, deactivated, and the next open of the same id places the same instance again: no `Instantiate`, no `Awake`, no inject. `OnOpen` and `OnVisible` still run. |
| Open the heavy view early | The first open is the expensive one. With `KeepInstance`, open the view once at a moment when a hitch is acceptable, for example behind a loading screen, and close it; the parked instance serves the next open. There is no call that creates an instance without showing it. |
| Preload an asset-key row | `PreloadAsync(viewId, owner)` loads the prefab asset through the provider and holds it until the owner ends, so the open does not wait for a load. It does not instantiate. The instance is still created at the open. For a row with a direct `Prefab` it does nothing, because that prefab is resident while the catalog is registered. |
| Make the prefab lighter | Fewer graphics and layout groups in the prefab, and fill long lists in `OnVisible` over several frames. Anything the prefab does not contain is anything it does not cost. |

The input shield goes up inside the call that opens a view on a shielding layer, so a long first
frame does not let a second tap through.

Retention has a price. A kept instance stays in memory while idle: one idle instance per id, with
no budget. `INavigator.Trim()` releases every idle kept instance, and the root calls it on
`Application.lowMemory`. A request in flight is never touched. See [View authoring](View-Authoring.md#kept-instances) for what runs again on a
reopen, and [Assets and Addressables](Assets-and-Addressables.md) for preload leases.

Rows with an asset key load asynchronously: the view provider loads the asset over as many frames as
it needs, and the load indicator and the load timeout apply
([UIDIRECTOR105](Troubleshooting.md#uidirector105)). The instance itself is still created in one
step. The Addressables provider loads the prefab asset and then calls `Object.Instantiate` in the
frame the load completes, so the first open of a heavy asset-key view has the same one-frame cost as
a direct prefab. `KeepInstance` and a lighter prefab help in the same way. `PreloadAsync` removes
only the load wait.

---

## Canvases per view and rebuild cost

A view prefab root carries a `Canvas` and a `GraphicRaycaster` (the `ViewHost` requires both), and
the package sets the canvas to override sorting. Every live view is therefore its own nested canvas.
The package adds a few more:

| Canvas | When |
|---|---|
| One per live view | While the view is on screen or covered |
| One backdrop canvas per modal layer | Always built, switched off while the backdrop is hidden |
| One shield canvas | Always, with an empty mesh |
| The load indicator | A canvas on the indicator object, added the first time it shows |

The layer objects themselves are plain `RectTransform`s and carry no canvas.

What follows is how uGUI treats nested canvases, and it is why the package is built this way. It is
not a measurement:

- A change inside one view marks that view's canvas dirty. The root canvas and the other views'
  canvases do not rebuild for it. That keeps a busy view from making a quiet one expensive.
- Each canvas is batched on its own. Graphics in different canvases do not batch together, so the
  number of draw calls grows with the number of canvases that draw. A stack of screens that all
  draw is more expensive than one that draws; see the next point.
- A covered view is not drawn. The default `CoveredMode.DisableCanvas` disables every canvas under
  the view, so a covered screen costs no drawing and no rebuild, and revealing it restores the
  canvases without the rebuild that `SetActive(true)` would cause. Reveal is the most frequent
  transition, which is why this is the default. A disabled canvas stops drawing and input but not
  scripts: `Update`, animators and particle systems keep running. `CoveredMode.Deactivate` stops
  them as well, at the cost of a rebuild when the view returns.
- A view that is not interactive has the enabled `GraphicRaycaster`s under it disabled, so it takes no
  part in raycasting.
- Covering a view collects its canvases and raycasters with two walks of its hierarchy into lists
  that are reused, and records only the ones it switched off; revealing it turns those on again from
  the lists without walking the hierarchy again. Placement switches nothing, so it adds no walk.

Practical consequences: keep the number of simultaneous full-screen views small (`CloseTo` and
returning to an existing screen keep the stack short, see [Navigation and back](Navigation-and-Back.md)),
split static from dynamic graphics inside a heavy view with a canvas of your own if its content
changes every frame, and do not put a `CanvasScaler` on a view root ([View authoring](View-Authoring.md#prefab-structure)).

---

## IL2CPP code size of struct argument types

`ViewKey<TArgs, TResult>`, `IView<TArgs, TResult>`, `ViewResult<TResult>` and the code that runs a
request are generic. IL2CPP generates native code for a generic type once per combination of value
types it is used with, while reference types share one copy. Every distinct struct used as an
argument or a result type therefore adds its own copy of the generic code to a build. `NoArgs`,
`NoResult` and `bool` are structs too; they are shared by every view that uses them.

Keep the number of distinct struct combinations modest. Small `readonly struct` arguments, as in the
samples' `ViewArgs.cs`, are fine; a project with hundreds of views each with its own argument struct
and its own result struct will build more code than one that uses a handful of shapes or passes
classes.

---

## What diagnostics cost

- The console lines are compiled out of release builds. In the editor and in development builds a
  line is written when a problem is found, never per frame.
- The ring of the last 100 events behind `Window > UIDirector` is filled by an observer; it exists
  in the editor and in development builds only. A write overwrites a slot of a fixed array of
  structs in place.
- `Describe` and the validator run only when you call them, and the window's captures only while
  the window is open in Play Mode.

See [Diagnostics](Diagnostics.md).

---

## What the figures do not cover

- **Durations.** The tests measure allocation, not time. There is no figure here for the time of an
  `Instantiate`, of a canvas rebuild or of a frame.
- **Draw calls.** The effect of the number of canvases on batching is general uGUI behaviour, not a
  result of this package's tests.
- **Transitions other than `FadeTransition`,** `TweenTransition` and custom transitions.
- **A queue.** Enqueueing, gates and holds have no allocation test.
- **Addressables.** The cost of a load, an instance and a preload lease.
- **Code size.** See the section above.

To measure your own project, run the same tests in your editor. Add `"testables": ["com.ecanakli.uidirector"]`
to `Packages/manifest.json`, then open `Window > General > Test Runner`, choose the PlayMode tab and run
the `Ecanakli.UIDirector.PlayModeTests` tests in the Performance folder. The read-out test is in
`Ecanakli.UIDirector.Editor.Tests`, on the EditMode tab. For a device, use the Profiler on a
development build: the `GC.Alloc` column of `ViewRoot.Update` should read zero while the UI is idle, and the cost of the first open of a heavy prefab shows as one
frame spent in `Instantiate`.

## See also

- [Concepts](Concepts.md)
- [View authoring](View-Authoring.md): kept instances and prefab structure
- [Catalogs and keys](Catalogs-and-Keys.md): retention and cover policies of a row
- [Assets and Addressables](Assets-and-Addressables.md): preload and trimming
- [Transitions](Transitions.md): built-in and custom transitions
- [Diagnostics](Diagnostics.md)
- [Threading](Threading.md)
- [Limitations](Limitations.md)
- [Architecture: allocation and idle cost](Design/Architecture.md#18-allocation-and-idle-cost)
