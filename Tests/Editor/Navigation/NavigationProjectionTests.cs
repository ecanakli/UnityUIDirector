using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Derived state: hidden, interactive, sorting order and backdrop as the stage received them.
    public sealed class NavigationProjectionTests
    {
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

        private void AssertBackdrop(bool active, int order)
        {
            Assert.That(_harness.Stage.IsBackdropActive(ViewLayers.Popups), Is.EqualTo(active), "Popup backdrop active.");
            if (active)
                Assert.That(_harness.Stage.BackdropOrder(ViewLayers.Popups), Is.EqualTo(order), "Popup backdrop order.");
        }

        // Screens

        [Test]
        public void SingleScreen_IsVisibleAndInteractive_AtTheOrderOfSlotOne()
        {
            _harness.Row("a");

            Open("a");

            var visual = VisualOf("a");
            Assert.That(visual.Hidden, Is.False);
            Assert.That(visual.Interactive, Is.True);
            Assert.That(visual.SortingOrder, Is.EqualTo(10), "base 0 + layer 0 x 1000 + slot 1 x 10");
        }

        [Test]
        public void StackedScreens_TheOneBelowIsHiddenOnlyOnceTheTopIsFullyOpen()
        {
            _harness.Row("a").Row("b");
            Open("a");
            _harness.Stage.PlayMode = StageMode.Manual;
            Open("b");

            // Opening: nothing is covered yet, and the new view takes no input.
            Assert.That(VisualOf("a").Hidden, Is.False);
            Assert.That(VisualOf("b").Hidden, Is.False);
            Assert.That(VisualOf("b").Interactive, Is.False);

            _harness.Stage.CompletePlay();

            Assert.That(VisualOf("a").Hidden, Is.True);
            Assert.That(VisualOf("a").Interactive, Is.False);
            Assert.That(VisualOf("b").Hidden, Is.False);
            Assert.That(VisualOf("b").Interactive, Is.True);
            Assert.That(VisualOf("a").SortingOrder, Is.LessThan(VisualOf("b").SortingOrder));
        }

        [Test]
        public void ScreenWithTransparentCover_DoesNotHideTheScreenBelow()
        {
            _harness.Row("a").Row("b", cover: CoverPolicy.Transparent);
            Open("a");

            Open("b");

            Assert.That(VisualOf("a").Hidden, Is.False);
            Assert.That(VisualOf("b").Hidden, Is.False);
            Assert.That(VisualOf("b").Interactive, Is.True);
        }

        [Test]
        public void CoveredScreen_ReportsTheCoveredModeOfItsRow()
        {
            _harness.Row("a", whenCovered: CoveredMode.Deactivate).Row("b");
            Open("a");

            Open("b");

            Assert.That(VisualOf("a").Hidden, Is.True);
            Assert.That(VisualOf("a").CoveredMode, Is.EqualTo(CoveredMode.Deactivate));
        }

        [Test]
        public void Close_RevealsTheCoveredScreenBeforeTheHideTransitionPlays()
        {
            _harness.Row("a").Row("b");
            Open("a");
            Open("b");
            _harness.Stage.PlayMode = StageMode.Manual;
            _harness.Stage.ClearCalls();

            Assert.That(_harness.Navigator.Back(), Is.True);

            var calls = _harness.Stage.Calls;
            var reveal = NavigationTestKit.IndexOfApply(calls, "a", false);
            var hide = NavigationTestKit.IndexOfCall(calls, StageCallKind.Play, "b", false);
            Assert.That(reveal, Is.GreaterThanOrEqualTo(0), "The screen below is revealed.\n" + _harness.Stage.DescribeCalls());
            Assert.That(hide, Is.GreaterThanOrEqualTo(0), "The top screen plays its hide transition.\n" + _harness.Stage.DescribeCalls());
            Assert.That(reveal, Is.LessThan(hide), _harness.Stage.DescribeCalls());
            Assert.That(VisualOf("a").Hidden, Is.False);
        }

        [Test]
        public void VisibleScope_EndsWhenTheViewIsCovered_AndANewOneBeginsWhenItIsRevealed()
        {
            _harness.Row("a").Row("b");
            _harness.Map("a", () => new FakeVisibleView<NoArgs, NoResult>());
            Open("a");
            var viewA = _harness.ViewOf<FakeVisibleView<NoArgs, NoResult>>("a");
            Assert.That(viewA.VisibleCount, Is.EqualTo(1));
            Assert.That(viewA.VisibleScopes[0].IsEnded, Is.False);

            Open("b");

            Assert.That(viewA.VisibleScopes[0].IsEnded, Is.True, "Covering ends the visible scope.");
            Assert.That(viewA.VisibleCount, Is.EqualTo(1));

            Assert.That(_harness.Navigator.Back(), Is.True);

            Assert.That(viewA.VisibleCount, Is.EqualTo(2), "Revealing begins a new visible scope.");
            Assert.That(viewA.VisibleScopes[1].IsEnded, Is.False);
            Assert.That(viewA.VisibleScopes[0].IsEnded, Is.True);
            Assert.That(viewA.OpenCount, Is.EqualTo(1));
        }

        // Popups

        [Test]
        public void StackedPopups_OnlyTheTopIsInteractive_OrdersRise_AndTheBackdropSitsFiveBelowTheTop()
        {
            _harness.Row("home").Row("p1", ViewLayers.Popups).Row("p2", ViewLayers.Popups).Row("p3", ViewLayers.Popups);
            Open("home");

            var first = Open("p1");
            Assert.That(VisualOf("p1").SortingOrder, Is.EqualTo(1010), "base 0 + layer 1 x 1000 + slot 1 x 10");
            Assert.That(VisualOf("p1").Interactive, Is.True);
            Assert.That(VisualOf("home").Interactive, Is.False, "A modal view is above the screen.");
            Assert.That(VisualOf("home").Hidden, Is.False, "Popups do not cover by default.");
            AssertBackdrop(true, 1005);

            var second = Open("p2");
            Assert.That(VisualOf("p2").SortingOrder, Is.EqualTo(1020));
            Assert.That(VisualOf("p2").Interactive, Is.True);
            Assert.That(VisualOf("p1").Interactive, Is.False);
            Assert.That(VisualOf("p1").Hidden, Is.False);
            AssertBackdrop(true, 1015);

            var third = Open("p3");
            Assert.That(VisualOf("p3").SortingOrder, Is.EqualTo(1030));
            Assert.That(VisualOf("p3").Interactive, Is.True);
            Assert.That(VisualOf("p2").Interactive, Is.False);
            AssertBackdrop(true, 1025);

            third.Close();
            Assert.That(VisualOf("p2").Interactive, Is.True);
            AssertBackdrop(true, 1015);

            second.Close();
            Assert.That(VisualOf("p1").Interactive, Is.True);
            AssertBackdrop(true, 1005);

            first.Close();
            AssertBackdrop(false, 0);
            Assert.That(VisualOf("home").Interactive, Is.True);
        }

        [Test]
        public void CoveringPopup_HidesTheScreenBelow_AndItsBackdropStillSitsUnderIt()
        {
            _harness.Row("home").Row("p", ViewLayers.Popups, cover: CoverPolicy.Covers);
            Open("home");

            var popup = Open("p");

            Assert.That(VisualOf("home").Hidden, Is.True);
            Assert.That(VisualOf("home").Interactive, Is.False);
            Assert.That(VisualOf("p").Hidden, Is.False);
            Assert.That(VisualOf("p").Interactive, Is.True);
            AssertBackdrop(true, 1005);

            popup.Close();

            Assert.That(VisualOf("home").Hidden, Is.False);
            Assert.That(VisualOf("home").Interactive, Is.True);
            AssertBackdrop(false, 0);
        }

        [Test]
        public void CoveringPopupOverAPopup_HidesIt_AndTheBackdropSitsUnderTheTopOnly()
        {
            _harness.Row("p1", ViewLayers.Popups).Row("p2", ViewLayers.Popups, cover: CoverPolicy.Covers);
            Open("p1");

            var second = Open("p2");

            Assert.That(VisualOf("p1").Hidden, Is.True);
            Assert.That(VisualOf("p1").Interactive, Is.False);
            Assert.That(VisualOf("p2").Hidden, Is.False);
            AssertBackdrop(true, 1015);

            second.Close();

            Assert.That(VisualOf("p1").Hidden, Is.False);
            Assert.That(VisualOf("p1").Interactive, Is.True);
            AssertBackdrop(true, 1005);
        }

        [Test]
        public void Backdrop_AppearsWhenThePopupStartsOpening_AndTheViewTakesInputOnlyOnceOpen()
        {
            _harness.Row("home").Row("p", ViewLayers.Popups);
            Open("home");
            _harness.Stage.PlayMode = StageMode.Manual;

            Open("p");

            AssertBackdrop(true, 1005);
            Assert.That(VisualOf("p").Interactive, Is.False);

            _harness.Stage.CompletePlay();

            AssertBackdrop(true, 1005);
            Assert.That(VisualOf("p").Interactive, Is.True);
        }

        [Test]
        public void Backdrop_StaysUnderAClosingPopup_UntilTheViewIsGone()
        {
            _harness.Row("p", ViewLayers.Popups);
            var popup = Open("p");
            _harness.Stage.PlayMode = StageMode.Manual;

            popup.Close();

            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Closing));
            AssertBackdrop(true, 1005);

            _harness.Stage.CompletePlay();

            Assert.That(popup.IsAlive, Is.False);
            AssertBackdrop(false, 0);
        }

        // System

        [Test]
        public void SystemView_SitsOverEverything_AndBlocksInteractionBelow()
        {
            _harness.Row("home").Row("p", ViewLayers.Popups).Row("sys", ViewLayers.System);
            Open("home");
            Open("p");

            var system = Open("sys");

            Assert.That(VisualOf("sys").SortingOrder, Is.EqualTo(2010), "base 0 + layer 2 x 1000 + slot 1 x 10");
            Assert.That(VisualOf("sys").SortingOrder, Is.GreaterThan(VisualOf("p").SortingOrder));
            Assert.That(VisualOf("p").SortingOrder, Is.GreaterThan(VisualOf("home").SortingOrder));
            Assert.That(VisualOf("sys").Hidden, Is.False);
            Assert.That(VisualOf("sys").Interactive, Is.True);
            Assert.That(VisualOf("p").Hidden, Is.False);
            Assert.That(VisualOf("p").Interactive, Is.False);
            Assert.That(VisualOf("home").Hidden, Is.False);
            Assert.That(VisualOf("home").Interactive, Is.False);
            Assert.That(_harness.Stage.IsBackdropActive(ViewLayers.System), Is.True);
            Assert.That(_harness.Stage.BackdropOrder(ViewLayers.System), Is.EqualTo(2005));
            AssertBackdrop(true, 1005);

            system.Close();

            Assert.That(VisualOf("p").Interactive, Is.True);
            Assert.That(VisualOf("home").Interactive, Is.False, "The popup is still modal over the screen.");
            Assert.That(_harness.Stage.IsBackdropActive(ViewLayers.System), Is.False);
            AssertBackdrop(true, 1005);
        }

        // Slots

        [Test]
        public void SlotsAfterAnOutOfOrderClose_KeepTheOrdersOfOpenViews_AndNewViewsTakeTheHighestLivePlusOne()
        {
            _harness.Row("p1", ViewLayers.Popups).Row("p2", ViewLayers.Popups).Row("p3", ViewLayers.Popups);
            _harness.Row("p4", ViewLayers.Popups).Row("p5", ViewLayers.Popups);
            Open("p1");
            var second = Open("p2");
            var third = Open("p3");
            Assert.That(VisualOf("p3").SortingOrder, Is.EqualTo(1030));

            second.Close();

            Assert.That(VisualOf("p1").SortingOrder, Is.EqualTo(1010));
            Assert.That(VisualOf("p3").SortingOrder, Is.EqualTo(1030));
            AssertBackdrop(true, 1025);

            var fourth = Open("p4");
            Assert.That(VisualOf("p4").SortingOrder, Is.EqualTo(1040), "Highest live slot 3 plus one.");
            AssertBackdrop(true, 1035);

            fourth.Close();
            third.Close();

            // Only the first popup is live now, so the next one takes slot 2 again.
            Open("p5");
            Assert.That(VisualOf("p5").SortingOrder, Is.EqualTo(1020));
            Assert.That(VisualOf("p1").SortingOrder, Is.LessThan(VisualOf("p5").SortingOrder));
            AssertBackdrop(true, 1015);

            // An open view never changed its order while it was open.
            foreach (var call in _harness.Stage.CallsOf(StageCallKind.Apply))
            {
                if (call.ViewId == "p1")
                    Assert.That(call.Visual.SortingOrder, Is.EqualTo(1010), call.ToString());
                else if (call.ViewId == "p3")
                    Assert.That(call.Visual.SortingOrder, Is.EqualTo(1030), call.ToString());
            }
        }

        [Test]
        public void SortingBase_OffsetsEveryViewAndTheBackdrop()
        {
            _harness.Dispose();
            _harness = new NavigatorHarness(sortingBase: 200);
            _harness.Row("home").Row("p", ViewLayers.Popups);

            Open("home");
            Open("p");

            Assert.That(VisualOf("home").SortingOrder, Is.EqualTo(210));
            Assert.That(VisualOf("p").SortingOrder, Is.EqualTo(1210));
            AssertBackdrop(true, 1205);
        }

        // Interactive and close pending

        [Test]
        public void ViewWithACloseRequested_IsNotInteractiveWhileItsHideTransitionRuns()
        {
            _harness.Row("p", ViewLayers.Popups);
            var popup = Open("p");
            var view = _harness.ViewOf<FakeView<NoArgs, NoResult>>("p");
            Assert.That(VisualOf("p").Interactive, Is.True);
            _harness.Stage.PlayMode = StageMode.Manual;

            view.Close();

            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Closing));
            Assert.That(VisualOf("p").Interactive, Is.False);
            Assert.That(VisualOf("p").Hidden, Is.False, "The view stays visible for its hide transition.");
        }

        [Test]
        public void CloseLatchedWhileOpening_ViewNeverBecomesInteractive_AndTheCloseRunsAfterTheOpen()
        {
            _harness.Row("p", ViewLayers.Popups);
            _harness.Stage.PlayMode = StageMode.Manual;
            var popup = Open("p");
            var view = _harness.ViewOf<FakeView<NoArgs, NoResult>>("p");
            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Opening));

            view.Close();

            // Never interrupted: the close waits for the show transition.
            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Opening));
            Assert.That(_harness.Stage.PendingPlayCount, Is.EqualTo(1));

            _harness.Stage.CompletePlay();

            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Closing));
            Assert.That(_harness.Stage.PendingPlayCount, Is.EqualTo(1));
            Assert.That(NavigationTestKit.HasEvent(_harness.Observer, ViewEventKind.Shown, "p"), Is.True);
            foreach (var call in _harness.Stage.CallsOf(StageCallKind.Apply))
                Assert.That(call.Visual.Interactive, Is.False, "The view was interactive in: " + call);

            _harness.Stage.CompletePlay();

            Assert.That(popup.IsAlive, Is.False);
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "p").Reason, Is.EqualTo(CloseReason.Dismissed));
        }

        [Test]
        public void Finalize_DisablesTheViewBeforeItIsReleased()
        {
            _harness.Row("p", ViewLayers.Popups);
            var popup = Open("p");
            var instance = _harness.Stage.LastInstanceOf("p");
            var orderBefore = VisualOf("p").SortingOrder;
            _harness.Stage.ClearCalls();

            popup.Close();

            var calls = _harness.Stage.Calls;
            var disable = NavigationTestKit.IndexOfApply(calls, "p", true);
            var release = NavigationTestKit.IndexOfCall(calls, StageCallKind.Release, "p");
            Assert.That(disable, Is.GreaterThanOrEqualTo(0), _harness.Stage.DescribeCalls());
            Assert.That(release, Is.GreaterThanOrEqualTo(0), _harness.Stage.DescribeCalls());
            Assert.That(disable, Is.LessThan(release), _harness.Stage.DescribeCalls());
            Assert.That(instance.Visual.Hidden, Is.True);
            Assert.That(instance.Visual.Interactive, Is.False);
            Assert.That(instance.Visual.SortingOrder, Is.EqualTo(orderBefore));
        }
    }
}
