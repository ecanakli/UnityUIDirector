using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector.Tests;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.UIDirector.EditorTools.Tests
{
    /// <summary>The window model shows the stacks, the shield holders, the queues and the events of a navigator, and formats them for the view.</summary>
    [TestFixture]
    public sealed class UIDirectorWindowModelTests
    {
        private NavigatorHarness _harness;
        private ManualTicker _ticker;
        private UIDirectorWindowModel _model;
        private readonly List<CancellationTokenSource> _lifetimes = new List<CancellationTokenSource>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<ViewRoot> _initialisedRoots = new List<ViewRoot>();
        private NavigatorHarness _second;

        // What BuildRunningNavigator created.
        private ViewHandle _home;
        private ViewHandle _settings;
        private ViewHandle _confirm;
        private int _dailyId;
        private int _offerId;

        [SetUp]
        public void SetUp()
        {
            _harness = new NavigatorHarness();
            _ticker = new ManualTicker();
            _model = new UIDirectorWindowModel();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _lifetimes.Count; i++)
            {
                _lifetimes[i].Cancel();
                _lifetimes[i].Dispose();
            }

            _lifetimes.Clear();
            _harness.MainThread.Flush();
            _harness.Dispose();

            if (_second != null)
            {
                _second.MainThread.Flush();
                _second.Dispose();
                _second = null;
            }

            for (var i = 0; i < _initialisedRoots.Count; i++)
                EndRoot(_initialisedRoots[i]);

            _initialisedRoots.Clear();

            for (var i = 0; i < _objects.Count; i++)
                UnityEngine.Object.DestroyImmediate(_objects[i]);

            _objects.Clear();
        }

        // Refresh(Navigator): a running navigator on the fake stage

        [Test]
        public void Refresh_RunningNavigator_ListsTheStacksInLayerOrder()
        {
            BuildRunningNavigator();

            _model.Refresh(Navigator);

            Assert.That(_model.HasNavigator, Is.True, "has navigator");
            Assert.That(LayerIds(), Is.EqualTo(new[] { "screens", "popups", "system" }), "layers");
            Assert.That(ViewIds(_model.State.Layers[0]), Is.EqualTo(new[] { "home", "settings" }), "screens");
            Assert.That(RequestIds(_model.State.Layers[0]), Is.EqualTo(new[] { _home.RequestId, _settings.RequestId }), "screen request ids");
            Assert.That(ViewIds(_model.State.Layers[1]), Is.EqualTo(new[] { "confirm" }), "popups");
            Assert.That(RequestIds(_model.State.Layers[1]), Is.EqualTo(new[] { _confirm.RequestId }), "popup request ids");
            Assert.That(_model.State.Layers[2].Requests, Is.Empty, "system");
        }

        [Test]
        public void Refresh_RunningNavigator_ListsTheShieldHolders()
        {
            BuildRunningNavigator();

            _model.Refresh(Navigator);

            Assert.That(_model.State.ShieldCount, Is.EqualTo(1), "count");
            Assert.That(_model.State.ShieldHolders.Count, Is.EqualTo(1), "holders");
            ShieldHolderSnapshot holder = _model.State.ShieldHolders[0];
            Assert.That(holder.Kind, Is.EqualTo(ShieldHolderKind.InputHold), "kind");
            Assert.That(holder.Reason, Is.EqualTo("sync"), "reason");
            Assert.That(holder.CallSite, Is.EqualTo("ExitCheck (line 12)"), "call site");
        }

        [Test]
        public void Refresh_RunningNavigator_ListsTheQueueWithItsHoldAndItsWaitingItemsInOrder()
        {
            BuildRunningNavigator();

            _model.Refresh(Navigator);

            Assert.That(_model.Queues.Count, Is.EqualTo(1), "queue count");
            QueueState queue = _model.Queues[0];
            Assert.That(queue.Name, Is.EqualTo("onboarding"), "name");
            Assert.That(queue.LayerId, Is.EqualTo("popups"), "layer id");
            Assert.That(queue.Held, Is.True, "held");
            Assert.That(queue.Holds.Count, Is.EqualTo(1), "hold count");
            Assert.That(queue.Holds[0].Reason, Is.EqualTo("tutorial"), "hold reason");
            Assert.That(queue.Waiting.Count, Is.EqualTo(2), "waiting count");
            Assert.That(queue.Waiting[0].RequestId, Is.EqualTo(_dailyId), "first waiting request id");
            Assert.That(queue.Waiting[0].ViewId, Is.EqualTo("daily"), "first waiting view id");
            Assert.That(queue.Waiting[1].RequestId, Is.EqualTo(_offerId), "second waiting request id");
            Assert.That(queue.Waiting[1].ViewId, Is.EqualTo("offer"), "second waiting view id");
        }

        [Test]
        public void Refresh_RunningNavigator_FormatsARequestAsOneRow()
        {
            BuildRunningNavigator();

            _model.Refresh(Navigator);

            RequestSnapshot home = _model.State.Layers[0].Requests[0];
            Assert.That(UIDirectorWindowModel.FormatRequest(home), Is.EqualTo("#" + _home.RequestId + " home Open slot 1"));
        }

        [Test]
        public void Refresh_RunningNavigator_FormatsAQueueItemWithItsPriorityAndSource()
        {
            BuildRunningNavigator();

            _model.Refresh(Navigator);

            QueueItemSnapshot daily = _model.Queues[0].Waiting[0];
            Assert.That(UIDirectorWindowModel.FormatQueueItem(daily), Does.StartWith("#" + _dailyId + " daily priority 5 source calendar age "));
        }

        [Test]
        public void Refresh_RunningNavigator_FormatsTheShieldHolderWithItsReasonAndCallSite()
        {
            BuildRunningNavigator();

            _model.Refresh(Navigator);

            ShieldHolderSnapshot holder = _model.State.ShieldHolders[0];
            string line = UIDirectorWindowModel.FormatShieldHolder(holder);
            Assert.That(line, Does.StartWith("InputHold"), "kind");
            Assert.That(line, Does.Contain("sync"), "reason");
            Assert.That(line, Does.EndWith("at ExitCheck (line 12)"), "call site");
        }

        [Test]
        public void Refresh_CalledTwice_DoesNotDuplicateRows()
        {
            BuildRunningNavigator();
            _model.Refresh(Navigator);

            _model.Refresh(Navigator);

            Assert.That(_model.State.Layers.Count, Is.EqualTo(3), "layers");
            Assert.That(_model.State.Layers[0].Requests.Count, Is.EqualTo(2), "screen requests");
            Assert.That(_model.State.ShieldHolders.Count, Is.EqualTo(1), "holders");
            Assert.That(_model.Queues.Count, Is.EqualTo(1), "queues");
            Assert.That(_model.Queues[0].Waiting.Count, Is.EqualTo(2), "waiting items");
        }

        [Test]
        public void Refresh_StateChangedBetweenCalls_ShowsTheNewState()
        {
            BuildRunningNavigator();
            _model.Refresh(Navigator);

            _settings.Close();
            _model.Refresh(Navigator);

            Assert.That(ViewIds(_model.State.Layers[0]), Is.EqualTo(new[] { "home" }));
        }

        [Test]
        public void Refresh_QueueLifetimeEnded_DropsTheQueueAndLeavesNoStaleItemsOnTheOneThatRemains()
        {
            CancellationTokenSource firstLifetime = NewLifetime();
            ViewQueue first = CreateQueue(firstLifetime.Token, "first");
            first.Hold("tutorial", _harness.Owner);
            Enqueue(first, "a", 0, null);
            Enqueue(first, "b", 0, null);
            CreateQueue(NewLifetime().Token, "second");
            _model.Refresh(Navigator);
            Assume.That(_model.Queues.Count, Is.EqualTo(2), "control: both queues are listed");
            Assume.That(_model.Queues[0].Waiting.Count, Is.EqualTo(2), "control: the first queue has items");

            firstLifetime.Cancel();
            _model.Refresh(Navigator);

            Assert.That(_model.Queues.Count, Is.EqualTo(1), "queue count");
            Assert.That(_model.Queues[0].Name, Is.EqualTo("second"), "name");
            Assert.That(_model.Queues[0].Held, Is.False, "held");
            Assert.That(_model.Queues[0].Holds, Is.Empty, "holds");
            Assert.That(_model.Queues[0].Waiting, Is.Empty, "waiting");
        }

        [Test]
        public void Refresh_NoQueue_ShowsNoQueues()
        {
            _model.Refresh(Navigator);

            Assert.That(_model.Queues, Is.Empty);
        }

        [Test]
        public void Refresh_ClosedKeepInstanceViewAndHeldPreloadLease_ListsTheKeptAndPreloadIds()
        {
            _harness.Row("k", ViewLayers.Popups, ViewRetention.KeepInstance).Row("pre");
            Open("k").Close();
            // Not awaited: the fake stage completes the preload inside the call.
            Navigator.PreloadAsync("pre", _harness.Owner, "PreloadSite", 1).Forget();

            _model.Refresh(Navigator);

            Assert.That(_model.State.KeptViewIds, Is.EqualTo(new[] { "k" }), "kept ids");
            Assert.That(_model.State.PreloadViewIds, Is.EqualTo(new[] { "pre" }), "preload ids");
        }

        // Switching navigators, the window's root picker

        [Test]
        public void Refresh_SwitchingToAnEmptyNavigator_LeavesNothingOfTheFirstOne()
        {
            BuildRunningNavigator();
            _model.Refresh(Navigator);
            Assume.That(_model.Queues.Count, Is.EqualTo(1), "control: the first navigator has a queue");
            Assume.That(_model.Events.Count, Is.GreaterThan(0), "control: the first navigator has events");
            _second = new NavigatorHarness();

            _model.Refresh(_second.Navigator);

            Assert.That(_model.Queues, Is.Empty, "queues");
            Assert.That(_model.Events, Is.Empty, "events");
            Assert.That(LayerIds(), Is.EqualTo(new[] { "screens", "popups", "system" }), "layers");
            Assert.That(_model.State.Layers, Has.All.Matches<LayerSnapshot>(layer => layer.Requests.Count == 0), "requests");
            Assert.That(_model.State.ShieldHolders, Is.Empty, "shield holders");
            Assert.That(_model.State.KeptViewIds, Is.Empty, "kept ids");
            Assert.That(_model.State.PreloadViewIds, Is.Empty, "preload ids");
            Assert.That(_model.StatusLine(), Is.EqualTo("Settled, shield 0"), "status");
        }

        [Test]
        public void Refresh_SwitchingBetweenTwoLiveNavigators_ShowsOnlyTheOneRefreshed()
        {
            BuildRunningNavigator();
            _second = new NavigatorHarness();
            _second.Row("other", ViewLayers.Popups);
            _second.Navigator.Open(ViewKey.Create("other"), default(NoArgs), _second.Owner, default(OpenOptions), "OtherOpenSite", 1);
            _second.Navigator.HoldInput("other hold", _second.Owner, "OtherHoldSite", 5);

            _model.Refresh(_second.Navigator);

            Assert.That(ViewIds(_model.State.Layers[0]), Is.Empty, "screens of the second navigator");
            Assert.That(ViewIds(_model.State.Layers[1]), Is.EqualTo(new[] { "other" }), "popups of the second navigator");
            Assert.That(_model.State.ShieldHolders.Count, Is.EqualTo(1), "holder count");
            Assert.That(_model.State.ShieldHolders[0].Reason, Is.EqualTo("other hold"), "holder reason");
            Assert.That(_model.Queues, Is.Empty, "queues");
            Assert.That(_model.Events.Count, Is.EqualTo(_second.Observer.Count), "events are the second navigator's own");
            Assert.That(_model.Events, Has.All.Matches<RecentEvent>(recent => !recent.FromQueue), "no queue events");

            _model.Refresh(Navigator);

            Assert.That(ViewIds(_model.State.Layers[0]), Is.EqualTo(new[] { "home", "settings" }), "screens of the first navigator");
            Assert.That(ViewIds(_model.State.Layers[1]), Is.EqualTo(new[] { "confirm" }), "popups of the first navigator");
            Assert.That(_model.State.ShieldHolders.Count, Is.EqualTo(1), "holder count again");
            Assert.That(_model.State.ShieldHolders[0].Reason, Is.EqualTo("sync"), "holder reason again");
            Assert.That(_model.Queues.Count, Is.EqualTo(1), "queue again");
            Assert.That(_model.Events.Count, Is.EqualTo(_harness.Observer.Count + 2), "the first navigator's events and the queue's two Enqueued events");
        }

        [Test]
        public void Describe_AfterSwitchingNavigators_GivesTheNewNavigatorsTrace()
        {
            BuildRunningNavigator();
            _model.Refresh(Navigator);
            _second = new NavigatorHarness();

            _model.Refresh(_second.Navigator);

            Assert.That(_model.Describe(), Is.EqualTo(DescribeOf(_second.Navigator)));
        }

        // Refresh(ViewRoot) with an initialised root

        [Test]
        public void Refresh_InitialisedRoot_ShowsItsNavigator()
        {
            ViewRoot root = CreateInitialisedRoot();

            _model.Refresh(root);

            Assert.That(_model.HasNavigator, Is.True, "has navigator");
            Assert.That(LayerIds(), Is.EqualTo(new[] { "screens", "popups", "system" }), "layers");
            Assert.That(_model.StatusLine(), Does.StartWith("Settled"), "status");
            Assert.That(_model.Describe(), Is.EqualTo(DescribeOf(root.Core)), "describe");
        }

        [Test]
        public void Refresh_InitialisedRootAfterARunningNavigator_ShowsTheRootsNavigatorOnly()
        {
            BuildRunningNavigator();
            _model.Refresh(Navigator);
            ViewRoot root = CreateInitialisedRoot();

            _model.Refresh(root);

            Assert.That(_model.State.Layers, Has.All.Matches<LayerSnapshot>(layer => layer.Requests.Count == 0), "requests");
            Assert.That(_model.State.ShieldHolders, Is.Empty, "shield holders");
            Assert.That(_model.Queues, Is.Empty, "queues");
            Assert.That(_model.Events, Is.Empty, "events");
        }

        // Events

        [Test]
        public void Refresh_NothingPublished_ShowsNoEvents()
        {
            _model.Refresh(Navigator);

            Assert.That(_model.Events, Is.Empty);
        }

        [Test]
        public void Refresh_EventsPublished_ShowsThemNewestFirst()
        {
            _harness.Row("p", ViewLayers.Popups);
            OpenAndClose("p", 1);

            _model.Refresh(Navigator);

            IReadOnlyList<ViewEvent> observed = _harness.Observer.Events;
            Assume.That(observed.Count, Is.GreaterThan(1), "control: several events were published");
            Assert.That(_model.Events.Count, Is.EqualTo(observed.Count), "count");
            Assert.That(_model.Events[0].Event.Kind, Is.EqualTo(observed[observed.Count - 1].Kind), "newest kind");
            Assert.That(_model.Events[0].Event.RequestId, Is.EqualTo(observed[observed.Count - 1].RequestId), "newest request id");
            Assert.That(_model.Events[_model.Events.Count - 1].Event.Kind, Is.EqualTo(ViewEventKind.Requested), "oldest kind");
        }

        [Test]
        public void Refresh_QueueEventWritten_ShowsItMarkedAsFromTheQueue()
        {
            ViewQueue queue = CreateQueue(NewLifetime().Token, "flow");

            Enqueue(queue, "offer", 3, "tutorial");
            _model.Refresh(Navigator);

            Assert.That(_model.Events.Count, Is.EqualTo(1), "count");
            Assert.That(_model.Events[0].FromQueue, Is.True, "from queue");
            Assert.That(_model.Events[0].Event.Kind, Is.EqualTo(ViewEventKind.Enqueued), "kind");
        }

        [Test]
        public void Refresh_MoreThanAHundredEvents_KeepsTheNewestHundredNewestFirst()
        {
            _harness.Row("p", ViewLayers.Popups);
            OpenAndClose("p", 40);
            Assume.That(_harness.Observer.Count, Is.GreaterThan(UIDirectorWindowModel.MaxEvents), "control: more events than the limit were published");

            _model.Refresh(Navigator);

            Assert.That(_model.Events.Count, Is.EqualTo(UIDirectorWindowModel.MaxEvents), "count");
            long newest = _model.Events[0].Sequence;
            var expected = new long[_model.Events.Count];
            var actual = new long[_model.Events.Count];
            for (var i = 0; i < actual.Length; i++)
            {
                expected[i] = newest - i;
                actual[i] = _model.Events[i].Sequence;
            }

            Assert.That(actual, Is.EqualTo(expected), "sequence numbers go down by one from the newest");
            Assert.That(newest, Is.EqualTo((long)_harness.Observer.Count), "the newest is the last event published");
        }

        [Test]
        public void MaxEvents_IsOneHundred()
        {
            Assert.That(UIDirectorWindowModel.MaxEvents, Is.EqualTo(100));
        }

        [Test]
        public void GetEventLine_ValidIndex_GivesTheFormattedEvent()
        {
            _harness.Row("p", ViewLayers.Popups);
            OpenAndClose("p", 1);
            _model.Refresh(Navigator);
            RecentEvent newest = _model.Events[0];

            string line = _model.GetEventLine(0);

            Assert.That(line, Is.EqualTo(UIDirectorWindowModel.FormatEvent(newest)));
        }

        [Test]
        public void GetEventLine_CalledTwiceForTheSameCapture_GivesTheSameString()
        {
            _harness.Row("p", ViewLayers.Popups);
            OpenAndClose("p", 1);
            _model.Refresh(Navigator);

            string first = _model.GetEventLine(0);
            string second = _model.GetEventLine(0);

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void GetEventLine_AfterANewCapture_FollowsTheNewestEvent()
        {
            _harness.Row("p", ViewLayers.Popups);
            OpenAndClose("p", 1);
            _model.Refresh(Navigator);
            string before = _model.GetEventLine(0);

            Open("ghost");
            _model.Refresh(Navigator);

            Assert.That(_model.GetEventLine(0), Is.Not.EqualTo(before), "the newest line changed");
            Assert.That(_model.GetEventLine(0), Does.Contain("NotShown"), "the unknown id was refused");
        }

        [Test]
        public void GetEventLine_IndexOutOfRange_ThrowsArgumentOutOfRangeException()
        {
            _harness.Row("p", ViewLayers.Popups);
            OpenAndClose("p", 1);
            _model.Refresh(Navigator);
            int count = _model.Events.Count;

            Assert.That(() => { _model.GetEventLine(-1); }, Throws.TypeOf<ArgumentOutOfRangeException>(), "negative");
            Assert.That(() => { _model.GetEventLine(count); }, Throws.TypeOf<ArgumentOutOfRangeException>(), "one past the end");
        }

        // Status line

        [Test]
        public void StatusLine_SettledNavigator_SaysSettled()
        {
            _model.Refresh(Navigator);

            Assert.That(_model.StatusLine(), Does.StartWith("Settled"));
        }

        [Test]
        public void StatusLine_BusyNavigator_SaysBusyWithTheShieldCount()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");

            _model.Refresh(Navigator);

            string line = _model.StatusLine();
            Assert.That(line, Does.StartWith("Busy"), "busy");
            Assert.That(line, Does.Contain("1"), "the shield count");
        }

        [Test]
        public void StatusLine_SettledNavigator_ReadsSettledWithAShieldOfZero()
        {
            _model.Refresh(Navigator);

            Assume.That(Navigator.ShieldCount, Is.Zero, "control: the shield is down");
            Assert.That(_model.StatusLine(), Is.EqualTo("Settled, shield 0"));
        }

        [Test]
        public void StatusLine_ViewLoading_ReadsBusyWithTheNavigatorsShieldCount()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");

            _model.Refresh(Navigator);

            Assume.That(Navigator.ShieldCount, Is.GreaterThan(0), "control: the shield is up");
            Assert.That(_model.StatusLine(), Is.EqualTo("Busy, shield " + Navigator.ShieldCount));
        }

        [Test]
        public void StatusLine_SeveralHolders_ReadsBusyWithTheExactShieldCount()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");
            Navigator.HoldInput("first", _harness.Owner, "HoldSite", 1);
            Navigator.HoldInput("second", _harness.Owner, "HoldSite", 2);

            _model.Refresh(Navigator);

            Assume.That(Navigator.ShieldCount, Is.GreaterThan(1), "control: more than one holder");
            Assert.That(_model.StatusLine(), Is.EqualTo("Busy, shield " + Navigator.ShieldCount));
        }

        [Test]
        public void StatusLine_BusyThenSettled_FollowsTheNavigator()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");
            _model.Refresh(Navigator);
            Assume.That(_model.StatusLine(), Does.StartWith("Busy"), "control: busy while it loads");

            _harness.Stage.CompleteAcquire();
            _model.Refresh(Navigator);

            Assert.That(_model.StatusLine(), Does.StartWith("Settled"));
        }

        [Test]
        public void StatusLine_RootGone_SaysRootGone()
        {
            _harness.Row("home");
            Open("home");
            Navigator.Teardown();

            _model.Refresh(Navigator);

            Assert.That(_model.StatusLine(), Is.EqualTo("Root gone"));
        }

        [Test]
        public void Refresh_RootGone_ShowsNoLayersHoldersOrQueues()
        {
            BuildRunningNavigator();
            _model.Refresh(Navigator);
            Assume.That(_model.State.Layers.Count, Is.GreaterThan(0), "control: the first refresh has layers");

            Navigator.Teardown();
            _model.Refresh(Navigator);

            Assert.That(_model.State.RootGone, Is.True, "root gone");
            Assert.That(_model.State.Layers, Is.Empty, "layers");
            Assert.That(_model.State.ShieldHolders, Is.Empty, "holders");
            Assert.That(_model.Queues, Is.Empty, "queues");
        }

        // The empty model

        [Test]
        public void Refresh_NullNavigator_GivesTheEmptyModelWithTheNoNavigatorStatus()
        {
            _model.Refresh((Navigator)null);

            Assert.That(_model.HasNavigator, Is.False, "has navigator");
            Assert.That(_model.StatusLine(), Is.EqualTo(UIDirectorWindowModel.NoNavigatorStatus), "status");
            AssertEmpty();
        }

        [Test]
        public void Refresh_NullRoot_GivesTheEmptyModelWithTheNoRootStatus()
        {
            _model.Refresh((ViewRoot)null);

            Assert.That(_model.HasNavigator, Is.False, "has navigator");
            Assert.That(_model.StatusLine(), Is.EqualTo(UIDirectorWindowModel.NoRootStatus), "status");
            AssertEmpty();
        }

        [Test]
        public void Refresh_UninitialisedRoot_GivesTheEmptyModelWithTheNotInitializedStatus()
        {
            ViewRoot root = CreateUninitialisedRoot();

            _model.Refresh(root);

            Assert.That(_model.HasNavigator, Is.False, "has navigator");
            Assert.That(_model.StatusLine(), Is.EqualTo(UIDirectorWindowModel.NotInitializedStatus), "status");
            AssertEmpty();
        }

        [Test]
        public void Refresh_NullRootAfterARunningNavigator_EmptiesTheModel()
        {
            BuildRunningNavigator();
            _model.Refresh(Navigator);
            Assume.That(_model.HasNavigator, Is.True, "control: the model shows a navigator");

            _model.Refresh((ViewRoot)null);

            Assert.That(_model.HasNavigator, Is.False, "has navigator");
            Assert.That(_model.StatusLine(), Is.EqualTo(UIDirectorWindowModel.NoRootStatus), "status");
            AssertEmpty();
        }

        [Test]
        public void Refresh_UninitialisedRootAfterARunningNavigator_EmptiesTheModel()
        {
            BuildRunningNavigator();
            _model.Refresh(Navigator);
            ViewRoot root = CreateUninitialisedRoot();

            _model.Refresh(root);

            Assert.That(_model.StatusLine(), Is.EqualTo(UIDirectorWindowModel.NotInitializedStatus), "status");
            AssertEmpty();
        }

        [Test]
        public void Clear_AfterARefresh_EmptiesTheModel()
        {
            BuildRunningNavigator();
            _model.Refresh(Navigator);

            _model.Clear();

            Assert.That(_model.HasNavigator, Is.False, "has navigator");
            Assert.That(_model.StatusLine(), Is.EqualTo(UIDirectorWindowModel.NoNavigatorStatus), "status");
            AssertEmpty();
        }

        [Test]
        public void NewModel_BeforeAnyRefresh_IsEmptyWithTheNoNavigatorStatus()
        {
            Assert.That(_model.HasNavigator, Is.False, "has navigator");
            Assert.That(_model.StatusLine(), Is.EqualTo(UIDirectorWindowModel.NoNavigatorStatus), "status");
            AssertEmpty();
        }

        // Describe

        [Test]
        public void Describe_RunningNavigator_GivesTheNavigatorsOwnTrace()
        {
            BuildRunningNavigator();
            _model.Refresh(Navigator);

            Assert.That(_model.Describe(), Is.EqualTo(DescribeNavigator()));
        }

        [Test]
        public void Describe_EmptyModel_GivesTheStatusLine()
        {
            _model.Refresh((ViewRoot)null);

            Assert.That(_model.Describe(), Is.EqualTo(UIDirectorWindowModel.NoRootStatus));
        }

        // Static formatters

        [Test]
        public void FormatRequest_OpenRequest_GivesIdViewPhaseAndSlot()
        {
            var request = new RequestSnapshot { RequestId = 12, ViewId = "shop", Phase = ViewPhase.Open, Slot = 0 };

            Assert.That(UIDirectorWindowModel.FormatRequest(request), Is.EqualTo("#12 shop Open slot 0"));
        }

        [Test]
        public void FormatRequest_ClosePending_AppendsTheReason()
        {
            var request = new RequestSnapshot
            {
                RequestId = 12,
                ViewId = "shop",
                Phase = ViewPhase.Open,
                Slot = 0,
                ClosePending = true,
                HasOutcome = true,
                OutcomeReason = CloseReason.Back
            };

            Assert.That(UIDirectorWindowModel.FormatRequest(request), Is.EqualTo("#12 shop Open slot 0 close pending: Back"));
        }

        [Test]
        public void FormatLayerHeader_TwoRequestsAndNoOperations_GivesCountsAndIdle()
        {
            var layer = new LayerSnapshot { LayerId = "popups", PendingOperations = 0, IsRunning = false };
            layer.Requests.Add(new RequestSnapshot { RequestId = 1, ViewId = "a" });
            layer.Requests.Add(new RequestSnapshot { RequestId = 2, ViewId = "b" });

            Assert.That(UIDirectorWindowModel.FormatLayerHeader(layer), Is.EqualTo("popups: 2 requests, 0 operations, idle"));
        }

        [Test]
        public void FormatLayerHeader_OneRequestAndOneOperationWhileRunning_UsesTheSingularAndRunning()
        {
            var layer = new LayerSnapshot { LayerId = "screens", PendingOperations = 1, IsRunning = true };
            layer.Requests.Add(new RequestSnapshot { RequestId = 1, ViewId = "a" });

            Assert.That(UIDirectorWindowModel.FormatLayerHeader(layer), Is.EqualTo("screens: 1 request, 1 operation, running"));
        }

        [Test]
        public void FormatShieldHolder_HolderOfARequest_GivesKindRequestReasonAgeAndCallSite()
        {
            var holder = new ShieldHolderSnapshot
            {
                Kind = ShieldHolderKind.InputHold,
                RequestId = 3,
                Reason = "reason",
                AgeSeconds = 1.2,
                CallSite = "call site"
            };

            Assert.That(UIDirectorWindowModel.FormatShieldHolder(holder), Is.EqualTo("InputHold #3 reason 1.2s at call site"));
        }

        [Test]
        public void FormatShieldHolder_NoRequest_LeavesTheRequestIdOut()
        {
            var holder = new ShieldHolderSnapshot { Kind = ShieldHolderKind.InputHold, RequestId = 0, Reason = "sync", AgeSeconds = 2.5 };

            string line = UIDirectorWindowModel.FormatShieldHolder(holder);

            Assert.That(line, Does.Not.Contain("#"));
        }

        [Test]
        public void FormatShieldHolder_NoRequestAndNoCallSite_GivesKindReasonAndAge()
        {
            var holder = new ShieldHolderSnapshot { Kind = ShieldHolderKind.InputHold, RequestId = 0, Reason = "sync", AgeSeconds = 2.5 };

            Assert.That(UIDirectorWindowModel.FormatShieldHolder(holder), Is.EqualTo("InputHold sync 2.5s"));
        }

        [Test]
        public void FormatQueueHold_HoldWithReasonAgeAndCallSite_GivesAllThree()
        {
            var hold = new QueueHoldSnapshot { Reason = "reason", AgeSeconds = 1.2, CallSite = "call site" };

            Assert.That(UIDirectorWindowModel.FormatQueueHold(hold), Is.EqualTo("reason 1.2s at call site"));
        }

        [Test]
        public void FormatQueueHold_NoCallSite_LeavesTheCallSiteOut()
        {
            var hold = new QueueHoldSnapshot { Reason = "tutorial", AgeSeconds = 1.2 };

            Assert.That(UIDirectorWindowModel.FormatQueueHold(hold), Does.Not.Contain(" at "));
        }

        [Test]
        public void FormatQueueHold_NoCallSite_GivesReasonAndAge()
        {
            var hold = new QueueHoldSnapshot { Reason = "tutorial", AgeSeconds = 1.2 };

            Assert.That(UIDirectorWindowModel.FormatQueueHold(hold), Is.EqualTo("tutorial 1.2s"));
        }

        [Test]
        public void FormatQueueItem_ItemWithSource_GivesIdViewPrioritySourceAndAge()
        {
            var item = new QueueItemSnapshot { RequestId = 5, ViewId = "shop", Priority = 10, Source = "tutorial", AgeSeconds = 2.3 };

            Assert.That(UIDirectorWindowModel.FormatQueueItem(item), Is.EqualTo("#5 shop priority 10 source tutorial age 2.3s"));
        }

        [Test]
        public void FormatQueueItem_ItemWithoutSource_LeavesTheSourceOut()
        {
            var item = new QueueItemSnapshot { RequestId = 5, ViewId = "shop", Priority = 10, Source = null, AgeSeconds = 2.3 };

            Assert.That(UIDirectorWindowModel.FormatQueueItem(item), Does.Not.Contain("source"));
        }

        [Test]
        public void FormatQueueItem_NoSource_GivesIdViewPriorityAndAge()
        {
            var item = new QueueItemSnapshot { RequestId = 5, ViewId = "shop", Priority = 10, Source = null, AgeSeconds = 2.3 };

            Assert.That(UIDirectorWindowModel.FormatQueueItem(item), Is.EqualTo("#5 shop priority 10 age 2.3s"));
        }

        [Test]
        public void FormatQueueHeader_FreeNamedQueue_GivesNameLayerAndTheWaitingCount()
        {
            var queue = new QueueState { Name = "Name", LayerId = "popups" };

            Assert.That(UIDirectorWindowModel.FormatQueueHeader(queue), Is.EqualTo("Name (layer popups), waiting 0"));
        }

        [Test]
        public void FormatQueueHeader_TwoWaitingItems_GivesWaitingTwo()
        {
            var queue = new QueueState { Name = "Name", LayerId = "popups" };
            queue.Waiting.Add(new QueueItemSnapshot { RequestId = 1, ViewId = "a" });
            queue.Waiting.Add(new QueueItemSnapshot { RequestId = 2, ViewId = "b" });

            Assert.That(UIDirectorWindowModel.FormatQueueHeader(queue), Is.EqualTo("Name (layer popups), waiting 2"));
        }

        [Test]
        public void FormatQueueHeader_NoName_GivesTheLayerIdAndTheWaitingCount()
        {
            var queue = new QueueState { Name = null, LayerId = "popups" };

            Assert.That(UIDirectorWindowModel.FormatQueueHeader(queue), Is.EqualTo("popups, waiting 0"));
        }

        [Test]
        public void FormatQueueHeader_HeldInertNamedQueue_GivesNameLayerFlagsAndTheWaitingCount()
        {
            var queue = new QueueState { Name = "Name", LayerId = "popups", Held = true, Inert = true };

            Assert.That(UIDirectorWindowModel.FormatQueueHeader(queue), Is.EqualTo("Name (layer popups), held, inert, waiting 0"));
        }

        [Test]
        public void FormatQueueHeader_HeldInertNamedQueue_GivesNameLayerAndFlags()
        {
            var queue = new QueueState { Name = "Name", LayerId = "popups", Held = true, Inert = true };

            Assert.That(UIDirectorWindowModel.FormatQueueHeader(queue), Does.StartWith("Name (layer popups), held, inert"));
        }

        [Test]
        public void FormatQueueHeader_QueueWithWaitingItems_EndsWithTheWaitingCount()
        {
            var queue = new QueueState { Name = "Name", LayerId = "popups" };
            queue.Waiting.Add(new QueueItemSnapshot { RequestId = 1, ViewId = "a" });
            queue.Waiting.Add(new QueueItemSnapshot { RequestId = 2, ViewId = "b" });

            Assert.That(UIDirectorWindowModel.FormatQueueHeader(queue), Does.EndWith("2"));
        }

        [Test]
        public void FormatQueueHeader_FreeQueue_LeavesTheFlagsOut()
        {
            var queue = new QueueState { Name = "Name", LayerId = "popups" };

            string line = UIDirectorWindowModel.FormatQueueHeader(queue);

            Assert.That(line, Does.Not.Contain("held"), "held");
            Assert.That(line, Does.Not.Contain("inert"), "inert");
        }

        [Test]
        public void FormatQueueHeader_QueueWithoutAName_StartsWithTheLayerId()
        {
            var queue = new QueueState { Name = null, LayerId = "popups" };

            string line = UIDirectorWindowModel.FormatQueueHeader(queue);

            Assert.That(line, Does.StartWith("popups"), "starts with the layer id");
            Assert.That(line, Does.Not.Contain("(layer"), "no layer in brackets");
        }

        [Test]
        public void FormatEvent_EnqueuedEventFromAQueue_GivesTheDocumentedLine()
        {
            RecentEvent recent = Recent(ViewEventKind.Enqueued, 5, "shop", "popups", "tutorial", 3, true);

            Assert.That(UIDirectorWindowModel.FormatEvent(recent), Is.EqualTo("[queue] Enqueued #5 shop layer popups source tutorial priority 3"));
        }

        [Test]
        public void FormatEvent_SettledEvent_HasNoRequestAndNoQueueMark()
        {
            RecentEvent recent = Recent(ViewEventKind.Settled, 0, null, null, null, 0, false);

            string line = UIDirectorWindowModel.FormatEvent(recent);

            Assert.That(line, Does.StartWith("Settled"), "kind");
            Assert.That(line, Does.Not.Contain("#"), "request");
            Assert.That(line, Does.Not.Contain("[queue]"), "queue mark");
        }

        [Test]
        public void FormatEvent_SettledEvent_IsExactlyTheKind()
        {
            RecentEvent recent = Recent(ViewEventKind.Settled, 0, null, null, null, 0, false);

            Assert.That(UIDirectorWindowModel.FormatEvent(recent), Is.EqualTo("Settled"));
        }

        [Test]
        public void FormatEvent_EventFromTheNavigator_HasNoQueueMark()
        {
            RecentEvent recent = Recent(ViewEventKind.Requested, 7, "home", "screens", null, 0, false);

            string line = UIDirectorWindowModel.FormatEvent(recent);

            Assert.That(line, Does.StartWith("Requested"), "kind first");
            Assert.That(line, Does.Contain("#7 home"), "request and view");
        }

        [Test]
        public void FormatEvent_ClosedEvent_NamesTheReason()
        {
            var viewEvent = new ViewEvent(ViewEventKind.Closed, 7, "home", "screens", null, 0, CloseReason.Back, RejectReason.None, null, 0f, 12.5f);
            var recent = new RecentEvent(in viewEvent, false, 1);

            string line = UIDirectorWindowModel.FormatEvent(recent);

            Assert.That(line, Does.Contain("Closed #7 home"), "kind, request and view");
            Assert.That(line, Does.Contain("Back"), "reason");
        }

        [Test]
        public void FormatEvent_NotShownEvent_NamesTheRejection()
        {
            var viewEvent = new ViewEvent(ViewEventKind.NotShown, 3, "ghost", "popups", null, 0, CloseReason.NotShown, RejectReason.Cancelled, "withdrawn", 0f, 0f);
            var recent = new RecentEvent(in viewEvent, true, 1);

            string line = UIDirectorWindowModel.FormatEvent(recent);

            Assert.That(line, Does.Contain("NotShown #3 ghost"), "kind, request and view");
            Assert.That(line, Does.Contain("Cancelled"), "rejection");
            Assert.That(line, Does.Contain("withdrawn"), "detail");
        }

        [Test]
        public void FormatEvent_ShownEvent_NamesTheKindRequestAndView()
        {
            var viewEvent = new ViewEvent(ViewEventKind.Shown, 4, "home", "screens", null, 0, default(CloseReason), RejectReason.None, null, 0.5f, 0f);
            var recent = new RecentEvent(in viewEvent, false, 1);

            Assert.That(UIDirectorWindowModel.FormatEvent(recent), Does.StartWith("Shown #4 home"));
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

        // Two screens, a popup, an input hold, and a held queue with two waiting items.
        private void BuildRunningNavigator()
        {
            _harness.Row("home").Row("settings").Row("confirm", ViewLayers.Popups);
            _home = Open("home");
            _settings = Open("settings");
            _confirm = Open("confirm");
            Navigator.HoldInput("sync", _harness.Owner, "ExitCheck", 12);

            ViewQueue queue = CreateQueue(NewLifetime().Token, "onboarding");
            queue.Hold("tutorial", _harness.Owner);
            _offerId = Enqueue(queue, "offer", 1, "shop");
            _dailyId = Enqueue(queue, "daily", 5, "calendar");
        }

        private CancellationTokenSource NewLifetime()
        {
            var lifetime = new CancellationTokenSource();
            _lifetimes.Add(lifetime);
            return lifetime;
        }

        private ViewQueue CreateQueue(CancellationToken lifetime, string name)
        {
            return new ViewQueue(
                Navigator,
                ViewLayers.Popups,
                lifetime,
                name,
                _ticker,
                _harness.MainThread,
                Navigator.Errors,
                Navigator.Diagnostics);
        }

        private static int Enqueue(ViewQueue queue, string viewId, int priority, string source)
        {
            return queue.Enqueue(ViewKey.Create(viewId), default(NoArgs), CancellationToken.None, new QueueOptions { Priority = priority, Source = source });
        }

        // Awake does not run in Edit Mode, so the root stays uninitialised.
        private ViewRoot CreateUninitialisedRoot()
        {
            var go = new GameObject("UninitialisedRoot", typeof(ViewRoot));
            go.hideFlags = HideFlags.HideAndDontSave;
            _objects.Add(go);

            var root = go.GetComponent<ViewRoot>();
            Assume.That(root.IsInitialized, Is.False, "control: the new root is not initialised");
            return root;
        }

        // Awake does not run in Edit Mode, so the root is initialised by hand; an EventSystem keeps Initialize from warning.
        private ViewRoot CreateInitialisedRoot()
        {
            var eventSystem = new GameObject("TestEventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            eventSystem.hideFlags = HideFlags.HideAndDontSave;
            _objects.Add(eventSystem);

            var go = new GameObject("InitialisedRoot", typeof(RectTransform), typeof(Canvas), typeof(ViewRoot));
            go.hideFlags = HideFlags.HideAndDontSave;
            _objects.Add(go);

            var root = go.GetComponent<ViewRoot>();
            root.Initialize(new ViewRootSetup());
            _initialisedRoots.Add(root);
            Assume.That(root.IsInitialized, Is.True, "control: the root is initialised");
            return root;
        }

        // OnDestroy does not run in Edit Mode; run it so the root ends its navigator and drops its lowMemory subscription.
        private static void EndRoot(ViewRoot root)
        {
            if (root == null)
                return;

            MethodInfo onDestroy = typeof(ViewRoot).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
            if (onDestroy == null)
                throw new InvalidOperationException("ViewRoot has no OnDestroy; the clean-up in UIDirectorWindowModelTests needs updating.");

            onDestroy.Invoke(root, null);
        }

        private string DescribeNavigator()
        {
            return DescribeOf(Navigator);
        }

        private static string DescribeOf(Navigator navigator)
        {
            var output = new StringBuilder();
            navigator.Describe(output);
            return output.ToString();
        }

        private void AssertEmpty()
        {
            Assert.That(_model.State.Layers, Is.Empty, "layers");
            Assert.That(_model.State.ShieldHolders, Is.Empty, "shield holders");
            Assert.That(_model.State.KeptViewIds, Is.Empty, "kept ids");
            Assert.That(_model.State.PreloadViewIds, Is.Empty, "preload ids");
            Assert.That(_model.Queues, Is.Empty, "queues");
            Assert.That(_model.Events, Is.Empty, "events");
        }

        private static RecentEvent Recent(ViewEventKind kind, int requestId, string viewId, string layerId, string source, int priority, bool fromQueue)
        {
            var viewEvent = new ViewEvent(kind, requestId, viewId, layerId, source, priority, default(CloseReason), RejectReason.None, null, 0f, 0f);
            return new RecentEvent(in viewEvent, fromQueue, 1);
        }

        private string[] LayerIds()
        {
            var ids = new string[_model.State.Layers.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = _model.State.Layers[i].LayerId;
            return ids;
        }

        private static string[] ViewIds(LayerSnapshot layer)
        {
            var ids = new string[layer.Requests.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = layer.Requests[i].ViewId;
            return ids;
        }

        private static int[] RequestIds(LayerSnapshot layer)
        {
            var ids = new int[layer.Requests.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = layer.Requests[i].RequestId;
            return ids;
        }
    }
}
