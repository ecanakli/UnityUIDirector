# Concepts

[Back to index](index.md)

This page is the model behind every other guide: what the root and its layers are, how a prefab
becomes a view through a catalog row and a key, what a request is and how it ends, why an ending is
data and never an exception, who owns an open, and what a scope is. Read it once; each guide then
goes deep on one task.

---

## The picture

```text
Canvas                         yours: render mode, scaler, sorting layer
  ViewRoot                     one per UI; owns the navigator (INavigator)
    screens   order 0          History, TakesBack, ShieldsInput
    popups    order 1          Modal, TakesBack, ShieldsInput, ClosedByNavigation
    system    order 2          Modal, ShieldsInput
    toasts    order 3          your own layer: no rules at all
    shield                     above every layer; counted; blocks all input while it is up

view      = a prefab with a ViewHost on its root and a component that implements IView<TArgs, TResult>
row       = one line of a ViewCatalog asset: id, prefab or asset key, layer, policies
key       = ViewKey<TArgs, TResult> in code: the id plus the two types
request   = one call to Open or ShowAsync; Waiting > Loading > Opening > Open > Closing > Ended
outcome   = a ViewResult: how it ended, why it was refused, and the value when there is one
owner     = a CancellationToken you pass with every open; when it ends, the view ends
scope     = what the package ends for you: bindings and tasks of one open, or of one visible span
```

---

## One root, layers with rules

A `ViewRoot` sits on a canvas that your project owns. It builds the layers under itself, creates the
navigator and exposes it as `ViewRoot.Navigator`. It is not a singleton: each root has its own
navigator, input shield and back rule.

A layer is an immutable `ViewLayer`: an id, an order and a set of `ViewLayerRules` flags. Every root
has three:

| Layer | Order | Rules |
|---|---|---|
| `ViewLayers.Screens` (`screens`) | 0 | `History`, `TakesBack`, `ShieldsInput` |
| `ViewLayers.Popups` (`popups`) | 1 | `Modal`, `TakesBack`, `ShieldsInput`, `ClosedByNavigation` |
| `ViewLayers.System` (`system`) | 2 | `Modal`, `ShieldsInput` |

What each flag does:

| Flag | Effect |
|---|---|
| `History` | The layer is a stack and the back history. A view covers what is below it. Opening an id that is already in the stack returns to it instead of stacking a second copy; a key with arguments reopens it in place. The bottom view is the root of `Back()`. |
| `Modal` | One shared backdrop sits under the layer's top view, and everything below that view is non-interactive. |
| `TakesBack` | `Back()` may close the layer's top view. Without it a modal layer swallows back and a plain layer lets it pass. |
| `ShieldsInput` | Opens, closes and loads on this layer hold the input shield. |
| `ClosedByNavigation` | Views on this layer end when a view opens on a `History` layer. |

A project adds layers in code. The Basic Usage sample declares one above the system layer, with no
rules, so a toast never blocks a tap:

<!-- source: Samples~/BasicUsage/AppLayers.cs -->
```csharp
// Above the system layer, no history, no back and no input shield: a toast never blocks a tap.
public static readonly ViewLayer Toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);
```

The root takes it in `ViewRootSetup.ExtraLayers`; see [Catalogs and keys](Catalogs-and-Keys.md).

Two rules are easy to get wrong:

- `ClosedByNavigation` fires when a view **opens** on a history layer, never on a pop, `Back()` or
  `CloseTo`. It ends only the views and waiting requests that were **accepted earlier** than the view
  that opened. A popup that the new screen opens itself, or that is requested after the screen, stays.
  The views it ends are snapped to their hidden pose with `CloseReason.Navigated`; they get no hide
  transition.
- A tap on the backdrop closes the top view of a modal layer only when that view is fully open and its
  row sets `CloseOnBackdrop`. While the input shield is up, the tap does nothing.

The layers and their rules are the subject of [Navigation and back](Navigation-and-Back.md),
[Popups and backdrop](Popups-and-Backdrop.md) and [System overlays](System-Overlays.md).

---

## Views, rows and keys

A view is a prefab. Its root carries a `ViewHost` (which also needs a `Canvas` and a
`GraphicRaycaster`) and one component that implements `IView<TArgs, TResult>`. The easy way to write
that component is to derive from `ViewBehaviour`, `ViewBehaviour<TArgs>` or
`ViewBehaviour<TArgs, TResult>`; see [View authoring](View-Authoring.md).

A **catalog row** describes the view: an id, the prefab (or an asset key), the layer id it opens on
and its policies. Rows live in `ViewCatalog` assets that you assign to the root.

A **key** is how code names a row. It is a typed value, `ViewKey<TArgs, TResult>`, made of the row's
id plus the argument and result types the view takes. Keep them in one static class marked
`[ViewKeys]`, so the editor can check them against the catalogs:

