using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Input shield: holders per operation, HoldInput, the long-hold diagnostic and the load indicator.
    public sealed class NavigationShieldTests
    {
        private const string LongHoldId = "UIDIRECTOR110";

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

        // Count per operation

        [Test]
        public void Open_OnAShieldingLayer_HoldsTheShieldFromTheCallUntilTheViewIsShown()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;

            Open("a");

            // The flag is set inside the Open call.
            Assert.That(_harness.ShieldCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.ShieldBlocking, Is.True);
            Assert.That(_harness.Stage.ShieldRaiseCount, Is.EqualTo(1));

            _harness.Stage.CompleteAcquire();

            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);
            Assert.That(_harness.Stage.ShieldDropCount, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Open_OnEachDefaultLayer_HoldsTheShieldWhileItLoads(int layerIndex)
        {
            var layers = new[] { ViewLayers.Screens, ViewLayers.Popups, ViewLayers.System };
            _harness.Row("v", layers[layerIndex]);
            _harness.Stage.AcquireMode = StageMode.Manual;

            Open("v");

            Assert.That(_harness.ShieldCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.ShieldBlocking, Is.True);
        }

        [Test]
        public void Open_OnALayerWithoutShieldsInput_HoldsNoShield()
        {
            _harness.Dispose();
            _harness = NavigationTestKit.NewHarnessWithHud();
            _harness.Row("overlay", NavigationTestKit.Hud);
            _harness.Stage.AcquireMode = StageMode.Manual;

            Open("overlay");

            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);
            Assert.That(_harness.Stage.CountOf(StageCallKind.SetShield), Is.Zero);
        }

        [Test]
        public void Open_EachAcceptedRequestHoldsOne_AndTheShieldDropsWhenTheLastIsShown()
        {
            _harness.Row("a").Row("b");
            _harness.Stage.AcquireMode = StageMode.Manual;

            Open("a");
            Open("b");

            Assert.That(_harness.ShieldCount, Is.EqualTo(2));
            Assert.That(_harness.Stage.ShieldRaiseCount, Is.EqualTo(1), "The shield goes up once, on the first holder.");

            _harness.Stage.CompleteAcquire();

            Assert.That(_harness.ShieldCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.ShieldBlocking, Is.True);
            Assert.That(_harness.Stage.ShieldDropCount, Is.Zero);

            _harness.Stage.CompleteAcquire();

            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);
            Assert.That(_harness.Stage.ShieldDropCount, Is.EqualTo(1));
        }

        [Test]
        public void Close_HoldsTheShieldFromTheCloseCauseUntilTheViewIsFinalized()
        {
            _harness.Row("p", ViewLayers.Popups);
            Open("p");
            var view = _harness.ViewOf<FakeView<NoArgs, NoResult>>("p");
            Assert.That(_harness.ShieldCount, Is.Zero);
            _harness.Stage.PlayMode = StageMode.Manual;

            view.Close();

            Assert.That(_harness.ShieldCount, Is.EqualTo(1), "One hold for the pending close, not one more for the close operation.");
            Assert.That(_harness.Stage.ShieldBlocking, Is.True);

            _harness.Stage.CompletePlay();

            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);
        }

        [Test]
        public void Close_OnALayerWithoutShieldsInput_HoldsNoShield()
        {
            _harness.Dispose();
            _harness = NavigationTestKit.NewHarnessWithHud();
            _harness.Row("overlay", NavigationTestKit.Hud);
            Open("overlay");
            var view = _harness.ViewOf<FakeView<NoArgs, NoResult>>("overlay");
            _harness.Stage.PlayMode = StageMode.Manual;

            view.Close();

            Assert.That(_harness.Stage.PendingPlayCount, Is.EqualTo(1));
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldRaiseCount, Is.Zero);
        }

        // Each view the pop removes holds its own pending close, so the count is not one; the stage shield goes up and down once.
        [Test]
        public void CloseTo_KeepsTheShieldUpForTheWholeOperation()
        {
            _harness.Row("a").Row("b").Row("c").Row("d");
            Open("a");
            Open("b");
            Open("c");
            Open("d");
            _harness.Stage.PlayMode = StageMode.Manual;
            var raisesBefore = _harness.Stage.ShieldRaiseCount;
            var dropsBefore = _harness.Stage.ShieldDropCount;

            Assert.That(_harness.Navigator.CloseTo("a"), Is.True);

            Assert.That(_harness.ShieldCount, Is.EqualTo(3), "one pending-close hold per latched view");
            Assert.That(_harness.Stage.ShieldBlocking, Is.True, "stage shield blocking right after the call");
            Assert.That(_harness.Stage.ShieldRaiseCount - raisesBefore, Is.EqualTo(1), "raises since the call");
            Assert.That(_harness.Stage.ShieldDropCount - dropsBefore, Is.Zero, "drops while the hide is pending");

            _harness.Stage.CompletePlay();

            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1), "only the target remains");
            Assert.That(_harness.Navigator.IsOpen("a"), Is.True, "the target is still open");
            Assert.That(_harness.ShieldCount, Is.Zero, "shield count after the hide");
            Assert.That(_harness.Stage.ShieldBlocking, Is.False, "stage shield blocking after the hide");
            Assert.That(_harness.Stage.ShieldDropCount - dropsBefore, Is.EqualTo(1), "drops since the call");
        }

        // Each view the close-all removes holds its own pending close, so the count is not one; the stage shield goes up and down once.
        [Test]
        public void CloseAll_KeepsTheShieldUpForTheWholeOperation()
        {
            _harness.Row("a").Row("b").Row("c");
            Open("a");
            Open("b");
            Open("c");
            _harness.Stage.PlayMode = StageMode.Manual;
            var raisesBefore = _harness.Stage.ShieldRaiseCount;
            var dropsBefore = _harness.Stage.ShieldDropCount;

            _harness.Navigator.CloseAll(ViewLayers.Screens);

            Assert.That(_harness.ShieldCount, Is.EqualTo(3), "one pending-close hold per latched view");
            Assert.That(_harness.Stage.ShieldBlocking, Is.True, "stage shield blocking right after the call");
            Assert.That(_harness.Stage.ShieldRaiseCount - raisesBefore, Is.EqualTo(1), "raises since the call");
            Assert.That(_harness.Stage.ShieldDropCount - dropsBefore, Is.Zero, "drops while the hide is pending");

            _harness.Stage.CompletePlay();

            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.Zero, "requests left on the layer");
            Assert.That(_harness.ShieldCount, Is.Zero, "shield count after the hide");
            Assert.That(_harness.Stage.ShieldBlocking, Is.False, "stage shield blocking after the hide");
            Assert.That(_harness.Stage.ShieldDropCount - dropsBefore, Is.EqualTo(1), "drops since the call");
        }

        [Test]
        public void CloseAll_WithdrawnWaitingRequestsReturnTheirHolds_LeavingOneForTheOperation()
        {
            _harness.Row("home").Row("b").Row("c");
            Open("home");
            _harness.Stage.AcquireMode = StageMode.Manual;
            _harness.Stage.PlayMode = StageMode.Manual;
            Open("b");
            Open("c");
            Assert.That(_harness.ShieldCount, Is.EqualTo(2));

            _harness.Navigator.CloseAll(ViewLayers.Screens);

            Assert.That(_harness.ShieldCount, Is.EqualTo(1), "Only home's pending-close hold, which also carries the close-all, holds the shield.");

            _harness.Stage.CompletePlay();

            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.Zero);
            Assert.That(_harness.ShieldCount, Is.Zero);
        }

        [Test]
        public void WithdrawingALoadingRequest_ReturnsItsHold()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var handle = Open("a");
            Assert.That(_harness.ShieldCount, Is.EqualTo(1));

            handle.Close();

            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);
        }

        [Test]
        public void OwnerEndingWhileLoading_ReturnsTheHold()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var owner = _harness.NewOwner();
            _harness.Navigator.ShowAsync(ViewKey.Create("a"), owner.Token);
            Assert.That(_harness.ShieldCount, Is.EqualTo(1));

            owner.Cancel();

            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);
        }

        // HoldInput

        [Test]
        public void HoldInput_RaisesTheShield_AndRemoveDropsIt()
        {
            var hold = _harness.Navigator.HoldInput("tutorial", _harness.Owner);

            Assert.That(hold.IsActive, Is.True);
            Assert.That(_harness.ShieldCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.ShieldBlocking, Is.True);

            hold.Remove();

            Assert.That(hold.IsActive, Is.False);
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);

            // Removing twice returns nothing twice.
            hold.Remove();
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldRaiseCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.ShieldDropCount, Is.EqualTo(1));
        }

        [Test]
        public void HoldInput_TwoHolds_ShieldStaysUpUntilTheLastIsRemoved()
        {
            var first = _harness.Navigator.HoldInput("tutorial", _harness.Owner);
            var second = _harness.Navigator.HoldInput("cutscene", _harness.Owner);

            Assert.That(_harness.ShieldCount, Is.EqualTo(2));

            first.Remove();

            Assert.That(_harness.ShieldCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.ShieldBlocking, Is.True);
            Assert.That(_harness.Stage.ShieldDropCount, Is.Zero);

            second.Remove();

            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);
            Assert.That(_harness.Stage.ShieldRaiseCount, Is.EqualTo(1));
            Assert.That(_harness.Stage.ShieldDropCount, Is.EqualTo(1));
        }

        [Test]
        public void HoldInput_OwnerEnds_RemovesTheHold()
        {
            var owner = _harness.NewOwner();
            var hold = _harness.Navigator.HoldInput("tutorial", owner.Token);
            Assert.That(_harness.ShieldCount, Is.EqualTo(1));

            owner.Cancel();

            Assert.That(hold.IsActive, Is.False);
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);
        }

        [Test]
        public void HoldInput_WithAnOwnerThatAlreadyEnded_HoldsNothing()
        {
            var owner = _harness.NewOwner();
            owner.Cancel();

            var hold = _harness.Navigator.HoldInput("tutorial", owner.Token);

            Assert.That(hold.IsActive, Is.False);
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);
        }

        [Test]
        public void HoldInput_KeepsTheNavigatorBusy_AndSettledIsPublishedWhenItEnds()
        {
            var hold = _harness.Navigator.HoldInput("tutorial", _harness.Owner);

            Assert.That(_harness.Navigator.IsBusy, Is.True);
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Settled), Is.Zero);

            hold.Remove();

            Assert.That(_harness.Navigator.IsBusy, Is.False);
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Settled), Is.EqualTo(1));
        }

        // Long-hold diagnostic

        [Test]
        public void HoldInput_OlderThan30Seconds_LogsUIDIRECTOR110OnceWithItsReasonAndCallSite()
        {
            _harness.Navigator.HoldInput("tutorial", _harness.Owner);

            _harness.Advance(29f);
            Assert.That(_harness.Diagnostics.Count(LongHoldId), Is.Zero);

            _harness.Advance(2f);
            Assert.That(_harness.Diagnostics.Count(LongHoldId), Is.EqualTo(1));

            string message = null;
            foreach (var entry in _harness.Diagnostics.Entries)
            {
                if (entry.Id == LongHoldId)
                    message = entry.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("tutorial"));
            Assert.That(message, Does.Contain(nameof(HoldInput_OlderThan30Seconds_LogsUIDIRECTOR110OnceWithItsReasonAndCallSite)));

            // Once: the hold lives on and the diagnostic does not repeat.
            _harness.Advance(120f);
            Assert.That(_harness.Diagnostics.Count(LongHoldId), Is.EqualTo(1));
            Assert.That(_harness.ShieldCount, Is.EqualTo(1));
        }

        [Test]
        public void HoldInput_RemovedBefore30Seconds_LogsNothing()
        {
            var hold = _harness.Navigator.HoldInput("tutorial", _harness.Owner);

            _harness.Advance(20f);
            hold.Remove();
            _harness.Advance(120f);

            Assert.That(_harness.Diagnostics.Count(LongHoldId), Is.Zero);
        }

        [Test]
        public void OperationHold_LongerThan30Seconds_DoesNotLogTheInputHoldDiagnostic()
        {
            _harness.Dispose();
            _harness = new NavigatorHarness(loadTimeoutSeconds: 200f);
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");

            _harness.Advance(40f);

            Assert.That(_harness.ShieldCount, Is.EqualTo(1));
            Assert.That(_harness.Diagnostics.Count(LongHoldId), Is.Zero);
        }

        // Shield order

        [Test]
        public void ShieldOrder_SitsAboveTheHighestLayer()
        {
            _harness.Navigator.HoldInput("probe", _harness.Owner);

            Assert.That(_harness.Stage.ShieldOrder, Is.EqualTo(3000), "base 0 + (highest layer order 2 + 1) x 1000");
        }

        [Test]
        public void ShieldOrder_FollowsAnExtraLayerAndTheSortingBase()
        {
            _harness.Dispose();
            _harness = NavigationTestKit.NewHarnessWithHud(sortingBase: 200);

            _harness.Navigator.HoldInput("probe", _harness.Owner);

            Assert.That(_harness.Stage.ShieldOrder, Is.EqualTo(4200), "base 200 + (highest layer order 3 + 1) x 1000");
        }

        // Load indicator

        [Test]
        public void LoadIndicator_ShowsAfterPointFourSecondsInLoading_NotBefore()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");

            _harness.Advance(0.3f);
            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.False);
            Assert.That(_harness.Stage.CountOf(StageCallKind.SetLoadIndicator), Is.Zero);

            _harness.Advance(0.2f);
            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.True);
        }

        [Test]
        public void LoadIndicator_HidesWhenTheLoadEnds()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");
            _harness.Advance(1f);
            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.True);

            _harness.Stage.CompleteAcquire();

            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.False);
        }

        [Test]
        public void LoadIndicator_ALoadThatEndsBeforeTheDelay_NeverShowsIt()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");
            _harness.Advance(0.2f);

            _harness.Stage.CompleteAcquire();
            _harness.Advance(2f);

            Assert.That(_harness.Stage.CountOf(StageCallKind.SetLoadIndicator), Is.Zero);
            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.False);
        }

        [Test]
        public void LoadIndicator_ALoadOnALayerWithoutShieldsInput_NeverShowsIt()
        {
            _harness.Dispose();
            _harness = NavigationTestKit.NewHarnessWithHud();
            _harness.Row("overlay", NavigationTestKit.Hud);
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("overlay");

            _harness.Advance(2f);

            Assert.That(_harness.Stage.CountOf(StageCallKind.SetLoadIndicator), Is.Zero);
            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.False);
        }

        [Test]
        public void LoadIndicator_StaysWhileAnyShieldHoldingRequestIsLoading()
        {
            _harness.Row("a").Row("p", ViewLayers.Popups);
            _harness.Stage.AcquireMode = StageMode.Manual;
            Open("a");
            Open("p");
            _harness.Advance(0.5f);
            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.True);

            // The oldest pending load is the screen's.
            _harness.Stage.CompleteAcquire();

            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.True, "The popup is still loading.");

            _harness.Stage.CompleteAcquire();

            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.False);
        }

        [Test]
        public void LoadIndicator_WithdrawingTheLoadingRequest_HidesIt()
        {
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var handle = Open("a");
            _harness.Advance(1f);
            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.True);

            handle.Close();

            Assert.That(_harness.Stage.LoadIndicatorVisible, Is.False);
        }
    }
}
