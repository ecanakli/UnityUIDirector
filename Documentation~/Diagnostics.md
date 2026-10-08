# Diagnostics

[Back to index](index.md)

This page is about how UIDirector tells you what it is doing and what went wrong: the console lines
with their stable ids, the `Window > UIDirector` window, the text read-out you can print from code,
the error handler for exceptions thrown by your own code, and the catalog validator that you can run
from the menu, from the catalog's Inspector and from a build pipeline. The meaning of each id, with
its cause and its fix, is in [Troubleshooting](Troubleshooting.md).

---

## The console line

Every problem the package can name is logged as one line:

```text
UIDIRECTOR101: No catalog row has the id 'shop'. (see Troubleshooting#uidirector101)
UIDIRECTOR112: Layer 'popups' already has 4 requests waiting. (see Troubleshooting#uidirector112)
```

The format is `UIDIRECTOR1xx: <message> (see Troubleshooting#uidirector1xx)`. The id comes first, so
typing `UIDIRECTOR` in the Console search box finds every line, and typing a full id finds one kind.
The message names the view id, the layer or the request involved. The tail names the page and the
anchor: the section for that id is `## UIDIRECTOR1xx` in [Troubleshooting](Troubleshooting.md), and
the anchor is the id in lower case. An id never changes meaning and is never reused.

The tail in the console is text, not a clickable link. The links that open a page are in the editor:

| Where | What it opens |
|---|---|
| The `?` button on the `Window > UIDirector` toolbar | The documentation index |
| The `?` button next to a finding in a catalog's Inspector | The Troubleshooting section of that finding's id |
| `Tools > UIDirector > Documentation` | The documentation index |

These links follow the installed version. The editor reads the version of the installed package and
opens the documentation at the tag `v<version>` of the package's repository, so the page you read
matches the code in your project. When the version cannot be read, the link falls back to the `main`
branch.

### Which builds log

| Output | Editor | Development build | Release build |
|---|---|---|---|
| Console lines from the running package: `UIDIRECTOR101` to `UIDIRECTOR120`, `UIDIRECTOR125` and `UIDIRECTOR126` | Yes | Yes | No |
| Console lines from the validator: `UIDIRECTOR121` to `UIDIRECTOR125`, and its findings for 101, 102, 103, 108, 109 and 120 | When you run the validator | No | No |
| Exceptions from your code: the error handler, or `Debug.LogException` when none is set | Yes | Yes | Yes |

A release build logs no id line at all: the code that writes the line is compiled out of it. The
outcome still reaches your code: an open that was refused or
failed ends with a `ViewResult` that carries a `Rejected` reason and a `Detail` text, and that is
the way to learn about it in a release build. The Addressables sample reads it like this:

<!-- source: Samples~/AddressablesUsage/AddressablesBootstrap.cs -->
```csharp
// A gallery that was shown and later closed is not a failure.
if (result.Rejected == RejectReason.None)
    return;

Debug.LogWarning(
    $"The gallery did not open ({result.Rejected}): {result.Detail}. " +
    "Mark Prefabs/Gallery and Prefabs/Photo as addressable, as the sample README describes, " +
    "and create the Addressables settings if the project has none.");
```

