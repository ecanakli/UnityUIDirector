# Events and analytics

[Back to index](index.md)

The navigator reports what happens to every request as events: accepted, shown, closed, not shown,
and the moment it settles. One observer interface receives all of them. Use it to log, to feed a
debug overlay, or to send screen views and funnels to an analytics service of your own. Events are
facts in the past tense; no command travels as an event, and they carry no view arguments, so
nothing is boxed and no argument data leaks into your analytics.

This page covers adding an observer, every event kind and when it is published, events caused inside
an observer, the fields of an event, the order at close, observing a queue as well, a generic analytics adapter, and what
dispatch costs.

---

## Watch every event

Add an observer with an owner token. The Basic Usage sample adds a log with the root's token as
owner, so it ends with the root:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
// The observer ends with the root: RootToken is cancelled when the root is destroyed.
_navigator.AddObserver(new ViewEventLog(), _navigator.RootToken);
```

The observer prints one line per event:

<!-- source: Samples~/BasicUsage/ViewEventLog.cs -->
```csharp
public sealed class ViewEventLog : IViewObserver
{
    public void OnViewEvent(in ViewEvent viewEvent)
    {
        // Settled has no request and no view.
        if (viewEvent.Kind == ViewEventKind.Settled)
        {
            Debug.Log("[views] Settled");
            return;
        }

        var line = $"[views] {viewEvent.Kind} #{viewEvent.RequestId} view={viewEvent.ViewId} layer={viewEvent.LayerId} source={viewEvent.Source}";

        switch (viewEvent.Kind)
        {
            case ViewEventKind.Shown:
                line += $" waited={viewEvent.SecondsWaiting:0.00}s";
                break;
            case ViewEventKind.Closed:
                line += $" reason={viewEvent.Reason} open={viewEvent.SecondsOpen:0.00}s";
                break;
            case ViewEventKind.NotShown:
                line += $" rejected={viewEvent.Rejected} detail={viewEvent.Detail}";
                break;
        }

        Debug.Log(line);
    }
}
```

Add the observer before the first `Open`. Events are published inside the navigator's own calls and
nothing is replayed, so an observer added later misses what already happened. Two safe places: right after
`Initialize`, as the sample does (it adds the observer in `Awake` and opens the first view in `Start`),
or, when the root initializes itself on `Awake`, from a component's `Start`, provided no other script
opens a view from its own `Awake` or `Start`. Unity orders neither `Awake` nor `Start` across objects, so
an open made in another script's `Awake` or `Start` can come before your observer. Make the first open
after the observer is added, from the same bootstrap or later.

Play the sample and the Console reads `[views] <Kind> #<request> view=<id> layer=<layer> source=<tag>`.
`Shown` adds `waited=`, `Closed` adds `reason=` and `open=`, `NotShown` adds `rejected=` and
`detail=`, and `Settled` has no request and no view. The `source` tags (`home.library`,
`settings.reset`, `back.root` and so on) say which line of code asked.

---

## The observer interface

<!-- signature -->
```csharp
public interface IViewObserver
{
    void OnViewEvent(in ViewEvent viewEvent);
}
```

<!-- signature -->
```csharp
ViewRegistration AddObserver(
    IViewObserver observer,
    CancellationToken owner,
    [CallerMemberName] string member = null,
    [CallerLineNumber] int line = 0);
```

- **Owner.** The observer receives events until the owner token ends or the returned
  `ViewRegistration` is removed with `Remove()`, whichever comes first. Ending it again does nothing.
  Your code needs no removal line. An owner that is already cancelled adds nothing and returns an
  inactive registration.
- **Thread.** `AddObserver` throws `InvalidOperationException` off the main thread, and a null
  observer throws `ArgumentNullException`. Events are delivered on the main thread, from inside the
  navigator's own call. See [Threading](Threading.md).
- **Order.** Observers are called in the order they were added.
- **Isolation.** An observer that throws is routed to the root's error handler as
  `ViewErrorSource.Observer`, with the view id and request id of the event, and the observers after it
  still run. A throwing observer never changes what happens to the view.
- **Adding and removing during a dispatch.** An observer added while an event is being delivered
  receives events from the next one. A removal takes effect at once, so a removed observer is not
  called for the event in progress.
