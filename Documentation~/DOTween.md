# DOTween

[Back to index](index.md)

UIDirector ships an optional integration for DOTween: a ready-made `TweenTransition` that you configure in the Inspector, and the pieces you need to write your own transition on DOTween without leaving a tween behind. This page covers turning the integration on, `TweenTransition` and its settings, writing a kill-safe custom transition, and the trap that `DOTween.KillAll()` ends a transition that is playing. The contract that every transition keeps is on [Transitions](Transitions.md); this page assumes it.

The core of the package needs no tween library. Without DOTween the integration is not compiled, the types on this page do not exist, and the rest of the package works unchanged.

---

## Turn the integration on

The integration is a separate assembly, `Ecanakli.UIDirector.DOTween`, in the namespace `Ecanakli.UIDirector`. Unity compiles it when one of two scripting defines exists:

- `DOTWEEN`. DOTween from the Asset Store adds it itself when it is imported; Setup DOTween in its Utility Panel (`Tools > Demigiant > DOTween Utility Panel`) keeps it.
- `ECANAKLI_UIDIRECTOR_DOTWEEN`. The assembly sets it for itself, through a version define, when a package named `com.demigiant.dotween` is installed. DOTween publishes no UPM package of its own; this is for a project that wraps DOTween in a package under that name.

If neither define is set but the DOTween assembly is in the project, turn the integration on with the menu item `Tools > UIDirector > DOTween Integration`. It adds `ECANAKLI_UIDIRECTOR_DOTWEEN` to every build target and removes it when you choose it again. It shows a check mark while the integration is on: when `DOTWEEN` is defined, when a package named `com.demigiant.dotween` is installed, or when `ECANAKLI_UIDIRECTOR_DOTWEEN` is set. When `DOTWEEN` is already defined, or that package is installed, choosing the item only tells you that the integration is already on and that there is nothing to toggle; it writes no define. When no assembly named `DOTween` is loaded it asks you to import DOTween first.

The core `DOTween.dll` is enough. The integration references that precompiled library only. It does not need DOTween's module scripts (the extension methods for UI, physics and so on) and never calls them. A transition of your own can live in `Assembly-CSharp` (see below), or in an assembly definition that references `Ecanakli.UIDirector` and `UniTask` (every `PlayAsync` returns a `UniTask`) and, if it uses `TweenTransition`, `Ecanakli.UIDirector.DOTween`. Such an assembly definition turns on Override References, adds `DOTween.dll` to its precompiled references and copies the define constraint `DOTWEEN || ECANAKLI_UIDIRECTOR_DOTWEEN`, as the DOTween Usage sample's assembly definition does. The constraint keeps the assembly out of the build when DOTween or the integration is absent.

If DOTween is deleted from the project while a define that turns the integration on is still set, the integration assembly can no longer resolve `DOTween.dll`. Remove `DOTWEEN` from every build target, or, if you turned the integration on with the menu, choose `Tools > UIDirector > DOTween Integration` again to remove `ECANAKLI_UIDIRECTOR_DOTWEEN`.

Game code in the default `Assembly-CSharp` can use `TweenTransition` and DOTween directly, with no assembly definition of its own: `Ecanakli.UIDirector.DOTween` and `DOTween.dll` are auto-referenced, so a loose script compiles against both whenever the integration is on. The version define `ECANAKLI_UIDIRECTOR_DOTWEEN` is set inside the package's own assembly only, so game code does not test it with `#if`. When the menu item adds the same symbol as a scripting define it is project-wide, but game code still should not depend on it. A script that names these types fails to compile when the integration is off. If a project needs to build with and without DOTween, keep such scripts in an assembly definition with the define constraint shown above.

The DOTween Usage sample shows both transitions. Its bootstrap has no integration code at all, only the ordinary root setup:

<!-- source: Samples~/DOTweenUsage/DOTweenBootstrap.cs -->
```csharp
public sealed class DOTweenBootstrap : MonoBehaviour
{
    // "Initialize On Awake" is off on this root; the bootstrap initialises it in Awake.
    [SerializeField] private ViewRoot _viewRoot;

    private void Awake()
    {
        // Before the root initialises, so the root finds an EventSystem.
        SampleEventSystem.EnsureExists();
        _viewRoot.Initialize(new ViewRootSetup());
    }

    private void Start()
    {
        // Owned by the root, so the welcome screen ends only when the root does.
        var navigator = _viewRoot.Navigator;
        navigator.Open(TweenViews.Welcome, navigator.RootToken, new OpenOptions { Source = "boot" });
    }
}
```

