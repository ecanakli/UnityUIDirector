# Transitions

[Back to index](index.md)

A transition animates a view in and out. It is a component on the view prefab, referenced by the prefab's `ViewHost`. A view without one shows and hides instantly. This page covers the built-in transitions, putting one on a prefab, time, writing your own, and the contract a custom transition must keep. The DOTween based transition is on [DOTween](DOTween.md).

---

## Put a built-in transition on a view

1. Open the view prefab and select its root, the object that has the `ViewHost`.
2. Add `FadeTransition`, `ScaleTransition` or `SlideTransition` to it.
3. Drag that component into the `Transition` field of the `ViewHost`.
4. Set the duration and the eases.

Nothing else is needed: no code, no tween library. The same component plays both the show and the hide. A `ViewHost` references one transition, so a view has one.

| Transition | What it animates | Settings and defaults |
|---|---|---|
| `FadeTransition` | The alpha of a `CanvasGroup` on its own object: shown is 1, hidden is 0. It requires a `CanvasGroup`, which Unity adds with it. | `Duration` 0.25 s, `Show Ease` OutCubic, `Hide Ease` InCubic |
| `ScaleTransition` | The uniform scale of a target `RectTransform`: shown is 1, hidden is `Hidden Scale`. Optionally fades a `CanvasGroup` too, added when missing. | `Target` (empty means its own object), `Hidden Scale` 0.85, `Fade` off, `Duration` 0.25 s, `Show Ease` OutBack, `Hide Ease` InCubic |
| `SlideTransition` | The `anchoredPosition` of a target `RectTransform`: shown is the position authored in the prefab, hidden is that position moved one layer width or height past an edge. | `Target`, `Edge` (Left, Right, Top or Bottom; default Bottom), `Duration` 0.25 s, `Show Ease` OutCubic, `Hide Ease` InCubic |

The eases are `Linear`, `OutCubic`, `InCubic`, `InOutCubic` and `OutBack`; they are chosen in the Inspector and there is no easing API. `SlideTransition` measures the distance from the layer's rectangle in canvas units when it plays, not from the screen size, so it follows a resized layer. It reads the authored position the first time it is used.

To combine motions that the built-ins do not combine, such as a fade with a slide, write a custom transition or use `TweenTransition` from the [DOTween](DOTween.md) integration.

A built-in transition plays its motion in both directions: the same parts and the same duration run towards the shown pose on a show and towards the hidden pose on a hide. Only the eases (`Show Ease`, `Hide Ease`) differ. A view that needs a different show and hide, such as scaling in and fading out, needs a custom `ViewTransition`, because `PlayAsync(show, ct)` is where the two directions can differ.

## Time

Every built-in transition runs on unscaled time. A paused game (`Time.timeScale` 0) still shows and hides views. The backdrop of a modal layer fades on the same loop for 0.2 s; that fade is separate from the view's transition.

