# UIDirector documentation

UIDirector runs uGUI screens, popups and overlays from prefabs under one persistent root: typed keys, awaitable results, one back rule and an optional priority queue. Requirements, install steps and a five-minute start are in the [README](../README.md); every public type is listed in the [API reference](API-Reference.md).

## Start here

Read these in order the first time.

- [Concepts](Concepts.md): the root and its layers, views, rows and keys, a request from start to finish, outcomes as data, owners and scopes.
- [Root canvas](Root-Canvas.md): creating the root, every Inspector field, `ViewRootSetup`, overlay and camera roots, the sorting band, adding a layer of your own.
- [Catalogs and keys](Catalogs-and-Keys.md): catalog rows, typed keys, giving catalogs to the root, adding and removing a catalog at run time, the validator.
- [View authoring](View-Authoring.md): writing a view on the base class or the interfaces, every hook, `Decline`, `Complete`, scopes and kept instances.
- [Opening and results](Opening-and-Results.md): `Open` against `ShowAsync`, `OpenOptions`, the handle, reading a result, owners, closing many views.
- [FAQ](FAQ.md): short answers, each linking to the page that explains it.

## Guides

One task each.

- [Navigation and back](Navigation-and-Back.md): opening screens forward, returning to one, `CloseTo`, `Back()` and its order, `BackPolicy`, `BackInput`.
- [Popups and backdrop](Popups-and-Backdrop.md): awaiting a popup's answer, the shared backdrop, closing on a tap, the input shield, `RejectWhenBusy`.
- [System overlays](System-Overlays.md): a blocking overlay around a piece of work, back while it is up, waiting until it is gone.
- [Transitions](Transitions.md): the fade, scale and slide transitions, time, writing a custom transition and the contract it keeps.
- [Queue](Queue.md): presenting requests one at a time by priority, gates, holds, why the navigator's waiting cap is never reached and a feature that brings its own catalog.
- [Events and analytics](Events-and-Analytics.md): the observer interface, every event kind, the order at close, an analytics adapter.
- [Input and platform](Input-and-Platform.md): the EventSystem, what the input shield blocks, double taps, `HoldInput`, time scale 0, the back button.
- [Migration](Migration.md): moving a project that has a UI manager of its own to UIDirector one view at a time.

## Integrations

Optional assemblies that compile only when the library is present.

- [DOTween](DOTween.md): turning the integration on, `TweenTransition`, and a custom transition that leaves no tween behind.
- [Assets and Addressables](Assets-and-Addressables.md): views loaded by address, preload leases, keep and trim, load failures, writing a provider.
- [Zenject](Zenject.md): `NavigatorInstaller`, injection into every view instance, the SceneContext and the EventSystem.

## Reference

- [API reference](API-Reference.md): every public type and member as declared, with its assembly and namespace.
- [Diagnostics](Diagnostics.md): console lines with stable ids, the `Window > UIDirector` window, the text read-out, the error handler, the catalog validator.
- [Troubleshooting](Troubleshooting.md): one section per `UIDIRECTOR1xx` id with cause and fix, then problems that carry no id.
- [Performance](Performance.md): what the package guarantees about cost, which test holds each guarantee.
- [Threading](Threading.md): which calls throw off the main thread, which are posted to it and where an awaited result resumes.
- [Limitations](Limitations.md): what the package does not do and its provisional defaults.

## Design

Why the package is shaped the way it is.

- [Architecture](Design/Architecture.md): the root, requests and phases, the per-layer runner, the shield, back resolution, scopes, events, the queue, the provider seam.
- [ADR-001 Outcomes as data](Design/Decisions/ADR-001-Outcomes-As-Data.md): `ShowAsync` always completes with a result and never throws for what happens to a view.
- [ADR-002 Strict serialization](Design/Decisions/ADR-002-Strict-Serialization.md): one operation at a time per layer and a started step is never interrupted.
- [ADR-003 Begin-only hooks and scopes](Design/Decisions/ADR-003-Begin-Only-Hooks-And-Scopes.md): hooks only begin things, and the package ends what a scope holds.
- [ADR-004 The queue sits above the navigator](Design/Decisions/ADR-004-Queue-Above-The-Navigator.md): `ViewQueue` talks to the navigator through `INavigator` only.
- [ADR-005 Token owners](Design/Decisions/ADR-005-Token-Owners.md): every request and registration takes a required `CancellationToken` as its owner.

For AI tools, [llms.txt](../llms.txt) holds a summary, the rules that bite and a description of every page, and [llms-full.txt](../llms-full.txt) is all of it in one file.
