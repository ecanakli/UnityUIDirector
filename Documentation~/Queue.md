# Queue

[Back to index](index.md)

A `ViewQueue` presents requests one at a time, the most important first, on one layer. Use it for
things that appear because of the app and not because the player asked: a maintenance notice, a
what-is-new card, a reward, a request for feedback. Several features can queue at the same moment
without knowing about each other, and the queue decides the order. It sits above the navigator and
talks to it through `INavigator` only; a project that needs no queue pays nothing for it
([ADR-004](Design/Decisions/ADR-004-Queue-Above-The-Navigator.md)).

This page covers creating a queue, queueing, priorities, gates, holds, the ways a request ends
without being shown, observing, and a feature that adds its own catalog. The examples are the
files of the Queue Usage sample.

---

## Present notices by priority

A queue needs the navigator, the layer it presents on, and a lifetime. The sample creates one for
the popups layer that lives as long as the UI, and adds a gate to it:

<!-- source: Samples~/QueueUsage/StartupNotices.cs -->
```csharp
public StartupNotices(INavigator navigator, IOnboardingState onboarding)
{
    _lifetime = navigator.RootToken;
    _queue = new ViewQueue(navigator, ViewLayers.Popups, _lifetime, "startup");
    _queue.AddGate(new OnboardingGate(onboarding), _lifetime);
}
```

Two notices are then queued. The one queued second has the higher priority:

<!-- source: Samples~/QueueUsage/StartupNotices.cs -->
```csharp
public void EnqueueAll()
{
    _queue.Enqueue(QueueViews.Notice, new NoticeArgs("What is new", "Two new guides were added."), _lifetime,
        new QueueOptions { Priority = 10, Source = "startup.news" });

    // Enqueued second but presented first: the higher priority wins.
    _queue.Enqueue(QueueViews.Notice, new NoticeArgs("Maintenance", "The service pauses at 02:00."), _lifetime,
        new QueueOptions { Priority = 100, Source = "startup.maintenance" });
}
```

In the sample, the bootstrap opens the lobby and queues the notices in the same moment:

<!-- source: Samples~/QueueUsage/QueueBootstrap.cs -->
```csharp
_navigator.Open(QueueViews.Lobby, _navigator.RootToken, new OpenOptions { Source = "boot" });

// The notices wait until the lobby is shown, then present by priority.
notices.EnqueueAll();
```

