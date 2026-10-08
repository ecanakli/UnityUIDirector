# Zenject

[Back to index](index.md)

This page connects a `ViewRoot` to a Zenject or Extenject container. After it, the container injects
every view instance the root creates, and `INavigator` and `ViewRoot` can be injected into your own
classes. It covers turning the integration on, the call that wires the root and its overload that takes a
`ViewRootSetup`, what the call does while Zenject validates a scene, when a view is injected, which container a root belongs to, how to
set up the SceneContext, and where the EventSystem comes from.

The integration adds one class and no new concepts. Without it, the same job is one delegate,
`ViewRootSetup.Inject` ([Root canvas](Root-Canvas.md#initialize-with-viewrootsetup)).

The examples come from the Zenject Usage sample. Its catalog has a `scoreboard` screen and a
`reset-score` popup, and both receive a `ScoreService` from the container.

---

## Turn the integration on

The integration is its own assembly, `Ecanakli.UIDirector.DependencyInjection.Zenject`. Unity compiles
it only while the scripting define `ECANAKLI_UIDIRECTOR_DI_ZENJECT` exists. How the define gets there
depends on how Zenject is installed:

- **Extenject as a package** (`com.svermeulen.extenject`, version 9.0.0 or newer, from UPM or
  OpenUPM): the assembly sets the define for itself. There is nothing to do.
- **Zenject copied under `Assets/`:** choose `Tools/UIDirector/Zenject Integration` once. The menu
  item adds the define to the Player Settings of every build target, and it is ticked while the
  integration is active. Commit the `ProjectSettings` change. Choosing it again removes the define.

The menu item refuses to turn the integration on when no assembly named `Zenject` exists in the
project, and says so. When Extenject is installed as a package at 9.0.0 or newer, it explains that
there is nothing to toggle.

If Zenject is removed while the define is still set, the integration assembly can no longer compile.
Turn the menu item off, or delete `ECANAKLI_UIDIRECTOR_DI_ZENJECT` from the scripting define symbols
by hand.

Names and references:

- `NavigatorInstaller` is in the namespace `Ecanakli.UIDirector.DependencyInjection`.
- Code in an assembly definition references `Ecanakli.UIDirector`,
  `Ecanakli.UIDirector.DependencyInjection.Zenject` and `Zenject`.
- **Game code does not test the define.** With Extenject as a package, `ECANAKLI_UIDIRECTOR_DI_ZENJECT`
  is set inside the integration's own assembly only, so an `#if ECANAKLI_UIDIRECTOR_DI_ZENJECT` in your
  code is false and would skip your installer. Use `NavigatorInstaller` directly: it is there whenever
  Extenject and the integration are installed.
- The integration assembly references the `Zenject` assembly and the precompiled `Zenject-usage.dll`
  by name, so the Zenject install must provide both.

---

## Wire the root in an installer

The whole integration is one static method with two overloads:

<!-- signature -->
```csharp
namespace Ecanakli.UIDirector.DependencyInjection
{
    public static class NavigatorInstaller
    {
        public static void Install(DiContainer container, ViewRoot root);
        public static void Install(DiContainer container, ViewRoot root, ViewRootSetup setup);
    }
}
```

Call it from `InstallBindings` of an installer that holds a reference to the root. The installer is a
`MonoInstaller`, declared like this:

<!-- source: Samples~/ZenjectUsage/AppInstaller.cs -->
```csharp
public sealed class AppInstaller : MonoInstaller
```

A Scene Context lists it under Mono Installers ([Set up the SceneContext](#set-up-the-scenecontext)).
This is the sample's installer, without the declaration:

<!-- source: Samples~/ZenjectUsage/AppInstaller.cs -->
```csharp
// "Initialize On Awake" must be off on this root: Install initializes it with the container as the injector, and throws otherwise.
[SerializeField] private ViewRoot _viewRoot;

public override void InstallBindings()
{
    // Before the root initialises, which warns when no EventSystem exists; never while validating.
    if (!Container.IsValidating)
        SampleEventSystem.EnsureExists();

    Container.Bind<ScoreService>().AsSingle();
    NavigatorInstaller.Install(Container, _viewRoot);
    Container.BindInterfacesTo<AppStart>().AsSingle();
}
```

What `Install` does, in order:

1. Binds the root: `ViewRoot` from the instance you passed, as a single.
2. Initializes the root with `ViewRootSetup { Inject = container.InjectGameObject }`. From now on every
   new view instance is injected by this container.
3. Binds `INavigator` to the navigator of that root, as a single.

After it, any class the container creates can take `INavigator` or `ViewRoot` in its constructor.

The two-argument overload passes an empty `ViewRootSetup`, so `ErrorHandler`, `ExtraLayers` and
`AssetProvider` keep their defaults: the root has the three default layers and logs errors itself. An
`IViewProvider` component on the root's object, such as the Addressables provider, is still found.

### Pass layers, an error handler or an asset provider

A project that needs a layer of its own, an error handler or an asset provider uses the overload that
takes a `ViewRootSetup`. Fill in `ExtraLayers`, `ErrorHandler` and `AssetProvider` as on
[Root canvas](Root-Canvas.md#initialize-with-viewrootsetup) and leave `Inject` null: the installer sets
`Inject` to the container itself. The same steps run, with your three members carried over, and the
`setup` object you pass is never changed, so one setup can be reused.

- A `setup` whose `Inject` is not null throws `ArgumentException`. The installer does not chain your
  delegate after the container's, because the order would be ambiguous.
- A null `setup` throws `ArgumentNullException`.

### Initialize On Awake must be off

`NavigatorInstaller.Install` initializes the root, and a root initializes once. Turn off `Initialize
On Awake` on the `ViewRoot` Inspector. If the root is already initialized when `Install` runs,
`Install` throws an `InvalidOperationException`. The message names the root and says what to do:

```text
The ViewRoot 'UIDirector Root' was initialised before NavigatorInstaller.Install ran, so its views
would not be injected. Turn off "Initialize On Awake" on it.
```

A root is initialized before `Install` when its own `Awake` ran first with the setting on, or when any
code read `ViewRoot.Navigator` first, because that read initializes the root with default settings.
Leaving the setting on makes the result depend on which `Awake` runs first. Keep it off.

`Install` also throws `ArgumentNullException` for a null container or a null root (and, in the
overload with a setup, for a null `setup`). Install each root once: a second `Install` for the same
root finds it initialized and throws.

### While the container validates

Zenject validates a scene without Play Mode and without constructing the bound classes. In that mode
`Install` binds `ViewRoot` and binds `INavigator` to a method that returns `root.Navigator`, and does
nothing else. The root stays uninitialized and nothing in the scene is created or changed. Because
Zenject never calls the method while validating, validation needs no running root.

The arguments are still checked: a null container, root or setup, and a setup with `Inject` set, throw
even while validating.

Anything else in `InstallBindings` that creates scene objects needs the same guard. That is why the
sample's installer creates the EventSystem only when `Container.IsValidating` is false.

---

## Open the first screen

Nothing opens a view by itself. `AppStart` is a plain class that the container creates and runs once
the container is built; it takes the navigator through its constructor:

<!-- source: Samples~/ZenjectUsage/AppStart.cs -->
```csharp
public sealed class AppStart : IInitializable
{
    private readonly INavigator _navigator;

    public AppStart(INavigator navigator)
    {
        _navigator = navigator;
    }

    public void Initialize()
    {
        // Owned by the root, so the scoreboard ends only when the root does.
        _navigator.Open(ZenjectViews.Scoreboard, _navigator.RootToken, new OpenOptions { Source = "boot" });
    }
}
```

The installer binds it with `BindInterfacesTo<AppStart>().AsSingle()`, so Zenject calls `Initialize`
after every installer has run. The owner is the root's token, as in the other samples: the screen
ends when the root does. See [Opening and results](Opening-and-Results.md).

---

## Receive services in a view

A view is a `MonoBehaviour`, so it takes its services through an `[Inject]` method. The scoreboard
keeps the service in a field:

<!-- source: Samples~/ZenjectUsage/ScoreboardView.cs -->
```csharp
// Runs before Awake, while the new instance is still inactive; a kept instance is not injected again.
[Inject]
private void Construct(ScoreService scores)
{
    _scores = scores;
}
```

The reset popup does the same, and uses the service in `OnOpen`:

<!-- source: Samples~/ZenjectUsage/ResetScoreView.cs -->
```csharp
// Runs before Awake, while the new instance is still inactive.
[Inject]
private void Construct(ScoreService scores)
{
    _scores = scores;
}
```

When injection happens:

- **Every new instance is injected while it is inactive, before its `Awake` and `OnEnable`.** The
  package injects after the prefab is loaded and before it activates the instance, so `Awake`,
  `OnOpen` and every later callback see the injected fields.
- **The components on the instance's child objects are injected too,** before their own `Awake`.
- **A kept instance is injected once.** A catalog row with `KeepInstance` creates the instance on its
  first open and reuses it afterwards, and the reuse does not inject again. That is why
  `ScoreboardView` reads the score in `OnVisible` and not in the `[Inject]` method: the screen is
  kept, and the number changes between opens.
- **A released instance is a new instance.** A row that releases its view on close creates, and
  injects, a fresh instance on every open. `ResetScoreView` is such a popup.
- **A reopen in place does not inject.** It reuses the live instance
  ([Navigation and back](Navigation-and-Back.md#return-to-a-screen-that-is-already-open)).

If injection throws, for example because a service the view asks for has no binding, the open ends as
not shown with `RejectReason.LoadFailed`, logged as
[UIDIRECTOR104](Troubleshooting.md#uidirector104), and the exception goes to the root's error handler
with the source `ViewErrorSource.Inject`. The view is never shown half-injected. An instance that was
already destroyed when its turn to be injected came is skipped by injection and fails when the view is
resolved, as a `LoadFailed` with the source `Provider`.

---

## The root's scope

`Install` hands the root one container, and that container injects every view the root opens, from
whichever scene the call comes from. A view sees what that container sees: its bindings and those of
the containers above it.

- A root installed in a **SceneContext** sees the scene's bindings and the app-wide ones, because a
  scene container sits below the app-wide container.
- A root installed under an **app-wide context** (the ProjectContext) sees only app-wide bindings.
  A view opened from a scene cannot be injected with something that is bound only in that scene's
  context: with the root installed in the app-wide container, a view receives the app value and never
  the scene value.

Two ways out when a view needs something from a scene context:

- Use a scene-owned root, installed by that scene's installer, as the sample does.
- Keep the app-wide root and pass the data as the arguments of the open call: `ShowAsync(key, args,
  owner)` carries any value, and the view takes it in `OnOpen`. This also keeps the view free of the
  container.

Several roots can exist, each with its own container: every root injects through the container it
was installed in. The documented setup is one root per application ([Limitations](Limitations.md)).

A root keeps the container it was installed with for as long as it lives. Put a root that persists
across scenes under an app-wide context so the root and its container share a lifetime.

---

## Set up the SceneContext

The Zenject Usage sample ships a scene without a Scene Context, because Zenject's script GUIDs depend
on how Zenject was installed. Add it yourself, in the sample scene or in your own:

1. Make sure the integration is active (see above).
2. In the scene, have a root made with `GameObject > UI > UIDirector Root`, with `Initialize On Awake`
   off and your catalogs assigned.
3. Create the installer object. In the sample it is an object named `Installers` that holds the
   `AppInstaller` component, with its `View Root` field pointing at the root.
4. Choose `GameObject > Zenject > Scene Context`.
5. Drag the `AppInstaller` component of the `Installers` object into the Scene Context's Mono
   Installers list.
6. Press Play. The scoreboard opens by itself. `Window > UIDirector` shows the stacks and the shield
   live.

The sample's scene has no EventSystem on purpose; the next section explains who creates it.

---

## The EventSystem

A root warns with [UIDIRECTOR116](Troubleshooting.md#uidirector116) when the scene has no EventSystem
at the moment it initializes, and a scene without one gives views no input. `Install` initializes the
root, so an EventSystem that the installer creates must exist before `Install` runs. The sample's
installer does it in the first lines of `InstallBindings`, behind `if (!Container.IsValidating)`:
validation must not create scene objects.

If your scene already has an EventSystem, none of that is needed. How the sample's `EnsureExists`
picks the input module is on [Input and platform](Input-and-Platform.md#give-the-ui-an-eventsystem).

---

## Traps

- **`Initialize On Awake` left on.** `Install` throws when the root was initialized first. Turn it
  off.
- **Reading `root.Navigator` before `Install`.** The read initializes the root with defaults and
  `Install` then throws. Take the navigator from the container.
- **A scene binding that a view cannot see.** The root belongs to the container it was installed in,
  not to the scene that opens the view. See [The root's scope](#the-roots-scope).
- **Creating scene objects in `InstallBindings` during validation.** Guard them with
  `Container.IsValidating`.
- **An EventSystem created after `Install`.** The root has already warned. Create it before.
- **Per-open data read in the `[Inject]` method of a kept view.** It runs once. Read it in `OnOpen` or
  `OnVisible`.
- **Missing binding in a view.** The open fails with
  [UIDIRECTOR104](Troubleshooting.md#uidirector104); the view does not appear.

## See also

- [Root canvas](Root-Canvas.md)
- [Input and platform](Input-and-Platform.md)
- [View authoring](View-Authoring.md)
- [Opening and results](Opening-and-Results.md)
- [Catalogs and keys](Catalogs-and-Keys.md)
- [Limitations](Limitations.md)
- [Troubleshooting](Troubleshooting.md)