- **Navigator calls from an observer are legal.** An observer may call `Open`, `Close` or `Back`. They are
  accepted or refused as at any other call site; the outcome follows the usual rules. The events such a
  call causes are held back; see [Events caused inside an observer](#events-caused-inside-an-observer).
- **The event is passed by `in`.** It is a readonly struct on the dispatcher's stack. Copy the
  fields you need; do not keep a reference to it.

---

## Every event kind

| `ViewEventKind` | Published by | When |
|---|---|---|
| `Enqueued` | A queue | The queue accepted the item. The queue's observers only. |
| `Requested` | The navigator | The request was accepted. It is already in its layer, and the input shield is already up when the layer shields input. |
| `Shown` | The navigator | The show transition ended and the view is open. |
| `Closed` | The navigator | The view ended after being shown. Its instance was released, or parked for reuse when the row says `KeepInstance`. |
| `NotShown` | The navigator, or a queue for an item that never reached it | The request ended without being shown, or was refused at the call. |
| `Settled` | The navigator | The navigator went from busy to settled. The request id is 0. |

The sequence for one request id is one of these:

- `NotShown`: refused at the call (unknown key, duplicate, owner already ended, the waiting cap, or
  `WrongLayer` for a queued key whose row is on another layer).
- `Requested`, `NotShown`: accepted, then ended before it was shown (the load failed, the view
  declined, the owner ended, it was withdrawn).
- `Requested`, `Shown`, `Closed`: shown, then closed.

A request that came from a queue has `Enqueued` in front, published by the queue. An item that never
reaches the navigator ends with the queue's own `NotShown` and has no `Requested`. See
[Observing the queue too](#observing-the-queue-too).

`Settled` is published once for each change from busy to settled, after the events of the work that
ended it. The navigator is busy while an operation runs or pends on any layer or an input hold
exists. It is not published while the root is being torn down; at teardown every request that is
still alive receives its terminal event, and `Settled` does not follow. When the root is destroyed while the navigator is delivering a
notification, observers do not receive those terminal events either; see
[Events caused inside an observer](#events-caused-inside-an-observer).

An event describes state that is already in place when it arrives. At `Requested` the request is
in its layer and, on a layer that shields input, the shield is up. At `Shown` the view is open and the open's shield hold is
released. At `Closed` the request has left its layer, the shield holds are released and the
instance has been released, or parked when the row says `KeepInstance`. The one exception is an event
that was held back because it was caused inside an observer callback, described next.

An observer that frees per-instance resources must not do it on `Closed` for a `KeepInstance` row:
the parked instance is the object the next open reuses.

---

## Events caused inside an observer

An observer may call the navigator from `OnViewEvent`, and that call may cause events of its own: the
`Requested` or `NotShown` of a new request, a `Shown`, the `Closed` of a view it closed. While an
observer dispatch is running on the navigator, those events are held back. They are delivered, in the
order they were caused, after the event in progress has reached every observer. So each event reaches
every observer before any event that one of them caused. Outside an observer callback
nothing is held back and the timing is unchanged.

What this means for code:

- The call itself is not held back. The navigator accepts or refuses it and changes its state as it
  does anywhere else; only the notifications for it (the scopes ending, `Closed` or `NotShown`, the
  `ShowAsync` awaiters, `Settled`) wait until the dispatch is over.
- A held-back `Requested` or `Shown` can describe a state that has since moved on. If an observer
  opens view B from a callback and then closes it in the same callback, the observers see `Requested`
  for B after B has already been withdrawn; the `NotShown` for B follows it.
- Order is kept for every request: an observer never sees `Closed` before `Shown`, or a terminal event
  before `Requested`.
- An observer added with `ViewQueue.AddObserver` receives the queue's own events directly; its callbacks do not hold
  anything back by themselves.
- If the root is destroyed while the navigator is delivering a notification (inside an observer
  callback, a `ShowAsync` continuation or a view hook, for example with `DestroyImmediate`), the
  observers are removed before the pending terminal events are delivered, so no observer and no entry
  of the event list in the window sees them. The awaiters of those requests still complete, with the result that says the root was
  destroyed.

---

## The fields of an event

| Field | Type | What it holds |
|---|---|---|
| `Kind` | `ViewEventKind` | The kind of fact. |
| `RequestId` | `int` | The id of the request, the same on every event of one request. A queue reserves it at enqueue, so one id follows an item from `Enqueued` to `Closed`. 0 for `Settled`. |
| `ViewId` | `string` | The view id. `null` for `Settled`. |
| `LayerId` | `string` | The id of the layer the view is on. `null` for `Settled`, and `null` for a request refused before its row was looked up (the root is gone, the owner already ended, an unknown id or layer). A queued key refused as `WrongLayer` carries the queue's layer id. |
| `Source` | `string` | The free tag from `OpenOptions.Source` or `QueueOptions.Source`. `null` when none was given: a `ViewButton` with the Open View action passes no options, so its `Requested`, `Shown` and `Closed` carry `null`. |
| `Priority` | `int` | The queue priority of the item. 0 for a request that did not come from a queue. A queued item carries its priority on its navigator events as well. |
| `Reason` | `CloseReason` | How the view closed. Set on `Closed`, and `CloseReason.NotShown` on `NotShown`. |
| `Rejected` | `RejectReason` | Why the request was not shown. Set on `NotShown`; `None` on every other kind. |
| `Detail` | `string` | Optional text with more detail about the outcome, such as a gate's reason or a decline reason. |
| `SecondsWaiting` | `float` | Seconds from acceptance to `Shown`. Set on `Shown`; 0 on every other kind. |
| `SecondsOpen` | `float` | Seconds from `Shown` to `Closed`. Set on `Closed`; 0 on every other kind. |

How the values are measured:

- **`SecondsWaiting` includes the show transition**, because `Shown` is published when the transition
  ends. It starts when the navigator accepts the request. For a queued item that is when the queue
  presents it, so the time spent waiting in the queue is not in it. To measure that, note the clock
  when you see `Enqueued` for the request id and subtract when `Requested` arrives.
- **`SecondsOpen` starts at `Shown`**, not when the show transition starts.
- **Both run on the navigator clock**, which the root advances once per frame in unscaled time and by
  at most 0.1 s per frame. A long hitch or a suspension therefore adds one short step, not the time
  that really passed; the figures can be shorter than wall-clock time. See [Limitations](Limitations.md).

Traps in the fields:

- `Reason` has a value on every event, because an enum has a default. On `Enqueued`, `Requested`
  and `Shown` it reads `CloseReason.NotShown`, which is the enum's zero. Read `Reason` only on
  `Closed` and `NotShown`.
- A request that ended during its show transition counts as shown. It publishes `Closed` with the
  reason it ended with, not `NotShown`.

The `CloseReason` and `RejectReason` values are listed in
[Opening and results](Opening-and-Results.md#close-reasons).

---

## The order at close

An observer never sees `Closed` without `Shown`. A request that ends after its show transition began
but before `Shown` went out publishes `Shown` immediately before `Closed`, in the same sequence.

For one view that ends, the package first applies the state with no user code: the request leaves its
layer, its hidden state is applied, its shield holds are released and the layers below are
recomputed. Then it runs the notifications one at a time, each isolated, in this order:

1. The visible scope ends.
2. The open scope ends.
3. The instance is released: handed to the provider, destroyed, or parked for `KeepInstance`.
4. Views that were revealed begin their visible scopes (`OnVisible`).
5. `Shown`, if it is owed.
6. `Closed` or `NotShown` is published.
7. The `ShowAsync` awaiters resume, in the order they joined, then the `WaitForCloseAsync` awaiters.
8. `Settled`, if the navigator is now settled.

A notification caused by another notification runs right after it. What this means for code:

- When an observer sees `Closed`, the view's scopes have already ended and its instance is released,
  or parked when the row says `KeepInstance`.
- The code that awaited `ShowAsync` resumes after `Closed` was published, so an observer sees the
  event first.
- A queue pumps on the next tick, so the continuation of an awaited popup runs before the queue
  presents the next item. A follow-up popup that the continuation opens makes the layer busy and the
  queued item waits for it. See [Queue](Queue.md).

---

## Observing the queue too

A queued request has two publishers. The queue publishes `Enqueued`, and its own `NotShown` for an
item that ends in the queue. The navigator publishes everything after the queue hands the item over.
A queue sends its events only to the observers added with `ViewQueue.AddObserver`, and a navigator
observer never sees `Enqueued` or a queue's own `NotShown`. To see the whole story of an item,
register an observer on both. The Queue Usage sample uses one class and a tag:

<!-- source: Samples~/QueueUsage/QueueEventLog.cs -->
```csharp
public sealed class QueueEventLog : IViewObserver
{
    private readonly string _tag;

    // A queue observer sees only Enqueued and the queue's own NotShown; everything after presentation is a navigator event.
    public QueueEventLog(string tag)
    {
        _tag = tag;
    }

    public void OnViewEvent(in ViewEvent viewEvent)
    {
        var line = $"{_tag} {viewEvent.Kind} #{viewEvent.RequestId} {viewEvent.ViewId} priority={viewEvent.Priority} source={viewEvent.Source}";

        if (viewEvent.Kind == ViewEventKind.NotShown)
            line += $" rejected={viewEvent.Rejected} detail={viewEvent.Detail}";
        else if (viewEvent.Kind == ViewEventKind.Closed)
            line += $" reason={viewEvent.Reason}";

        Debug.Log(line);
    }
}
```

<!-- source: Samples~/QueueUsage/QueueBootstrap.cs -->
```csharp
// Events of a queued request come from two publishers, so a log watches each.
_navigator.AddObserver(new QueueEventLog("[views]"), _navigator.RootToken);
notices.Queue.AddObserver(new QueueEventLog("[queue]"), _navigator.RootToken);
```

What each publisher sends for a queued item:

| Moment | Publisher | Event |
|---|---|---|
| The queue accepts the item | queue | `Enqueued` |
| A gate denies it at enqueue, the owner already ended, or the queue ended (lifetime `Cancelled`, root `RootGone`) | queue | `NotShown` only; no `Enqueued`. After the queue ended no observer is left, so only the `EnqueueAsync` result and the window's list see it |
| It ends while it waits (owner, gate at presentation, `Clear()`, lifetime, root destroyed; an observer or item owned by `RootToken` may end first) | queue | `NotShown` |
| The navigator accepts it | navigator | `Requested` |
| It is shown and then closed | navigator | `Shown`, `Closed` |
| The navigator refuses it at the call (including `WrongLayer`), or it ends before being shown | navigator | `NotShown` (the queue publishes nothing more) |
| The call that presents it throws, or the task it returned faults and the navigator published no end | queue | `NotShown` with `Rejected = Faulted`, published on the main thread |

A `NotShown` exists once per request when the package's navigator presents the item; for a task that
faults after the navigator already ended the request, the queue publishes nothing more. In
the sample, a request that a gate denies is the only kind that shows `NotShown` without a preceding
`Enqueued`; it appears only when code queues a notice while the tutorial runs, because the tutorial
tip covers the lobby buttons. The fields are the same; `SecondsWaiting` and `SecondsOpen` are 0 on the queue's events.

In the editor and in development builds the navigator keeps the last 100 events in a ring, queue
events included, and `Window > UIDirector` lists them. Release builds have no ring.

---

## Send the events to an analytics service

The package ships no analytics adapter and names no service. An adapter is a class that implements
`IViewObserver` and turns an event into a call to whatever your project uses. `ViewEventLog` above is
the pattern: a `switch` on `Kind`, with the fields that matter for each kind.

A reasonable mapping:

| Event | What to record | Fields |
|---|---|---|
| `Shown` | A screen or popup was viewed | `ViewId`, `LayerId`, `Source`, `SecondsWaiting` |
| `Closed` | How long it was open and how it ended | `ViewId`, `Reason`, `SecondsOpen` |
| `NotShown` | A view that should have appeared did not | `ViewId`, `Rejected`, `Source` |
| `Enqueued` | An item entered a queue | `ViewId`, `Priority`, `Source` |
| `Requested` | The start of a funnel step | `RequestId`, `ViewId` |

`Settled` carries nothing worth sending.

`Shown` fires once per request. A screen revealed again by Back, `CloseTo` or by opening an id that is
already in the stack publishes no event. To count those views, record them from the covering view's
`Closed` or from `OnVisible` in the view.

Rules for an adapter:

- **Copy, do not keep.** The event arrives by `in`. Read the fields inside `OnViewEvent` and hand
  strings and numbers on. Do not store the struct or a reference to it.
- **Pair by `RequestId`.** The same id appears on `Requested`, `Shown` and `Closed` (and on
  `Enqueued` for a queued item), so a funnel or a duration is a lookup on the id. `Closed`
  already carries `SecondsOpen`, so a session timer is not needed for it.
- **Send enums as names, keep `Detail` out.** `Rejected` and `Reason` are stable enum values with a
  small set of names, which suits an analytics dimension. `Detail` is free text: gate reasons and
  decline reasons are written by your code, and a load failure carries an exception message. Keep it
  for logs.
- **Expect a `null` `Source`.** The package adds no tag of its own. A `ViewButton` opens with no tag,
  and `Back()` adds no tag of its own: it closes the top view, which publishes `Closed` with `Reason`
  `Back`, or it withdraws a request still waiting or loading, which publishes `NotShown` with `Rejected`
  `Cancelled`. Either event carries the `Source` of the call that opened that view. A view whose row says
  `HandledByView` decides in its own back hook, and any view that hook opens carries the tag the hook
  passes. A screen that Back reveals publishes no event. Map `null` to
  a fixed value such as `"untagged"` in the adapter.
- **Keep `Source` constant.** It is a free tag; use one constant string per call site so the number
  of distinct values stays small.
- **Do not wait.** Events are delivered on the main thread inside the navigator's call. Queue the
  record for your service and return. A slow observer delays the view that caused the event; an
  exception is isolated, but the delay is not.
- **Arguments are not in the event.** By design. If an analytics record needs a value, such as which
  item a detail screen showed, the code that opens the view knows it and can record it.

The adapter needs no removal line: give it `INavigator.RootToken`, or the token of the feature that
owns the analytics, as the owner.

---

## What dispatch costs

- **Dispatch allocates nothing once warm.** The event is a struct built on the stack and handed on by
  `in`; delivering it to any number of observers allocates nothing.
- **Your observer's work is the cost.** `ViewEventLog` builds a string for every event with string
  interpolation and writes it to the Console, so it allocates; it is a debug aid, not a production
  pattern. An adapter that copies a few fields into a preallocated buffer costs almost nothing.
- **Events are built whether or not anyone listens.** With no observer the dispatch is an empty loop.
- **The ring is development only.** The editor and development builds keep the last 100 events for the
  window; release builds have no ring and no recording cost.
- `INavigator.Describe(StringBuilder)` writes the current layers, requests and holds as text on any
  build, which helps when you need a snapshot instead of a stream.

Opening and closing a view is not a zero-allocation path; see [Performance](Performance.md).

---

## Traps

- **Expecting `Enqueued` to mean shown.** It means the queue accepted the item.
- **Reading `Reason` on `Shown`.** It reads `NotShown`, the default; the field is meaningful on
  `Closed` and `NotShown` only.
- **Observing only the navigator for a queued flow.** `Enqueued` and the queue's own `NotShown` never
  reach it.
- **Doing slow work in `OnViewEvent`.** It runs in the navigator's own call, on the main thread.
- **Expecting the answer to a navigator call at once, inside `OnViewEvent`.** The events and awaiters of
  a call made from a callback arrive after the dispatch; read the state through `INavigator` if you need it now.
- **Freeing per-instance data on `Closed` of a `KeepInstance` view.** The next open can reuse the
  same object.
- **Keeping the event after the call.** Copy fields; the event is not yours to keep.
- **Counting `Settled` as a request.** It has request id 0 and no view.

---

## See also

- [Queue](Queue.md): the queue's own observer and the order of events for an item
- [Opening and results](Opening-and-Results.md): `OpenOptions.Source`, `CloseReason` and `RejectReason`
- [Threading](Threading.md): the main-thread rules for observers and their owners
- [Diagnostics](Diagnostics.md): the log messages the package writes beside the events
- [Performance](Performance.md): allocation and idle cost
- [Architecture](Design/Architecture.md#10-events-and-the-order-at-close): the stream and the order at close
- [API reference](API-Reference.md): `IViewObserver`, `ViewEvent`, `ViewEventKind`