---

## TweenTransition

`TweenTransition` fades, scales and offsets a view with DOTween on unscaled time. You use it exactly like a built-in transition ([Transitions](Transitions.md)): add it to the prefab root and assign it to the `Transition` field of the `ViewHost`. No code is needed. In the sample, the `Hint` prefab has it and its view class is a plain popup:

<!-- source: Samples~/DOTweenUsage/HintView.cs -->
```csharp
public sealed class HintView : ViewBehaviour<HintArgs>
{
    [SerializeField] private TMP_Text _text;
    [SerializeField] private Button _ok;

    protected override void OnOpen(HintArgs args, ViewScope open)
    {
        _text.text = args.Text;
        open.Bind(_ok.onClick, Close);
    }
}
```

The `Card` prefab uses the same class with a different transition; only the transition on the prefab differs.

| Setting | Default | Meaning |
|---|---|---|
| `Fade` | on | Fades a `CanvasGroup` on its own object, added when missing. Shown is alpha 1, hidden is 0. |
| `Scale` | on | Scales its own `RectTransform`. Shown is 1. |
| `Hidden Scale` | 0.85 | The uniform scale of the hidden pose. |
| `Offset` | off | Moves its own `anchoredPosition`. Shown is the authored position. |
| `Hidden Offset` | (0, -1) | The hidden position as a fraction of the layer rectangle's size. (0, -1) is one layer height below. |
| `Duration` | 0.25 s | Seconds of unscaled time per play. A duration of 0 jumps to the end pose. |
| `Show Ease` | OutCubic | The DOTween `Ease` when showing. |
| `Hide Ease` | InCubic | The DOTween `Ease` when hiding. |

Each part can be turned off. With every part off, a play returns at once.

The enabled parts apply to both directions: a show animates them towards the shown pose and a hide towards the hidden pose, with the same duration. Only the two eases differ. A view that scales in and fades out, or otherwise shows and hides with different motions, needs a custom `ViewTransition`, such as the one below.

What it does at run time:

