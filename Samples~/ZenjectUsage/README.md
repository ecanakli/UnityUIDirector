# Zenject Usage

Core plus the Zenject integration: the container creates the services and injects them into every view. Needs
Zenject or Extenject, the uGUI package (`com.unity.ugui`, which includes TextMeshPro) and UniTask.

## Setup

1. Have Zenject or Extenject installed and the integration active. With Zenject under `Assets`, turn it on with
   `Tools/UIDirector/Zenject Integration`. With Extenject 9.0.0 or newer from UPM it turns on by itself. The
   sample's assembly compiles only when the integration is active.
2. Import TextMeshPro's Essential Resources once: `Window > TextMeshPro > Import TMP Essential Resources`.
   The texts stay blank until then.
3. Open the scene `ZenjectUsage.unity` from the folder the Package Manager imported the sample into:
   `Assets/Samples/UIDirector/<version>/Zenject Usage/`. The scene has no Event System on purpose: the right input
   module depends on your project's input handling, so the sample creates one when Play starts.
4. Add the Scene Context yourself: choose `GameObject > Zenject > Scene Context`, then drag the `AppInstaller`
   component of the `Installers` object into the Scene Context's Mono Installers list. The scene ships no Scene
   Context, because Zenject's script GUIDs depend on how Zenject was installed.
5. Press Play. Optionally open `Window > UIDirector` to watch the stacks and the shield live (it shows something
   in Play Mode only).

The view ids in this sample (`scoreboard`, `reset-score`) differ from the ids in the other samples, so importing
several samples into one project never makes `Tools/UIDirector/Validate Catalogs` report a duplicate.

## What to do

The catalog has two rows: `scoreboard` (a screen that keeps its instance) and `reset-score` (a popup that closes on
a tap on the backdrop). The sample writes nothing to the Console: what you check is the score on screen.

- **Play** opens the scoreboard on its own (`source=boot`), from `AppStart`, which the container runs once it is
  built. The score label reads "0". The buttons are "Add point" and "Reset score".
- **Add point** adds one to the score and the label shows the new number. The score lives in `ScoreService`, a
  single instance bound by the installer, not in the view.
- **Reset score** opens the reset popup (`source=scoreboard.reset`) with the question "Reset the score of N?",
  where N is the current score, and the buttons "No" and "Yes". "Yes" closes it with a true answer: the score goes
  to 0 and the label reads "0". "No" closes it with a false answer and nothing changes. A tap on the backdrop, and
  closing it any other way, end without a value and also change nothing.
- **Reset score with a score of 0** still opens the popup and asks "Reset the score of 0?".

## What to look at

- What the installer does: [AppInstaller](AppInstaller.cs) binds `ScoreService`, calls `NavigatorInstaller.Install`
  and binds `AppStart`. `NavigatorInstaller` initialises the root with the container as its injector, so the root's
  "Initialize On Awake" must be off: if the root was initialised already, `Install` throws an
  `InvalidOperationException` that names the root.
- When injection happens: every new view instance is injected while it is still inactive, before its `Awake`.
  [ScoreboardView](ScoreboardView.cs) and [ResetScoreView](ResetScoreView.cs) receive `ScoreService` in a method
  marked `[Inject]`, so the service is there in `OnOpen`. The scoreboard is a kept instance: it is injected once,
  and not again when it opens a second time (the screen reads the score again in `OnVisible` instead). The reset
  popup is not kept, so every open creates and injects a new instance.
- The root's scope: the container that injects the views is the one the root was installed into. A root under an
  app-wide context cannot see bindings that live in a scene's context. Use a scene-owned root, as this sample
  does, or pass the data as arguments of the open call.
- `Window > UIDirector`: the scoreboard in `screens`; the reset popup in `popups` while it is open.
- The Hierarchy: the root made with `GameObject > UI > UIDirector Root` has "Initialize On Awake" off; the
  `Installers` object holds `AppInstaller` with its root field wired. The prefabs are in `Prefabs/`: Scoreboard and
  ResetScore.
- [AppInstaller](AppInstaller.cs) - also creates the event system, before the root initialises and never while
  the container is validating.
- [AppStart](AppStart.cs) - opens the scoreboard once the container is built, with the root as the owner.
- [ScoreboardView](ScoreboardView.cs) - a screen with an injected service, and a popup awaited with `ShowAsync`
  whose answer is read through `HasValue`.
- [ResetScoreView](ResetScoreView.cs) - a confirm popup that completes with true or false.
- [ZenjectViews](ZenjectViews.cs) - the two keys.
- [ScoreService](Support/ScoreService.cs) - the stand-in for your own game state.
- [SampleEventSystem](Support/SampleEventSystem.cs) - the event system creation.
