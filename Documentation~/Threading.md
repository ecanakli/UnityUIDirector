# Threading

[Back to index](index.md)

UIDirector is for Unity's main thread. This page says exactly what that means: which calls throw on another thread, which calls are posted to the main thread instead, where the continuation of an awaited `ShowAsync` resumes, who owns which `CancellationToken`, and what the package expects from custom providers and transitions.

---

## The contract in one paragraph

Everything that opens a view, registers something or queues a request must be called on the main thread and throws on any other thread. The calls that only stop or release things (`ViewRegistration.Remove()`, `INavigator.CloseAll`, `INavigator.Trim`, `ViewQueue.Clear`) may be called from any thread: off the main thread they are posted and run on the main thread at its next update. An owner `CancellationToken` may be cancelled from any thread: the package receives the cancellation on that thread, posts the work to the main thread and logs [UIDIRECTOR113](Troubleshooting.md#uidirector113). The package never runs your callbacks, closes a view or touches a Unity object on a thread other than the main one.

The reason is Unity, not caution. A view is a GameObject, and instantiating, activating or destroying one is only allowed on the main thread. The state of the navigator, its layers and its queue is also not locked.

---

## What throws off the main thread

These calls throw `InvalidOperationException` when they are made on another thread. The message names the object: `The navigator must be used on the main thread.` or `The queue must be used on the main thread.`

| Call | Notes |
|---|---|
| `INavigator.ShowAsync`, `INavigator.Open` and the key shortcuts in `NavigatorExtensions` | The check is the first thing the call does: no request is created, no event is published, no shield is raised, nothing is loaded |
| `INavigator.HoldInput`, `AddObserver`, `AddCatalog` | Nothing is registered |
| `INavigator.PreloadAsync` | Throws at the call, not through the returned task |
| `ViewQueue.Enqueue`, `EnqueueAsync`, `Hold`, `AddGate`, `AddObserver` | Nothing is queued or registered |
| Creating a `ViewQueue` | The constructor registers an observer on the navigator, so it throws too while its lifetime token is alive and the root exists. With a lifetime already cancelled or a root already destroyed it makes an inert queue and throws nothing |

A call that throws has changed nothing. Two exceptions to "throws": after the root was destroyed the navigator's calls throw nothing any more. A call then answers that the root is gone: `ShowAsync` completes at once with `RejectReason.RootGone`, and a registration call returns an empty `ViewRegistration`. The calls of a `ViewQueue` keep their own check and still throw off the main thread after that.

Some calls are not checked at all, and you must not make them from another thread: `ViewHandle.Close()`, the `Complete` and `Close` calls of a view, every member of `ViewScope`, and the read-only queries (`IsOpen`, `TryGetOpen`, `TryGetTop`, `IsBusy`, `IsLayerClear`, `Describe`, `ViewQueue.Count`, `IsHeld`). They read or change state that nothing synchronises, so a call from another thread gives a wrong answer or corrupts state, and nothing reports it.

---

## What is posted to the main thread

These calls never throw on another thread. They are put in UniTask's post queue and run on the main thread at the next update.

| Call off the main thread | What happens |
|---|---|
| `ViewRegistration.Remove()` | Posted. `IsActive` stays `true` until the main thread runs it. No warning |
| `INavigator.CloseAll(layer)` | Posted |
| `INavigator.Trim()` | Posted |
| `ViewQueue.Clear()` | Posted. Every waiting item ends as not shown with `RejectReason.Cancelled` |
| An owner token cancelled: a request opened with `ShowAsync` or `Open` | Posted; [UIDIRECTOR113](Troubleshooting.md#uidirector113) |
| An owner token cancelled: a registration (`HoldInput`, `AddObserver`, `AddCatalog`, a preload, a queue hold, gate or observer) | Posted; UIDIRECTOR113 |
| An owner token cancelled: a waiting queue item | Posted; UIDIRECTOR113 |
| The lifetime token of a `ViewQueue` cancelled | Posted; the queue ends on the main thread; UIDIRECTOR113 |

A queue also ends when the root is destroyed. The root cancels `RootToken` on the main thread, so that end needs no post.

The queue completes its items on the main thread as well. If the task that `ShowAsync` returned for the presented item faults on another thread, the queue only reports the fault to the diagnostics there; it publishes the item's `NotShown` and completes the `EnqueueAsync` result on the main thread.

Two calls are neither checked nor posted. `INavigator.Back()` and `INavigator.CloseTo(viewId)` return `false` and do nothing when they are called off the main thread. A `false` from `Back()` normally means "nothing handled it, the game decides", so do not call `Back()` from a worker thread; call it from the main thread, as `BackInput` does.

### An owner cancelled on another thread, moment by moment

A view was opened with a `CancellationTokenSource` whose token is the owner. A timer thread cancels the source.

1. **The timer thread calls `Cancel()`.** Unity's `CancellationTokenSource` runs the package's callback on that thread. The callback sees that it is not on the main thread. In the editor and in development builds it logs UIDIRECTOR113. It posts "end this request" to the main thread and returns.
2. **Right after `Cancel()` returns, nothing has ended.** The view is still on screen, the request is alive, the awaiting `ShowAsync` is still pending and the shield holds whatever it held.
3. **At the next main-thread update the post runs.** It does exactly what a cancellation on the main thread does: a shown request closes, a request that is still waiting or loading is withdrawn. Every awaiter completes with data, and `ShowAsync` reports `OwnerEnded` as the reason.
4. **A stale post is harmless.** If the request ended on its own before the post ran, the post finds nothing to do.

The result is correct, which is why the id is a warning and not an error. It tells you that the end did not happen at the moment of the call. After an off-thread `Cancel()`, do not write code that assumes the view is already gone. Cancel owner tokens on the main thread when you can; the sample feature below ends its owner from the main thread:

<!-- source: Samples~/QueueUsage/NewsFeature.cs -->
```csharp
public void Remove()
{
    if (_owner == null)
        return;

    // Ending the owner removes the catalog and releases its kept instances; the news popup ends too, because it was queued with this owner.
    _owner.Cancel();
    _owner.Dispose();
    _owner = null;
}
```

A token that is already cancelled when you pass it is not posted anywhere: `ShowAsync` and `Open` refuse the request at the call, on the main thread, with `RejectReason.OwnerEnded`.

---

## Awaiting ShowAsync

`ShowAsync` must be called on the main thread. The task it returns is completed by the package, on the main thread, when the request ends. That fixes where your code resumes:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
        private async UniTaskVoid ConfirmQuitAsync()
        {
            var answer = await _navigator.ShowAsync(
                AppViews.Confirm,
                new ConfirmArgs("Quit", "Close the application?"),
                _navigator.RootToken,
                new OpenOptions { Source = "back.root" });

            if (!answer.HasValue || !answer.Value)
                return;

            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
```

1. **The call runs on the main thread.** The request is accepted or refused at once; the returned task is pending.
2. **The view opens, the user answers.** The view calls `Complete(value)` on the main thread.
3. **The request ends.** The package first applies its own state: the view leaves its layer, the shield is released, the scopes end. Only then does it complete the awaiters, in the order they joined, so code after your `await` sees a consistent navigator.
4. **Your code resumes on the main thread**, inline, as part of that completion. UniTask does not capture a synchronisation context and does not switch threads, so the thread that completes the task is the thread that runs the continuation. You do not need `UniTask.SwitchToMainThread()` after the await.
5. **The continuation may call the navigator.** Opening, closing or going back from the continuation is legal and never throws.

Other facts about the await:

- If the request had already ended when you await the task, the await completes synchronously on whatever thread awaits it.
- If you hand the task to code on another thread and await it there, the continuation still arrives on the main thread, because the main thread completes the task. Switch back or marshal before the code touches anything that is not thread-safe.
- `ShowAsync` is never cancelled and never faults for anything that happens to the view. An owner that ends, a load that fails and a root that is destroyed all complete the task with a `ViewResult` that says what happened. The call itself throws only in the off-thread case above.
- `ViewHandle.WaitForCloseAsync(waiter)` is cancelled only by its own `waiter` token. A `waiter` token cancelled on another thread makes that await throw `OperationCanceledException`, and the continuation may run on the cancelling thread, because UniTask does not move it. Switch to the main thread before touching Unity objects there. The cancelled waiter is removed from the request at once, so it holds nothing until the request ends.

---

## CancellationToken owners

Every call that creates something has an owner token, and the package never keeps the thing longer than the owner. Who cancels the token is your decision, and the cancelling thread decides where the end is noticed.

| Token | Cancelled by | The package notices it |
|---|---|---|
| The owner of `ShowAsync`, `Open`, `Enqueue`, `EnqueueAsync` | Your code | On the cancelling thread; the end runs on the main thread (posted when it is another thread) |
| The owner of `HoldInput`, `AddObserver`, `AddCatalog`, `PreloadAsync`, `ViewQueue.Hold`, `AddGate`, `AddObserver` | Your code | Same; the registration is removed on the main thread |
| `INavigator.RootToken` | The package, when the root is destroyed, on the main thread, as the last step of the teardown | Always on the main thread |
| `ViewScope.Token` | The package, when the open or visible span ends, on the main thread | Always on the main thread |
| The lifetime token of a `ViewQueue` | Your code | The queue ends on the main thread. It also ends with `RootToken` |
| The `waiter` of `WaitForCloseAsync` | Your code | Only that await is affected |

`RootToken` is the owner to use for anything that should last as long as the UI. The tokens the package hands you (`RootToken`, `ViewScope.Token`) are ordinary `CancellationToken` values: you may pass them to background work and check them there, and their callbacks run on the main thread, because the package cancels them there.

---

## UniTask usage

- UniTask is a required dependency. The package returns `UniTask` and `UniTask<T>`, never `Task`. Convert with `AsTask()` only where an API demands it.
- The package decides "am I on the main thread" through UniTask's `PlayerLoopHelper` and posts through `UniTask.Post`. Before UniTask has captured the main thread id, every thread counts as the main thread, so the check cannot reject a call that early.
- The navigator's timers (the load and transition timeouts and the indicator delay) are ticked by the root's `Update` on unscaled time, so `Time.timeScale` does not affect them. One frame advances them by at most 0.1 s. A queue's tick runs through UniTask on the `Update` step. The long-hold warning of a queue on the root's own navigator is a timer on the same navigator clock; a queue on any other `INavigator` times it with a UniTask delay on unscaled time.
- A UI event handler cannot await. Start the async method and say why it is not awaited, as the sample above does with `.Forget()`.
- The package does not switch threads after an await. A custom `IViewProvider` must complete `AcquireAsync` and `PreloadAsync` on the main thread, and a custom `ViewTransition` must complete `PlayAsync` on the main thread, because package code, including the creation and placement of the view, runs in the continuation on whatever thread completed the task.

---

## The guard in the editor and in players

The checks are part of the runtime, not editor helpers.

| Behaviour | Editor | Development build | Release build |
|---|---|---|---|
| Throw off the main thread (open, register, queue) | Yes | Yes | Yes |
| Post off the main thread (`Remove`, `CloseAll`, `Trim`, `Clear`, owner cancelled) | Yes | Yes | Yes |
| UIDIRECTOR113 console warning | Yes | Yes | No |

The check compares the id of the current thread with the id of the main thread that UniTask captured. It costs one comparison per call.

## See also

- [Concepts](Concepts.md)
- [Opening and results](Opening-and-Results.md)
- [Queue](Queue.md)
- [Troubleshooting: UIDIRECTOR113](Troubleshooting.md#uidirector113)
- [Limitations](Limitations.md)
- [Performance](Performance.md)
