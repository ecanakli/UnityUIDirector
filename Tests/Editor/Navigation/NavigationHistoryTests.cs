using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // History layer rules: push, pop, pop-to, return to existing, reopen in place, joiners.
    public sealed class NavigationHistoryTests
    {
        // How a test pops the stack back to home.
        public enum PopKind
        {
            CloseTo,
            ReturnToExisting
        }

        // What ends the newer request of a reopen in place before its open runs.
        public enum ReplacementWithdrawal
        {
            Back,
            NewOwnerEnded
        }

        // What ends the older request of a reopen in place while the pop above it runs.
        public enum OldRequestEnd
        {
            OldOwnerEnded,
            OldInstanceDestroyed
        }

        // Home, detail opened with "first" on its own owner, and other above it.
        private sealed class ReopenSetup
        {
            public ViewKey<string, NoResult> Key;
            public ViewHandle Home;
            public CancellationTokenSource FirstOwner;
            public UniTask<ViewResult<NoResult>> First;
            public ViewHandle FirstHandle;
            public FakeVisibleView<string, NoResult> View;
            public FakeInstance Instance;
            public int VisibleBefore;
        }

        private NavigatorHarness _harness;

        [SetUp]
        public void SetUp()
        {
            _harness = new NavigatorHarness();
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        private ViewHandle Open(string id)
        {
            return NavigationTestKit.Open(_harness, id);
        }

        private ViewVisualState VisualOf(string id)
        {
            return NavigationTestKit.VisualOf(_harness, id);
        }

        // Completes every pending play, including the ones the completions start.
        private void CompletePendingPlays()
        {
            for (var completed = 0; _harness.Stage.PendingPlayCount > 0; completed++)
            {
                Assert.That(completed, Is.LessThan(100), "Plays keep starting.\n" + _harness.Stage.DescribeCalls());
                _harness.Stage.CompletePlay();
            }
        }

        private void PopToHome(PopKind pop)
        {
            switch (pop)
            {
                case PopKind.CloseTo:
                    Assert.That(_harness.Navigator.CloseTo("home"), Is.True, "CloseTo finds home.");
                    break;

                case PopKind.ReturnToExisting:
                    Open("home");
                    break;
            }
        }

        // Plays are manual from the end of this call on.
        private ReopenSetup ArrangeDetailBelowOther()
        {
            _harness.Row("home").Row("detail").Map("detail", () => new FakeVisibleView<string, NoResult>()).Row("other");
            var navigator = _harness.Navigator;
            var setup = new ReopenSetup
            {
                Key = ViewKey.Create<string>("detail"),
                FirstOwner = _harness.NewOwner()
            };

            setup.Home = Open("home");
            setup.First = navigator.ShowAsync(setup.Key, "first", setup.FirstOwner.Token);

            ViewHandle firstHandle;
            Assert.That(navigator.TryGetOpen("detail", out firstHandle), Is.True, "detail is open.\n" + _harness.Describe());
            setup.FirstHandle = firstHandle;

            Open("other");
            setup.View = _harness.ViewOf<FakeVisibleView<string, NoResult>>("detail");
            setup.Instance = _harness.Stage.LastInstanceOf("detail");
            setup.VisibleBefore = setup.View.VisibleCount;
            _harness.Stage.PlayMode = StageMode.Manual;
            return setup;
        }

        private void WithdrawReplacement(ReplacementWithdrawal cause, CancellationTokenSource secondOwner)
        {
            switch (cause)
            {
                case ReplacementWithdrawal.Back:
                    Assert.That(_harness.Navigator.Back(), Is.True, "Back withdraws the waiting request.");
                    break;

                case ReplacementWithdrawal.NewOwnerEnded:
                    secondOwner.Cancel();
                    break;
            }
        }

        private void EndOldRequest(OldRequestEnd cause, ReopenSetup setup)
        {
            switch (cause)
            {
                case OldRequestEnd.OldOwnerEnded:
                    setup.FirstOwner.Cancel();
                    break;

                case OldRequestEnd.OldInstanceDestroyed:
                    _harness.Stage.RaiseInstanceLost(setup.Instance, true);
                    break;
            }
        }

        // The documented recipe for a caller that stops waiting at its own token.
        private static async UniTask<(bool IsCanceled, ViewResult<NoResult> Result)> ShowUntilStopped(
            UniTask<ViewResult<NoResult>> show,
            CancellationToken stop)
        {
            return await show.AttachExternalCancellation(stop).SuppressCancellationThrow();
        }

        // The operation is over: no shield holder, the shield is down and nothing runs or waits.
        private void AssertSettled()
        {
            Assert.That(_harness.ShieldCount, Is.Zero, "Shield holders left.\n" + _harness.Describe());
            Assert.That(_harness.Stage.ShieldBlocking, Is.False, "The shield is still up.");
            Assert.That(_harness.Navigator.IsBusy, Is.False, "navigator busy");
        }

        // Position of the first event of the kind for the request; -1 when there is none.
        private int IndexOfEventFor(ViewEventKind kind, int requestId)
        {
            var events = _harness.Observer.Events;
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == kind && events[i].RequestId == requestId)
                    return i;
            }

            return -1;
        }

        private int CountEventsFor(ViewEventKind kind, int requestId)
        {
            var count = 0;
            var events = _harness.Observer.EventsFor(requestId);
            for (var i = 0; i < events.Length; i++)
            {
                if (events[i].Kind == kind)
                    count++;
            }

            return count;
        }

        // Records the navigator's busy flag at every NotShown event.
        private void RecordBusyAtNotShown(List<bool> busyAtNotShown)
        {
            var navigator = _harness.Navigator;
            var observer = new LifecycleActionObserver(viewEvent =>
            {
                if (viewEvent.Kind == ViewEventKind.NotShown)
                    busyAtNotShown.Add(navigator.IsBusy);
            });
            navigator.AddObserver(observer, _harness.Owner);
        }

        // A view whose OnOpen sends the stack back to home.
        private void RedirectToHome(ViewContext<NoResult> context)
        {
            _harness.Navigator.CloseTo("home");
        }

        private static CloseReason ReasonOf(PopKind pop)
        {
            return pop == PopKind.CloseTo ? CloseReason.Code : CloseReason.Navigated;
        }

        private static CloseReason ReasonOf(OldRequestEnd cause)
        {
            return cause == OldRequestEnd.OldOwnerEnded ? CloseReason.OwnerEnded : CloseReason.Destroyed;
        }

        private static RejectReason RejectionOf(ReplacementWithdrawal cause)
        {
            return cause == ReplacementWithdrawal.Back ? RejectReason.Cancelled : RejectReason.OwnerEnded;
        }

        // Push

        [Test]
        public void Push_EachOpenOnAHistoryLayer_StacksANewScreenAboveThePreviousOne()
        {
            _harness.Row("a").Row("b").Row("c");

            var a = Open("a");
            var b = Open("b");
            var c = Open("c");

            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(3));
            Assert.That(a.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(b.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(c.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_harness.Stage.Instances.Count, Is.EqualTo(3));

            ViewHandle top;
            Assert.That(_harness.Navigator.TryGetTop(ViewLayers.Screens, out top), Is.True);
            Assert.That(top.ViewId, Is.EqualTo("c"));

            // Sort order is base + layer order x 1000 + slot x 10, one slot higher per screen.
            Assert.That(VisualOf("a").SortingOrder, Is.EqualTo(10));
            Assert.That(VisualOf("b").SortingOrder, Is.EqualTo(20));
            Assert.That(VisualOf("c").SortingOrder, Is.EqualTo(30));
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Shown), Is.EqualTo(3));
        }

        [Test]
        public void Push_AllowMultiple_OpensASecondInstanceInsteadOfReturningToTheFirst()
        {
            _harness.Row("a", allowMultiple: true);

            var first = Open("a");
            var second = Open("a");

            Assert.That(second.RequestId, Is.Not.EqualTo(first.RequestId));
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2));
            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "a"), Is.EqualTo(2));
        }

        // Pop

        [Test]
        public void Pop_BackOnTheTopScreen_ClosesItAndRevealsTheOneBelowWithoutAnotherOnOpen()
        {
            _harness.Row("a").Row("b");
            Open("a");
            Open("b");
            var viewA = _harness.ViewOf<FakeView<NoArgs, NoResult>>("a");
            Assert.That(VisualOf("a").Hidden, Is.True, "A screen is covered while another one is open above it.");

            var handled = _harness.Navigator.Back();

            Assert.That(handled, Is.True);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1));
            Assert.That(_harness.Navigator.IsOpen("b"), Is.False);
            Assert.That(_harness.Stage.LastInstanceOf("b").ReleaseCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.LastInstanceOf("a").ReleaseCount, Is.Zero);
            Assert.That(viewA.OpenCount, Is.EqualTo(1), "Revealing a screen does not open it again.");
            Assert.That(VisualOf("a").Hidden, Is.False);
            Assert.That(VisualOf("a").Interactive, Is.True);
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "b").Reason, Is.EqualTo(CloseReason.Back));
        }

        // Pop-to

        [Test]
        public void PopTo_HidesTheTopWithAnimation_AndSnapsEveryEntryBetween()
        {
            _harness.Row("a").Row("b").Row("c").Row("d");
            var navigator = _harness.Navigator;
            Open("a");
            var showB = navigator.ShowAsync(ViewKey.Create("b"), _harness.Owner);
            var showC = navigator.ShowAsync(ViewKey.Create("c"), _harness.Owner);
            var showD = navigator.ShowAsync(ViewKey.Create("d"), _harness.Owner);
            _harness.Stage.PlayMode = StageMode.Manual;
            _harness.Stage.ClearCalls();

            var popped = navigator.CloseTo("a");

            Assert.That(popped, Is.True);

            // Only the top view animates, and nothing between is finalized before that animation ends.
            var plays = _harness.Stage.CallsOf(StageCallKind.Play);
            Assert.That(plays.Length, Is.EqualTo(1));
            Assert.That(plays[0].ViewId, Is.EqualTo("d"));
            Assert.That(plays[0].On, Is.False);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(4));
            Assert.That(navigator.IsOpen("b"), Is.True);
            Assert.That(showB.IsPending(), Is.True);
            Assert.That(showD.IsPending(), Is.True);

            _harness.Stage.CompletePlay();

            var calls = _harness.Stage.Calls;
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1));
            Assert.That(navigator.IsOpen("a"), Is.True);
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Play, "b"), Is.Zero);
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Play, "c"), Is.Zero);
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Snap, "b"), Is.EqualTo(1));
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Snap, "c"), Is.EqualTo(1));
            Assert.That(NavigationTestKit.IndexOfCall(calls, StageCallKind.Snap, "b", false), Is.GreaterThanOrEqualTo(0));
            Assert.That(NavigationTestKit.IndexOfCall(calls, StageCallKind.Snap, "c", false), Is.GreaterThanOrEqualTo(0));
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Snap, "d"), Is.Zero, "The top view is hidden by its transition, not snapped.");

            // The top view is finalized first, then the entries between it and the target.
            var observer = _harness.Observer;
            var closedD = NavigationTestKit.IndexOfEvent(observer, ViewEventKind.Closed, "d");
            Assert.That(closedD, Is.GreaterThanOrEqualTo(0));
            Assert.That(closedD, Is.LessThan(NavigationTestKit.IndexOfEvent(observer, ViewEventKind.Closed, "c")));
            Assert.That(closedD, Is.LessThan(NavigationTestKit.IndexOfEvent(observer, ViewEventKind.Closed, "b")));

            Assert.That(_harness.Stage.LastInstanceOf("b").ReleaseCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.LastInstanceOf("c").ReleaseCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.LastInstanceOf("d").ReleaseCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.LastInstanceOf("a").ReleaseCount, Is.Zero);
            Assert.That(VisualOf("a").Hidden, Is.False);
            Assert.That(VisualOf("a").Interactive, Is.True);
            Assert.That(_harness.ShieldCount, Is.Zero);
        }

        [Test]
        public void PopTo_AwaitersOfEveryPoppedScreen_CompleteWithReasonCode()
        {
            _harness.Row("a").Row("b").Row("c").Row("d");
            var navigator = _harness.Navigator;
            Open("a");
            var showB = navigator.ShowAsync(ViewKey.Create("b"), _harness.Owner);
            var showC = navigator.ShowAsync(ViewKey.Create("c"), _harness.Owner);
            var showD = navigator.ShowAsync(ViewKey.Create("d"), _harness.Owner);

            navigator.CloseTo("a");

            foreach (var task in new[] { showB, showC, showD })
            {
                var result = task.ResultNow();
                Assert.That(result.WasShown, Is.True);
                Assert.That(result.Reason, Is.EqualTo(CloseReason.Code));
                Assert.That(result.HasValue, Is.False);
            }
        }

        [Test]
        public void CloseTo_TheTopScreenItself_ReturnsTrueAndPopsNothing()
        {
            _harness.Row("a").Row("b");
            Open("a");
            Open("b");
            _harness.Stage.ClearCalls();

            var popped = _harness.Navigator.CloseTo("b");

            Assert.That(popped, Is.True);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2));
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.CallsOf(StageCallKind.Play, StageCallKind.Snap, StageCallKind.Release), Is.Empty);
        }

        [Test]
        public void CloseTo_AnIdThatIsNotInAHistoryLayer_ReturnsFalseAndChangesNothing()
        {
            _harness.Row("a").Row("b").Row("p", ViewLayers.Popups);
            Open("a");
            Open("b");
            Open("p");

            Assert.That(_harness.Navigator.CloseTo("p"), Is.False);
            Assert.That(_harness.Navigator.CloseTo("missing"), Is.False);

            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2));
            Assert.That(_harness.RequestCount(ViewLayers.Popups), Is.EqualTo(1));
            Assert.That(_harness.ShieldCount, Is.Zero);
        }

        // Pop-to and return: the views a pending pop or close-all removes

        [Test]
        public void Open_IdPoppedByAPendingPop_StartsANewRequestThatOpensAfterThePop([Values] PopKind pop)
        {
            _harness.Row("home").Row("shop").Row("detail");
            Open("home");
            var firstShop = Open("shop");
            var firstShopInstance = _harness.Stage.LastInstanceOf("shop");
            Open("detail");
            _harness.Stage.PlayMode = StageMode.Manual;
            PopToHome(pop);

            var newShop = Open("shop");

            Assert.That(newShop.RequestId, Is.Not.EqualTo(firstShop.RequestId), "A shop the pop removes is not joined.");
            Assert.That(newShop.IsAlive, Is.True, "new shop alive");
            Assert.That(newShop.Phase, Is.EqualTo(ViewPhase.Waiting), "The new shop waits for the pop.\n" + _harness.Describe());
            Assert.That(NavigationTestKit.CountCalls(_harness.Stage.Calls, StageCallKind.Acquire, "shop"), Is.EqualTo(1), "The new shop does not load before the pop.");

            CompletePendingPlays();

            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2), "home and the new shop remain.\n" + _harness.Describe());
            ViewHandle top;
            Assert.That(_harness.Navigator.TryGetTop(ViewLayers.Screens, out top), Is.True);
            Assert.That(top.RequestId, Is.EqualTo(newShop.RequestId), "the new shop is on top");
            Assert.That(newShop.Phase, Is.EqualTo(ViewPhase.Open), "new shop phase");
            Assert.That(firstShop.IsAlive, Is.False, "the first shop ended");
            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "shop"), Is.EqualTo(2), "shop instances acquired");
            Assert.That(firstShopInstance.ReleaseCount, Is.EqualTo(1), "first shop instance releases");
            Assert.That(
                _harness.Observer.KindsFor(newShop.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }),
                "events of the new shop");
            var firstShopClosed = NavigationTestKit.ClosedOf(_harness.Observer, "shop");
            Assert.That(firstShopClosed.RequestId, Is.EqualTo(firstShop.RequestId), "the first Closed of shop is the first shop's");
            Assert.That(
                NavigationTestKit.IndexOfEvent(_harness.Observer, ViewEventKind.Closed, "shop"),
                Is.LessThan(IndexOfEventFor(ViewEventKind.Shown, newShop.RequestId)),
                "The first shop closes before the new shop is shown.");
            AssertSettled();
        }

        [Test]
        public void Open_IdPoppedByAPendingPop_TheFirstRequestEndsWithThePopReason([Values] PopKind pop)
        {
            _harness.Row("home").Row("shop").Row("detail");
            Open("home");
            var firstShop = Open("shop");
            Open("detail");
            _harness.Stage.PlayMode = StageMode.Manual;
            PopToHome(pop);

            Open("shop");
            CompletePendingPlays();

            var firstShopClosed = NavigationTestKit.ClosedOf(_harness.Observer, "shop");
            Assert.That(firstShopClosed.RequestId, Is.EqualTo(firstShop.RequestId), "the first Closed of shop is the first shop's");
            Assert.That(firstShopClosed.Reason, Is.EqualTo(ReasonOf(pop)), "first shop reason");
        }

        [Test]
        public void CloseAll_ThenOpenOfAnIdInTheStack_OpensItFreshAfterTheCloseAll()
        {
            _harness.Row("home").Row("shop");
            var originalHome = Open("home");
            Open("shop");
            _harness.Stage.PlayMode = StageMode.Manual;
            _harness.Navigator.CloseAll(ViewLayers.Screens);

            var newHome = Open("home");

            Assert.That(newHome.RequestId, Is.Not.EqualTo(originalHome.RequestId), "A home the close-all removes is not returned to.");

            CompletePendingPlays();

            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1), "Only the new home remains.\n" + _harness.Describe());
            ViewHandle top;
            Assert.That(_harness.Navigator.TryGetTop(ViewLayers.Screens, out top), Is.True, "The layer is not empty.");
            Assert.That(top.RequestId, Is.EqualTo(newHome.RequestId), "the new home is on top");
            Assert.That(newHome.Phase, Is.EqualTo(ViewPhase.Open), "new home phase");
            Assert.That(VisualOf("home").Interactive, Is.True, "new home interactive");
            var closed = NavigationTestKit.ClosedOf(_harness.Observer, "home");
            Assert.That(closed.RequestId, Is.EqualTo(originalHome.RequestId), "the first Closed of home is the original's");
            Assert.That(closed.Reason, Is.EqualTo(CloseReason.Code), "original home reason");
            AssertSettled();
        }

        [Test]
        public void CloseTo_UnstartedRequestsAboveTheTarget_AreWithdrawnAsCancelledWithoutLoading()
        {
            _harness.Row("home").Row("shop").Row("loading").Row("waiting");
            var navigator = _harness.Navigator;
            Open("home");
            Open("shop");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var loading = navigator.ShowAsync(ViewKey.Create("loading"), _harness.Owner);
            var waiting = navigator.ShowAsync(ViewKey.Create("waiting"), _harness.Owner);

            var popped = navigator.CloseTo("home");

            Assert.That(popped, Is.True);
            Assert.That(loading.IsPending(), Is.False, "The loading request ends at the call.");
            Assert.That(waiting.IsPending(), Is.False, "The waiting request ends at the call.");
            LifecycleDriver.AssertNotShown(loading.ResultNow(), RejectReason.Cancelled);
            LifecycleDriver.AssertNotShown(waiting.ResultNow(), RejectReason.Cancelled);

            var calls = _harness.Stage.Calls;
            var acquire = NavigationTestKit.IndexOfCall(calls, StageCallKind.Acquire, "loading");
            Assert.That(acquire, Is.GreaterThanOrEqualTo(0), "loading was acquired");
            Assert.That(calls[acquire].Token.IsCancellationRequested, Is.True, "The load of the withdrawn request is cancelled.");
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Acquire, "waiting"), Is.Zero, "The waiting request never loads.");
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "shop").Reason, Is.EqualTo(CloseReason.Code), "shop reason");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1), "Only home remains.\n" + _harness.Describe());
            Assert.That(navigator.IsOpen("home"), Is.True, "home stays");
            Assert.That(_harness.ShieldCount, Is.Zero, _harness.Describe());
        }

        [Test]
        public void ReturnToExisting_UnstartedRequestsAboveTheTarget_AreWithdrawnAsCancelledWithoutLoading()
        {
            _harness.Row("home").Row("shop").Row("loading").Row("waiting");
            var navigator = _harness.Navigator;
            var home = Open("home");
            Open("shop");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var loading = navigator.ShowAsync(ViewKey.Create("loading"), _harness.Owner);
            var waiting = navigator.ShowAsync(ViewKey.Create("waiting"), _harness.Owner);

            var again = Open("home");

            Assert.That(again.RequestId, Is.EqualTo(home.RequestId), "Open returns the existing home.");
            Assert.That(loading.IsPending(), Is.False, "The loading request ends at the call.");
            Assert.That(waiting.IsPending(), Is.False, "The waiting request ends at the call.");
            LifecycleDriver.AssertNotShown(loading.ResultNow(), RejectReason.Cancelled);
            LifecycleDriver.AssertNotShown(waiting.ResultNow(), RejectReason.Cancelled);

            var calls = _harness.Stage.Calls;
            var acquire = NavigationTestKit.IndexOfCall(calls, StageCallKind.Acquire, "loading");
            Assert.That(acquire, Is.GreaterThanOrEqualTo(0), "loading was acquired");
            Assert.That(calls[acquire].Token.IsCancellationRequested, Is.True, "The load of the withdrawn request is cancelled.");
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Acquire, "waiting"), Is.Zero, "The waiting request never loads.");
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "shop").Reason, Is.EqualTo(CloseReason.Navigated), "shop reason");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1), "Only home remains.\n" + _harness.Describe());
            Assert.That(home.Phase, Is.EqualTo(ViewPhase.Open), "home phase");
            AssertSettled();
        }

        [Test]
        public void CloseTo_OnlyUnstartedRequestsAboveTheTarget_AreWithdrawnAndNothingIsPopped()
        {
            _harness.Row("home").Row("loading").Row("waiting");
            var navigator = _harness.Navigator;
            var home = Open("home");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var loading = navigator.ShowAsync(ViewKey.Create("loading"), _harness.Owner);
            var waiting = navigator.ShowAsync(ViewKey.Create("waiting"), _harness.Owner);
            var playsBefore = _harness.Stage.CountOf(StageCallKind.Play);

            var popped = navigator.CloseTo("home");

            Assert.That(popped, Is.True, "CloseTo finds home.");
            LifecycleDriver.AssertNotShown(loading.ResultNow(), RejectReason.Cancelled);
            LifecycleDriver.AssertNotShown(waiting.ResultNow(), RejectReason.Cancelled);
            Assert.That(_harness.Stage.CountOf(StageCallKind.Play), Is.EqualTo(playsBefore), "Nothing is hidden.");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1), "Only home remains.\n" + _harness.Describe());
            Assert.That(home.Phase, Is.EqualTo(ViewPhase.Open), "home phase");
            Assert.That(VisualOf("home").Hidden, Is.False, "home hidden");
            Assert.That(VisualOf("home").Interactive, Is.True, "home interactive");
            AssertSettled();
        }

        [Test]
        public void CloseTo_UnstartedRequestsAboveTheTarget_AreWithdrawnWhileTheNavigatorIsStillBusy()
        {
            _harness.Row("home").Row("shop").Row("loading").Row("waiting");
            var navigator = _harness.Navigator;
            Open("home");
            Open("shop");
            _harness.Stage.PlayMode = StageMode.Manual;
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("loading");
            Open("waiting");
            var busyAtNotShown = new List<bool>();
            RecordBusyAtNotShown(busyAtNotShown);
            var settledBefore = _harness.Observer.CountOf(ViewEventKind.Settled);

            navigator.CloseTo("home");

            Assert.That(busyAtNotShown, Is.EqualTo(new[] { true, true }), "Both withdrawals end while the pop is queued.");
            Assert.That(_harness.Stage.PendingPlayCount, Is.EqualTo(1), "The hide of shop is pending.\n" + _harness.Describe());
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Settled), Is.EqualTo(settledBefore), "No Settled while the hide is pending.");

            CompletePendingPlays();

            Assert.That(_harness.Observer.CountOf(ViewEventKind.Settled), Is.EqualTo(settledBefore + 1), "One Settled after the hide.");
        }

        [Test]
        public void CloseTo_CalledInsideOnOpenOfAScreenAbove_EndsThatScreenNotShownAndPopsTheOthers()
        {
            _harness.Row("home").Row("shop").Row("detail")
                .Map("detail", () => new FakeView<NoArgs, NoResult> { RunOnOpen = RedirectToHome });
            var navigator = _harness.Navigator;
            Open("home");
            Open("shop");

            var detail = navigator.ShowAsync(ViewKey.Create("detail"), _harness.Owner);

            LifecycleDriver.AssertNotShown(detail.ResultNow(), RejectReason.Cancelled);
            Assert.That(NavigationTestKit.HasEvent(_harness.Observer, ViewEventKind.Shown, "detail"), Is.False, "detail is never shown");
            Assert.That(_harness.Stage.LastInstanceOf("detail").ReleaseCount, Is.EqualTo(1), "detail instance releases");
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "shop").Reason, Is.EqualTo(CloseReason.Code), "shop reason");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1), "Only home remains.\n" + _harness.Describe());
            ViewHandle top;
            Assert.That(navigator.TryGetTop(ViewLayers.Screens, out top), Is.True);
            Assert.That(top.ViewId, Is.EqualTo("home"), "top view");
            Assert.That(_harness.Errors, Is.Empty, "routed errors");
            AssertSettled();
        }

        [Test]
        public void CloseTo_HideNeverCompletes_WatchdogSnapsAndEveryLatchedViewEnds()
        {
            _harness.Row("home").Row("shop").Row("detail");
            var navigator = _harness.Navigator;
            Open("home");
            Open("shop");
            Open("detail");
            _harness.Stage.PlayMode = StageMode.Manual;
            Assert.That(navigator.CloseTo("home"), Is.True, "CloseTo finds home.");

            _harness.Advance(LayerRunner.DefaultTransitionTimeoutSeconds + 0.5f);

            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "shop").Reason, Is.EqualTo(CloseReason.Code), "shop reason");
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "detail").Reason, Is.EqualTo(CloseReason.Code), "detail reason");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1), "Only home remains.\n" + _harness.Describe());
            ViewHandle top;
            Assert.That(navigator.TryGetTop(ViewLayers.Screens, out top), Is.True);
            Assert.That(top.ViewId, Is.EqualTo("home"), "top view");
            AssertSettled();
        }

        [Test]
        public void CloseTo_RootDestroyedWhileThePopIsPending_EndsEachLatchedRequestOnceAndReleasesInstancesOnce()
        {
            _harness.Row("home").Row("shop").Row("detail");
            var navigator = _harness.Navigator;
            var home = Open("home");
            var shop = Open("shop");
            var detail = Open("detail");
            _harness.Stage.PlayMode = StageMode.Manual;
            Assert.That(navigator.CloseTo("home"), Is.True, "CloseTo finds home.");

            navigator.Teardown();

            Assert.That(CountEventsFor(ViewEventKind.Closed, home.RequestId), Is.EqualTo(1), "Closed events of home");
            Assert.That(CountEventsFor(ViewEventKind.Closed, shop.RequestId), Is.EqualTo(1), "Closed events of shop");
            Assert.That(CountEventsFor(ViewEventKind.Closed, detail.RequestId), Is.EqualTo(1), "Closed events of detail");
            _harness.Stage.AssertAllReleased();
            Assert.That(_harness.ShieldCount, Is.Zero, "shield holders");
        }

        [Test]
        public void CloseTo_TargetEndsBeforeThePopRuns_StillPopsTheScreensAboveIt()
        {
            _harness.Row("home").Row("shop").Row("detail");
            var navigator = _harness.Navigator;
            var homeOwner = _harness.NewOwner();
            navigator.Open(ViewKey.Create("home"), homeOwner.Token);
            Open("shop");
            _harness.Stage.PlayMode = StageMode.Manual;
            var detail = Open("detail");
            Assert.That(detail.Phase, Is.EqualTo(ViewPhase.Opening), "detail is still in its show");
            Assert.That(navigator.CloseTo("home"), Is.True);

            homeOwner.Cancel();
            CompletePendingPlays();

            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "shop").Reason, Is.EqualTo(CloseReason.Code), "shop reason");
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "detail").Reason, Is.EqualTo(CloseReason.Code), "detail reason");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.Zero, "The layer is empty.\n" + _harness.Describe());
            Assert.That(_harness.ShieldCount, Is.Zero, _harness.Describe());
            Assert.That(navigator.IsBusy, Is.False, "navigator busy");
        }

        [Test]
        public void CloseTo_WhileQueuedBehindARunningOpen_DescribesEveryScreenAboveTheTargetAsClosePending()
        {
            _harness.Row("home").Row("shop").Row("detail");
            var home = Open("home");
            var shop = Open("shop");
            _harness.Stage.PlayMode = StageMode.Manual;
            var detail = Open("detail");

            _harness.Navigator.CloseTo("home");

            var described = _harness.Describe();
            Assert.That(described, Does.Match("#" + shop.RequestId + " shop \\w+ slot \\d+ close pending: Code"), "shop line");
            Assert.That(described, Does.Match("#" + detail.RequestId + " detail \\w+ slot \\d+ close pending: Code"), "detail line");
            Assert.That(described, Does.Not.Match("#" + home.RequestId + " home \\w+ slot \\d+ close pending"), "The target is not latched.");
        }

        [Test]
        public void CloseTo_AReopenedRequestStillWaitingWithTheInstance_IsWithdrawnAsCancelledAndReleasesTheInstanceOnce()
        {
            _harness.Row("home").Row("library").Row("item").Map<int, NoResult>("item");
            var key = ViewKey.Create<int>("item");
            var navigator = _harness.Navigator;
            var home = Open("home");
            Open("library");
            var first = navigator.ShowAsync(key, 1, _harness.Owner);
            var instance = _harness.Stage.LastInstanceOf("item");
            _harness.Stage.PlayMode = StageMode.Manual;
            // Home's hide keeps the runner busy, so the reopened request waits holding the instance.
            home.Close();
            var second = navigator.ShowAsync(key, 2, _harness.Owner);
            ViewHandle standIn;
            Assert.That(navigator.TryGetOpen("item", out standIn), Is.True);
            Assert.That(standIn.Phase, Is.EqualTo(ViewPhase.Waiting), "The reopened request has not run yet.\n" + _harness.Describe());

            var popped = navigator.CloseTo("library");
            CompletePendingPlays();

            Assert.That(popped, Is.True);
            LifecycleDriver.AssertNotShown(second.ResultNow(), RejectReason.Cancelled);
            var firstResult = first.ResultNow();
            Assert.That(firstResult.WasShown, Is.True, "first was shown");
            Assert.That(firstResult.Reason, Is.EqualTo(CloseReason.Navigated), "first reason");
            Assert.That(instance.ReleaseCount, Is.EqualTo(1), "item instance releases");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1), "Only library remains.\n" + _harness.Describe());
            ViewHandle top;
            Assert.That(navigator.TryGetTop(ViewLayers.Screens, out top), Is.True);
            Assert.That(top.ViewId, Is.EqualTo("library"), "top view");
            Assert.That(_harness.ShieldCount, Is.Zero, _harness.Describe());
        }

        // Return to an existing screen, key without arguments

        [Test]
        public void ReturnToExisting_KeyWithoutArguments_PopsEverythingAbove_AndRunsOnOpenOnlyOnce()
        {
            _harness.Row("a").Row("b").Row("c");
            var original = Open("a");
            Open("b");
            Open("c");
            var viewA = _harness.ViewOf<FakeView<NoArgs, NoResult>>("a");

            var again = Open("a");

            Assert.That(again.RequestId, Is.EqualTo(original.RequestId), "Open returns the existing handle.");
            Assert.That(again.IsAlive, Is.True);
            Assert.That(viewA.OpenCount, Is.EqualTo(1), "Nothing is rebound for a key without arguments.");
            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "a"), Is.EqualTo(1));
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1));
            Assert.That(_harness.Navigator.IsOpen("b"), Is.False);
            Assert.That(_harness.Navigator.IsOpen("c"), Is.False);
            Assert.That(VisualOf("a").Hidden, Is.False);
            Assert.That(VisualOf("a").Interactive, Is.True);
            Assert.That(_harness.ShieldCount, Is.Zero);

            // No second request exists for "a": its events are those of the first request.
            Assert.That(
                _harness.Observer.KindsForView("a"),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }));
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "b").Reason, Is.EqualTo(CloseReason.Navigated));
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "c").Reason, Is.EqualTo(CloseReason.Navigated));
        }

        [Test]
        public void ReturnToExisting_KeyWithoutArguments_WhenAlreadyOnTop_PopsNothingAndPlaysNoTransition()
        {
            _harness.Row("a").Row("b");
            Open("a");
            var original = Open("b");
            var viewB = _harness.ViewOf<FakeView<NoArgs, NoResult>>("b");
            _harness.Stage.ClearCalls();

            var again = Open("b");

            Assert.That(again.RequestId, Is.EqualTo(original.RequestId));
            Assert.That(viewB.OpenCount, Is.EqualTo(1));
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2));
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(
                _harness.Stage.CallsOf(StageCallKind.Acquire, StageCallKind.Play, StageCallKind.Snap, StageCallKind.Release),
                Is.Empty);
        }

        [Test]
        public void ReturnToExisting_WhileTheExistingRequestIsStillLoading_JoinsItWithoutASecondLoad()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;

            var first = Open("a");
            var second = Open("a");

            Assert.That(second.RequestId, Is.EqualTo(first.RequestId));
            Assert.That(_harness.Stage.CountOf(StageCallKind.Acquire), Is.EqualTo(1));
            Assert.That(_harness.Stage.PendingAcquireCount, Is.EqualTo(1));

            _harness.Stage.CompleteAcquire();

            Assert.That(first.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_harness.ViewOf<FakeView<NoArgs, NoResult>>("a").OpenCount, Is.EqualTo(1));
            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "a"), Is.EqualTo(1));
        }

        // Joiners

        [Test]
        public void Joiners_ShowAsyncOnAnExistingScreen_ReceiveTheValueTheViewCompletesWith()
        {
            var key = ViewKey.Create<NoArgs, int>("counter");
            _harness.Row("counter").Map<NoArgs, int>("counter").Row("other");
            var navigator = _harness.Navigator;

            var first = navigator.ShowAsync(key, _harness.Owner);
            Open("other");
            var joiner = navigator.ShowAsync(key, _harness.Owner);
            var view = _harness.ViewOf<FakeView<NoArgs, int>>("counter");

            Assert.That(first.IsPending(), Is.True);
            Assert.That(joiner.IsPending(), Is.True);
            Assert.That(view.OpenCount, Is.EqualTo(1));

            view.Complete(42);

            var firstResult = first.ResultNow();
            var joinerResult = joiner.ResultNow();
            Assert.That(firstResult.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(firstResult.HasValue, Is.True);
            Assert.That(firstResult.Value, Is.EqualTo(42));
            Assert.That(joinerResult.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(joinerResult.HasValue, Is.True);
            Assert.That(joinerResult.Value, Is.EqualTo(42));
            Assert.That(joinerResult.RequestId, Is.EqualTo(firstResult.RequestId), "A joiner shares the request of the existing screen.");
        }

        [Test]
        public void Joiners_AllReceiveTheSameReason_WhenBackClosesTheScreen()
        {
            _harness.Row("home").Row("b");
            var navigator = _harness.Navigator;
            Open("home");
            var first = navigator.ShowAsync(ViewKey.Create("b"), _harness.Owner);
            var joiner = navigator.ShowAsync(ViewKey.Create("b"), _harness.Owner);

            Assert.That(navigator.Back(), Is.True);

            var firstResult = first.ResultNow();
            var joinerResult = joiner.ResultNow();
            Assert.That(firstResult.Reason, Is.EqualTo(CloseReason.Back));
            Assert.That(joinerResult.Reason, Is.EqualTo(CloseReason.Back));
            Assert.That(joinerResult.WasShown, Is.True);
            Assert.That(joinerResult.RequestId, Is.EqualTo(firstResult.RequestId));
        }

        [Test]
        public void ShowAsync_JoinWhoseOwnerEnds_StaysPendingAndLeavesTheScreenOpen()
        {
            _harness.Row("home");
            var navigator = _harness.Navigator;
            var key = ViewKey.Create("home");
            var home = navigator.Open(key, navigator.RootToken);
            var joinOwner = _harness.NewOwner();
            var join = navigator.ShowAsync(key, joinOwner.Token);

            joinOwner.Cancel();

            Assert.That(join.IsPending(), Is.True, "A join does not bind its owner token.");
            Assert.That(home.IsAlive, Is.True, "home alive");
            Assert.That(home.Phase, Is.EqualTo(ViewPhase.Open), "home phase");
            Assert.That(
                _harness.Observer.KindsForView("home"),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }),
                "No NotShown or Closed is published for home.");

            home.Close();

            var result = join.ResultNow();
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Code), "join reason");
            Assert.That(result.RequestId, Is.EqualTo(home.RequestId), "join request id");
        }

        [Test]
        public void ShowAsync_JoinWithAttachedCancellation_ResumesAsStoppedWhenItsTokenEnds_AndLeavesTheScreenOpen()
        {
            _harness.Row("home");
            var navigator = _harness.Navigator;
            var key = ViewKey.Create("home");
            var home = navigator.Open(key, navigator.RootToken);
            var plainJoin = navigator.ShowAsync(key, _harness.Owner);
            var stop = _harness.NewOwner();
            var stoppable = ShowUntilStopped(navigator.ShowAsync(key, stop.Token), stop.Token);

            stop.Cancel();

            var (stopped, _) = stoppable.ResultNow();
            Assert.That(stopped, Is.True, "The wait stops at the caller's token.");
            Assert.That(home.IsAlive, Is.True, "home alive");
            Assert.That(home.Phase, Is.EqualTo(ViewPhase.Open), "home phase");
            Assert.That(plainJoin.IsPending(), Is.True, "plain join pending");

            home.Close();

            var result = plainJoin.ResultNow();
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Code), "plain join reason");
            Assert.That(result.RequestId, Is.EqualTo(home.RequestId), "plain join request id");
        }

        [Test]
        public void ShowAsync_NewRequestWithAttachedCancellation_ResumesAsStoppedWhenItsOwnerEnds_AndTheRequestEndsAsOwnerEnded()
        {
            _harness.Row("shop");
            var navigator = _harness.Navigator;
            var owner = _harness.NewOwner();
            var stoppable = ShowUntilStopped(navigator.ShowAsync(ViewKey.Create("shop"), owner.Token), owner.Token);
            ViewHandle shop;
            Assert.That(navigator.TryGetOpen("shop", out shop), Is.True, "shop is open");

            owner.Cancel();

            var (stopped, _) = stoppable.ResultNow();
            // Assumes the runtime runs token callbacks newest first, so the attach callback beats the owner callback.
            Assert.That(stopped, Is.True, "The attach callback registered after the owner registration runs first, so the recipe reports stopped.");
            Assert.That(shop.IsAlive, Is.False, "shop alive");
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "shop").Reason, Is.EqualTo(CloseReason.OwnerEnded), "shop reason");
        }

        // Reopen in place, key with arguments

        [Test]
        public void ReopenInPlace_KeyWithArguments_ReusesTheInstance_AndOpensItAgainWithTheNewArguments()
        {
            _harness.Row("home").Row("detail").Map<string, NoResult>("detail");
            var key = ViewKey.Create<string>("detail");
            var navigator = _harness.Navigator;
            Open("home");
            var first = navigator.ShowAsync(key, "first", _harness.Owner);
            var view = _harness.ViewOf<FakeView<string, NoResult>>("detail");
            var instance = _harness.Stage.LastInstanceOf("detail");
            _harness.Stage.ClearCalls();

            var second = navigator.ShowAsync(key, "second", _harness.Owner);

            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "detail"), Is.EqualTo(1), "One instance throughout.");
            Assert.That(instance.ReleaseCount, Is.Zero, "The instance is not released in between.");
            Assert.That(_harness.Stage.CountOf(StageCallKind.Acquire), Is.Zero);
            Assert.That(_harness.Stage.CountOf(StageCallKind.Release), Is.Zero);
            Assert.That(_harness.Stage.CountOf(StageCallKind.Stash), Is.Zero);
            Assert.That(view.OpenArgs, Is.EqualTo(new[] { "first", "second" }), "The new arguments are shown.");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2), "The stack does not grow.");
            Assert.That(first.IsPending(), Is.False);
            Assert.That(second.IsPending(), Is.True);
        }

        [Test]
        public void ReopenInPlace_OldRequestEndsAsNavigated_ItsScopeEnds_AndANewRequestTakesOver()
        {
            _harness.Row("home").Row("detail").Map<string, NoResult>("detail");
            var key = ViewKey.Create<string>("detail");
            var navigator = _harness.Navigator;
            Open("home");
            var first = navigator.ShowAsync(key, "first", _harness.Owner);
            var view = _harness.ViewOf<FakeView<string, NoResult>>("detail");
            ViewHandle oldHandle;
            Assert.That(navigator.TryGetOpen("detail", out oldHandle), Is.True);
            var oldWaiter = oldHandle.WaitForCloseAsync(CancellationToken.None);
            var firstScope = view.Context.Open;

            var second = navigator.ShowAsync(key, "second", _harness.Owner);

            var firstResult = first.ResultNow();
            Assert.That(firstResult.WasShown, Is.True);
            Assert.That(firstResult.Reason, Is.EqualTo(CloseReason.Navigated));
            Assert.That(firstResult.RequestId, Is.EqualTo(oldHandle.RequestId));
            Assert.That(oldWaiter.ResultNow().Reason, Is.EqualTo(CloseReason.Navigated));
            Assert.That(oldHandle.IsAlive, Is.False);
            Assert.That(firstScope.IsEnded, Is.True, "The scope of the old request ends.");
            Assert.That(view.Context.Open, Is.Not.SameAs(firstScope), "The new request opens on a fresh scope.");
            Assert.That(view.Context.Open.IsEnded, Is.False);
            Assert.That(second.IsPending(), Is.True);

            ViewHandle newHandle;
            Assert.That(navigator.TryGetOpen("detail", out newHandle), Is.True);
            Assert.That(newHandle.RequestId, Is.Not.EqualTo(oldHandle.RequestId));
            Assert.That(newHandle.Phase, Is.EqualTo(ViewPhase.Open));

            var observer = _harness.Observer;
            Assert.That(
                observer.KindsFor(oldHandle.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }));
            Assert.That(
                observer.KindsFor(newHandle.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }));
        }

        [Test]
        public void ReopenInPlace_NewRequestIsSnappedToShown_WithoutATransition_InTheSameSlot()
        {
            _harness.Row("home").Row("detail").Map<string, NoResult>("detail");
            var key = ViewKey.Create<string>("detail");
            var navigator = _harness.Navigator;
            Open("home");
            navigator.ShowAsync(key, "first", _harness.Owner);
            var instance = _harness.Stage.LastInstanceOf("detail");
            var orderBefore = VisualOf("detail").SortingOrder;
            _harness.Stage.ClearCalls();

            navigator.ShowAsync(key, "second", _harness.Owner);

            Assert.That(instance.LastSnapShown, Is.EqualTo(true));
            Assert.That(instance.PlayCount, Is.EqualTo(1), "Only the first open played a show transition.");
            Assert.That(_harness.Stage.CountOf(StageCallKind.Play), Is.Zero);
            var visual = VisualOf("detail");
            Assert.That(visual.SortingOrder, Is.EqualTo(orderBefore), "The new request takes over the slot.");
            Assert.That(visual.Hidden, Is.False);
            Assert.That(visual.Interactive, Is.True);
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(navigator.IsBusy, Is.False);
        }

        [Test]
        public void ReopenInPlace_WithAScreenAbove_PopsFirst_AndOnlyThenFinalizesTheOldRequest()
        {
            _harness.Row("home").Row("detail").Map<string, NoResult>("detail").Row("other");
            var key = ViewKey.Create<string>("detail");
            var navigator = _harness.Navigator;
            Open("home");
            var first = navigator.ShowAsync(key, "first", _harness.Owner);
            Open("other");
            var view = _harness.ViewOf<FakeView<string, NoResult>>("detail");
            var instance = _harness.Stage.LastInstanceOf("detail");
            _harness.Stage.PlayMode = StageMode.Manual;

            var second = navigator.ShowAsync(key, "second", _harness.Owner);

            // The pop-to waits for the hide of the screen above; the old request ends after it.
            Assert.That(_harness.Stage.PendingPlayCount, Is.EqualTo(1));
            Assert.That(first.IsPending(), Is.True, "The old request is finalized after the pop-to, not before.");
            Assert.That(view.OpenCount, Is.EqualTo(1));

            _harness.Stage.CompletePlay();

            Assert.That(first.ResultNow().Reason, Is.EqualTo(CloseReason.Navigated));
            Assert.That(view.OpenCount, Is.EqualTo(2));
            Assert.That(view.LastArgs, Is.EqualTo("second"));
            Assert.That(navigator.IsOpen("other"), Is.False);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2));
            Assert.That(second.IsPending(), Is.True);
            Assert.That(instance.ReleaseCount, Is.Zero);
            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "detail"), Is.EqualTo(1));
        }

        [Test]
        public void ReopenInPlace_BackGoesToTheScreenBelow_NotToTheEarlierContent()
        {
            _harness.Row("home").Row("detail").Map<string, NoResult>("detail");
            var key = ViewKey.Create<string>("detail");
            var navigator = _harness.Navigator;
            Open("home");
            navigator.ShowAsync(key, "first", _harness.Owner);
            var second = navigator.ShowAsync(key, "second", _harness.Owner);
            var view = _harness.ViewOf<FakeView<string, NoResult>>("detail");

            Assert.That(navigator.Back(), Is.True);

            Assert.That(second.ResultNow().Reason, Is.EqualTo(CloseReason.Back));
            Assert.That(navigator.IsOpen("detail"), Is.False);
            Assert.That(navigator.IsOpen("home"), Is.True);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1));
            Assert.That(view.OpenCount, Is.EqualTo(2), "The earlier arguments are not opened again.");
            Assert.That(_harness.Stage.LastInstanceOf("detail").ReleaseCount, Is.EqualTo(1));

            // Home is now the root.
            Assert.That(navigator.Back(), Is.False);
        }

        // Reopen in place with a screen above: the old request waits while the pop runs

        [Test]
        public void ReopenInPlaceWithAScreenAbove_NewRequestWithdrawnBeforeItsOpen_LeavesTheOldScreenOpenAndUsable(
            [Values] ReplacementWithdrawal cause)
        {
            var setup = ArrangeDetailBelowOther();
            var secondOwner = _harness.NewOwner();
            _harness.Navigator.ShowAsync(setup.Key, "second", secondOwner.Token);
            var visibleAtWithdrawal = setup.View.VisibleCount;

            WithdrawReplacement(cause, secondOwner);
            CompletePendingPlays();

            Assert.That(setup.First.IsPending(), Is.True, "first pending");
            Assert.That(setup.FirstHandle.IsAlive, Is.True, "first handle alive");
            Assert.That(setup.FirstHandle.Phase, Is.EqualTo(ViewPhase.Open), "first phase");
            var visual = VisualOf("detail");
            Assert.That(visual.Hidden, Is.False, "detail hidden");
            Assert.That(visual.Interactive, Is.True, "detail interactive");
            Assert.That(setup.View.OpenArgs, Is.EqualTo(new[] { "first" }), "OnOpen arguments");
            Assert.That(setup.View.VisibleCount, Is.EqualTo(visibleAtWithdrawal + 1), "A visible scope begins after the withdrawal.");
            Assert.That(setup.View.LastVisible.IsEnded, Is.False, "the new visible scope is live");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2), "home and detail remain.\n" + _harness.Describe());
            ViewHandle top;
            Assert.That(_harness.Navigator.TryGetTop(ViewLayers.Screens, out top), Is.True);
            Assert.That(top.RequestId, Is.EqualTo(setup.FirstHandle.RequestId), "first is on top");
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "other").Reason, Is.EqualTo(CloseReason.Navigated), "other reason");
            Assert.That(setup.Instance.ReleaseCount, Is.Zero, "detail instance releases");
            Assert.That(_harness.ShieldCount, Is.Zero, _harness.Describe());
            Assert.That(_harness.Navigator.IsBusy, Is.False, "navigator busy");
        }

        [Test]
        public void ReopenInPlaceWithAScreenAbove_NewRequestWithdrawnByBack_EndsItAsCancelled()
        {
            var setup = ArrangeDetailBelowOther();
            var second = _harness.Navigator.ShowAsync(setup.Key, "second", _harness.NewOwner().Token);

            _harness.Navigator.Back();

            LifecycleDriver.AssertNotShown(second.ResultNow(), RejectReason.Cancelled);
        }

        [Test]
        public void ReopenInPlaceWithAScreenAbove_NewRequestOwnerEnds_EndsItAsOwnerEnded()
        {
            var setup = ArrangeDetailBelowOther();
            var secondOwner = _harness.NewOwner();
            var second = _harness.Navigator.ShowAsync(setup.Key, "second", secondOwner.Token);

            secondOwner.Cancel();

            LifecycleDriver.AssertNotShown(second.ResultNow(), RejectReason.OwnerEnded);
        }

        [Test]
        public void ReopenInPlaceWithAScreenAbove_OwnedByTheOpenScopeOfTheScreenAbove_LeavesTheOldScreenOpen()
        {
            var setup = ArrangeDetailBelowOther();
            var otherScope = _harness.ViewOf<FakeView<NoArgs, NoResult>>("other").Context.Open;
            var second = _harness.Navigator.ShowAsync(setup.Key, "second", otherScope.Token);

            CompletePendingPlays();

            LifecycleDriver.AssertNotShown(second.ResultNow(), RejectReason.OwnerEnded);
            Assert.That(setup.FirstHandle.Phase, Is.EqualTo(ViewPhase.Open), "first phase");
            var visual = VisualOf("detail");
            Assert.That(visual.Hidden, Is.False, "detail hidden");
            Assert.That(visual.Interactive, Is.True, "detail interactive");
            ViewHandle top;
            Assert.That(_harness.Navigator.TryGetTop(ViewLayers.Screens, out top), Is.True);
            Assert.That(top.RequestId, Is.EqualTo(setup.FirstHandle.RequestId), "first is on top");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2), "home and detail remain.\n" + _harness.Describe());
            AssertSettled();
        }

        [Test]
        public void ReopenInPlaceWithAScreenAbove_CloseAllBeforeTheOpenRuns_EndsEveryRequestAndEmptiesTheLayer()
        {
            var setup = ArrangeDetailBelowOther();
            var second = _harness.Navigator.ShowAsync(setup.Key, "second", _harness.NewOwner().Token);

            _harness.Navigator.CloseAll(ViewLayers.Screens);
            CompletePendingPlays();

            var firstResult = setup.First.ResultNow();
            Assert.That(firstResult.WasShown, Is.True, "first was shown");
            Assert.That(firstResult.Reason, Is.EqualTo(CloseReason.Code), "first reason");
            LifecycleDriver.AssertNotShown(second.ResultNow(), RejectReason.Cancelled);
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "other").Reason, Is.EqualTo(CloseReason.Navigated), "other reason");
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "home").Reason, Is.EqualTo(CloseReason.Code), "home reason");
            Assert.That(setup.Home.IsAlive, Is.False, "home alive");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.Zero, "The layer is empty.\n" + _harness.Describe());
            Assert.That(setup.Instance.ReleaseCount, Is.EqualTo(1), "detail instance releases");
            Assert.That(_harness.ShieldCount, Is.Zero, _harness.Describe());
        }

        [Test]
        public void ReopenInPlaceWithAScreenAbove_OldRequestEndsWhileThePopIsPending_PopsTheScreenAboveAndOpensTheNewRequestOnAFreshInstance(
            [Values] OldRequestEnd cause)
        {
            var setup = ArrangeDetailBelowOther();
            var second = _harness.Navigator.ShowAsync(setup.Key, "second", _harness.NewOwner().Token);

            EndOldRequest(cause, setup);
            CompletePendingPlays();

            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "other").Reason, Is.EqualTo(CloseReason.Navigated), "other reason");
            Assert.That(setup.Instance.ReleaseCount, Is.EqualTo(1), "old detail instance releases");
            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "detail"), Is.EqualTo(2), "detail instances acquired");
            var newView = _harness.ViewOf<FakeVisibleView<string, NoResult>>("detail");
            Assert.That(newView, Is.Not.SameAs(setup.View), "The new request runs on a fresh view.");
            Assert.That(newView.OpenArgs, Is.EqualTo(new[] { "second" }), "new view OnOpen arguments");
            Assert.That(second.IsPending(), Is.True, "second pending");
            ViewHandle current;
            Assert.That(_harness.Navigator.TryGetOpen("detail", out current), Is.True);
            Assert.That(current.RequestId, Is.Not.EqualTo(setup.FirstHandle.RequestId), "detail is the new request");
            Assert.That(current.Phase, Is.EqualTo(ViewPhase.Open), "new detail phase");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2), "home and the new detail remain.\n" + _harness.Describe());
            var firstResult = setup.First.ResultNow();
            Assert.That(firstResult.Reason, Is.EqualTo(ReasonOf(cause)), "first ends with its own reason");
            Assert.That(firstResult.Reason, Is.Not.EqualTo(CloseReason.Navigated), "first is not ended as Navigated");
            Assert.That(CountEventsFor(ViewEventKind.Closed, setup.FirstHandle.RequestId), Is.EqualTo(1), "Closed events of first");
            AssertSettled();
        }

        [Test]
        public void ReopenInPlaceWithAScreenAbove_OldRequestClosedByItsHandle_EndsWithCodeAndTheNewRequestTakesTheInstance()
        {
            var setup = ArrangeDetailBelowOther();
            var second = _harness.Navigator.ShowAsync(setup.Key, "second", _harness.NewOwner().Token);

            setup.FirstHandle.Close();
            CompletePendingPlays();

            Assert.That(setup.First.ResultNow().Reason, Is.EqualTo(CloseReason.Code), "first reason");
            Assert.That(second.IsPending(), Is.True, "second pending");
            ViewHandle current;
            Assert.That(_harness.Navigator.TryGetOpen("detail", out current), Is.True);
            Assert.That(current.RequestId, Is.Not.EqualTo(setup.FirstHandle.RequestId), "detail is the new request");
            Assert.That(current.Phase, Is.EqualTo(ViewPhase.Open), "new detail phase");
            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "detail"), Is.EqualTo(1), "detail instances acquired");
            Assert.That(setup.Instance.ReleaseCount, Is.Zero, "detail instance releases");
            Assert.That(setup.View.OpenArgs, Is.EqualTo(new[] { "first", "second" }), "OnOpen arguments");
            AssertSettled();
        }

        [Test]
        public void ReopenInPlaceWithAScreenAbove_RootDestroyedWhileThePopIsPending_EndsEachRequestOnceAndReleasesTheInstanceOnce()
        {
            var setup = ArrangeDetailBelowOther();
            var second = _harness.Navigator.ShowAsync(setup.Key, "second", _harness.NewOwner().Token);

            _harness.Navigator.Teardown();

            var firstResult = setup.First.ResultNow();
            Assert.That(firstResult.WasShown, Is.True, "first was shown");
            Assert.That(firstResult.Reason, Is.EqualTo(CloseReason.Destroyed), "first reason");
            LifecycleDriver.AssertNotShown(second.ResultNow(), RejectReason.RootGone);
            Assert.That(setup.Instance.ReleaseCount, Is.EqualTo(1), "detail instance releases");
            Assert.That(_harness.ShieldCount, Is.Zero, "shield holders");
        }

        [Test]
        public void ReopenInPlace_OfARequestStillWaitingToReplace_HandsTheOriginalInstanceToTheNewest()
        {
            var setup = ArrangeDetailBelowOther();
            var second = _harness.Navigator.ShowAsync(setup.Key, "second", _harness.NewOwner().Token);

            var third = _harness.Navigator.ShowAsync(setup.Key, "third", _harness.Owner);

            LifecycleDriver.AssertNotShown(second.ResultNow(), RejectReason.Cancelled);

            CompletePendingPlays();

            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "detail"), Is.EqualTo(1), "detail instances acquired");
            Assert.That(setup.View.OpenArgs, Is.EqualTo(new[] { "first", "third" }), "OnOpen arguments");
            Assert.That(setup.First.ResultNow().Reason, Is.EqualTo(CloseReason.Navigated), "first reason");
            Assert.That(third.IsPending(), Is.True, "third pending");
            ViewHandle current;
            Assert.That(_harness.Navigator.TryGetOpen("detail", out current), Is.True);
            Assert.That(current.Phase, Is.EqualTo(ViewPhase.Open), "third phase");
            AssertSettled();
        }

        [Test]
        public void ReopenInPlace_OfARequestStillWaitingToReplace_NewestWithdrawn_LeavesTheOriginalOpenAndUsable(
            [Values] ReplacementWithdrawal cause)
        {
            var setup = ArrangeDetailBelowOther();
            var second = _harness.Navigator.ShowAsync(setup.Key, "second", _harness.NewOwner().Token);
            var thirdOwner = _harness.NewOwner();
            var third = _harness.Navigator.ShowAsync(setup.Key, "third", thirdOwner.Token);

            WithdrawReplacement(cause, thirdOwner);
            CompletePendingPlays();

            LifecycleDriver.AssertNotShown(second.ResultNow(), RejectReason.Cancelled);
            LifecycleDriver.AssertNotShown(third.ResultNow(), RejectionOf(cause));
            Assert.That(setup.First.IsPending(), Is.True, "first pending");
            Assert.That(setup.FirstHandle.IsAlive, Is.True, "first handle alive");
            Assert.That(setup.FirstHandle.Phase, Is.EqualTo(ViewPhase.Open), "first phase");
            var visual = VisualOf("detail");
            Assert.That(visual.Hidden, Is.False, "detail hidden");
            Assert.That(visual.Interactive, Is.True, "detail interactive");
            ViewHandle top;
            Assert.That(_harness.Navigator.TryGetTop(ViewLayers.Screens, out top), Is.True);
            Assert.That(top.RequestId, Is.EqualTo(setup.FirstHandle.RequestId), "first is on top");
            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "detail"), Is.EqualTo(1), "detail instances acquired");
            Assert.That(setup.Instance.ReleaseCount, Is.Zero, "detail instance releases");
            Assert.That(setup.View.OpenArgs, Is.EqualTo(new[] { "first" }), "OnOpen arguments");
            Assert.That(setup.View.VisibleCount, Is.EqualTo(setup.VisibleBefore + 1), "One visible scope begins after the withdrawal.");
            AssertSettled();
        }

        [Test]
        public void ReopenInPlace_UnstartedRequestAboveTheOldScreen_IsWithdrawnAndTheNewRequestTakesOverAtTheCall()
        {
            _harness.Row("home").Row("detail").Map<string, NoResult>("detail").Row("loading");
            var key = ViewKey.Create<string>("detail");
            var navigator = _harness.Navigator;
            Open("home");
            var first = navigator.ShowAsync(key, "first", _harness.Owner);
            var view = _harness.ViewOf<FakeView<string, NoResult>>("detail");
            var instance = _harness.Stage.LastInstanceOf("detail");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var loading = navigator.ShowAsync(ViewKey.Create("loading"), _harness.Owner);
            _harness.Stage.ClearCalls();

            var second = navigator.ShowAsync(key, "second", _harness.Owner);

            LifecycleDriver.AssertNotShown(loading.ResultNow(), RejectReason.Cancelled);
            var firstResult = first.ResultNow();
            Assert.That(firstResult.WasShown, Is.True, "first was shown");
            Assert.That(firstResult.Reason, Is.EqualTo(CloseReason.Navigated), "first reason");
            Assert.That(_harness.Stage.CountOf(StageCallKind.Play), Is.Zero, "Nothing is popped.");

            CompletePendingPlays();

            Assert.That(second.IsPending(), Is.True, "second pending");
            ViewHandle current;
            Assert.That(navigator.TryGetOpen("detail", out current), Is.True);
            Assert.That(current.RequestId, Is.Not.EqualTo(firstResult.RequestId), "detail is the new request");
            Assert.That(current.Phase, Is.EqualTo(ViewPhase.Open), "new detail phase");
            Assert.That(NavigationTestKit.InstanceCount(_harness.Stage, "detail"), Is.EqualTo(1), "detail instances acquired");
            Assert.That(instance.ReleaseCount, Is.Zero, "detail instance releases");
            Assert.That(view.OpenArgs, Is.EqualTo(new[] { "first", "second" }), "OnOpen arguments");
            AssertSettled();
        }

        [Test]
        public void ReopenInPlaceWithAScreenAbove_WhileThePopRuns_OldScreenIsNotInteractiveAndGetsNoOnVisible()
        {
            var setup = ArrangeDetailBelowOther();

            _harness.Navigator.ShowAsync(setup.Key, "second", _harness.NewOwner().Token);

            Assert.That(_harness.Stage.PendingPlayCount, Is.EqualTo(1), "The hide of other is pending.");
            var visual = VisualOf("detail");
            Assert.That(visual.Hidden, Is.False, "detail hidden");
            Assert.That(visual.Interactive, Is.False, "detail interactive");
            Assert.That(setup.View.VisibleCount, Is.EqualTo(setup.VisibleBefore), "OnVisible calls");
        }
    }
}