One frame advances a built-in transition or a backdrop fade by at most 1/30 s, while the clock of the timeouts advances by at most 0.1 s per frame. Below 30 fps a transition therefore takes longer than its duration: a duration of D seconds takes at least 30 x D frames, so the default 0.25 s fade takes 8 frames, about 0.4 s, at 20 fps. With the default 3 s transition timeout, a built-in transition longer than about fps / 10 seconds is snapped with [UIDIRECTOR106](Troubleshooting.md#uidirector106) although it animates correctly (at 20 fps, a duration above 2 s). Below 10 fps the clock is capped as well, so any built-in transition of 1 s or longer is snapped. Raise `Transition Timeout Seconds` or shorten the transition. Custom transitions do not use this loop.

The timeout that bounds a transition counts on the root's clock, which also runs on unscaled time (see the contract below).

## When a transition does not play

The package snaps a view to its end pose, with no animation, when:

- the view is closed by navigation: popups that are older than a screen that opens are ended at once with `CloseReason.Navigated`, and a screen that is reopened in place with new arguments is shown again without a transition;
- the request is ended from outside: the owner token ended or the instance was deactivated (a destroyed instance, or an instance whose root was destroyed, is gone: the request just ends and nothing is snapped);
- a transition runs longer than the transition timeout, or throws.

A close that is requested while the show plays does not cut the show. When the view, its handle or a result call asks for the close during `Opening` (`Close`, `Complete`, a handle's `Close`), the show plays to its end and the hide plays after it. Only the cases above cut a show: a terminate cause (the owner ended, the instance deactivated, the view closed by navigation) snaps the view to the hidden pose; a timeout or a transition that throws snaps it to the end pose of the direction that was playing; a destroyed instance or root just ends the request.

A view without a transition is always snapped. See [Navigation and back](Navigation-and-Back.md) for what closes by navigation.

---

## Write a custom transition

Derive from `ViewTransition` and implement two members:

<!-- signature -->
```csharp
public abstract class ViewTransition : MonoBehaviour
{
    public abstract UniTask PlayAsync(bool show, CancellationToken ct);
    public abstract void Snap(bool shown);
}
```

`PlayAsync(show, ct)` animates to the shown pose when `show` is `true` and to the hidden pose when it is `false`. `Snap(shown)` writes the end pose at once. `PopTransition` fades and scales the view on unscaled time:

<!-- source: Samples~/BasicUsage/PopTransition.cs -->
```csharp
[RequireComponent(typeof(CanvasGroup))]
public sealed class PopTransition : ViewTransition
{
    // One frame advances at most this much: a hitch cannot skip the animation, and below 30 fps it runs slower.
    private const float MaxStepSeconds = 1f / 30f;

    [SerializeField, Min(0f)] private float _duration = 0.2f;
    [SerializeField] private float _hiddenScale = 0.9f;

    private CanvasGroup _group;

    // Snap and a newer play bump this, which ends a play that is still running.
    private int _generation;

    public override async UniTask PlayAsync(bool show, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !TryGetGroup(out var group))
            return;

        var target = transform;
        var fromAlpha = group.alpha;
        var fromScale = target.localScale.x;
        var toAlpha = show ? 1f : 0f;
        var toScale = show ? 1f : _hiddenScale;
        var generation = ++_generation;
        var elapsed = 0f;

        while (elapsed < _duration)
        {
            await UniTask.NextFrame(PlayerLoopTiming.Update);

            // Stop on cancel, on Snap, and when the view was destroyed mid-play.
            if (ct.IsCancellationRequested || generation != _generation || group == null || target == null)
                return;

            // Unscaled, so a paused game still shows and hides views.
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxStepSeconds);
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _duration));
            Apply(group, target, Mathf.Lerp(fromAlpha, toAlpha, t), Mathf.Lerp(fromScale, toScale, t));
        }

        // The last write is exact, whatever the frame steps added up to.
        Apply(group, target, toAlpha, toScale);
    }
```

What to read in it:

- The loop steps one frame at a time and adds `Time.unscaledDeltaTime`, capped, so it finishes while the game is paused.
- After every await it checks three things: the token, a generation counter and whether the view still exists. `Snap` and a newer play bump the counter, which ends an older play that has not yet seen the cancel. The older play returns on its next frame, never inside `Snap`.
- The last write is exact, so the pose never depends on how the frame steps added up.

`Snap` writes the end pose and ends a play that is still running:

<!-- source: Samples~/BasicUsage/PopTransition.cs -->
```csharp
public override void Snap(bool shown)
{
    // Never throws: a destroyed view is skipped.
    if (!TryGetGroup(out var group))
        return;

    _generation++;
    Apply(group, transform, shown ? 1f : 0f, shown ? 1f : _hiddenScale);
}
```

The helpers treat a destroyed component or transform as missing instead of throwing:

<!-- source: Samples~/BasicUsage/PopTransition.cs -->
```csharp
private static void Apply(CanvasGroup group, Transform target, float alpha, float scale)
{
    if (group == null || target == null)
        return;

    group.alpha = alpha;
    target.localScale = new Vector3(scale, scale, 1f);
}

private bool TryGetGroup(out CanvasGroup group)
{
    // A destroyed component cannot reach its transform, so it counts as missing.
    if (this == null)
    {
        group = null;
        return false;
    }

    if (_group == null)
        _group = GetComponent<CanvasGroup>();

    group = _group;
    return group != null;
}
```

Put the component on the prefab root, next to the `ViewHost`, and assign it to the `Transition` field, as for a built-in.

## The contract

The package gives a transition a fixed sequence. Before the first show it calls `Snap(false)`, turns the canvases on and calls `PlayAsync(true, ct)`. To hide, it calls `PlayAsync(false, ct)`. When a terminate cause cuts the request (owner ended, instance deactivated, closed by navigation), the package calls `Snap(false)` and then cancels `ct`. When the timeout fires or the play throws, it cancels `ct` and then calls `Snap` with the end pose of that direction. When the instance or the root is destroyed it only cancels `ct`.

In a cut, `Snap` runs before `ct` is cancelled. A play that `Snap` ends must therefore not report completion from inside `Snap`: the runner would continue as if the transition had finished normally while the request is still being cut.

A custom transition keeps these rules:

| Rule | Why |
|---|---|
| **Run on unscaled time.** | A transition on scaled time never finishes while `timeScale` is 0. The timeout then snaps it and reports [UIDIRECTOR106](Troubleshooting.md#uidirector106). Use `Time.unscaledDeltaTime`, `UniTask.Delay(..., DelayType.UnscaledDeltaTime)` or DOTween with unscaled update. |
| **Start from the opposite end pose and finish on the pose `Snap` writes for the same direction.** | The runner never interrupts a transition with another one, so there is no "continue from the current pose" case. A play that ends on a different pose than `Snap` leaves the view in a state a later snap would change. The runner snaps to the hidden pose before every show, and a hide always starts from the shown pose, so starting from the current pose, as `PopTransition` does, is the same thing. |
| **Stop when `ct` is cancelled.** Return within a few frames, or throw `OperationCanceledException`, and write nothing after you have returned. | A terminate cause snaps and then cancels the token; the timeout cancels and then snaps. A play that goes on writing overwrites the snapped pose. On cancel, do not jump to the end pose; the runner snaps if it needs to. |
| **`Snap` is synchronous, idempotent and never throws, from any state.** | It can arrive before any play, during a play, after a play, twice in a row, or on a destroyed component. It writes the end pose and ends a running play. Guard every reference to Unity objects, because the component may already be destroyed. |
| **Never complete the returned task inside `Snap`, or inside a Unity destroy or disable message.** | If `Snap` ends the running play and that completes its task on the spot, the runner resumes inside `Snap`, before the package has cancelled `ct`, and carries on as if the play had finished normally. If your play ends because `OnDestroy` or `OnDisable` killed a tween or cancelled a source, the runner resumes inside that message, and its next steps activate, deactivate or reparent objects, which is an engine error inside `OnDisable`. A play that is ended by `Snap` finishes one player-loop step later, for example with `await UniTask.Yield()`, or polls as the DOTween sample does. |
| **Do not call the navigator.** | A transition is a presentation detail. Opening or closing views from inside one re-enters the runner that is waiting for it. |
| **Finish within the transition timeout.** | See below. |

An exception from `PlayAsync` or from `Snap` is reported to the root's error handler as a transition error (`ViewErrorSource.Transition`) with the view id, or logged with `Debug.LogException` when no handler is set. After a failed play the view is snapped. That is a fallback; do not rely on it.

### The timeout

A show or hide transition that does not finish within the transition timeout is cancelled, the view is snapped to its end pose, and the package logs [UIDIRECTOR106](Troubleshooting.md#uidirector106), naming the view and whether it was the show or the hide. The runner then continues as if the transition had finished. The request is not lost.

- The timeout is the `Transition Timeout Seconds` field of `ViewRoot` (serialized as `_transitionTimeoutSeconds`). The default is 3 s, set by the constant `DefaultTransitionTimeoutSeconds`, and the minimum is 0.1 s.
- It counts on the root's clock. `ViewRoot.Update` advances that clock by `min(Time.unscaledDeltaTime, 0.1 s)` per frame, so it keeps running at `timeScale` 0, and a suspension or a breakpoint adds at most one step.
- If a transition really needs more than 3 s, raise the field.
- The runner stops waiting at the timeout even if the transition ignores its token, but a transition that ignores the token may keep running and write poses after the snap. Honour the token.

### Checking a custom transition

The package ships no checker for custom transitions, so check yours by hand: `Snap(true)` and `Snap(false)` write two different poses and each can be called twice; a play ends on the pose `Snap` writes; calling `Snap` in the middle of a play does not complete the play's task before `Snap` returns; cancelling a play stops it within a few frames and the pose stays put; a play finishes while `Time.timeScale` is 0; and destroying the view in the middle of a play throws nothing.

---

## Traps

- **Scaled time.** `Time.deltaTime`, `WaitForSeconds` and `UniTask.Delay` with the default delay type all stop at `timeScale` 0. The symptom is a 3 s stall and [UIDIRECTOR106](Troubleshooting.md#uidirector106).
- **`Snap` that creates things.** Adding a component in `Snap` can fail on an object that is being destroyed. Resolve and cache references in advance, and guard them.
- **A tween library that completes from a kill callback.** The kill may run inside `OnDestroy`. See [DOTween](DOTween.md).
- **A transition on a child with the wrong target.** The built-ins act on their own object's `CanvasGroup` or on their `Target`. Put them on the prefab root unless you set a target on purpose.
- **Expecting a hide animation everywhere.** Navigation, a lost instance and the timeout snap without one.

## See also

- [DOTween](DOTween.md): `TweenTransition` and writing a transition on DOTween
- [View authoring](View-Authoring.md): the prefab structure a view needs
- [Popups and backdrop](Popups-and-Backdrop.md): the backdrop fade and the input shield during a transition
- [Navigation and back](Navigation-and-Back.md): what closes a view without a hide transition
- [Diagnostics](Diagnostics.md): how warnings and errors are reported
- [Troubleshooting](Troubleshooting.md#uidirector106): UIDIRECTOR106, a transition that did not finish
- [Limitations](Limitations.md): transitions are not interruptible
- [Architecture: transitions](Design/Architecture.md#15-transitions): how the runner plays and cuts a transition
