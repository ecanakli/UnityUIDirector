# Queue Usage

Core only: no DOTween, no Zenject. Needs the uGUI package (`com.unity.ugui`, which includes TextMeshPro) and
UniTask.

## Setup

1. Import TextMeshPro's Essential Resources once: `Window > TextMeshPro > Import TMP Essential Resources`.
   The texts stay blank until then.
2. Open the scene `QueueUsage.unity` from the folder the Package Manager imported the sample into:
   `Assets/Samples/UIDirector/<version>/Queue Usage/`. The scene has no Event System on purpose: the right input
   module depends on your project's input handling, so the bootstrap creates one when Play starts.
3. Press Play. Optionally open `Window > UIDirector` to watch the queue and the input shield live (it shows
   something in Play Mode only). Click the Game view once so it has keyboard focus: device back is the Escape key
   in the editor.

The view ids in this sample (`lobby`, `notice`, `ask`, `tutorial-tip`, `news`) differ from the ids in the Basic
Usage sample, so importing both samples into one project never makes `Tools/UIDirector/Validate Catalogs` report a
duplicate.

## What to do

Console lines come from two [QueueEventLog](QueueEventLog.cs) instances: `[queue] ...` for what the queue itself
reports (`Enqueued` when it accepts an item, `NotShown` when it turns one away or drops it), and `[views] ...` for
everything the navigator reports once an item is presented (`Requested`, `Shown`, `Closed`, `NotShown`, `Settled`).
Every line has the request id, the view id, the priority and the source tag.

- **Play**, without pressing anything: the lobby opens (`Requested ... lobby priority=0 source=boot`) and the
  bootstrap queues the startup notices at the same moment. The Console shows `[queue] Enqueued ... source=startup.news`
  with `priority=10` first and `[queue] Enqueued ... source=startup.maintenance` with `priority=100` second. Once the
  lobby is shown, the queue presents "Maintenance" first, although it was queued second, because the higher
  priority goes first. Close it with "OK" (`Closed ... reason=Dismissed`) and "What is new" follows at once. Only one
  popup is on screen at a time. The lobby status reads "Pick an action." and its buttons answer after the notices
  are gone.
- **Show startup notices** queues the same two notices again. The status reads "Notices queued. They appear one
  at a time, most urgent first." and the order is the same: "Maintenance" (priority 100), then "What is new"
  (priority 10). With `Window > UIDirector` open, the `startup` queue shows `Presenting:` the first notice and
  `Waiting (1)` for the second while the first is open.
- **Ask for feedback** sets the status "Waiting for the question to reach the screen..." and queues a yes or no
  question with the lowest priority (`priority=1`, `source=startup.feedback`). With nothing else waiting it
  appears at once: "Would you like to tell us what to improve?" with "Yes" and "No". "Yes" sets the status to
  "Thanks!". "No" sets "Maybe later.", and so does closing the popup with Escape, because a close without an
  answer carries no value.
- **Run the tutorial** sets the status "Tutorial started. Queued notices wait until it ends.", takes a hold on the
  `startup` queue named `tutorial`, and opens three tips one after the other (`source=tutorial`): "Step 1 of 3",
  "Step 2 of 3", "Step 3 of 3". Each tip's "Next" button closes it and the next one opens right away. The tips are
  shown directly, so the hold does not affect them. In `Window > UIDirector` the `startup` queue reads `held` and
  lists one hold. When the third tip closes, the hold is removed and the queue carries on; the lobby status keeps
  its text. Escape on a tip leaves the tutorial early: the tip closes with `reason=Back`, the remaining tips are
  not shown and the hold is removed in the same moment.
- **What the gate does during the tutorial**: while the tutorial runs, [OnboardingGate](OnboardingGate.cs) turns away
  every queued request below priority 100, and lets 100 and above wait for the hold. A turned-away request never
  enters the queue: the Console shows no `Enqueued` line, only
  `[queue] NotShown #n notice priority=10 source=startup.news rejected=Denied detail=onboarding is running`.
  A tip is a modal popup that covers the lobby, so you cannot press a lobby button while the tutorial runs. The
  line appears when code queues something meanwhile, which is what the gate and the hold are written for.
- **Add news feature** adds `NewsCatalog.asset` to the running navigator (the root's own catalog list holds only
  `QueueUsageCatalog.asset`) and queues one popup from it: `[queue] Enqueued ... news priority=50 source=news.added`.
  The popup appears right away: title "News", text "A news feature was added at run time.", and an "OK" button.
  The button's label becomes "Remove news feature". The popup and the catalog share one owner.
- **Remove news feature** ends that owner: the catalog is taken off the root and the label becomes "Add news
  feature" again. If the news popup were still waiting in the queue it would end as
  `NotShown ... rejected=OwnerEnded`, and if it were open it would close with `reason=OwnerEnded`. By hand the
  popup is already closed when you reach the button, because an open popup covers the lobby, so you see only the
  catalog leaving and the label changing. Press "Add news feature" again and a new catalog registration and a
  new news popup follow.
- **Escape** closes the popup on top with `reason=Back` (a notice, the question or a tip). With only the lobby
  on screen it does nothing: the lobby is the root of the history and the sample wires no quit.

## What to look at

- The Console. A notice that was queued second but has the higher priority is presented first: compare the
  `Enqueued` order with the `Requested` order. A denied request is the only kind that shows `NotShown` without a
  preceding `Enqueued`.
- `Window > UIDirector`: the `Queue startup (layer popups)` section with `Held`, `Gates: 1`, `Holds`,
  `Presenting` and `Waiting`, and the `Events` list that mixes `[queue]` and navigator events.
- The Hierarchy: the root made with `GameObject > UI > UIDirector Root` has "Initialize On Awake" off and carries
  `BackInput`. The `Bootstrap` object holds `QueueBootstrap`, whose `News Catalog` field points at
  `NewsCatalog.asset`; that catalog is not on the root. The prefabs are in `Prefabs/` (Lobby, Notice, News, Ask,
  TutorialTip) and `QueueUsageCatalog.asset` lists the ones the root owns.
- [QueueBootstrap](QueueBootstrap.cs) - the setup: the root initialised in `Awake`, the services built in
  `Start`, one log registered on the navigator and on the queue, the lobby opened, the startup notices queued.
- [StartupNotices](StartupNotices.cs) - the queue on the popups layer, a gate with the root's lifetime, the two
  priorities, and `EnqueueAsync` for the question, whose answer is read through `HasValue` because the call also
  completes when the item is turned away.
- [OnboardingGate](OnboardingGate.cs) - a gate asked at enqueue and again right before presentation.
- [TutorialFlow](TutorialFlow.cs) - a flow that takes a `Hold` and shows its own tips directly, with the hold
  removed in a `finally` so no way out of the flow leaves the queue held.
- [NewsFeature](NewsFeature.cs) with [NewsViews](NewsViews.cs) - a feature that brings its own keys and catalog,
  adds the catalog with an owner, and ends everything by ending that owner.
- [LobbyView](LobbyView.cs), [NoticeView](NoticeView.cs), [AskView](AskView.cs), [TutorialTipView](TutorialTipView.cs)
  and [QueueViews](QueueViews.cs) - the views and the keys.
- [QueueEventLog](QueueEventLog.cs) - one observer registered twice, because an item's events come from two
  publishers: the queue (`Enqueued` and its own `NotShown`) and the navigator.
- [QueueServices](QueueServices.cs), [OnboardingState](Support/OnboardingState.cs) and
  [SampleEventSystem](Support/SampleEventSystem.cs) - what the lobby's buttons drive, the flag the tutorial sets
  and the gate reads, and the event system creation.