<!-- source: Samples~/BasicUsage/AppViews.cs -->
```csharp
[ViewKeys]
public static class AppViews
{
    public static readonly ViewKey<NoArgs, NoResult> Home = ViewKey.Create("home");
    public static readonly ViewKey<NoArgs, NoResult> Library = ViewKey.Create("library");
    public static readonly ViewKey<ItemArgs, NoResult> ItemDetail = ViewKey.Create<ItemArgs>("item-detail");
    public static readonly ViewKey<NoArgs, NoResult> Settings = ViewKey.Create("settings");
    public static readonly ViewKey<ConfirmArgs, bool> Confirm = ViewKey.Create<ConfirmArgs, bool>("confirm");
    public static readonly ViewKey<RenameArgs, string> Rename = ViewKey.Create<RenameArgs, string>("rename");
    public static readonly ViewKey<NoArgs, NoResult> Saving = ViewKey.Create("saving");
    public static readonly ViewKey<ToastArgs, NoResult> Toast = ViewKey.Create<ToastArgs>("toast");
}
```

`Confirm` takes a `ConfirmArgs` and answers with a `bool`. `Home` takes nothing and answers nothing,
which is what `NoArgs` and `NoResult` say. The compiler therefore checks what you pass in and what you
read out. Rows, keys and the validator are in [Catalogs and keys](Catalogs-and-Keys.md).

---

## A request, start to finish

Every `Open` or `ShowAsync` call is one **request**. The navigator decides at the call, synchronously,
whether to accept it. An accepted request has a request id and passes through six phases, each at most
once and never backwards:

| Phase | Meaning |
|---|---|
| `Waiting` | Accepted and in its layer's list; the layer's runner has not reached it. |
| `Loading` | The runner is getting the instance (a kept one, a direct prefab, or a provider load). |
| `Opening` | `OnOpen` returned and the show transition runs. |
| `Open` | Fully on screen. The view is interactive when nothing covers it. |
| `Closing` | The hide transition runs. |
| `Ended` | Finished. Its handle is inert and every awaiter has completed. |

Each layer runs one operation at a time, in the order requests were accepted, and a started step is
never interrupted. Two requests on one layer therefore never race; the second waits. A close asked
for while a view is still opening is remembered and runs when the open finishes. Only a terminate
cause (the owner ending, the view being destroyed or deactivated from outside, the root being
destroyed) or the transition timeout cut a transition short, and both snap the view. A load is also
cut short when its request is withdrawn (`Close()`, `Back()`, `CloseAll`, a screen change, or a
`CloseTo`, a return or a reopen in place that pops past it) or when the load timeout passes. See [Opening and results](Opening-and-Results.md).

A request ends exactly once, with exactly one outcome. The first cause that ends it wins and later
causes are ignored.

---

## Outcomes are data

`ShowAsync` completes with a `ViewResult<TResult>` in every case. It does not throw for anything that
happens to the view: a refusal, a failed load, the owner ending or the root being destroyed all arrive
as values.

| Field | Meaning |
|---|---|
| `WasShown` | The view reached the screen before it ended. |
| `Reason` | A `CloseReason`: `Completed`, `Dismissed`, `Back`, `Backdrop`, `Code`, `Navigated`, `OwnerEnded`, `Destroyed`, or `NotShown`. |
| `Rejected` | A `RejectReason` when it was never shown: `UnknownKey`, `Duplicate`, `Busy`, `Declined`, `OwnerEnded`, and others; `None` when it was shown. |
| `HasValue`, `Value` | The view's answer. `HasValue` is true only when the view was shown and closed itself with `Complete(value)`. |

A call site has one shape: await, then check `HasValue` or switch on `Reason`. The full tables and the
traps are in [Opening and results](Opening-and-Results.md); the reasoning is in
[ADR-001](Design/Decisions/ADR-001-Outcomes-As-Data.md).

Exceptions are for usage errors only, raised at the call: a call off the main thread, a null argument,
an empty or whitespace-only view id, or a result factory given a combination that is not an outcome.
See [Threading](Threading.md).

---

## Every open has an owner

`Open` and `ShowAsync` take a `CancellationToken` as a required argument. That token is the **owner**
of the request. When it is cancelled, the request ends: a shown view is snapped away and reports
`CloseReason.OwnerEnded`, a waiting one is withdrawn and reports `NotShown` with `RejectReason.OwnerEnded`.
An owner that is already cancelled at the call ends the request at once, before anything is shown.

Pick the owner by asking what the view should not outlive:

