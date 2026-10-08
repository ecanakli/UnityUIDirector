using System;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Back(): the five rules below, checked in order, and the backdrop tap.
    public sealed class NavigationBackTests
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

        private bool Back()
        {
            return _harness.Navigator.Back();
        }

        // Opens "home" and then a "target" above it, on the popup layer or as a second screen.
        private ViewHandle OpenTarget(bool onPopupLayer, BackPolicy policy, Func<object> view = null)
        {
            var layer = onPopupLayer ? ViewLayers.Popups : ViewLayers.Screens;
            _harness.Row("home").Row("target", layer, back: policy);
            if (view != null)
                _harness.Map("target", view);

            Open("home");
            return Open("target");
        }

        // Row 1: root gone

        [Test]
        public void Row1_RootGone_ReturnsFalse()
        {
            _harness.Row("a").Row("b");
            Open("a");
            Open("b");

            _harness.Navigator.Teardown();

            Assert.That(Back(), Is.False);
        }

        // Row 2: a request in Waiting or Loading on a TakesBack layer is withdrawn

        [Test]
        public void Row2_ScreenStillLoading_IsWithdrawnAsCancelled_AndBackReturnsTrue()
        {
            _harness.Row("home").Row("next");
            Open("home");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var next = Open("next");
            Assert.That(next.Phase, Is.EqualTo(ViewPhase.Loading));

            var handled = Back();

            Assert.That(handled, Is.True);
            Assert.That(next.IsAlive, Is.False);
            var notShown = NavigationTestKit.NotShownOf(_harness.Observer, "next");
            Assert.That(notShown.Reason, Is.EqualTo(CloseReason.NotShown));
            Assert.That(notShown.Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(_harness.Navigator.IsOpen("home"), Is.True);
            Assert.That(_harness.ShieldCount, Is.Zero);

            // The load answers late and the stage releases that instance itself.
            _harness.Stage.CompleteAcquire();
            Assert.That(_harness.Stage.LastInstanceOf("next").ReleasedLate, Is.True);
        }

        [Test]
        public void Row2_PopupStillLoading_IsWithdrawnAsCancelled_AndTheScreenStaysOpen()
        {
            _harness.Row("home").Row("p", ViewLayers.Popups);
            Open("home");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var popup = Open("p");

            var handled = Back();

            Assert.That(handled, Is.True);
            Assert.That(popup.IsAlive, Is.False);
            Assert.That(NavigationTestKit.NotShownOf(_harness.Observer, "p").Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(_harness.Navigator.IsOpen("home"), Is.True);
        }

        [Test]
        public void Row2_RequestWaitingBehindALoadingOne_IsTheNewestAndIsWithdrawnFirst()
        {
            _harness.Row("home").Row("b").Row("c");
            Open("home");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var b = Open("b");
            var c = Open("c");
            Assert.That(b.Phase, Is.EqualTo(ViewPhase.Loading));
            Assert.That(c.Phase, Is.EqualTo(ViewPhase.Waiting));

            Assert.That(Back(), Is.True);

            Assert.That(c.IsAlive, Is.False);
            Assert.That(b.Phase, Is.EqualTo(ViewPhase.Loading));
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2));

            Assert.That(Back(), Is.True);

            Assert.That(b.IsAlive, Is.False);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1));
            Assert.That(_harness.Navigator.IsOpen("home"), Is.True);
        }

        [Test]
        public void Row2_TwoPendingRequestsOnDifferentLayers_TheNewestOneIsWithdrawn()
        {
            _harness.Row("s").Row("p", ViewLayers.Popups);
            _harness.Stage.AcquireMode = StageMode.Manual;
            var screen = Open("s");
            var popup = Open("p");

            Assert.That(Back(), Is.True);
            Assert.That(popup.IsAlive, Is.False);
            Assert.That(screen.Phase, Is.EqualTo(ViewPhase.Loading));

            Assert.That(Back(), Is.True);
            Assert.That(screen.IsAlive, Is.False);

            // Nothing is pending or open any more.
            Assert.That(Back(), Is.False);
        }

        [Test]
        public void Row2_PendingRequestWithIgnorePolicy_IsNotWithdrawn_ButBackIsStillSwallowed()
        {
            _harness.Row("home").Row("b", back: BackPolicy.Ignore);
            Open("home");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var b = Open("b");

            var handled = Back();

            Assert.That(handled, Is.True, "The shield is up, so rule 3 swallows it.");
            Assert.That(b.Phase, Is.EqualTo(ViewPhase.Loading));
            Assert.That(_harness.Navigator.IsOpen("home"), Is.True);
        }

        [Test]
        public void Row2_PendingRequestOnALayerWithoutTakesBack_IsNotWithdrawn()
        {
            _harness.Row("home").Row("sys", ViewLayers.System);
            Open("home");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var system = Open("sys");

            var handled = Back();

            Assert.That(handled, Is.True, "The shield is up, so rule 3 swallows it.");
            Assert.That(system.Phase, Is.EqualTo(ViewPhase.Loading));
        }

        // Row 3: shield above zero

        [Test]
        public void Row3_WhileAnInputHoldKeepsTheShieldUp_BackIsSwallowed()
        {
            _harness.Row("a").Row("b");
            Open("a");
            Open("b");
            var hold = _harness.Navigator.HoldInput("tutorial", _harness.Owner);

            Assert.That(Back(), Is.True);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2));
            Assert.That(_harness.Navigator.IsOpen("b"), Is.True);

            hold.Remove();

            Assert.That(Back(), Is.True);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1));
        }

        [Test]
        public void Row3_OnTheRootScreenWhileTheShieldIsUp_BackReturnsTrueNotFalse()
        {
            _harness.Row("root");
            Open("root");
            var hold = _harness.Navigator.HoldInput("tutorial", _harness.Owner);

            Assert.That(Back(), Is.True);

            hold.Remove();

            Assert.That(Back(), Is.False);
        }

        // Row 4: the first layer from the top with an open view that is Modal or TakesBack

        [TestCase(BackPolicy.LayerDefault)]
        [TestCase(BackPolicy.Close)]
        public void Row4_TopScreenWithDefaultOrClosePolicy_IsClosedWithReasonBack(BackPolicy policy)
        {
            var target = OpenTarget(false, policy);

            var handled = Back();

            Assert.That(handled, Is.True);
            Assert.That(target.IsAlive, Is.False);
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "target").Reason, Is.EqualTo(CloseReason.Back));
            Assert.That(_harness.Navigator.IsOpen("home"), Is.True);
        }

        [TestCase(BackPolicy.LayerDefault)]
        [TestCase(BackPolicy.Close)]
        public void Row4_RootScreen_ReturnsFalseAndStaysOpen(BackPolicy policy)
        {
            _harness.Row("root", back: policy);
            var root = Open("root");

            var handled = Back();

            Assert.That(handled, Is.False);
            Assert.That(root.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Closed), Is.Zero);
        }

        [Test]
        public void Row4_PopupAboveScreens_IsClosedBeforeAnyScreen()
        {
            _harness.Row("home").Row("b").Row("p", ViewLayers.Popups);
            Open("home");
            Open("b");
            Open("p");

            Assert.That(Back(), Is.True);
            Assert.That(_harness.Navigator.IsOpen("p"), Is.False);
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "p").Reason, Is.EqualTo(CloseReason.Back));
            Assert.That(_harness.Navigator.IsOpen("b"), Is.True);

            Assert.That(Back(), Is.True);
            Assert.That(_harness.Navigator.IsOpen("b"), Is.False);

            Assert.That(Back(), Is.False, "Home is the root.");
        }

        [Test]
        public void Row4_TheOnlyPopup_IsClosedEvenThoughItIsTheBottomViewOfItsLayer()
        {
            _harness.Row("p", ViewLayers.Popups);
            var popup = Open("p");

            Assert.That(Back(), Is.True);

            Assert.That(popup.IsAlive, Is.False);
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "p").Reason, Is.EqualTo(CloseReason.Back));
        }

        [TestCase(BackPolicy.LayerDefault)]
        [TestCase(BackPolicy.Close)]
        [TestCase(BackPolicy.Ignore)]
        public void Row4_SystemView_SwallowsBack_AndIsNeverClosedOrAsked(BackPolicy policy)
        {
            _harness.Row("home").Row("sys", ViewLayers.System, back: policy);
            _harness.Map("sys", () => new FakeBackView<NoArgs, NoResult> { BackResult = false });
            Open("home");
            var system = Open("sys");
            var view = _harness.ViewOf<FakeBackView<NoArgs, NoResult>>("sys");

            var handled = Back();

            Assert.That(handled, Is.True);
            Assert.That(system.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(view.BackCount, Is.Zero, "Only HandledByView asks the view.");
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Closed), Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Row4_SystemViewWithHandledByView_AsksTheView_AndSwallowsBothAnswers(bool onBackAnswer)
        {
            _harness.Row("home").Row("sys", ViewLayers.System, back: BackPolicy.HandledByView);
            _harness.Map("sys", () => new FakeBackView<NoArgs, NoResult> { BackResult = onBackAnswer });
            Open("home");
            var system = Open("sys");
            var view = _harness.ViewOf<FakeBackView<NoArgs, NoResult>>("sys");

            var handled = Back();

            Assert.That(handled, Is.True);
            Assert.That(view.BackCount, Is.EqualTo(1));
            Assert.That(system.Phase, Is.EqualTo(ViewPhase.Open), "A System view is never closed by back.");
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Closed), Is.Zero);
        }

        [Test]
        public void Row4_SystemViewOverPopupAndScreen_SwallowsBack_AndLeavesTheOthersOpen()
        {
            _harness.Row("home").Row("p", ViewLayers.Popups).Row("sys", ViewLayers.System);
            Open("home");
            Open("p");
            Open("sys");

            Assert.That(Back(), Is.True);

            Assert.That(_harness.Navigator.IsOpen("sys"), Is.True);
            Assert.That(_harness.Navigator.IsOpen("p"), Is.True);
            Assert.That(_harness.Navigator.IsOpen("home"), Is.True);
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Closed), Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Row4_TargetWithIgnorePolicy_IsSwallowed_AndStaysOpenWithoutBeingAsked(bool onPopupLayer)
        {
            var target = OpenTarget(onPopupLayer, BackPolicy.Ignore, () => new FakeBackView<NoArgs, NoResult> { BackResult = false });
            var view = _harness.ViewOf<FakeBackView<NoArgs, NoResult>>("target");

            var handled = Back();

            Assert.That(handled, Is.True);
            Assert.That(target.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(view.BackCount, Is.Zero);
        }

        [Test]
        public void Row4_IgnorePolicyOnTheRootScreen_ReturnsTrue()
        {
            _harness.Row("root", back: BackPolicy.Ignore);
            var root = Open("root");

            Assert.That(Back(), Is.True);
            Assert.That(root.Phase, Is.EqualTo(ViewPhase.Open));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Row4_HandledByView_WhenOnBackReturnsTrue_BackReturnsTrueAndTheViewStaysOpen(bool onPopupLayer)
        {
            var target = OpenTarget(onPopupLayer, BackPolicy.HandledByView, () => new FakeBackView<NoArgs, NoResult> { BackResult = true });
            var view = _harness.ViewOf<FakeBackView<NoArgs, NoResult>>("target");

            var handled = Back();

            Assert.That(handled, Is.True);
            Assert.That(view.BackCount, Is.EqualTo(1));
            Assert.That(target.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Closed), Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Row4_HandledByView_WhenOnBackReturnsFalse_ContinuesAsClose(bool onPopupLayer)
        {
            var target = OpenTarget(onPopupLayer, BackPolicy.HandledByView, () => new FakeBackView<NoArgs, NoResult> { BackResult = false });
            var view = _harness.ViewOf<FakeBackView<NoArgs, NoResult>>("target");

            var handled = Back();

            Assert.That(handled, Is.True);
            Assert.That(view.BackCount, Is.EqualTo(1));
            Assert.That(target.IsAlive, Is.False);
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "target").Reason, Is.EqualTo(CloseReason.Back));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Row4_HandledByViewOnTheRoot_BackAnswersWhatOnBackAnswers(bool onBackAnswer)
        {
            _harness.Row("root", back: BackPolicy.HandledByView);
            _harness.Map("root", () => new FakeBackView<NoArgs, NoResult> { BackResult = onBackAnswer });
            var root = Open("root");
            var view = _harness.ViewOf<FakeBackView<NoArgs, NoResult>>("root");

            var handled = Back();

            // True ends it; false continues as Close, and Close on the root is false.
            Assert.That(handled, Is.EqualTo(onBackAnswer));
            Assert.That(view.BackCount, Is.EqualTo(1));
            Assert.That(root.Phase, Is.EqualTo(ViewPhase.Open));
        }

        [Test]
        public void Row4_ALayerThatIsNeitherModalNorTakesBack_IsSkipped()
        {
            _harness.Dispose();
            _harness = NavigationTestKit.NewHarnessWithHud();
            _harness.Row("home").Row("b").Row("overlay", NavigationTestKit.Hud);
            Open("home");
            Open("b");
            var overlay = Open("overlay");

            Assert.That(Back(), Is.True);

            Assert.That(_harness.Navigator.IsOpen("b"), Is.False);
            Assert.That(overlay.Phase, Is.EqualTo(ViewPhase.Open));
        }

        [Test]
        public void Row4_WhileTheRootIsStillOpening_BackIsSwallowedNotReportedAsRoot()
        {
            _harness.Row("a");
            _harness.Stage.PlayMode = StageMode.Manual;
            var a = Open("a");
            Assert.That(a.Phase, Is.EqualTo(ViewPhase.Opening));

            Assert.That(Back(), Is.True);
            Assert.That(a.Phase, Is.EqualTo(ViewPhase.Opening));

            _harness.Stage.CompletePlay();

            Assert.That(a.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(Back(), Is.False);
        }

        [Test]
        public void Row4_WhileAScreenIsOpening_BackIsSwallowedAndClosesNothing()
        {
            _harness.Row("a").Row("b");
            Open("a");
            _harness.Stage.PlayMode = StageMode.Manual;
            var b = Open("b");
            Assert.That(b.Phase, Is.EqualTo(ViewPhase.Opening));

            Assert.That(Back(), Is.True);

            Assert.That(b.Phase, Is.EqualTo(ViewPhase.Opening));
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2));
            Assert.That(_harness.Stage.PendingPlayCount, Is.EqualTo(1), "No hide transition was started.");

            _harness.Stage.CompletePlay();

            Assert.That(b.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(Back(), Is.True);
            _harness.Stage.CompletePlay();
            Assert.That(b.IsAlive, Is.False);
            Assert.That(_harness.Navigator.IsOpen("a"), Is.True);
        }

        [Test]
        public void Row4_WhileAScreenIsClosing_BackIsSwallowedAndDoesNotCloseTheOneBelow()
        {
            _harness.Row("a").Row("b");
            Open("a");
            var b = Open("b");
            _harness.Stage.PlayMode = StageMode.Manual;

            Assert.That(Back(), Is.True);
            Assert.That(b.Phase, Is.EqualTo(ViewPhase.Closing));

            Assert.That(Back(), Is.True);

            Assert.That(b.Phase, Is.EqualTo(ViewPhase.Closing));
            Assert.That(_harness.Navigator.IsOpen("a"), Is.True);
            Assert.That(_harness.Stage.PendingPlayCount, Is.EqualTo(1));

            _harness.Stage.CompletePlay();

            Assert.That(b.IsAlive, Is.False);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1));
            Assert.That(_harness.Navigator.IsOpen("a"), Is.True);
            Assert.That(Back(), Is.False);
        }

        // Row 5: nothing matched

        [Test]
        public void Row5_NothingOpen_ReturnsFalse()
        {
            Assert.That(Back(), Is.False);
        }

        [Test]
        public void Row5_OnlyAViewOnAPlainLayerIsOpen_ReturnsFalse()
        {
            _harness.Dispose();
            _harness = NavigationTestKit.NewHarnessWithHud();
            _harness.Row("overlay", NavigationTestKit.Hud);
            var overlay = Open("overlay");

            Assert.That(Back(), Is.False);
            Assert.That(overlay.Phase, Is.EqualTo(ViewPhase.Open));
        }

        // Backdrop tap

        [Test]
        public void BackdropTap_WithCloseOnBackdrop_ClosesTheTopViewWithReasonBackdrop()
        {
            _harness.Row("home").Row("p", ViewLayers.Popups, closeOnBackdrop: true);
            Open("home");
            var show = _harness.Navigator.ShowAsync(ViewKey.Create("p"), _harness.Owner);

            _harness.Stage.RaiseBackdropTapped(ViewLayers.Popups);

            var result = show.ResultNow();
            Assert.That(result.WasShown, Is.True);
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Backdrop));
            Assert.That(result.HasValue, Is.False);
            Assert.That(_harness.RequestCount(ViewLayers.Popups), Is.Zero);
            Assert.That(_harness.Stage.IsBackdropActive(ViewLayers.Popups), Is.False);
            Assert.That(_harness.Navigator.IsOpen("home"), Is.True);
        }

        [Test]
        public void BackdropTap_WithoutCloseOnBackdrop_IsSwallowed()
        {
            _harness.Row("home").Row("p", ViewLayers.Popups);
            Open("home");
            var popup = Open("p");

            _harness.Stage.RaiseBackdropTapped(ViewLayers.Popups);

            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Closed), Is.Zero);
            Assert.That(_harness.Stage.IsBackdropActive(ViewLayers.Popups), Is.True);
            Assert.That(_harness.ShieldCount, Is.Zero);
        }

        [Test]
        public void BackdropTap_ClosesOnlyTheTopViewOfTheLayer()
        {
            _harness.Row("p1", ViewLayers.Popups, closeOnBackdrop: true).Row("p2", ViewLayers.Popups, closeOnBackdrop: true);
            var first = Open("p1");
            var second = Open("p2");

            _harness.Stage.RaiseBackdropTapped(ViewLayers.Popups);

            Assert.That(second.IsAlive, Is.False);
            Assert.That(NavigationTestKit.ClosedOf(_harness.Observer, "p2").Reason, Is.EqualTo(CloseReason.Backdrop));
            Assert.That(first.Phase, Is.EqualTo(ViewPhase.Open));
        }

        [Test]
        public void BackdropTap_ReadsTheRowOfTheTopView_NotTheRowOfTheOnesBelow()
        {
            _harness.Row("p1", ViewLayers.Popups, closeOnBackdrop: true).Row("p2", ViewLayers.Popups);
            var first = Open("p1");
            var second = Open("p2");

            _harness.Stage.RaiseBackdropTapped(ViewLayers.Popups);

            Assert.That(second.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(first.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Closed), Is.Zero);
        }
    }
}