What you see when you press Play: the lobby opens. Once it is shown, the queue presents
"Maintenance" (priority 100) although it was queued second. You close it with "OK" and "What is
new" (priority 10) follows at once. Only one popup is on screen at a time. The Console shows
`[queue] Enqueued ...` lines for both notices, in queueing order, and then the navigator's
`Requested` lines in presentation order (see [Observing the queue](#observing-the-queue)).

The keys are ordinary keys; the queue takes the same `ViewKey` values as the navigator:

<!-- source: Samples~/QueueUsage/QueueViews.cs -->
```csharp
[ViewKeys]
public static class QueueViews
{
    public static readonly ViewKey<NoArgs, NoResult> Lobby = ViewKey.Create("lobby");
    public static readonly ViewKey<NoticeArgs, NoResult> Notice = ViewKey.Create<NoticeArgs>("notice");
    public static readonly ViewKey<AskArgs, bool> Ask = ViewKey.Create<AskArgs, bool>("ask");
    public static readonly ViewKey<TipArgs, NoResult> TutorialTip = ViewKey.Create<TipArgs>("tutorial-tip");
}
```

---

## Create a queue

<!-- signature -->
```csharp
public ViewQueue(INavigator navigator, ViewLayer layer, CancellationToken lifetime, string name = null)
```

| Parameter | What it is |
|---|---|
| `navigator` | The navigator that presents the items. A queue built on the root's own navigator (`ViewRoot.Navigator`) is listed in the `Window > UIDirector` window and its events reach the window's event list. A queue built on any other `INavigator` works but is invisible to the window. |
| `layer` | The layer the queue watches and presents on, for example `ViewLayers.Popups`. The queue presents an item only while this layer is clear. On the root's own navigator the layer must exist there: a layer id that is neither one of `ViewLayers` nor in `ViewRootSetup.ExtraLayers` makes the constructor throw `ArgumentException` (parameter `layer`) before anything is registered. Another `INavigator` implementation is not checked. |
| `lifetime` | The queue lives until this token ends. Pass `INavigator.RootToken` for "as long as the UI exists". A token that is already cancelled gives an inert queue. |
| `name` | A label for the window and for `Describe`. Defaults to the layer id. |

There is no `Dispose`; ending the lifetime ends the queue. The queue also ends when the
navigator's root is destroyed, whatever lifetime you gave it, so a queue with an independent
lifetime does not outlive the UI. A queue built after the root is gone is inert from the start.
Create it on the main thread: the constructor registers an observer on the navigator
([Threading](Threading.md)). A project can have several queues, for different layers or different
features.

**When the queue presents.** After an enqueue, a removed hold, an added or removed gate, an ended item, or any navigator
request that ends or settles, the queue looks at its layer on the next `Update` tick. A gate that starts answering differently
does not wake the queue; the new answer is used the next time the queue looks. It presents the first waiting item only
when `INavigator.IsLayerClear(layer)` is true, which means all of these hold:

- the layer has no request at all, shown or waiting, including views opened by hand;
- no modal layer above it has a request (a system overlay stops a popups queue);
- the navigator is not busy: no operation is running on any layer and no input hold exists.

Two consequences follow from that rule. A queue waits while something else is open on its layer,
so a popup opened by hand keeps the queue waiting until it closes. And a queue on a history
layer such as `screens` is clear only while the layer is empty, because the bottom screen of the
stack stays; queues belong on layers whose views come and go, such as popups.

**Queue only keys whose rows are on the queue's layer.** The queue does not look at the rows when
you enqueue. It watches `layer` and asks the navigator to open the key; the navigator checks the
row at that moment. A row on another layer is turned away: the item ends as `NotShown` with
`RejectReason.WrongLayer`, nothing is placed, the input shield is not taken, and the navigator logs
[UIDIRECTOR126](Troubleshooting.md#uidirector126) naming both layers. The event carries the queue's
layer id, so every event of the item names one layer.

The usual cause is the row's layer. A row's `Layer Id` starts as `screens`, and the Inspector's +
copies the last row's layer, so a reward or news row that was added and never moved can still be on
`screens`. Queued on a `popups` queue it ends as `NotShown` with `WrongLayer` and UIDIRECTOR126, and
the view never appears. Set the row's `Layer Id` to the
queue's layer before you enqueue its key.

---

## Enqueue and EnqueueAsync

Both calls take the key, the arguments, an owner token and optional `QueueOptions`. They differ
in what they give back.

| Call | Returns | Use it when |
|---|---|---|
| `Enqueue(key, args, owner, options)` | `int`, the request id | You do not need the outcome. The id is the one every event of this item carries. |
| `EnqueueAsync(key, args, owner, options)` | `UniTask<ViewResult<TResult>>` | You need to know how it ended, or the view's answer. |

For a key without arguments pass `default(NoArgs)`; the queue has no shortcut overloads.

`EnqueueAsync` completes in every case, once. It completes when the view closes, and also when the
item ends inside the queue without being shown (denied, owner ended, cleared, queue ended). The
outcome is data, as with `ShowAsync` ([Opening and results](Opening-and-Results.md)). The sample
asks a yes or no question and reads the answer only through `HasValue`:

<!-- source: Samples~/QueueUsage/StartupNotices.cs -->
```csharp
public async UniTask<bool> AskForFeedbackAsync()
{
    // Completes also when the item never shows (denied, cleared), so the answer is read only through HasValue.
    var answer = await _queue.EnqueueAsync(QueueViews.Ask,
        new AskArgs("Would you like to tell us what to improve?"), _lifetime,
        new QueueOptions { Priority = 1, Source = "startup.feedback" });

    return answer.HasValue && answer.Value;
}
```

A turned-away item has `WasShown == false`, `Reason == CloseReason.NotShown`, a `Rejected` reason and
a `Detail`. In the sample, "Yes" completes with `true` and "No" with `false`. Escape, or an item that never
showed, carries no value (`HasValue` is `false`), so the method returns `false` for them too.

Neither call presents anything before the next tick, even when the layer is clear, so enqueues made
in one frame are ordered by priority and not by call order. Both throw `InvalidOperationException` off the main thread and `ArgumentNullException`
for a default key; a refused call reserves no request id and publishes nothing. A key whose id has
no catalog row is not noticed at enqueue; the navigator turns it away when the item is presented
(`RejectReason.UnknownKey`, [UIDIRECTOR101](Troubleshooting.md#uidirector101)).

---

## QueueOptions

<!-- signature -->
```csharp
public struct QueueOptions
{
    public int Priority;
    public string Source;
}
```

These are the only two options. Leave the struct out and both are default.

| Field | Default | What it does |
|---|---|---|
| `Priority` | `0` | Higher is presented first. Negative values are allowed and go after the default. Equal priorities keep the order in which they were queued. The value is carried by the item's events and by `GateRequest.Priority`. |
| `Source` | `null` | A free tag, as `OpenOptions.Source`. It travels with the item's events and with the `GateRequest`. Use one constant string per call site, such as `"startup.news"`, so a log line or an analytics event names the code that queued. |

The priority only orders waiting items. It does not close or replace a view that is already on
screen ([Limitations](Limitations.md)); urgent content that must not wait opens immediately.

---

## Priority order

The queue keeps its waiting items sorted by priority, highest first, and equal priorities in the
order they were queued. A new item goes after every waiting item with the same or a higher priority.

- Items queued in one frame while the layer is clear are all waiting when the next tick comes, so the
  highest priority is presented first whatever the call order. The Queue Usage sample shows it
  with "Maintenance" (100) queued after "What is new" (10).
- An item queued while another is on screen, with a higher priority than the waiting ones, goes
  before them. It does not go before the item that is on screen.
- The item that is being presented is not in the waiting list, so `Count` does not include it.
- When the presented view closes, the code that awaited it resumes before the queue presents the
  next item, because the queue pumps on the next tick. If that code opens a follow-up popup
  immediately, the layer is busy again and the next queued item keeps waiting.

---

## Gates

A gate is a synchronous rule that sees a request and answers yes or no. Use one for a condition
that belongs to no single feature: do not show a promotion while a tutorial runs, or while the
player is in a purchase.

<!-- signature -->
```csharp
public interface IViewGate
{
    GateDecision Evaluate(in GateRequest request);
}
```

The sample's gate turns away every queued request below priority 100 while the tutorial runs:

<!-- source: Samples~/QueueUsage/OnboardingGate.cs -->
```csharp
public sealed class OnboardingGate : IViewGate
{
    private const int UrgentPriority = 100;

    private readonly IOnboardingState _onboarding;

    public OnboardingGate(IOnboardingState onboarding) => _onboarding = onboarding;

    // Asked at enqueue and again right before presentation, so a notice queued earlier is caught too.
    public GateDecision Evaluate(in GateRequest request) =>
        _onboarding.IsRunning && request.Priority < UrgentPriority
            ? GateDecision.Deny("onboarding is running")
            : GateDecision.Allow;
}
```

A gate is added with an owner and an optional order, and it is asked until the owner ends or the
returned `ViewRegistration` is removed:

<!-- signature -->
```csharp
public ViewRegistration AddGate(IViewGate gate, CancellationToken owner, int order = 0)
```

**What the gate sees.** A `GateRequest` has these read-only fields:

| Field | Meaning |
|---|---|
| `RequestId` | The id the item keeps from enqueue to close. |
| `ViewId` | The id of the requested view. |
| `Layer` | The layer the queue presents on. |
| `Priority` | The item's priority. |
| `Source` | The item's source tag; may be null. |
| `AtPresentation` | `false` at enqueue, `true` right before the item is presented. |

There are no arguments in it. A gate decides by view id, priority, source and your own state.

**When it is asked.** At enqueue, and again right before presentation, once the layer is clear and
the item is next. A gate that allowed an item at enqueue can deny it later. Gates run in ascending
`order`; equal orders keep the order they were added. The first deny wins and later gates are not
asked. While the queue is held or its layer is busy, no gate is asked at presentation.

**What a deny does.** The item ends as `NotShown` with `RejectReason.Denied`, and the reason string
you gave to `GateDecision.Deny(reason)` is its `Detail`. A deny at enqueue means the item never
entered the queue (see the next section). A deny at presentation ends that item and the queue goes
on to the next one in the same pump.

`default(GateDecision)` is `Allow`, so a `GateDecision` field or local that was never assigned lets
the request pass; only `GateDecision.Deny(reason)` denies.

**A gate that throws** counts as a deny. The exception goes to the root's error handler
(`ViewErrorSource.Gate`), the gate type's name is in the `Detail`, and the queue reports
[UIDIRECTOR119](Troubleshooting.md#uidirector119). Later gates are not asked for that item.

Keep a gate quick and free of side effects: it runs inside the enqueue call and inside the pump.
A gate is not the place to open a view or to hold the queue; a gate that does so at presentation
gets no `ShowAsync` for that item, which waits until the layer is clear again.

A gate answers yes or no. "Not now, later" is a hold.

**Postpone instead of refuse.** A deny at presentation ends the item for good: it is not kept for
later. To get "never during a fight, but do not lose it", do not deny in a gate and re-enqueue in a
loop. Hold the queue for as long as the condition lasts, so the items stay waiting. Call
`queue.Hold("fight", fightToken)` when the fight starts, with the fight's token as owner, and call
`Remove()` on the returned registration when the fight ends; ending the token removes the hold too.

A hold does not touch a popup the queue presented before the fight started; it stays until it closes
(a screen the fight opens closes it, see
[A screen closes a presented popup](#a-screen-closes-a-presented-popup)).

Use a gate to refuse an item, such as a promotion that must not appear while a tutorial runs.

---

## Holds

A flow that needs the screen for itself holds the queue: while any hold exists, the queue presents
nothing. The tutorial in the sample takes a hold, shows its own tips directly, and removes the hold
on every way out:

<!-- source: Samples~/QueueUsage/TutorialFlow.cs -->
```csharp
IsRunning = true;
_onboarding.IsRunning = true;

// Queued items wait; the flow's own opens below are not affected by the hold.
var hold = _queue.Hold("tutorial", ct);
try
{
    for (var step = 1; step <= Tips.Length; step++)
    {
        var result = await _navigator.ShowAsync(QueueViews.TutorialTip,
            new TipArgs(Tips[step - 1], step, Tips.Length), ct,
            new OpenOptions { Source = "tutorial" });

        // Not shown, backed out, replaced or owner ended: the player left the tutorial.
        if (result.Reason != CloseReason.Dismissed && result.Reason != CloseReason.Completed)
            return;
    }
}
finally
{
    // Ending the hold lets the queue continue; while the tutorial ran, the gate denied notices below priority 100.
    hold.Remove();
    _onboarding.IsRunning = false;
    IsRunning = false;
}
```

<!-- signature -->
```csharp
public ViewRegistration Hold(
    string reason,
    CancellationToken owner,
    [CallerMemberName] string member = null,
    [CallerLineNumber] int line = 0)
public bool IsHeld { get; }
```

The compiler fills in `member` and `line`; call `Hold(reason, owner)` and leave them out.

- **Holds are counted.** Two holds need two removals; the queue resumes when the last one ends.
  A hold ends when its `ViewRegistration.Remove()` is called or its owner token ends, whichever
  comes first. Ending it again does nothing.
- **A hold does not touch views.** It stops the queue from presenting the next item. A view that is
  already presented stays, and the flow's own `Open` and `ShowAsync` calls are not affected.
- **No automatic timeout.** An automatic release would let popups through in the middle of a flow.
  A hold that is older than 60 seconds logs [UIDIRECTOR111](Troubleshooting.md#uidirector111) once,
  naming the reason you gave and the member and line that called `Hold`. Give the hold the owner of
  the flow and remove it in a `finally`, as the sample does, so no way out leaves the queue held.
  On the root's own navigator the 60 seconds run on the navigator clock, which the root advances by
  at most 0.1 s per frame; below 10 frames per second the warning comes later in wall time, and the
  age in the message is measured in unscaled time. A queue on any other `INavigator` times the
  warning with a UniTask delay instead.
- **Hold and gate together.** In the sample, the hold keeps queued notices waiting until the
  tutorial ends, and the gate turns away the ones that should not wait. While the tutorial runs, a
  request below priority 100 is turned away at once and one at 100 or above waits for the hold.
  Escape on a tip leaves the tutorial early: the tip closes with `CloseReason.Back`, the remaining
  tips are not shown and the hold is removed in the same moment.
- A `Hold` with a null reason throws `ArgumentNullException`. On a queue that has ended, or with an
  owner that has ended, it takes no hold and returns an inactive registration.

---

## A screen closes a presented popup

The `popups` layer is closed by navigation. When code opens a screen, a queued popup the navigator
has already taken is ended, and the queue does not show it again. A popup that was on screen snaps
away with `CloseReason.Navigated`. One that was still loading or opening ends as `NotShown` /
`Cancelled`. A popup the new screen opens itself survives
([Popups and backdrop](Popups-and-Backdrop.md#popups-and-screen-changes)).

If the item must be seen, read the result of `EnqueueAsync` and enqueue it again: when
`result.Reason == CloseReason.Navigated`, call `queue.Enqueue(...)` with the same key, arguments, owner
and options. An item that was still loading when the screen opened ends as `NotShown` / `Cancelled`
instead. `Cancelled` also comes from `Clear()` and from the queue's end, so do not enqueue again on it
blindly.

To keep waiting items from appearing between the steps of a screen flow, hold the queue while the
flow runs (see [Holds](#holds)). The hold does not protect the item that is already on screen.

---

## When a request is turned away

A request that fails a check in the call is never queued. It publishes a single `NotShown` event on
the queue's observers, with no `Enqueued` before it, and `EnqueueAsync` completes at once with the
same facts. The queue checks in this order:

| Check | `Rejected` | `Detail` |
|---|---|---|
| The queue has ended because its lifetime token was cancelled | `Cancelled` | The queue's lifetime ended. |
| The queue has ended because the root was destroyed (also a queue built after that) | `RootGone` | The root was destroyed. |
| The item's owner is already cancelled | `OwnerEnded` | The owner had already ended. |
| A gate denies it at enqueue, or throws | `Denied` | The gate's reason, or `gate <TypeName> threw` for a throw |

`Enqueue` still returns a request id in these cases, the one the `NotShown` event carries. After the
queue has ended it has no observers left, so that `NotShown` reaches only the `EnqueueAsync` result
(and the window's event list in the editor and in development builds).

An item that was accepted ends later, with `Enqueued` first and then one more event:

| Cause | Event after `Enqueued` | `Rejected` |
|---|---|---|
| The owner ends while the item waits | `NotShown` from the queue | `OwnerEnded` |
| A gate denies it at presentation | `NotShown` from the queue | `Denied` |
| `Clear()` is called | `NotShown` from the queue | `Cancelled` |
| The queue's lifetime ends | `NotShown` from the queue | `Cancelled` |
| The root is destroyed | `NotShown` from the queue | `RootGone`; an item owned by `RootToken` may end first as `OwnerEnded` |
| The navigator turns it away at its call (unknown key, a duplicate, a full sorting band, a row on another layer) | `NotShown` from the navigator, with no `Requested` | The navigator's reason; `WrongLayer` for a row on another layer |
| The navigator accepts it and the view then fails to load, declines or throws | `Requested`, then `NotShown`, both from the navigator | The navigator's reason |
| The call that presents the item throws, or the task it returned faults later | `NotShown` from the queue (for a task that faults later: unless the navigator already published an end for the item) | `Faulted` |

An item that the navigator ended without showing is not retried; the next item follows on the next
tick. If the navigator itself throws or faults while an item is presented, which the package's
navigator does not do for anything that happens to a view, the item ends as `NotShown` / `Faulted`
and the queue carries on. A task that faults after the call returned is handled the same way as a
throw at the call: the queue publishes its own `NotShown` on the main thread, so an observer sees
one end for the item (a navigator that is not the package's and publishes its own end before it throws
can cause a second), and then completes the `EnqueueAsync` result. The `Rejected` values
and what each means are listed in [Opening and results](Opening-and-Results.md#reject-reasons).

The Queue Usage sample is written for a denied request: when code queues a notice below priority 100
while the tutorial runs (a tip is a modal popup that covers the lobby, so its buttons cannot do it),
the Console shows no `Enqueued` line, only
`[queue] NotShown #n notice priority=10 source=startup.news rejected=Denied detail=onboarding is running`.
That is the only kind of turned-away request in the sample's Console that has no `Enqueued` before it.

---

## The waiting cap

A queue has no cap of its own. Its waiting list is not limited, so queueing many items is safe.

The waiting cap you may know from [Opening and results](Opening-and-Results.md) belongs to the
navigator: one layer keeps at most four requests waiting by default (`Waiting Cap` on the
`ViewRoot`), and the next one is refused as `Busy` with
[UIDIRECTOR112](Troubleshooting.md#uidirector112). A queue never reaches it: it presents an item
only when its layer has no request, so it hands the navigator one request at a time. That is the
reason a burst of notices belongs in a queue and not in a loop of `Open` calls.

---

## Owners end queued items

The owner token you pass to `Enqueue` decides how long the item may live.

- **While the item waits,** an owner that ends takes the item out of the queue: it ends as
  `NotShown` with `RejectReason.OwnerEnded`, and `EnqueueAsync` completes. The queue's
  `Count` drops and the next item can follow.
- **Once the item is presented,** the navigator owns the token. A shown view closes with
  `CloseReason.OwnerEnded`, and a view that is still loading is withdrawn as `NotShown` /
  `OwnerEnded`.
- **The queue's lifetime is different from an item's owner.** The queue ends when its lifetime
  token is cancelled and also when the navigator's root is destroyed. It then becomes inert:
  waiting items end as `NotShown`, every hold, gate and observer registration ends, and later calls
  do nothing harmful (`Enqueue` returns the reserved id and ends the item as `NotShown`, which only
  the `EnqueueAsync` result and the window's event list can see, as no observer is left; the
  registration calls return inactive registrations). The reason is `Cancelled` when the lifetime
  ended and `RootGone` when the root was destroyed. An item whose own owner also ends with the root
  (for example an owner of `RootToken`, as in the sample) can end as `OwnerEnded` first, and a queue
  observer whose owner is `RootToken` can already be gone when the queue ends; the package does not
  fix the order of callbacks on one token. Neither token keeps the ended queue alive: its end callbacks are released when it ends. A view that was already presented is left to
  its own owner and the navigator: that item ends once, through the navigator's result, which can
  arrive after the queue's waiting items have ended.
- `CancellationToken.None` as an owner means the item never ends by owner; only a close, the
  lifetime or `Clear()` ends it.
- An owner token cancelled on another thread is handled on the main thread and logs
  [UIDIRECTOR113](Troubleshooting.md#uidirector113); see [Threading](Threading.md).

`Clear()` ends every waiting item as `NotShown` / `Cancelled`, leaves the presented one alone, and
leaves the queue working. It is safe to call from any thread (it is posted to the main thread).

---

## Observing the queue

A queued request has two publishers. The queue publishes `Enqueued` when it accepts an item and its
own `NotShown` for an item that ends in the queue. Everything after the queue hands the item to the
navigator (`Requested`, `Shown`, `Closed`, and a `NotShown` from the navigator) is the navigator's.
The sample therefore registers one observer class twice, each time with the root's token as owner:

<!-- source: Samples~/QueueUsage/QueueBootstrap.cs -->
```csharp
// Events of a queued request come from two publishers, so a log watches each.
_navigator.AddObserver(new QueueEventLog("[views]"), _navigator.RootToken);
notices.Queue.AddObserver(new QueueEventLog("[queue]"), _navigator.RootToken);
```

`ViewQueue.AddObserver(observer, owner)` delivers only the queue's own `Enqueued` and `NotShown`
events. The events, their fields and the observer rules are on
[Events and analytics](Events-and-Analytics.md). For one queued item the full story is:

```text
[queue] Enqueued #3 notice priority=100 source=startup.maintenance
[views] Requested #3 notice priority=100 source=startup.maintenance
[views] Shown #3 notice priority=100 source=startup.maintenance
[views] Closed #3 notice priority=100 source=startup.maintenance reason=Dismissed
```

Three ways to look at the queue itself:

- **In code.** `Count` is the number of waiting items (not the presented one) and `IsHeld` is true
  while at least one hold exists.
- **As text.** `Describe(StringBuilder)` writes the queue's state, its holds with their age, the
  number of gates, the presented item and the waiting items in order:

  ```text
  queue startup layer popups active held
    hold #1 'tutorial' age 12.4s at RunAsync (line 39)
  gates: 1
  presenting: none
  waiting: 1
    #4 notice priority 100 source startup.maintenance age 12.1s
  ```

  The request ids, ages and the call site are examples. This is the output to read when
  [UIDIRECTOR111](Troubleshooting.md#uidirector111) reports a long hold: the call site names the
  member and line that called `Hold`.
- **In the editor.** `Window > UIDirector` shows a `Queue <name>` section for every queue built on
  the root's navigator, with whether it is held, its gate count, its holds with the member and line
  that took each one, the presented item and the waiting items, and an event list that mixes the
  queue's events and the navigator's. A queue that has ended is no longer listed. The window shows
  something in Play Mode only.

---

## Add a catalog at run time from a feature

A feature that is not always present can bring its own keys and its own catalog, register the
catalog when it starts, and queue its views, all with one owner. When that owner ends, the catalog
leaves and the feature's queued items go with it. `NewsFeature` in the sample does this with one
token source linked to the root's token:

<!-- source: Samples~/QueueUsage/NewsFeature.cs -->
```csharp
_owner = CancellationTokenSource.CreateLinkedTokenSource(_navigator.RootToken);
_navigator.AddCatalog(_catalog, _owner.Token);

// The same owner: if the feature goes away while the notice waits, the notice ends with it.
_queue.Enqueue(
    NewsViews.News,
    new NoticeArgs("News", "A news feature was added at run time."),
    _owner.Token,
    new QueueOptions { Priority = 50, Source = "news.added" });
```

Taking the feature away is ending that owner:

<!-- source: Samples~/QueueUsage/NewsFeature.cs -->
```csharp
// Ending the owner removes the catalog and releases its kept instances; the news popup ends too, because it was queued with this owner.
_owner.Cancel();
_owner.Dispose();
_owner = null;
```

In the running sample, "Add news feature" registers `NewsCatalog.asset` and the Console shows
`[queue] Enqueued ... news priority=50 source=news.added`; the popup appears at once. "Remove news
feature" ends the owner. If the popup were still waiting it would end as `NotShown` /
`OwnerEnded`, and if it were open it would close with `reason=OwnerEnded`. Pressing "Add news
feature" again registers a new catalog and queues a new popup.

What the catalog calls do, including what happens to views that are already open when a catalog
is removed (they stay open until their own owner ends), is on
[Catalogs and keys](Catalogs-and-Keys.md#add-and-remove-a-catalog-at-run-time). The rule for the
queue is: register the catalog before the item is presented, and give the item the feature's owner.
An item whose catalog is gone when its turn comes is turned away by the navigator as `UnknownKey`.

---

## Traps

- **Reading the answer without `HasValue`.** `EnqueueAsync` completes for an item that never
  showed, and `Value` is then the default. Read the answer only when `HasValue` is true.
- **Treating `Enqueued` as shown.** `Enqueued` means the queue accepted the item. Wait for
  `Shown`, or await `EnqueueAsync`.
- **Expecting an `Enqueued` event for every `NotShown`.** An item turned away in the call has none.
- **A hold with no way out.** A flow that takes a hold and throws before it removes it holds the queue
  until its owner ends. Use a `finally` and an owner.
- **Gates that depend on state that may be gone.** A gate that throws is a deny and logs
  [UIDIRECTOR119](Troubleshooting.md#uidirector119).
- **A queue on a layer that never clears.** A queue on `screens` waits while a screen is open. Use
  the popups layer or a layer of your own.
- **A key whose row is on another layer.** The navigator turns the item away as `WrongLayer` and
  logs [UIDIRECTOR126](Troubleshooting.md#uidirector126). A row's `Layer Id` starts as `screens`
  (or is copied from the row above it), so a reward or news row for a `popups` queue must be checked first. Enqueue each key
  on the queue of its row's layer.
- **Denying in a gate to postpone.** A deny ends the item for good. Hold the queue for as long as
  the condition lasts instead ([Holds](#holds)).
- **Expecting a queued popup to survive a screen change.** A screen that opens ends a queued
  popup the navigator has taken, and the queue does not show it again
  ([A screen closes a presented popup](#a-screen-closes-a-presented-popup)).
- **A layer the root does not have.** `new ViewQueue(...)` throws `ArgumentException`. Pass one of
  `ViewLayers` or a layer you gave the root in `ViewRootSetup.ExtraLayers`.
- **Opening on the queue's layer by hand.** A popup opened directly keeps the queue waiting
  until it closes. That is the intended way to keep an item behind a flow, but it surprises code
  that expects the queue to be independent.
- **Waiting for the queue from a thread other than the main thread.** Queueing throws there;
  see [Threading](Threading.md).

---

## See also

- [Opening and results](Opening-and-Results.md): `ShowAsync`, `OpenOptions` and the reject reasons the queue reuses
- [Events and analytics](Events-and-Analytics.md): the observer interface and every event field
- [Popups and backdrop](Popups-and-Backdrop.md): the popups layer a queue usually presents on
- [Catalogs and keys](Catalogs-and-Keys.md): adding and removing a catalog at run time
- [Threading](Threading.md): which queue calls throw or are posted off the main thread
- [Limitations](Limitations.md): a queue never interrupts a shown view; gates answer yes or no
- [Troubleshooting](Troubleshooting.md#uidirector111): UIDIRECTOR111, UIDIRECTOR112, UIDIRECTOR113, UIDIRECTOR119 and UIDIRECTOR126
- [ADR-004](Design/Decisions/ADR-004-Queue-Above-The-Navigator.md): why the queue sits above the navigator
- [Architecture](Design/Architecture.md#13-the-queue-above-the-navigator): the queue in detail