- It builds one `Sequence` of `DOTween.To` tweens per play, from the opposite end pose to the target pose.
- It sets the ease, auto-kill and unscaled update on the sequence itself. Project-wide DOTween defaults, such as a changed default ease or auto-kill turned off, cannot compound the ease or stop the kill callback from firing.
- It awaits the end of the sequence through the sequence's own kill callback. A completed auto-kill sequence kills itself, which ends the await.
- A cancelled token kills the sequence with `Kill(false)` and ends the await at once, even when DOTween defers the kill inside its own update. The view stays in the pose the tween had reached; the runner snaps if it needs the end pose.
- `Snap` kills a running sequence and then writes the pose of the enabled parts.
- A kill that the component makes itself (`Snap`, a new play that replaces a running one, `OnDestroy`) resumes the awaiting caller on the next player-loop step, never inside that call. The task of a play that `Snap` ends therefore does not complete inside `Snap`, which is the rule for every custom transition ([Transitions](Transitions.md#the-contract)), and a kill from `OnDestroy` does not resume the caller inside Unity's destroy message.
- A kill that comes from outside, such as `DOTween.KillAll()` or `Kill()` on the sequence, ends the await at once.
- It drops its reference to the sequence inside the kill callback, so a tween that DOTween recycles is never touched.

---

## Write a kill-safe custom transition on DOTween

A transition on DOTween is kill-safe when nothing that kills its tween can leave the returned task pending, and no reference to a recycled tween is ever used. `PunchTransition` fades the view in with a scale punch and fades it out while shrinking. It starts with a few constants and the handles it needs:

<!-- source: Samples~/DOTweenUsage/PunchTransition.cs -->
```csharp
[RequireComponent(typeof(CanvasGroup))]
public sealed class PunchTransition : ViewTransition
{
    private const float HiddenScale = 0.9f;
    private const float PunchStrength = 0.12f;
    private const int PunchVibrato = 4;
    private const float PunchElasticity = 0.5f;

    [SerializeField, Min(0f)] private float _duration = 0.35f;

    private CanvasGroup _group;
    private DOGetter<float> _getAlpha;
    private DOSetter<float> _setAlpha;

    // The only handle to the running sequence; no SetLink, so a kill happens only here.
    private Sequence _running;
```

The play builds the sequence, makes its settings explicit and returns a wait:

<!-- source: Samples~/DOTweenUsage/PunchTransition.cs -->
```csharp
public override UniTask PlayAsync(bool show, CancellationToken ct)
{
    if (ct.IsCancellationRequested || !Prepare())
        return UniTask.CompletedTask;

    KillRunning();
    if (_duration <= 0f)
    {
        Snap(show);
        return UniTask.CompletedTask;
    }

    // A punch returns to the scale it starts from, so a show starts at the shown scale.
    Write(show ? 0f : 1f, 1f);

    var sequence = DOTween.Sequence();
    if (show)
    {
        sequence.Join(DOTween.To(_getAlpha, _setAlpha, 1f, _duration).SetEase(Ease.OutQuad));
        sequence.Join(transform.DOPunchScale(new Vector3(PunchStrength, PunchStrength, 0f), _duration, PunchVibrato, PunchElasticity).SetEase(Ease.OutQuad));
    }
    else
    {
        sequence.Join(DOTween.To(_getAlpha, _setAlpha, 0f, _duration).SetEase(Ease.InQuad));
        sequence.Join(transform.DOScale(HiddenScale, _duration).SetEase(Ease.InQuad));
    }

    // Explicit, so project-wide DOTween defaults cannot change the ease or the lifetime of this tween.
    // Unscaled time, so a paused game still shows and hides the view.
    sequence.SetEase(Ease.Linear).SetAutoKill(true).SetUpdate(true).SetRecyclable(false).Play();
    _running = sequence;
    return WaitAsync(sequence, ct);
}
```

Four decisions make it safe.

**One handle, kept only while the tween lives.** `_running` is the only reference to the sequence, and the sequence is created with `SetRecyclable(false)`. DOTween can recycle a finished tween into a pool and hand the same object to another owner, so a stale reference would stop or move someone else's tween. The kill path takes the reference out before it kills:

<!-- source: Samples~/DOTweenUsage/PunchTransition.cs -->
```csharp
private void KillRunning()
{
    // Dropped before the kill; the sequence is not recyclable, so a stale reference is inert.
    var running = _running;
    _running = null;
    if (running.IsActive())
        running.Kill();
}
```

The sample sets no `SetLink`, so the only kill is the one made here. A link adds a second, hidden kill path.

**Explicit settings.** The sequence sets its own ease, auto-kill, unscaled update and recyclability. Do not inherit them from project-wide DOTween defaults, which another team can change.

**A wait that does not depend on a callback.** The play does not resume from `OnKill` or `OnComplete`. It polls on the player loop while the sequence is active and playing:

<!-- source: Samples~/DOTweenUsage/PunchTransition.cs -->
```csharp
// Polls on the player loop instead of resuming from OnKill or OnComplete: a kill from OnDestroy
// would then resume the runner inside Unity's destroy message.
private static async UniTask WaitAsync(Sequence sequence, CancellationToken ct)
{
    while (sequence.IsActive() && sequence.IsPlaying())
    {
        var cancelled = await UniTask.Yield(ct).SuppressCancellationThrow();
        if (!cancelled)
            continue;

        if (sequence.IsActive())
            sequence.Kill();

        return;
    }

    // Paused from outside, the sequence would never end; finish it at its end pose.
    if (sequence.IsActive())
        sequence.Kill(true);
}
```

Whatever kills the sequence, the loop sees `IsActive()` turn false on its next frame and returns. A kill from `OnDestroy` cannot resume the runner inside Unity's destroy message, because the loop only ever resumes on the player loop. When the token is cancelled, the loop kills the sequence and returns. When the sequence was paused from outside, it would never end, so the loop finishes it at its end pose.

**A `Snap` that cannot throw.** `Snap` kills the running sequence first and then writes the pose, and `OnDestroy` kills it too:

<!-- source: Samples~/DOTweenUsage/PunchTransition.cs -->
```csharp
public override void Snap(bool shown)
{
    KillRunning();

    // Never throws: a destroyed view is skipped.
    if (Prepare())
        Write(shown ? 1f : 0f, shown ? 1f : HiddenScale);
}
```

<!-- source: Samples~/DOTweenUsage/PunchTransition.cs -->
```csharp
private void OnDestroy()
{
    KillRunning();
}
```

`Prepare` returns `false` for a destroyed component, so `Snap` skips it. It also creates the getter and setter once per component, so a play allocates no closure:

<!-- source: Samples~/DOTweenUsage/PunchTransition.cs -->
```csharp
// False on a destroyed component, which cannot reach its group.
private bool Prepare()
{
    if (this == null)
        return false;

    if (_group == null)
        _group = GetComponent<CanvasGroup>();

    // Created once per component, so a play allocates no getter or setter.
    _getAlpha ??= GetAlpha;
    _setAlpha ??= SetAlpha;
    return _group != null;
}
```

Other points in this sample:

- It uses `DOTween.To` with a getter and a setter for the alpha, and the `Transform` shortcuts `DOPunchScale` and `DOScale`. All of them are in the core `DOTween.dll`. The shortcut for fading a `CanvasGroup` (`DOFade`) lives in a DOTween module assembly, which the package does not reference, so the sample tweens the alpha through `DOTween.To`.
- `SetUpdate(true)` makes the tween independent of `Time.timeScale`. A DOTween transition on scaled time stalls at `timeScale` 0 and ends in [UIDIRECTOR106](Troubleshooting.md#uidirector106).
- A punch returns to the scale it starts from, so a show starts at the shown scale, and the sample writes that pose before it builds the sequence.

---

## The trap: DOTween.KillAll() ends a playing transition

`DOTween.KillAll()` kills every tween in the project, including the tween of a transition that is playing. It runs the tween's `OnKill` callback but not its `OnComplete`. After `DOTween.KillAll()`, `TweenTransition` ends its play at once and `PunchTransition` on its next frame, and the view stays in the pose the tween had reached: the runner treats the play as finished, does not snap and logs nothing, so a popup can stay half-faded while it is open. `DOTween.Clear()` removes every tween too, but do not rely on it running `OnKill`. `TweenTransition` waits for `OnKill`, so after `Clear()` it ends only if `Clear()` runs `OnKill`; otherwise the transition timeout ends it, like the `OnComplete` case below. `PunchTransition` polls and ends on its next frame either way. A transition that completes from `OnComplete` is left waiting until the transition timeout (3 s by default, the `Transition Timeout Seconds` field of `ViewRoot`) cancels it, snaps the view and logs [UIDIRECTOR106](Troubleshooting.md#uidirector106). The view is not lost, but it stalls for the timeout and on a layer that shields input the shield stays up for that time.

Two things follow:

- A custom transition should not hang its completion on `OnComplete`, which a kill never runs. Poll `IsActive()` on the player loop, as `PunchTransition` does.
- Game code should not call `DOTween.KillAll()` or `DOTween.Clear()` while a view is opening or closing. To stop a tween of your own, kill that tween.

`TweenTransition` ends its await when the sequence is killed from outside by `KillAll` or by killing the sequence, and its later plays and snaps stay clean. The view stays in whatever pose the tween had reached until a later snap writes the end pose.

## Traps

- **Holding a tween reference after the tween ended.** With recycling on, the object may belong to another tween. Drop the reference when you kill, or create the tween with `SetRecyclable(false)`.
- **Relying on project-wide defaults.** Set the ease, auto-kill and update mode on the tween you create.
- **Scaled time.** `SetUpdate(true)` is required.
- **Allocating getters and setters per play.** Create them once per component.
- **Using a module extension.** `DOFade` on a `CanvasGroup` and similar methods are not in the core library. Use `DOTween.To` unless your own assembly references the modules.
- **Integration defined but DOTween removed.** Remove `DOTWEEN` from the scripting define symbols, or choose `Tools > UIDirector > DOTween Integration` again to remove `ECANAKLI_UIDIRECTOR_DOTWEEN`.

## See also

- [Transitions](Transitions.md): the built-ins and the contract every transition keeps
- [View authoring](View-Authoring.md): the view prefab, its `ViewHost` and the hooks
- [Diagnostics](Diagnostics.md): how warnings and errors are reported
- [Troubleshooting](Troubleshooting.md#uidirector106): UIDIRECTOR106, a transition that did not finish
- [Limitations](Limitations.md): what the package does not do
- [Architecture: transitions](Design/Architecture.md#15-transitions): the `TweenTransition` design