| You want the view to end when | Pass |
|---|---|
| the UI as a whole goes away | `Navigator.RootToken`, cancelled when the root is destroyed |
| the screen that opened it ends | that screen's open-scope token, or a token it owns |
| a feature you control ends | a token source you cancel, linked to `RootToken` if you like |
| only someone closes it, or the root goes away | `CancellationToken.None` |

Do not pass the open-scope token of a screen to a view that replaces or pops that screen. A reopen in
place of the screen, or a pop that removes it, ends that scope, and the new request ends with it as
not shown. Pass `RootToken` there; the paragraph on reopening in
[Navigation and back](Navigation-and-Back.md#return-to-a-screen-that-is-already-open) has the details.

An owner decides the life of the **view**. It never cancels an awaiter: code that awaits
`ShowAsync` always resumes with a result, even when the owner ended first.

**A join is not bound to the token you pass.** A call that returns to a screen already on a history
layer (a key without arguments) joins that request and keeps its owner; the token you pass is
ignored. If your token ends, the request does not, and your `await` goes on until the request ends. To
stop waiting at a token of your own, wrap the call as shown in
[Opening and results](Opening-and-Results.md#stopping-the-wait-at-your-own-token).

Registrations such as a
catalog, an observer or an input hold take an owner too and end with it; nothing needs a removal
line. A feature that brings its own catalog shows the pattern: a token source linked to the root's
token, handed to every registration the feature makes:

<!-- source: Samples~/QueueUsage/NewsFeature.cs -->
```csharp
_owner = CancellationTokenSource.CreateLinkedTokenSource(_navigator.RootToken);
_navigator.AddCatalog(_catalog, _owner.Token);
```

See [Catalogs and keys](Catalogs-and-Keys.md) for what ending that owner does and
[ADR-005](Design/Decisions/ADR-005-Token-Owners.md) for why the owner is a token.

---

## Scopes end what a view started

A view that subscribes to a button, starts a task or holds a disposable must undo that when it
closes. UIDirector gives the view no close hook for that. It hands the view a `ViewScope` and ends
the scope for you on every path.

| Scope | Created | Ends |
|---|---|---|
| Open scope | When the request is accepted; handed to `OnOpen` | When the request ends, however it ends: closed, withdrawn before it was shown, declined, owner ended, lost from outside, root destroyed |
| Visible scope | Each time the view comes on screen and is not covered; handed to `OnVisible` | When the view is covered, or when the request ends |

On a scope you `Bind` a `UnityEvent`, `Subscribe` to any other event, `Add` a disposable, register an
`OnEnd` action or `Run` a task. The package removes, disposes or cancels each one when the scope
ends, newest first. A scope that has already ended is inert: registering on it does the cleanup at
once and adds nothing. A scope reference kept after the view closed is therefore harmless, and
`ViewContext.Open` after the end returns that ended scope, never `null`.

[View authoring](View-Authoring.md) covers the hooks in full, and
[ADR-003](Design/Decisions/ADR-003-Begin-Only-Hooks-And-Scopes.md) explains why there are no close
hooks.

---

## Where to go next

| You want to | Read |
|---|---|
| Make a catalog, write keys, add a catalog at run time | [Catalogs and keys](Catalogs-and-Keys.md) |
| Open a view, read its result, close several | [Opening and results](Opening-and-Results.md) |
| Understand Back, the history stack and returning to a screen | [Navigation and back](Navigation-and-Back.md) |
| Show a modal popup with a backdrop | [Popups and backdrop](Popups-and-Backdrop.md) |
| Block input with an overlay | [System overlays](System-Overlays.md) |
| Write a view | [View authoring](View-Authoring.md) |
| Animate a show or hide | [Transitions](Transitions.md), [DOTween](DOTween.md) |
| Present popups one at a time by priority | [Queue](Queue.md) |
| Observe what the navigator does | [Events and analytics](Events-and-Analytics.md) |
| Load views by asset key | [Assets and Addressables](Assets-and-Addressables.md) |
| Inject services into views | [Zenject](Zenject.md) |
| Set up the canvas, safe area and input | [Root canvas](Root-Canvas.md), [Input and platform](Input-and-Platform.md) |
| Find out what a log message means | [Diagnostics](Diagnostics.md), [Troubleshooting](Troubleshooting.md) |
| Check cost and limits | [Performance](Performance.md), [Threading](Threading.md), [Limitations](Limitations.md) |
| Look up a type | [API reference](API-Reference.md) |

---

## See also

- [Opening and results](Opening-and-Results.md): requests, handles, results and owners in detail
- [Catalogs and keys](Catalogs-and-Keys.md): rows, keys and the validator
- [FAQ](FAQ.md): short answers to the questions that come first
- [Migration](Migration.md): moving from ad-hoc screen scripts
- [Architecture](Design/Architecture.md): why the package is shaped this way