Three other things are written outside the id lines and are not gated by the build type: the
exceptions described under [The error handler](#the-error-handler), a plain warning from
`BackInput` or `ViewButton` when it cannot find the root or the view it needs, when a `ViewButton` has a blank view id, or when a `ViewButton`
is clicked before its root is initialised (no id, and it names the object), and `Debug.LogException` for a fault inside the package itself. The last is not
expected to happen; if you see one, it is a defect to report.

### Severity and where each id comes from

An id is logged as an error (`Debug.LogError`) or a warning (`Debug.LogWarning`). The table gives
the severity the code uses. Ids 101 to 120 and 126 come from the running package, 121 to 124 from the
validator, and 125 from both: the validator reports it for a catalog asset, and the root reports it
when it registers a catalog with such a row.

| Id | Severity | Meaning in one line |
|---|---|---|
| [UIDIRECTOR101](Troubleshooting.md#uidirector101) | Error | No catalog row has the requested id |
| [UIDIRECTOR102](Troubleshooting.md#uidirector102) | Error | The prefab has no view for the key's argument and result types |
| [UIDIRECTOR103](Troubleshooting.md#uidirector103) | Error | The prefab root has no `ViewHost` |
| [UIDIRECTOR104](Troubleshooting.md#uidirector104) | Error | The view could not be created |
| [UIDIRECTOR105](Troubleshooting.md#uidirector105) | Error | The load took longer than the load timeout |
| [UIDIRECTOR106](Troubleshooting.md#uidirector106) | Warning | A transition timed out and was snapped |
| [UIDIRECTOR107](Troubleshooting.md#uidirector107) | Warning | An open view was destroyed or deactivated outside the package |
| [UIDIRECTOR108](Troubleshooting.md#uidirector108) | Error | A second row with an existing id was refused |
| [UIDIRECTOR109](Troubleshooting.md#uidirector109) | Error | A row names a layer that does not exist |
| [UIDIRECTOR110](Troubleshooting.md#uidirector110) | Warning | An input hold is older than 30 seconds |
| [UIDIRECTOR111](Troubleshooting.md#uidirector111) | Warning | A queue hold is older than 60 seconds |
| [UIDIRECTOR112](Troubleshooting.md#uidirector112) | Warning | A layer reached its waiting cap |
| [UIDIRECTOR113](Troubleshooting.md#uidirector113) | Warning | An owner token was cancelled off the main thread |
| [UIDIRECTOR114](Troubleshooting.md#uidirector114) | Warning | A `Screen Space - Camera` root has lost its camera |
| [UIDIRECTOR115](Troubleshooting.md#uidirector115) | Error | The sorting band is exhausted |
| [UIDIRECTOR116](Troubleshooting.md#uidirector116) | Warning | There is no `EventSystem` in the scene |
| [UIDIRECTOR117](Troubleshooting.md#uidirector117) | Error | `OnOpen` threw |
| [UIDIRECTOR118](Troubleshooting.md#uidirector118) | Warning | `Complete` or `Close` was called inside `OnOpen` |
| [UIDIRECTOR119](Troubleshooting.md#uidirector119) | Error | A queue gate threw |
| [UIDIRECTOR120](Troubleshooting.md#uidirector120) | Error | A row has an asset key and the root has no view provider |
| [UIDIRECTOR121](Troubleshooting.md#uidirector121) | Error | A row has neither a prefab nor an asset key |
| [UIDIRECTOR122](Troubleshooting.md#uidirector122) | Warning | `BackPolicy.Close` on a layer that does not take back |
| [UIDIRECTOR123](Troubleshooting.md#uidirector123) | Warning | A view prefab contains a dropdown |
| [UIDIRECTOR124](Troubleshooting.md#uidirector124) | Warning | An asset key matches no Addressables address, GUID or label |
| [UIDIRECTOR125](Troubleshooting.md#uidirector125) | Error | A row has an empty or whitespace-only id |
| [UIDIRECTOR126](Troubleshooting.md#uidirector126) | Error | A queue presented a key whose row is on another layer |

The validator also reports ids 101, 102, 103, 108, 109 and 120 from the catalogs, before the game
runs. It reports 120 as a warning, because a root can still get a custom view provider that the
validator cannot see.

---

## Window > UIDirector

The window shows the state of one running root. It reads the root's navigator, so it has something
to show **in Play Mode only**. Outside Play Mode it shows a one-line message and nothing else.

Open it from `Window > UIDirector`. The Basic Usage and Queue Usage samples both suggest leaving it
open next to the Game view while you press the buttons.

### What the window shows

From top to bottom:

| Part | What it shows |
|---|---|
| Root dropdown | Every `ViewRoot` in the loaded scenes, as `1: Name (Scene)`. The selection follows the object, so it survives the list changing. |
| Status line | `Settled, shield 0` or `Busy, shield 2`: whether any layer is running or waiting, and how many holders the input shield has. `Root gone` after the root was destroyed. |
| One section per layer | A header such as `screens: 2 requests, 0 operations, idle`, then one line per request: `#1 home Open slot 1`. A request whose close is decided but not yet run adds `close pending: Back` (the reason). |
| Shield holders | One line per holder that keeps input blocked: the kind (`Operation`, `PendingClose` or `InputHold`), the request id when there is one, the reason, the age and the call site that took it. |
| One section per queue | Header, whether the queue is held, the number of gates, each hold with its reason, age and the call that took it, the item being presented and the waiting items in the order they will be presented. |
| Kept instances | The ids of views whose instance is parked for reuse. |
| Preload leases | The ids of views whose asset is held by a preload. |
| Events | The last events of the navigator and of its queues, newest first. |

The status line is the quickest check: a navigator that stays `Busy` after everything should have
finished has a shield holder that is not going away, and the holders section names it.

The toolbar has two buttons besides the root dropdown. **Copy state** puts the navigator's text
read-out on the clipboard, the same text as [`INavigator.Describe`](#a-text-read-out-from-code)
produces, for a bug report. `?` opens the documentation.

Sections can be folded, and the window keeps a section open or closed per layer and queue. It
refreshes four times a second in Play Mode and updates its labels in place. The read-out of the
navigator state that feeds it allocates nothing once warm.

When there is nothing to show, the window says why instead of staying empty:

| Message | Meaning |
|---|---|
| The window shows a running root. Enter Play Mode to see its layers, shield, queues and events. | The editor is not in Play Mode. |
| No view root in the loaded scenes. | No `ViewRoot` exists. |
| The selected root is not initialised yet. | The root has `Initialize On Awake` off and `Initialize` has not run, or the bootstrap has not reached it. |

### The recent-event list

The Events section lists up to 100 events, newest first. Each line is one `ViewEvent` written as
text:

```text
Closed #2 library layer screens source home.library reason Back open 4.30s
Shown #3 confirm layer popups source settings.reset waited 0.12s
NotShown #5 rename layer popups source item.rename rejected Declined detail This item cannot be renamed.
[queue] Enqueued #6 notice layer popups source startup.news priority 10
Settled
```

A line that starts with `[queue]` was written by a `ViewQueue`; the others come from the navigator.
`waited` is the time from acceptance to the shown event, `open` the time the view was on screen,
and `priority` is shown for queue items. The `source` is the tag you passed in `OpenOptions.Source`,
which is what makes a line point back at the code that asked. The events and their order are
described in [Events and analytics](Events-and-Analytics.md).

The list is a ring of the last 100 events that the navigator keeps for the window. It exists **in
the editor and in development builds**. In a release build the ring is not compiled, so there is
nothing to read there. If you need events in a release build, register an observer of your own, as
the next section shows.

### What the window cannot see

- **A queue built on an `INavigator` that is not the package's own** is not listed. A queue built on
  the navigator of a `ViewRoot` is.
- **Anything outside Play Mode.** The window never inspects a prefab or a catalog; the validator
  does that.
- **A built player.** The window is an editor window. For a development build, use the text
  read-out below and your own observer.

---

## A text read-out from code

`INavigator.Describe` writes the current state of the navigator into a `StringBuilder` you pass
in. A queue has the same method for itself.

<!-- signature -->
```csharp
/// <summary>Writes the layers, requests, shield holders and kept instances as text.</summary>
void Describe(StringBuilder output);
```

The text lists the state of the navigator (`settled`, `busy` or `destroyed`), the registered layers
and the row count, each layer with its requests and phases, each shield holder with its age and call
site, the kept instances, the preload leases, and the number of observers and registrations.
`ViewQueue.Describe` writes the queue's name and layer, whether it is held, each hold with its reason
and age, the number of gates, the item being presented and the waiting items.

Unlike the ring behind the window, `Describe` is compiled into every build, and the text it writes
is the same text that **Copy state** copies. Use it where the console is not enough: print it when a
screen stays blocked, attach it to a bug report, or call it from a debug menu. It reads state that
nothing locks, so call it on the main thread, as the other queries ([Threading](Threading.md)).

To follow events in any build, register an observer on the navigator. The Basic Usage sample does
it in its bootstrap, owned by the root so that nothing has to remove it:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
// The observer ends with the root: RootToken is cancelled when the root is destroyed.
_navigator.AddObserver(new ViewEventLog(), _navigator.RootToken);
```

and its observer writes the same kind of line the window shows:

<!-- source: Samples~/BasicUsage/ViewEventLog.cs -->
```csharp
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
```

The observer's owner is a `CancellationToken`, like every registration. An observer that throws is
isolated: the exception goes to the error handler and the other observers still run.

---

## The error handler

The package calls your code in many places: the view's hooks, scope cleanups and scope tasks,
transitions, the asset provider, the inject step, observers and queue gates. It isolates each call.
An exception that escapes one of them never breaks the navigator: it is caught, handed to one
place, and the package carries on with the request.

That place is `ViewRootSetup.ErrorHandler`, set when you call `ViewRoot.Initialize`. The handler
receives the exception and a `ViewErrorContext`. The Basic Usage bootstrap sets one:

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
_viewRoot.Initialize(new ViewRootSetup
{
    Inject = InjectServices,
    ErrorHandler = OnViewError,
    ExtraLayers = new[] { AppLayers.Toasts }
});
```

<!-- source: Samples~/BasicUsage/AppBootstrap.cs -->
```csharp
// A handler replaces the package's own log, so it logs the exception itself.
private static void OnViewError(Exception exception, in ViewErrorContext context)
{
    Debug.LogException(exception, context.Target);
    Debug.LogWarning($"[views] {context.Source} failed: view '{context.ViewId}', request {context.RequestId}");
}
```

What the handler gets in `ViewErrorContext`:

| Member | Meaning |
|---|---|
| `Source` | The kind of step that failed, a `ViewErrorSource` |
| `ViewId` | The id of the view involved; `null` when none applies |
| `RequestId` | The id of the request involved; `0` when none applies |
| `Target` | The Unity object involved, usually the view instance; `null` when none applies |

A fault from a transition's `Snap` carries the view id and the request id. A fault from a provider's
`Release` carries the view id; its `RequestId` is `0` when the instance was a kept one or the load
result arrived after the request was cancelled.

The sources:

| `ViewErrorSource` | Raised when |
|---|---|
| `ViewHook` | A hook on the view component threw: `OnOpen`, `OnVisible`, `OnBack` |
| `ScopeCleanup` | An action registered on a scope threw while the scope ended |
| `ScopeTask` | A task started with `ViewScope.Run` threw |
| `Transition` | A show or hide transition threw, or its `Snap` threw |
| `Provider` | The asset provider threw (also in `Release`), a preload failed, or binding or placing a view instance threw |
| `Inject` | `ViewRootSetup.Inject` threw on a new instance. The request ends as not shown with `RejectReason.LoadFailed` and logs [UIDIRECTOR104](Troubleshooting.md#uidirector104). |
| `Observer` | An observer threw while an event was dispatched |
| `Gate` | A queue gate threw |

How an error is routed:

- **With a handler**, the handler is the only place the exception goes. It replaces the package's
  log, so write the `Debug.LogException` yourself, as the sample does. Passing `context.Target` makes
  a click on the Console line select the view instance.
- **Without a handler**, the package calls `Debug.LogException(exception, context.Target)`.
- **A handler that throws** does not break anything: the package catches that exception too, logs it
  with `Debug.LogException` and carries on.
- The handler runs in all builds. It is the one place that sees an exception in a release build,
  so it is the right place to send a crash report.

An exception often has an id line next to it. `OnOpen` throwing logs `UIDIRECTOR117` and routes the
exception as `ViewHook`; a gate that throws logs `UIDIRECTOR119` and routes it as `Gate`; a failed
load logs `UIDIRECTOR104` and routes a provider exception as `Provider` (an exception from the inject
delegate as `Inject`). The id line (editor and
development builds) says what the package did about it, and the handler (all builds) gets the
exception itself with the stack.

A view that throws does not take the rest of the UI with it. The request that hit the error ends
(a failed `OnOpen` ends as not shown with `RejectReason.Faulted`), the shield is released, and the
next request runs.

---

## Validate Catalogs

The validator checks the data that the editor can see before the game runs: every `ViewCatalog`
asset in the project and every static class marked `[ViewKeys]`. Run it from
`Tools > UIDirector > Validate Catalogs`. It logs one line per finding, with the id first and the
Troubleshooting anchor last, and then one summary line:

```text
UIDIRECTOR108: Row 3 ('item') in catalog 'Ui' repeats the id first used by Row 0 ('item') in catalog 'Core'; the second row is refused. (see Troubleshooting#uidirector108)
UIDirector catalog validation FAILED: 1 error(s), 0 warning(s), 2 catalog(s).
```

An error is logged with `Debug.LogError` and a warning with `Debug.LogWarning`. A finding tied to a
row is logged with its catalog as the context, so a click on the line selects the catalog; a key with
no row ([UIDIRECTOR101](Troubleshooting.md#uidirector101)) names the key class and field in the
message, and when the id belongs to a refused row it also names that row and is logged with its catalog as the context. The summary says `PASSED`
when there is no error and `FAILED` otherwise; warnings never make it fail. The checks and their
ids are listed in [Catalogs and keys](Catalogs-and-Keys.md#validate).

Two details:

- Key classes in test assemblies (assemblies that reference `nunit.framework`) are skipped, because
  fixtures have no catalog rows. See [Troubleshooting](Troubleshooting.md#the-validator-reports-keys-in-my-test-assembly).
- The checks for asset keys (`UIDIRECTOR124`) run only when the Addressables package is installed.
  With the package installed but no Addressables settings in the project, every asset key is
  reported as unmatched, which is a warning.

### The catalog Inspector

Select a `ViewCatalog` asset. Under its rows the Inspector shows a Validation block with the
findings for **that catalog**, each as a red or yellow box with the id and the message and a `?`
button that opens the Troubleshooting section of the id. The `Validate` button runs the checks again.
The block validates every catalog in the project, not only the selected one, so a duplicate id that
spans two catalogs appears on the catalog that holds the second row. A finding without a row, such
as a key with no catalog row at all (`UIDIRECTOR101`), has no catalog to show on; it appears in the menu
run only. A key whose row is refused for an unknown layer is also reported as `UIDIRECTOR101`, and it
shows on the catalog that holds that row.

### Continuous integration

`Ecanakli.UIDirector.EditorTools.CatalogValidation.RunFromCommandLine` is a public static method for
Unity's `-executeMethod`. It validates the project exactly as the menu does, logs the same lines,
and then ends the editor with an exit code:

| Result | Exit code |
|---|---|
| No errors (warnings may exist) | 0 |
| At least one error | 1 |
| The validator itself threw (logged as `UIDirector catalog validation FAILED: the validator threw.`) | 1 |

The method takes no arguments of its own. Everything else on the command line is Unity's. A
typical call:

```bash
Unity -batchmode -nographics -projectPath /path/to/project \
  -executeMethod Ecanakli.UIDirector.EditorTools.CatalogValidation.RunFromCommandLine \
  -logFile -
```

`-logFile -` prints the editor log to standard output, where the summary line and the finding lines
are. The method calls `EditorApplication.Exit` itself, so `-quit` is not needed. The project must
not be open in another editor, and it must compile: when scripts do not compile Unity never reaches
the method, and its own exit code reports the failure.

Warnings do not fail a run. If your pipeline should fail on a warning too, search the log for the
warning ids (`UIDIRECTOR120`, `UIDIRECTOR122`, `UIDIRECTOR123`, `UIDIRECTOR124`).

---

## A scene unload or a root destruction logs no UIDIRECTOR107

[UIDIRECTOR107](Troubleshooting.md#uidirector107) means that a view was destroyed or deactivated by
code outside the package **while its root lives**. It is not logged when the root itself goes away.

When a scene that holds the root is unloaded, or the root is destroyed, the root tears the navigator
down: every request ends (a shown view as `CloseReason.Destroyed`, one not shown as
`RejectReason.RootGone`), awaiters complete, and the loss reports of the destroyed views are
dropped. The loss of the view objects is then expected, so nothing is logged. The same holds for the
views that were destroyed when the application quits.

A line with `UIDIRECTOR107` therefore always points at project code: something called `Destroy` on
a view, or `SetActive(false)` on a view, a layer object or the root object, while the UI was running.
A staged instance that is destroyed before it is placed, while it still waits inactive under the
staging object, is a different case. Unity sends no destroy message for it, and the request ends
through the load failure, [UIDIRECTOR104](Troubleshooting.md#uidirector104). Once the view is placed,
a loss is reported as `UIDIRECTOR107`. See [Limitations](Limitations.md) for both.

---

## Traps

- **Looking for an id line in a release build.** There is none. Read `ViewResult.Rejected` and
  `Detail`, register an observer, and use the error handler.
- **A handler that does not log.** `ErrorHandler` replaces the package's own output. An empty
  handler hides every exception from your code.
- **Reading the window outside Play Mode.** It shows the one-line message. Press Play first.
- **Expecting the window in a player.** It is an editor window, and the ring behind its event list
  exists in the editor and in development builds only.
- **Relying on the validator alone.** It checks catalogs and keys, not what your code does at run
  time. Row problems such as `UIDIRECTOR108`, `UIDIRECTOR109` and `UIDIRECTOR125` are logged in the
  editor and in development builds only, and a release build refuses the row without a message.
- **Treating a warning as noise.** `UIDIRECTOR110` and `UIDIRECTOR111` mean that something holds
  input or a queue for longer than it should. `UIDIRECTOR110` names the call site of the input hold,
  and the Shield holders section shows it. `UIDIRECTOR111` names the reason, the age and the call site
  of the `ViewQueue.Hold` call; the queue section lists each hold with its call site.

## See also

- [Troubleshooting](Troubleshooting.md): one section per id, with cause and fix
- [Catalogs and keys](Catalogs-and-Keys.md): the rows and keys that the validator reads
- [Events and analytics](Events-and-Analytics.md): the event stream behind the window's event list
- [Opening and results](Opening-and-Results.md): `ViewResult`, `Rejected` and `Detail`
- [Threading](Threading.md): which calls may be made from which thread
- [Performance](Performance.md): what the diagnostics cost
- [Limitations](Limitations.md)
- [API reference](API-Reference.md): `ViewRootSetup`, `ViewErrorHandler`, `ViewErrorContext`
