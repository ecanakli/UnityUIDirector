using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector.Tests;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.UIDirector.EditorTools.Tests
{
    /// <summary>Navigator.CaptureState, the event ring, the queue registry and ViewQueue.CaptureState on the fake stage.</summary>
    [TestFixture]
    public sealed class NavigatorReadOutTests
    {
        private NavigatorHarness _harness;
        private NavigatorState _state;
        private ManualTicker _ticker;
        private readonly List<CancellationTokenSource> _lifetimes = new List<CancellationTokenSource>();

        [SetUp]
        public void SetUp()
        {
            _harness = new NavigatorHarness();
            _state = new NavigatorState();
            _ticker = new ManualTicker();
        }

        [TearDown]
        public void TearDown()
        {
            _harness.MainThread.IsMainThread = true;
            for (var i = 0; i < _lifetimes.Count; i++)
            {
                _lifetimes[i].Cancel();
                _lifetimes[i].Dispose();
            }

            _lifetimes.Clear();
            _harness.MainThread.Flush();
            _harness.Dispose();
        }

        // CaptureState: layers and requests

        [Test]
        public void CaptureState_NothingOpen_ListsTheDefaultLayersInOrderWithNoRequests()
        {
            NavigatorState state = Capture();

            Assert.That(LayerIds(state), Is.EqualTo(new[] { "screens", "popups", "system" }));
            Assert.That(state.Layers, Has.All.Matches<LayerSnapshot>(layer => layer.Requests.Count == 0));
        }

        // CaptureState tells whether an owner that can end the request is registered.
        [Test]
        public void CaptureState_Requests_TellWhetherAnOwnerCanEndThem()
        {
            _harness.Row("owned").Row("unowned", ViewLayers.Popups);
            Navigator.Open(ViewKey.Create("owned"), default(NoArgs), _harness.Owner, default(OpenOptions), "OpenSite", 1);
            Navigator.Open(ViewKey.Create("unowned"), default(NoArgs), System.Threading.CancellationToken.None, default(OpenOptions), "OpenSite", 2);

            NavigatorState state = Capture();

            Assert.That(LayerOf(state, "screens").Requests[0].OwnerBound, Is.True, "a cancellable owner");
            Assert.That(LayerOf(state, "popups").Requests[0].OwnerBound, Is.False, "CancellationToken.None");
        }

        [Test]
        public void CaptureState_Layer_CarriesItsOrderAndRules()
        {
            NavigatorState state = Capture();

            LayerSnapshot popups = LayerOf(state, "popups");
            Assert.That(popups.Order, Is.EqualTo(ViewLayers.Popups.Order), "order");
            Assert.That(popups.Rules, Is.EqualTo(ViewLayers.Popups.Rules), "rules");
        }

        [Test]
        public void CaptureState_ScreenStackAndPopup_ListsTheRequestsPerLayerInStackOrder()
        {
            _harness.Row("home").Row("settings").Row("confirm", ViewLayers.Popups);
            ViewHandle home = Open("home");
            ViewHandle settings = Open("settings");
            ViewHandle confirm = Open("confirm");

            NavigatorState state = Capture();

            Assert.That(ViewIdsOf(LayerOf(state, "screens")), Is.EqualTo(new[] { "home", "settings" }), "screens");
            Assert.That(RequestIdsOf(LayerOf(state, "screens")), Is.EqualTo(new[] { home.RequestId, settings.RequestId }), "screen request ids");
            Assert.That(ViewIdsOf(LayerOf(state, "popups")), Is.EqualTo(new[] { "confirm" }), "popups");
            Assert.That(RequestIdsOf(LayerOf(state, "popups")), Is.EqualTo(new[] { confirm.RequestId }), "popup request id");
            Assert.That(LayerOf(state, "system").Requests, Is.Empty, "system");
        }

        [Test]
        public void CaptureState_OpenView_CarriesItsPhaseSlotAndNoPendingClose()
        {
            _harness.Row("home");
            ViewHandle home = Open("home");

            RequestSnapshot request = LayerOf(Capture(), "screens").Requests[0];

            Assert.That(request.RequestId, Is.EqualTo(home.RequestId), "request id");
            Assert.That(request.ViewId, Is.EqualTo("home"), "view id");
            Assert.That(request.Phase, Is.EqualTo(ViewPhase.Open), "phase");
            Assert.That(request.Slot, Is.EqualTo(1), "slot of the first request on a layer");
            Assert.That(request.ClosePending, Is.False, "close pending");
        }

        [Test]
        public void CaptureState_StackedViews_GiveEachRequestItsOwnSlotInStackOrder()
        {
            _harness.Row("home").Row("settings");
            Open("home");
            Open("settings");

            LayerSnapshot screens = LayerOf(Capture(), "screens");

            Assert.That(SlotsOf(screens), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void CaptureState_LoadingAndQueuedRequests_ReportLoadingAndWaiting()
        {
            _harness.Row("a").Row("b");
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");
            Open("b");

            LayerSnapshot screens = LayerOf(Capture(), "screens");

            Assert.That(PhasesOf(screens), Is.EqualTo(new[] { ViewPhase.Loading, ViewPhase.Waiting }));
            Assert.That(screens.IsRunning, Is.True, "the layer runs while it loads");
            Assert.That(screens.PendingOperations, Is.GreaterThan(0), "the second open waits as an operation");
        }

        [Test]
        public void CaptureState_ViewClosedByCode_ReportsThePendingCloseAndItsReason()
        {
            _harness.Row("p", ViewLayers.Popups);
            ViewHandle handle = Open("p");
            _harness.Stage.PlayMode = StageMode.Manual;

            handle.Close();

            RequestSnapshot request = LayerOf(Capture(), "popups").Requests[0];
            Assert.That(request.ClosePending, Is.True, "close pending");
            Assert.That(request.HasOutcome, Is.True, "has outcome");
            Assert.That(request.OutcomeReason, Is.EqualTo(CloseReason.Code), "reason");
        }

        [Test]
        public void CaptureState_QueuedRequestWithPriorityAndSource_CarriesBoth()
        {
            _harness.Row("offer", ViewLayers.Popups);
            _harness.Stage.AcquireMode = StageMode.Manual;
            Navigator.Open(ViewKey.Create("offer"), default(NoArgs), _harness.Owner, new OpenOptions { Source = "shop", Priority = 7 }, "OpenSite", 3);

            RequestSnapshot request = LayerOf(Capture(), "popups").Requests[0];

            Assert.That(request.Priority, Is.EqualTo(7), "priority");
            Assert.That(request.Source, Is.EqualTo("shop"), "source");
        }

        [Test]
        public void CaptureState_AfterTheStateChanged_ReplacesTheEarlierReadOut()
        {
            _harness.Row("home").Row("settings");
            Open("home");
            ViewHandle settings = Open("settings");
            ViewRegistration hold = Navigator.HoldInput("sync", _harness.Owner, "HoldSite", 1);
            Capture();

            settings.Close();
            hold.Remove();
            NavigatorState state = Capture();

            Assert.That(ViewIdsOf(LayerOf(state, "screens")), Is.EqualTo(new[] { "home" }), "screens");
            Assert.That(state.ShieldHolders, Is.Empty, "shield holders");
        }

        [Test]
        public void CaptureState_CalledTwiceWithNoChange_GivesTheSameReadOut()
        {
            _harness.Row("home").Row("confirm", ViewLayers.Popups);
            Open("home");
            Open("confirm");
            Navigator.HoldInput("sync", _harness.Owner, "HoldSite", 1);
            Capture();
            string[] first = Summarize(_state);

            Capture();

            Assert.That(Summarize(_state), Is.EqualTo(first));
        }

        // CaptureState: shield

        [Test]
        public void CaptureState_NoHolders_ReportsAnEmptyShield()
        {
            NavigatorState state = Capture();

            Assert.That(state.ShieldCount, Is.Zero, "count");
            Assert.That(state.ShieldHolders, Is.Empty, "holders");
        }

        [Test]
        public void CaptureState_InputHold_ListsKindReasonAndCallSite()
        {
            Navigator.HoldInput("sync", _harness.Owner, "HoldSite", 42);

            NavigatorState state = Capture();

            Assert.That(state.ShieldCount, Is.EqualTo(1), "count");
            ShieldHolderSnapshot holder = HolderOf(state, ShieldHolderKind.InputHold);
            Assert.That(holder.RequestId, Is.Zero, "an input hold belongs to no request");
            Assert.That(holder.Reason, Is.EqualTo("sync"), "reason");
            Assert.That(holder.CallSite, Is.EqualTo("HoldSite (line 42)"), "call site");
        }

        [Test]
        public void CaptureState_InputHoldHeldForAWhile_ReportsItsAgeFromTheClock()
        {
            Navigator.HoldInput("sync", _harness.Owner, "HoldSite", 1);
            _harness.Advance(2.5f);

            NavigatorState state = Capture();

            Assert.That(HolderOf(state, ShieldHolderKind.InputHold).AgeSeconds, Is.EqualTo(2.5).Within(0.05));
        }

        [Test]
        public void CaptureState_TwoInputHolds_ListsThemInTheOrderTheyWereTaken()
        {
            Navigator.HoldInput("first", _harness.Owner, "HoldSite", 1);
            Navigator.HoldInput("second", _harness.Owner, "HoldSite", 2);

            NavigatorState state = Capture();

            Assert.That(state.ShieldCount, Is.EqualTo(2), "count");
            Assert.That(ReasonsOf(state), Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public void CaptureState_ViewLoading_ListsAnOperationHolderForItsRequest()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            ViewHandle handle = Open("a", "OpenSite", 7);

            NavigatorState state = Capture();

            ShieldHolderSnapshot holder = HolderOf(state, ShieldHolderKind.Operation);
            Assert.That(holder.RequestId, Is.EqualTo(handle.RequestId), "request id");
            Assert.That(holder.CallSite, Does.Contain("OpenSite"), "call site");
        }

        [Test]
        public void CaptureState_CloseWaitingForItsTransition_ListsAPendingCloseHolder()
        {
            _harness.Row("p", ViewLayers.Popups);
            ViewHandle handle = Open("p");
            _harness.Stage.PlayMode = StageMode.Manual;
            handle.Close();

            NavigatorState state = Capture();

            ShieldHolderSnapshot holder = HolderOf(state, ShieldHolderKind.PendingClose);
            Assert.That(holder.RequestId, Is.EqualTo(handle.RequestId));
        }

        // CaptureState: kept ids, preload ids, counts, busy

        [Test]
        public void CaptureState_ClosedKeepInstanceView_ListsItsIdAsKeptUntilTrim()
        {
            _harness.Row("k", ViewLayers.Popups, ViewRetention.KeepInstance);
            ViewHandle handle = Open("k");
            handle.Close();
            Assume.That(_harness.Stage.StashedInstances.Count, Is.EqualTo(1), "control: the instance is kept");

            Assert.That(Capture().KeptViewIds, Is.EqualTo(new[] { "k" }), "before Trim");

            Navigator.Trim();

            Assert.That(Capture().KeptViewIds, Is.Empty, "after Trim");
        }

        [Test]
        public void CaptureState_HeldPreloadLease_ListsItsViewIdUntilTheOwnerEnds()
        {
            _harness.Row("pre");
            CancellationTokenSource owner = _harness.NewOwner();
            // Not awaited: the fake stage completes the preload inside the call.
            Navigator.PreloadAsync("pre", owner.Token, "PreloadSite", 1).Forget();

            Assert.That(Capture().PreloadViewIds, Is.EqualTo(new[] { "pre" }), "while held");

            owner.Cancel();
            _harness.MainThread.Flush();

            Assert.That(Capture().PreloadViewIds, Is.Empty, "after the owner ended");
        }

        [Test]
        public void CaptureState_PreloadStillLoading_ListsItsViewId()
        {
            _harness.Row("pre");
            _harness.Stage.PreloadMode = StageMode.Manual;
            // Not awaited: the test completes or ends the preload itself.
            Navigator.PreloadAsync("pre", _harness.Owner, "PreloadSite", 1).Forget();

            Assert.That(Capture().PreloadViewIds, Is.EqualTo(new[] { "pre" }));
        }

        [Test]
        public void CaptureState_AddedObserver_RaisesTheObserverAndRegistrationCountsByOne()
        {
            NavigatorState before = Capture();
            int observers = before.ObserverCount;
            int registrations = before.RegistrationCount;

            Navigator.AddObserver(new RecordingObserver(), _harness.Owner, "ObserverSite", 1);

            NavigatorState after = Capture();
            Assert.That(after.ObserverCount, Is.EqualTo(observers + 1), "observers");
            Assert.That(after.RegistrationCount, Is.EqualTo(registrations + 1), "registrations");
        }

        [Test]
        public void CaptureState_ObserverRegistrationRemoved_BringsBothCountsBack()
        {
            NavigatorState before = Capture();
            int observers = before.ObserverCount;
            int registrations = before.RegistrationCount;
            ViewRegistration registration = Navigator.AddObserver(new RecordingObserver(), _harness.Owner, "ObserverSite", 1);
            Assume.That(Capture().ObserverCount, Is.EqualTo(observers + 1), "control: the observer was counted");

            registration.Remove();

            NavigatorState after = Capture();
            Assert.That(after.ObserverCount, Is.EqualTo(observers), "observers");
            Assert.That(after.RegistrationCount, Is.EqualTo(registrations), "registrations");
        }

        [Test]
        public void CaptureState_ObserverOwnerEnds_BringsBothCountsBack()
        {
            NavigatorState before = Capture();
            int observers = before.ObserverCount;
            int registrations = before.RegistrationCount;
            CancellationTokenSource owner = _harness.NewOwner();
            Navigator.AddObserver(new RecordingObserver(), owner.Token, "ObserverSite", 1);
            Assume.That(Capture().ObserverCount, Is.EqualTo(observers + 1), "control: the observer was counted");

            owner.Cancel();
            _harness.MainThread.Flush();

            NavigatorState after = Capture();
            Assert.That(after.ObserverCount, Is.EqualTo(observers), "observers");
            Assert.That(after.RegistrationCount, Is.EqualTo(registrations), "registrations");
        }

        [Test]
        public void CaptureState_TwoObserversOneRemoved_KeepsTheOtherCounted()
        {
            NavigatorState before = Capture();
            int observers = before.ObserverCount;
            int registrations = before.RegistrationCount;
            ViewRegistration first = Navigator.AddObserver(new RecordingObserver(), _harness.Owner, "ObserverSite", 1);
            Navigator.AddObserver(new RecordingObserver(), _harness.Owner, "ObserverSite", 2);
            Assume.That(Capture().ObserverCount, Is.EqualTo(observers + 2), "control: both observers were counted");

            first.Remove();

            NavigatorState after = Capture();
            Assert.That(after.ObserverCount, Is.EqualTo(observers + 1), "observers");
            Assert.That(after.RegistrationCount, Is.EqualTo(registrations + 1), "registrations");
        }

        [Test]
        public void CaptureState_InputHoldRemoved_BringsTheRegistrationCountBack()
        {
            int registrations = Capture().RegistrationCount;
            ViewRegistration hold = Navigator.HoldInput("sync", _harness.Owner, "HoldSite", 1);
            Assume.That(Capture().RegistrationCount, Is.GreaterThan(registrations), "control: the hold was counted");

            hold.Remove();

            Assert.That(Capture().RegistrationCount, Is.EqualTo(registrations));
        }

        [Test]
        public void CaptureState_HarnessWithOneObserver_DoesNotCountTheEventRing()
        {
            Assert.That(Capture().ObserverCount, Is.EqualTo(1));
        }

        [Test]
        public void CaptureState_NothingRunning_IsSettled()
        {
            Assert.That(Capture().Busy, Is.False);
        }

        [Test]
        public void CaptureState_ViewLoading_IsBusyUntilItIsShown()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");

            Assert.That(Capture().Busy, Is.True, "while it loads");

            _harness.Stage.CompleteAcquire();

            Assert.That(Capture().Busy, Is.False, "once it is shown");
        }

        [Test]
        public void CaptureState_InputHold_IsBusyUntilItIsRemoved()
        {
            ViewRegistration hold = Navigator.HoldInput("sync", _harness.Owner, "HoldSite", 1);

            Assert.That(Capture().Busy, Is.True, "while held");

            hold.Remove();

            Assert.That(Capture().Busy, Is.False, "once removed");
        }

        // CaptureState: root gone, arguments

        [Test]
        public void CaptureState_AfterTeardown_ReportsRootGoneWithEmptyLists()
        {
            _harness.Row("home").Row("k", ViewLayers.Popups, ViewRetention.KeepInstance).Row("pre");
            Open("home");
            Open("k").Close();
            Navigator.HoldInput("sync", _harness.Owner, "HoldSite", 1);
            Navigator.PreloadAsync("pre", _harness.Owner, "PreloadSite", 1).Forget();
            NavigatorState populated = Capture();
            Assume.That(populated.Layers.Count, Is.GreaterThan(0), "control: the first capture has layers");
            Assume.That(populated.ShieldHolders.Count, Is.GreaterThan(0), "control: the first capture has holders");

            Navigator.Teardown();
            NavigatorState state = Capture();

            Assert.That(state.RootGone, Is.True, "root gone");
            Assert.That(state.Layers, Is.Empty, "layers");
            Assert.That(state.ShieldHolders, Is.Empty, "shield holders");
            Assert.That(state.KeptViewIds, Is.Empty, "kept ids");
            Assert.That(state.PreloadViewIds, Is.Empty, "preload ids");
            Assert.That(state.ShieldCount, Is.Zero, "shield count");
            Assert.That(state.Busy, Is.False, "busy");
        }

        [Test]
        public void CaptureState_RunningNavigator_IsNotRootGone()
        {
            Assert.That(Capture().RootGone, Is.False);
        }

        [Test]
        public void CaptureState_NullState_ThrowsArgumentNullException()
        {
            Assert.That(() => { Navigator.CaptureState(null); }, Throws.ArgumentNullException);
        }

        // CaptureState: allocation

        [Test]
        public void CaptureState_AfterOneWarmCall_AllocatesNothing()
        {
            BuildBusyNavigator();
            Navigator.CaptureState(_state);
            TestDelegate capture = () => { Navigator.CaptureState(_state); };

            Assert.That(capture, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void CaptureState_AfterSeveralWarmCalls_AllocatesNothing()
        {
            BuildBusyNavigator();
            Navigator.CaptureState(_state);
            Navigator.CaptureState(_state);
            Navigator.CaptureState(_state);
            TestDelegate capture = () => { Navigator.CaptureState(_state); };

            Assert.That(capture, Is.Not.AllocatingGCMemory());
        }

        // Event ring

        [Test]
        public void CopyRecentEvents_NothingPublished_ReturnsZeroAndAnEmptyList()
        {
            var events = new List<RecentEvent>();

            int count = Navigator.CopyRecentEvents(events);

            Assert.That(count, Is.Zero, "count");
            Assert.That(events, Is.Empty, "list");
        }

        [Test]
        public void CopyRecentEvents_ListWithOldContent_IsClearedFirst()
        {
            _harness.Row("p", ViewLayers.Popups);
            Open("p");
            var events = new List<RecentEvent> { QueueRecent(900), QueueRecent(901), QueueRecent(902) };

            int count = Navigator.CopyRecentEvents(events);

            Assert.That(events.Count, Is.EqualTo(count), "count matches the list");
            Assert.That(events, Has.None.Matches<RecentEvent>(recent => recent.Event.RequestId >= 900));
        }

        [Test]
        public void CopyRecentEvents_OpenCloseAndUnknownId_HoldsRequestedShownClosedNotShownAndSettled()
        {
            _harness.Row("p", ViewLayers.Popups);
            Open("p").Close();
            Open("ghost");
            Assume.That(
                _harness.Observer.CountOf(ViewEventKind.Settled),
                Is.GreaterThan(0),
                "control: the navigator published Settled in this flow");

            List<RecentEvent> events = Recent();

            ViewEventKind[] kinds = KindsOf(events);
            Assert.That(
                kinds,
                Is.SupersetOf(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed, ViewEventKind.NotShown, ViewEventKind.Settled }));
        }

        [Test]
        public void CopyRecentEvents_EventsFromTheNavigator_AreNotMarkedAsQueueEvents()
        {
            _harness.Row("p", ViewLayers.Popups);
            Open("p").Close();

            List<RecentEvent> events = Recent();

            Assert.That(events, Is.Not.Empty, "control: events were recorded");
            Assert.That(events, Has.All.Matches<RecentEvent>(recent => !recent.FromQueue));
        }

        [Test]
        public void CopyRecentEvents_FewEvents_ReturnsThemNewestFirstInPublishOrder()
        {
            _harness.Row("p", ViewLayers.Popups);
            Open("p").Close();
            Open("ghost");

            List<RecentEvent> events = Recent();

            Assert.That(Summarize(events), Is.EqualTo(SummarizeObservedNewestFirst(_harness.Observer.Events, 100)));
        }

        [Test]
        public void CopyRecentEvents_MoreThanAHundredEvents_KeepsOnlyTheNewestHundredInOrder()
        {
            _harness.Row("p", ViewLayers.Popups);
            OpenAndClose("p", 40);
            Assume.That(_harness.Observer.Count, Is.GreaterThan(100), "control: more than 100 events were published");

            List<RecentEvent> events = Recent();

            Assert.That(events.Count, Is.EqualTo(100), "count");
            Assert.That(Summarize(events), Is.EqualTo(SummarizeObservedNewestFirst(_harness.Observer.Events, 100)), "the newest hundred, newest first");
        }

        [Test]
        public void CopyRecentEvents_MoreThanAHundredEvents_NumbersThemByOneFromTheNewest()
        {
            _harness.Row("p", ViewLayers.Popups);
            OpenAndClose("p", 40);
            Assume.That(_harness.Observer.Count, Is.GreaterThan(100), "control: more than 100 events were published");

            List<RecentEvent> events = Recent();

            var expected = new long[events.Count];
            for (var i = 0; i < expected.Length; i++)
                expected[i] = _harness.Observer.Count - i;

            Assert.That(SequencesOf(events), Is.EqualTo(expected));
        }

        [Test]
        public void RecordQueueEvent_MoreThanAHundredEvents_KeepsTheNewestHundredInOrder()
        {
            for (var id = 1; id <= 150; id++)
            {
                ViewEvent viewEvent = QueueEvent(id);
                Navigator.RecordQueueEvent(in viewEvent);
            }

            List<RecentEvent> events = Recent();

            Assert.That(events.Count, Is.EqualTo(100), "count");
            Assert.That(RequestIdsOf(events), Is.EqualTo(DescendingIds(150, 100)), "request ids, newest first");
        }

        [Test]
        public void RecordQueueEvent_Event_IsMarkedAsFromTheQueue()
        {
            ViewEvent viewEvent = QueueEvent(5);

            Navigator.RecordQueueEvent(in viewEvent);

            List<RecentEvent> events = Recent();
            Assert.That(events.Count, Is.EqualTo(1), "count");
            Assert.That(events[0].FromQueue, Is.True, "from queue");
            Assert.That(events[0].Event.RequestId, Is.EqualTo(5), "request id");
        }

        [Test]
        public void RecordQueueEvent_AfterTheRingIsFull_AllocatesNothing()
        {
            for (var id = 1; id <= 150; id++)
            {
                ViewEvent filler = QueueEvent(id);
                Navigator.RecordQueueEvent(in filler);
            }

            ViewEvent viewEvent = QueueEvent(151);
            TestDelegate record = () => { Navigator.RecordQueueEvent(in viewEvent); };

            Assert.That(record, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void RecordQueueEvent_BeforeTheRingIsFull_AllocatesNothing()
        {
            ViewEvent viewEvent = QueueEvent(1);
            TestDelegate record = () => { Navigator.RecordQueueEvent(in viewEvent); };

            Assert.That(record, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void RecordQueueEvent_AfterOneWarmCallBeforeTheRingIsFull_AllocatesNothing()
        {
            ViewEvent warm = QueueEvent(1);
            Navigator.RecordQueueEvent(in warm);
            ViewEvent viewEvent = QueueEvent(2);
            Assume.That(Recent().Count, Is.LessThan(100), "control: the ring is not full yet");
            TestDelegate record = () => { Navigator.RecordQueueEvent(in viewEvent); };

            Assert.That(record, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void CopyRecentEvents_IntoAWarmList_AllocatesNothing()
        {
            for (var id = 1; id <= 150; id++)
            {
                ViewEvent viewEvent = QueueEvent(id);
                Navigator.RecordQueueEvent(in viewEvent);
            }

            var events = new List<RecentEvent>(100);
            Navigator.CopyRecentEvents(events);
            TestDelegate copy = () => { Navigator.CopyRecentEvents(events); };

            Assert.That(copy, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void CopyRecentEvents_NullList_ThrowsArgumentNullException()
        {
            Assert.That(() => { Navigator.CopyRecentEvents(null); }, Throws.ArgumentNullException);
        }

        // Event ring: queue events

        [Test]
        public void Enqueue_OnARegisteredQueue_WritesAnEnqueuedEventMarkedAsFromTheQueue()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");

            int id = Enqueue(queue, "offer");

            List<RecentEvent> events = Recent();
            Assert.That(events.Count, Is.EqualTo(1), "count");
            Assert.That(events[0].Event.Kind, Is.EqualTo(ViewEventKind.Enqueued), "kind");
            Assert.That(events[0].FromQueue, Is.True, "from queue");
            Assert.That(events[0].Event.RequestId, Is.EqualTo(id), "request id");
            Assert.That(events[0].Event.ViewId, Is.EqualTo("offer"), "view id");
            Assert.That(events[0].Event.LayerId, Is.EqualTo("popups"), "layer id");
        }

        [Test]
        public void Enqueue_ThenPresent_MarksOnlyTheQueueEventsAsFromTheQueue()
        {
            _harness.Row("offer", ViewLayers.Popups);
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            Enqueue(queue, "offer");

            _ticker.Tick();

            List<RecentEvent> events = Recent();
            Assert.That(KindsOf(events), Does.Contain(ViewEventKind.Requested), "control: the navigator took the request");
            Assert.That(events, Has.Exactly(1).Matches<RecentEvent>(recent => recent.FromQueue), "one queue event");
            Assert.That(events, Has.Exactly(1).Matches<RecentEvent>(recent => recent.FromQueue && recent.Event.Kind == ViewEventKind.Enqueued), "which is Enqueued");
        }

        [Test]
        public void Enqueue_PresentAndClear_KeepsQueueAndNavigatorEventsInOneContiguousSequence()
        {
            _harness.Row("offer", ViewLayers.Popups);
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            int offer = Enqueue(queue, "offer");
            _ticker.Tick();
            int second = Enqueue(queue, "second");
            queue.Clear();
            Assume.That(Navigator.IsOpen("offer"), Is.True, "control: the queue presented the first item");
            IReadOnlyList<ViewEvent> observed = _harness.Observer.Events;

            // Newest first: the queue's NotShown and Enqueued for the second item, the navigator's events, then the first Enqueued.
            var expectedLines = new List<string> { "NotShown #" + second + " second", "Enqueued #" + second + " second" };
            var expectedFromQueue = new List<bool> { true, true };
            for (var i = observed.Count - 1; i >= 0; i--)
            {
                expectedLines.Add(observed[i].Kind + " #" + observed[i].RequestId + " " + observed[i].ViewId);
                expectedFromQueue.Add(false);
            }

            expectedLines.Add("Enqueued #" + offer + " offer");
            expectedFromQueue.Add(true);

            List<RecentEvent> events = Recent();

            Assert.That(Summarize(events), Is.EqualTo(expectedLines), "order");
            Assert.That(FromQueueOf(events), Is.EqualTo(expectedFromQueue), "marks");
            var expectedSequences = new long[events.Count];
            for (var i = 0; i < expectedSequences.Length; i++)
                expectedSequences[i] = events.Count - i;

            Assert.That(SequencesOf(events), Is.EqualTo(expectedSequences), "sequence numbers");
        }

        [Test]
        public void LifetimeEnd_WithWaitingItems_StillWritesTheirNotShownEventsMarkedAsFromTheQueue()
        {
            CancellationTokenSource lifetime = NewLifetime();
            ViewQueue queue = CreateQueue(lifetime.Token, "flow");
            queue.Hold("tutorial", _harness.Owner);
            int first = Enqueue(queue, "a");
            int second = Enqueue(queue, "b");

            lifetime.Cancel();

            Assert.That(Navigator.CopyQueues(new List<ViewQueue>()), Is.Zero, "control: the queue unregistered");
            List<RecentEvent> events = Recent();
            Assert.That(KindsOf(events), Is.EqualTo(new[] { ViewEventKind.NotShown, ViewEventKind.NotShown, ViewEventKind.Enqueued, ViewEventKind.Enqueued }), "kinds");
            Assert.That(RequestIdsOf(events), Is.EqualTo(new[] { second, first, second, first }), "request ids");
            Assert.That(events, Has.All.Matches<RecentEvent>(recent => recent.FromQueue), "from queue");
            Assert.That(events[0].Event.Rejected, Is.EqualTo(RejectReason.Cancelled), "second item rejected");
            Assert.That(events[1].Event.Rejected, Is.EqualTo(RejectReason.Cancelled), "first item rejected");
        }

        [Test]
        public void Enqueue_ItemDeniedByAGate_WritesANotShownEventMarkedAsFromTheQueue()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            queue.AddGate(new DenyGate("closed for the night"), _harness.Owner);

            int id = Enqueue(queue, "offer");

            List<RecentEvent> events = Recent();
            Assert.That(events.Count, Is.EqualTo(1), "count: the item never entered the queue, so there is no Enqueued");
            Assert.That(events[0].Event.Kind, Is.EqualTo(ViewEventKind.NotShown), "kind");
            Assert.That(events[0].FromQueue, Is.True, "from queue");
            Assert.That(events[0].Event.RequestId, Is.EqualTo(id), "request id");
            Assert.That(events[0].Event.Rejected, Is.EqualTo(RejectReason.Denied), "rejected");
            Assert.That(events[0].Event.Detail, Is.EqualTo("closed for the night"), "detail");
        }

        [Test]
        public void Clear_WithWaitingItems_WritesOneNotShownEventPerItemMarkedAsFromTheQueue()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            queue.Hold("tutorial", _harness.Owner);
            int first = Enqueue(queue, "a");
            int second = Enqueue(queue, "b");

            queue.Clear();

            List<RecentEvent> events = Recent();
            Assert.That(KindsOf(events), Is.EqualTo(new[] { ViewEventKind.NotShown, ViewEventKind.NotShown, ViewEventKind.Enqueued, ViewEventKind.Enqueued }), "kinds");
            Assert.That(RequestIdsOf(events), Is.EqualTo(new[] { second, first, second, first }), "request ids");
            Assert.That(events, Has.All.Matches<RecentEvent>(recent => recent.FromQueue), "from queue");
            Assert.That(events[0].Event.Rejected, Is.EqualTo(RejectReason.Cancelled), "rejected");
        }

        [Test]
        public void Enqueue_OnAQueueOverAFakeNavigator_WritesNothingToThePackageNavigatorsRing()
        {
            var fake = new FakeNavigator();
            ViewQueue queue = CreateQueueOn(fake, NewLifetime().Token, "fake");

            Enqueue(queue, "offer");

            Assert.That(Recent(), Is.Empty);
        }

        // Queue registry

        [Test]
        public void CopyQueues_QueueBuiltOnThePackageNavigator_ListsIt()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            var queues = new List<ViewQueue>();

            int count = Navigator.CopyQueues(queues);

            Assert.That(count, Is.EqualTo(1), "count");
            Assert.That(queues, Has.Count.EqualTo(1), "list");
            Assert.That(queues[0], Is.SameAs(queue), "the queue");
        }

        [Test]
        public void CopyQueues_TwoQueues_ListsThemInRegistrationOrder()
        {
            ViewQueue first = CreateQueue(NewLifetime().Token, "first");
            ViewQueue second = CreateQueue(NewLifetime().Token, "second");
            var queues = new List<ViewQueue>();

            Navigator.CopyQueues(queues);

            Assert.That(queues, Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public void CopyQueues_LifetimeEnds_TheQueueLeavesTheList()
        {
            CancellationTokenSource lifetime = NewLifetime();
            CreateQueue(lifetime.Token, "flow");

            lifetime.Cancel();

            Assert.That(Navigator.CopyQueues(new List<ViewQueue>()), Is.Zero);
        }

        [Test]
        public void CopyQueues_OneOfTwoLifetimesEnds_KeepsTheOther()
        {
            CancellationTokenSource firstLifetime = NewLifetime();
            CreateQueue(firstLifetime.Token, "first");
            ViewQueue second = CreateQueue(NewLifetime().Token, "second");
            var queues = new List<ViewQueue>();

            firstLifetime.Cancel();
            Navigator.CopyQueues(queues);

            Assert.That(queues, Is.EqualTo(new[] { second }));
        }

        [Test]
        public void CopyQueues_LifetimeCancelledOffTheMainThread_ListsTheQueueUntilTheMainThreadRuns()
        {
            CancellationTokenSource lifetime = NewLifetime();
            ViewQueue queue = CreateQueue(lifetime.Token, "flow");
            queue.Hold("tutorial", _harness.Owner);
            Enqueue(queue, "a");
            var queues = new List<ViewQueue>();

            _harness.MainThread.IsMainThread = false;
            lifetime.Cancel();
            _harness.MainThread.IsMainThread = true;

            Assert.That(Navigator.CopyQueues(queues), Is.EqualTo(1), "still listed before the main thread runs");
            Assert.That(queues[0], Is.SameAs(queue), "the queue");
            QueueState before = CaptureQueue(queue);
            Assert.That(before.Inert, Is.False, "not inert yet");
            Assert.That(before.Waiting.Count, Is.EqualTo(1), "the waiting item is untouched");
            Assert.That(_harness.Diagnostics.Has(DiagnosticIds.OwnerCancelledOffThread), Is.True, "UIDIRECTOR113 is reported");

            _harness.MainThread.Flush();

            Assert.That(Navigator.CopyQueues(queues), Is.Zero, "gone after the main thread ran");
            QueueState after = CaptureQueue(queue);
            Assert.That(after.Inert, Is.True, "inert");
            Assert.That(after.Waiting, Is.Empty, "waiting");
        }

        [Test]
        public void CopyQueues_LifetimeAlreadyEnded_TheQueueIsNeverListed()
        {
            CreateQueue(new CancellationToken(true), "ended");

            Assert.That(Navigator.CopyQueues(new List<ViewQueue>()), Is.Zero);
        }

        [Test]
        public void CopyQueues_QueueBuiltOnAFakeNavigator_IsNotRegisteredOnThePackageNavigator()
        {
            var fake = new FakeNavigator();
            ViewQueue queue = CreateQueueOn(fake, NewLifetime().Token, "fake");

            Assert.That(Navigator.CopyQueues(new List<ViewQueue>()), Is.Zero, "registry");

            var state = new QueueState();
            queue.CaptureState(state);
            Assert.That(state.Name, Is.EqualTo("fake"), "the queue still reports its own state");
        }

        [Test]
        public void CopyQueues_AfterTeardown_ListsNothing()
        {
            CreateQueue(NewLifetime().Token, "flow");

            Navigator.Teardown();

            Assert.That(Navigator.CopyQueues(new List<ViewQueue>()), Is.Zero);
        }

        [Test]
        public void CopyQueues_ListWithOldContent_IsClearedFirst()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            var queues = new List<ViewQueue> { null, null };

            Navigator.CopyQueues(queues);

            Assert.That(queues, Is.EqualTo(new[] { queue }));
        }

        [Test]
        public void CopyQueues_NullList_ThrowsArgumentNullException()
        {
            Assert.That(() => { Navigator.CopyQueues(null); }, Throws.ArgumentNullException);
        }

        // ViewQueue.CaptureState

        [Test]
        public void ViewQueueCaptureState_NamedQueue_GivesNameLayerAndAnEmptyState()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "shop-flow");

            QueueState state = CaptureQueue(queue);

            Assert.That(state.Name, Is.EqualTo("shop-flow"), "name");
            Assert.That(state.LayerId, Is.EqualTo("popups"), "layer id");
            Assert.That(state.Inert, Is.False, "inert");
            Assert.That(state.Held, Is.False, "held");
            Assert.That(state.GateCount, Is.Zero, "gate count");
            Assert.That(state.Holds, Is.Empty, "holds");
            Assert.That(state.HasPresenting, Is.False, "has presenting");
            Assert.That(state.Waiting, Is.Empty, "waiting");
        }

        [Test]
        public void ViewQueueCaptureState_QueueWithoutAName_UsesTheLayerIdAsItsName()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, null);

            Assert.That(CaptureQueue(queue).Name, Is.EqualTo("popups"));
        }

        [Test]
        public void ViewQueueCaptureState_Holds_ListsReasonsInOrderWithAgesAndMarksTheQueueHeld()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            queue.Hold("tutorial", _harness.Owner);
            _ticker.Advance(1.5);
            queue.Hold("cutscene", _harness.Owner);
            _ticker.Advance(1.0);

            QueueState state = CaptureQueue(queue);

            Assert.That(state.Held, Is.True, "held");
            Assert.That(state.Holds.Count, Is.EqualTo(2), "hold count");
            Assert.That(state.Holds[0].Reason, Is.EqualTo("tutorial"), "first reason");
            Assert.That(state.Holds[0].AgeSeconds, Is.EqualTo(2.5).Within(1e-6), "first age");
            Assert.That(state.Holds[1].Reason, Is.EqualTo("cutscene"), "second reason");
            Assert.That(state.Holds[1].AgeSeconds, Is.EqualTo(1.0).Within(1e-6), "second age");
        }

        [Test]
        public void ViewQueueCaptureState_LastHoldRemoved_QueueIsNoLongerHeld()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            ViewRegistration hold = queue.Hold("tutorial", _harness.Owner);

            hold.Remove();

            QueueState state = CaptureQueue(queue);
            Assert.That(state.Held, Is.False, "held");
            Assert.That(state.Holds, Is.Empty, "holds");
        }

        [Test]
        public void ViewQueueCaptureState_Gates_CountsThemAndDropsOneWhenItsRegistrationEnds()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            queue.AddGate(new AllowGate(), _harness.Owner);
            ViewRegistration second = queue.AddGate(new AllowGate(), _harness.Owner);

            Assert.That(CaptureQueue(queue).GateCount, Is.EqualTo(2), "two gates");

            second.Remove();

            Assert.That(CaptureQueue(queue).GateCount, Is.EqualTo(1), "one gate");
        }

        [Test]
        public void ViewQueueCaptureState_WaitingItems_ListsThemInPresentationOrderWithTheirFields()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            queue.Hold("tutorial", _harness.Owner);
            int low = Enqueue(queue, "low", 0, null);
            _ticker.Advance(1.0);
            int urgent = Enqueue(queue, "urgent", 10, "tutorial");
            int mid = Enqueue(queue, "mid", 5, null);
            int lowLater = Enqueue(queue, "lowLater", 0, null);
            _ticker.Advance(2.0);

            QueueState state = CaptureQueue(queue);

            Assert.That(state.HasPresenting, Is.False, "nothing is presented while held");
            Assert.That(WaitingViewIds(state), Is.EqualTo(new[] { "urgent", "mid", "low", "lowLater" }), "presentation order");
            Assert.That(WaitingRequestIds(state), Is.EqualTo(new[] { urgent, mid, low, lowLater }), "request ids");
            Assert.That(state.Waiting[0].Priority, Is.EqualTo(10), "urgent priority");
            Assert.That(state.Waiting[0].Source, Is.EqualTo("tutorial"), "urgent source");
            Assert.That(state.Waiting[0].AgeSeconds, Is.EqualTo(2.0).Within(1e-6), "urgent age");
            Assert.That(state.Waiting[2].AgeSeconds, Is.EqualTo(3.0).Within(1e-6), "low age");
        }

        [Test]
        public void ViewQueueCaptureState_ItemBeingPresented_IsThePresentingItemAndNotWaiting()
        {
            _harness.Row("high", ViewLayers.Popups).Row("mid", ViewLayers.Popups).Row("low", ViewLayers.Popups);
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            int low = Enqueue(queue, "low", 0, null);
            int mid = Enqueue(queue, "mid", 5, null);
            int high = Enqueue(queue, "high", 10, "daily");

            _ticker.Tick();

            QueueState state = CaptureQueue(queue);
            Assert.That(state.HasPresenting, Is.True, "has presenting");
            Assert.That(state.Presenting.ViewId, Is.EqualTo("high"), "presenting view id");
            Assert.That(state.Presenting.RequestId, Is.EqualTo(high), "presenting request id");
            Assert.That(state.Presenting.Priority, Is.EqualTo(10), "presenting priority");
            Assert.That(state.Presenting.Source, Is.EqualTo("daily"), "presenting source");
            Assert.That(WaitingRequestIds(state), Is.EqualTo(new[] { mid, low }), "waiting");
            Assert.That(Navigator.IsOpen("high"), Is.True, "control: the navigator shows the presented item");
        }

        [Test]
        public void ViewQueueCaptureState_LifetimeEnded_ReportsInertAndNothingWaiting()
        {
            CancellationTokenSource lifetime = NewLifetime();
            ViewQueue queue = CreateQueue(lifetime.Token, "flow");
            queue.Hold("tutorial", _harness.Owner);
            Enqueue(queue, "a");

            lifetime.Cancel();

            QueueState state = CaptureQueue(queue);
            Assert.That(state.Inert, Is.True, "inert");
            Assert.That(state.Waiting, Is.Empty, "waiting");
            Assert.That(state.Holds, Is.Empty, "holds");
        }

        [Test]
        public void ViewQueueCaptureState_StateWithOldContent_IsClearedFirst()
        {
            ViewQueue busy = CreateQueue(NewLifetime().Token, "busy");
            busy.Hold("tutorial", _harness.Owner);
            Enqueue(busy, "a");
            Enqueue(busy, "b");
            ViewQueue quiet = CreateQueue(NewLifetime().Token, "quiet");
            var state = new QueueState();
            busy.CaptureState(state);
            Assume.That(state.Waiting.Count, Is.EqualTo(2), "control: the first capture has items");

            quiet.CaptureState(state);

            Assert.That(state.Name, Is.EqualTo("quiet"), "name");
            Assert.That(state.Held, Is.False, "held");
            Assert.That(state.Holds, Is.Empty, "holds");
            Assert.That(state.Waiting, Is.Empty, "waiting");
        }

        [Test]
        public void ViewQueueCaptureState_AfterOneWarmCall_AllocatesNothing()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");
            queue.Hold("tutorial", _harness.Owner);
            queue.AddGate(new AllowGate(), _harness.Owner);
            Enqueue(queue, "a", 1, "x");
            Enqueue(queue, "b", 2, "y");
            var state = new QueueState();
            queue.CaptureState(state);
            TestDelegate capture = () => { queue.CaptureState(state); };

            Assert.That(capture, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void ViewQueueCaptureState_NullState_ThrowsArgumentNullException()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");

            Assert.That(() => { queue.CaptureState(null); }, Throws.ArgumentNullException);
        }

        // Helpers

        private Navigator Navigator
        {
            get { return _harness.Navigator; }
        }

        private ViewHandle Open(string id, string member = "OpenSite", int line = 1)
        {
            return Navigator.Open(ViewKey.Create(id), default(NoArgs), _harness.Owner, default(OpenOptions), member, line);
        }

        private void OpenAndClose(string id, int cycles)
        {
            for (var i = 0; i < cycles; i++)
            {
                ViewHandle handle = Open(id);
                handle.Close();
                Assume.That(handle.IsAlive, Is.False, "control: the close runs inline on the fake stage");
            }
        }

        // A navigator with stacks, a popup, a hold, a kept id and a lease, for the allocation tests.
        private void BuildBusyNavigator()
        {
            _harness.Row("home").Row("settings").Row("confirm", ViewLayers.Popups).Row("k", ViewLayers.Popups, ViewRetention.KeepInstance).Row("pre");
            Open("home");
            Open("settings");
            Open("confirm");
            Open("k").Close();
            Navigator.HoldInput("sync", _harness.Owner, "HoldSite", 1);
            // Not awaited: the fake stage completes the preload inside the call.
            Navigator.PreloadAsync("pre", _harness.Owner, "PreloadSite", 1).Forget();
        }

        private NavigatorState Capture()
        {
            Navigator.CaptureState(_state);
            return _state;
        }

        private List<RecentEvent> Recent()
        {
            var events = new List<RecentEvent>();
            Navigator.CopyRecentEvents(events);
            return events;
        }

        private CancellationTokenSource NewLifetime()
        {
            var lifetime = new CancellationTokenSource();
            _lifetimes.Add(lifetime);
            return lifetime;
        }

        private ViewQueue CreateQueue(CancellationToken lifetime, string name)
        {
            return CreateQueueOn(Navigator, lifetime, name);
        }

        private ViewQueue CreateQueueOn(INavigator navigator, CancellationToken lifetime, string name)
        {
            return new ViewQueue(
                navigator,
                ViewLayers.Popups,
                lifetime,
                name,
                _ticker,
                _harness.MainThread,
                Navigator.Errors,
                Navigator.Diagnostics);
        }

        private static int Enqueue(ViewQueue queue, string viewId, int priority = 0, string source = null)
        {
            return queue.Enqueue(ViewKey.Create(viewId), default(NoArgs), CancellationToken.None, new QueueOptions { Priority = priority, Source = source });
        }

        private static QueueState CaptureQueue(ViewQueue queue)
        {
            var state = new QueueState();
            queue.CaptureState(state);
            return state;
        }

        private static ViewEvent QueueEvent(int requestId)
        {
            return new ViewEvent(
                ViewEventKind.Enqueued,
                requestId,
                "x",
                "popups",
                null,
                0,
                default(CloseReason),
                RejectReason.None,
                null,
                0f,
                0f);
        }

        private static RecentEvent QueueRecent(int requestId)
        {
            ViewEvent viewEvent = QueueEvent(requestId);
            return new RecentEvent(in viewEvent, true, requestId);
        }

        private static LayerSnapshot LayerOf(NavigatorState state, string layerId)
        {
            for (var i = 0; i < state.Layers.Count; i++)
            {
                if (state.Layers[i].LayerId == layerId)
                    return state.Layers[i];
            }

            Assert.Fail("No layer '" + layerId + "' in the capture.");
            return null;
        }

        private static ShieldHolderSnapshot HolderOf(NavigatorState state, ShieldHolderKind kind)
        {
            for (var i = 0; i < state.ShieldHolders.Count; i++)
            {
                if (state.ShieldHolders[i].Kind == kind)
                    return state.ShieldHolders[i];
            }

            Assert.Fail("No shield holder of kind " + kind + " in the capture.");
            return default(ShieldHolderSnapshot);
        }

        private static string[] LayerIds(NavigatorState state)
        {
            var ids = new string[state.Layers.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = state.Layers[i].LayerId;
            return ids;
        }

        private static string[] ViewIdsOf(LayerSnapshot layer)
        {
            var ids = new string[layer.Requests.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = layer.Requests[i].ViewId;
            return ids;
        }

        private static int[] RequestIdsOf(LayerSnapshot layer)
        {
            var ids = new int[layer.Requests.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = layer.Requests[i].RequestId;
            return ids;
        }

        private static int[] SlotsOf(LayerSnapshot layer)
        {
            var slots = new int[layer.Requests.Count];
            for (var i = 0; i < slots.Length; i++)
                slots[i] = layer.Requests[i].Slot;
            return slots;
        }

        private static ViewPhase[] PhasesOf(LayerSnapshot layer)
        {
            var phases = new ViewPhase[layer.Requests.Count];
            for (var i = 0; i < phases.Length; i++)
                phases[i] = layer.Requests[i].Phase;
            return phases;
        }

        private static string[] ReasonsOf(NavigatorState state)
        {
            var reasons = new string[state.ShieldHolders.Count];
            for (var i = 0; i < reasons.Length; i++)
                reasons[i] = state.ShieldHolders[i].Reason;
            return reasons;
        }

        // One line per layer, request, holder, kept id and preload id, so two captures compare as arrays.
        private static string[] Summarize(NavigatorState state)
        {
            var lines = new List<string>();
            for (var i = 0; i < state.Layers.Count; i++)
            {
                LayerSnapshot layer = state.Layers[i];
                lines.Add("layer " + layer.LayerId + " running " + layer.IsRunning + " operations " + layer.PendingOperations);
                for (var j = 0; j < layer.Requests.Count; j++)
                {
                    RequestSnapshot request = layer.Requests[j];
                    lines.Add("  #" + request.RequestId + " " + request.ViewId + " " + request.Phase + " slot " + request.Slot + " close " + request.ClosePending);
                }
            }

            for (var i = 0; i < state.ShieldHolders.Count; i++)
                lines.Add("holder " + state.ShieldHolders[i].Kind + " #" + state.ShieldHolders[i].RequestId + " " + state.ShieldHolders[i].Reason);

            for (var i = 0; i < state.KeptViewIds.Count; i++)
                lines.Add("kept " + state.KeptViewIds[i]);

            for (var i = 0; i < state.PreloadViewIds.Count; i++)
                lines.Add("preload " + state.PreloadViewIds[i]);

            lines.Add("shield " + state.ShieldCount + " busy " + state.Busy + " gone " + state.RootGone);
            return lines.ToArray();
        }

        private static ViewEventKind[] KindsOf(List<RecentEvent> events)
        {
            var kinds = new ViewEventKind[events.Count];
            for (var i = 0; i < kinds.Length; i++)
                kinds[i] = events[i].Event.Kind;
            return kinds;
        }

        private static int[] RequestIdsOf(List<RecentEvent> events)
        {
            var ids = new int[events.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = events[i].Event.RequestId;
            return ids;
        }

        private static long[] SequencesOf(List<RecentEvent> events)
        {
            var sequences = new long[events.Count];
            for (var i = 0; i < sequences.Length; i++)
                sequences[i] = events[i].Sequence;
            return sequences;
        }

        private static bool[] FromQueueOf(List<RecentEvent> events)
        {
            var marks = new bool[events.Count];
            for (var i = 0; i < marks.Length; i++)
                marks[i] = events[i].FromQueue;
            return marks;
        }

        private static string[] Summarize(List<RecentEvent> events)
        {
            var lines = new string[events.Count];
            for (var i = 0; i < lines.Length; i++)
                lines[i] = events[i].Event.Kind + " #" + events[i].Event.RequestId + " " + events[i].Event.ViewId;
            return lines;
        }

        // What an observer saw, newest first, at most take events: the oracle for the ring.
        private static string[] SummarizeObservedNewestFirst(IReadOnlyList<ViewEvent> observed, int take)
        {
            var lines = new List<string>();
            for (var i = observed.Count - 1; i >= 0 && lines.Count < take; i--)
                lines.Add(observed[i].Kind + " #" + observed[i].RequestId + " " + observed[i].ViewId);
            return lines.ToArray();
        }

        private static int[] DescendingIds(int newest, int count)
        {
            var ids = new int[count];
            for (var i = 0; i < count; i++)
                ids[i] = newest - i;
            return ids;
        }

        private static string[] WaitingViewIds(QueueState state)
        {
            var ids = new string[state.Waiting.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = state.Waiting[i].ViewId;
            return ids;
        }

        private static int[] WaitingRequestIds(QueueState state)
        {
            var ids = new int[state.Waiting.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = state.Waiting[i].RequestId;
            return ids;
        }

        private sealed class AllowGate : IViewGate
        {
            public GateDecision Evaluate(in GateRequest request)
            {
                return GateDecision.Allow;
            }
        }

        private sealed class DenyGate : IViewGate
        {
            private readonly string _reason;

            public DenyGate(string reason)
            {
                _reason = reason;
            }

            public GateDecision Evaluate(in GateRequest request)
            {
                return GateDecision.Deny(_reason);
            }
        }
    }
}
