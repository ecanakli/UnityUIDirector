# DOTween Usage

Core plus the DOTween integration: a view animated by the package's `TweenTransition`, and a view animated by a
transition of your own written on DOTween. Needs DOTween, the uGUI package (`com.unity.ugui`, which includes
TextMeshPro) and UniTask.

## Setup

1. Have DOTween in the project, in one of three ways:
   - DOTween from the Asset Store, with its setup run (`Tools > Demigiant > DOTween Utility Panel > Setup DOTween`);
     that defines `DOTWEEN`.
   - A UPM DOTween package: the package detects it through a version define, nothing to do.
   - Neither define is set but the DOTween assembly is in the project: turn the integration on by hand with
     `Tools/UIDirector/DOTween Integration`.
   The sample's assembly compiles only when one of these is active.
2. Import TextMeshPro's Essential Resources once: `Window > TextMeshPro > Import TMP Essential Resources`.
   The texts stay blank until then.
3. Open the scene `DOTweenUsage.unity` from the folder the Package Manager imported the sample into:
   `Assets/Samples/UIDirector/<version>/DOTween Usage/`. The scene has no Event System on purpose: the right input
   module depends on your project's input handling, so the bootstrap creates one when Play starts.
4. Press Play. Optionally open `Window > UIDirector` to watch the stacks and the shield live (it shows something
   in Play Mode only).

The view ids in this sample (`welcome`, `hint`, `card`) differ from the ids in the other samples, so importing
several samples into one project never makes `Tools/UIDirector/Validate Catalogs` report a duplicate.

## What to do

The catalog has three rows: `welcome` (a screen that keeps its instance, with the core `FadeTransition`), `hint` (a
popup that closes on a tap on the backdrop) and `card` (a popup). The scene's root has "Initialize On Awake" off;
the bootstrap initialises it in `Awake`. The sample writes nothing to the Console: what you check is the motion.

- **Play** opens the welcome screen on its own (`source=boot`), with a fade. It has two buttons, "Show hint" and
  "Show card".
- **Show hint** opens the hint popup (`source=welcome.hint`) with the text "TweenTransition fades and scales this
  popup." and an "OK" button. It fades in while scaling up from the hidden scale (0.85 by default) and leaves the same way in reverse. This is
  the package's `TweenTransition`, set up only in the Inspector of the `Hint` prefab, with no code. "OK" or a tap
  on the backdrop closes it.
- **Show card** opens the card popup (`source=welcome.card`) with the text "PunchTransition is a custom transition
  built on DOTween." and an "OK" button. It fades in with a short scale punch (a quick overshoot that settles back)
  and leaves by fading out while shrinking to 0.9. This is the sample's own `PunchTransition`. "OK" closes it; the
  card does not close on the backdrop.
- **Pause the game while a popup opens** (`Time.timeScale` set to 0): both popups still animate, because both transitions run on unscaled time.

## What to look at

- The `Hint` prefab: select the root of the prefab and read the `TweenTransition` component. Fade, scale, offset,
  duration and the two eases are fields. The `Card` prefab uses the same view class, [HintView](HintView.cs), with
  `PunchTransition` instead; only the transition differs.
- What `TweenTransition` does: it fades a `CanvasGroup` (added when missing), scales the view from a hidden scale
  to 1 and can move it by a fraction of its layer, all with DOTween on unscaled time. It sets the ease, the auto
  kill and the update mode on the sequence itself, so project-wide DOTween defaults (a changed default ease or
  auto-kill off) cannot change the result or make the wait for the end stall.
- What `PunchTransition` shows: how to write your own transition on DOTween. Read [PunchTransition](PunchTransition.cs)
  for the pieces: it derives from `ViewTransition`, it plays a sequence with an explicit ease, auto-kill and
  unscaled time, it creates its getter and setter once, and its `Snap` never throws. It waits for the sequence by
  polling on the player loop instead of resuming from a kill callback, so a kill from `OnDestroy` never resumes the
  caller inside Unity's destroy message.
- `Window > UIDirector`: the welcome screen in `screens`; the hint or the card in `popups` while it is open.
- The Hierarchy: the root made with `GameObject > UI > UIDirector Root` has "Initialize On Awake" off; the
  `Bootstrap` object holds `DOTweenBootstrap`. The prefabs are in `Prefabs/`: Welcome, Hint and Card.
- [DOTweenBootstrap](DOTweenBootstrap.cs) - the root initialised in `Awake`, the welcome screen opened in `Start`
  with the root as the owner.
- [WelcomeView](WelcomeView.cs) - two buttons that open popups with the root as the owner.
- [HintView](HintView.cs) - a popup with a text and an "OK" button, shared by the hint and the card.
- [TweenViews](TweenViews.cs) - the three keys and the argument struct.
- [PunchTransition](PunchTransition.cs) - the custom transition.
- [SampleEventSystem](Support/SampleEventSystem.cs) - the event system creation.
