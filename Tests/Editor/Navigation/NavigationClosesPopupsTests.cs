using System;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // A screen change closes the views of ClosedByNavigation layers.
    public sealed class NavigationClosesPopupsTests
    {
        // Ways a screen open can fail before its OnOpen returns or in it.
        public enum OpenFailure
        {
            OnOpenThrows,
            OnOpenDeclines,
            LoadFails,
            TypeMismatch
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

        [Test]
        public void ScreenOpen_ClosesOpenPopupsBySnap_WithReasonNavigated()
        {
            _harness.Row("home").Row("next").Row("p1", ViewLayers.Popups).Row("p2", ViewLayers.Popups);
            var navigator = _harness.Navigator;
            Open("home");
            var showP1 = navigator.ShowAsync(ViewKey.Create("p1"), _harness.Owner);
            var showP2 = navigator.ShowAsync(ViewKey.Create("p2"), _harness.Owner);
            Assert.That(_harness.Stage.IsBackdropActive(ViewLayers.Popups), Is.True);
            _harness.Stage.ClearCalls();

            Open("next");

            var calls = _harness.Stage.Calls;
            foreach (var id in new[] { "p1", "p2" })
            {
                // Closed by snap: no hide transition, then the instance is released.
                Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Snap, id), Is.EqualTo(1), id);
                Assert.That(NavigationTestKit.IndexOfCall(calls, StageCallKind.Snap, id, false), Is.GreaterThanOrEqualTo(0), id);
                Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Play, id), Is.Zero, id);
                Assert.That(_harness.Stage.LastInstanceOf(id).ReleaseCount, Is.EqualTo(1), id);

                var closed = NavigationTestKit.ClosedOf(_harness.Observer, id);
                Assert.That(closed.Reason, Is.EqualTo(CloseReason.Navigated), id);
            }

            foreach (var task in new[] { showP1, showP2 })
            {
                var result = task.ResultNow();
                Assert.That(result.WasShown, Is.True);
                Assert.That(result.Reason, Is.EqualTo(CloseReason.Navigated));
            }

            Assert.That(_harness.RequestCount(ViewLayers.Popups), Is.Zero);
            Assert.That(_harness.Stage.IsBackdropActive(ViewLayers.Popups), Is.False);
            Assert.That(navigator.IsOpen("next"), Is.True);
            Assert.That(_harness.ShieldCount, Is.Zero);
        }

        [Test]
        public void ScreenOpen_ClosesPopupsAfterOnOpen_AndBeforeTheShowTransition()
        {
            var popupOpenInsideOnOpen = false;
            _harness.Row("home").Row("next").Row("p1", ViewLayers.Popups);
            _harness.Map("next", () => new FakeView<NoArgs, NoResult>
            {
                RunOnOpen = context => popupOpenInsideOnOpen = context.Navigator.IsOpen("p1")
            });
            Open("home");
            Open("p1");
            _harness.Stage.PlayMode = StageMode.Manual;

            Open("next");

            Assert.That(popupOpenInsideOnOpen, Is.True, "The popups close after OnOpen returned, not before.");

            // The show transition of the new screen is still running.
            Assert.That(_harness.Stage.PendingPlayCount, Is.EqualTo(1));
            Assert.That(_harness.Navigator.IsOpen("p1"), Is.False);
            Assert.That(NavigationTestKit.CountCalls(_harness.Stage.Calls, StageCallKind.Play, "p1"), Is.EqualTo(1), "Only the show transition of the popup itself played.");
        }

        [Test]
        public void ScreenOpen_WithdrawsPopupsThatAreStillLoadingOrWaiting()
        {
            _harness.Row("home").Row("next").Row("p1", ViewLayers.Popups).Row("p2", ViewLayers.Popups);
            var navigator = _harness.Navigator;
            Open("home");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var showP1 = navigator.ShowAsync(ViewKey.Create("p1"), _harness.Owner);
            var showP2 = navigator.ShowAsync(ViewKey.Create("p2"), _harness.Owner);
            Assert.That(_harness.Stage.PendingAcquireCount, Is.EqualTo(1), "p1 loads, p2 waits behind it in the same layer.");
            _harness.Stage.AcquireMode = StageMode.Auto;

            Open("next");

            foreach (var task in new[] { showP1, showP2 })
            {
                var result = task.ResultNow();
                Assert.That(result.WasShown, Is.False);
                Assert.That(result.Reason, Is.EqualTo(CloseReason.NotShown));
                Assert.That(result.Rejected, Is.EqualTo(RejectReason.Cancelled));
            }

            Assert.That(NavigationTestKit.NotShownOf(_harness.Observer, "p1").Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(NavigationTestKit.NotShownOf(_harness.Observer, "p2").Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(_harness.RequestCount(ViewLayers.Popups), Is.Zero);
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(NavigationTestKit.CountCalls(_harness.Stage.Calls, StageCallKind.Acquire, "p2"), Is.Zero, "A withdrawn waiting request never loads.");

            // The load of p1 answers late and the stage releases that instance itself.
            _harness.Stage.CompleteAcquire();
            var late = _harness.Stage.LastInstanceOf("p1");
            Assert.That(late.ReleasedLate, Is.True);
            Assert.That(late.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void ScreenOpen_LeavesSystemViewsUntouched()
        {
            _harness.Row("home").Row("next").Row("sys", ViewLayers.System);
            var navigator = _harness.Navigator;
            Open("home");
            var system = Open("sys");
            _harness.Stage.ClearCalls();

            Open("next");

            var calls = _harness.Stage.Calls;
            Assert.That(system.IsAlive, Is.True);
            Assert.That(navigator.IsOpen("sys"), Is.True);
            Assert.That(_harness.RequestCount(ViewLayers.System), Is.EqualTo(1));
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Snap, "sys"), Is.Zero);
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Release, "sys"), Is.Zero);
            Assert.That(NavigationTestKit.HasEvent(_harness.Observer, ViewEventKind.Closed, "sys"), Is.False);
            Assert.That(NavigationTestKit.VisualOf(_harness, "sys").Hidden, Is.False);
        }

        [TestCase(OpenFailure.OnOpenThrows)]
        [TestCase(OpenFailure.OnOpenDeclines)]
        [TestCase(OpenFailure.LoadFails)]
        [TestCase(OpenFailure.TypeMismatch)]
        public void FailedScreenOpen_LeavesPopupsAlone(OpenFailure failure)
        {
            _harness.Row("home").Row("bad").Row("p1", ViewLayers.Popups);
            Open("home");
            var popup = Open("p1");
            ArmFailure(failure);
            _harness.Stage.ClearCalls();

            var bad = Open("bad");

            var calls = _harness.Stage.Calls;
            Assert.That(bad.IsAlive, Is.False, "The failed screen did not stay open.");
            Assert.That(NavigationTestKit.HasEvent(_harness.Observer, ViewEventKind.NotShown, "bad"), Is.True);
            Assert.That(popup.IsAlive, Is.True);
            Assert.That(_harness.Navigator.IsOpen("p1"), Is.True);
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Snap, "p1"), Is.Zero);
            Assert.That(NavigationTestKit.CountCalls(calls, StageCallKind.Release, "p1"), Is.Zero);
            Assert.That(NavigationTestKit.HasEvent(_harness.Observer, ViewEventKind.Closed, "p1"), Is.False);
            Assert.That(_harness.Stage.IsBackdropActive(ViewLayers.Popups), Is.True);
            Assert.That(_harness.Navigator.IsOpen("home"), Is.True);
        }

        private void ArmFailure(OpenFailure failure)
        {
            switch (failure)
            {
                case OpenFailure.OnOpenThrows:
                    _harness.Map("bad", () => new FakeView<NoArgs, NoResult> { ThrowOnOpen = new InvalidOperationException("OnOpen failed.") });
                    break;

                case OpenFailure.OnOpenDeclines:
                    _harness.Map("bad", () => new FakeView<NoArgs, NoResult> { RunOnOpen = context => context.Decline("Not now.") });
                    break;

                case OpenFailure.LoadFails:
                    _harness.Stage.FailNextAcquire();
                    break;

                case OpenFailure.TypeMismatch:
                    _harness.Stage.NextAcquireReturnsNoView();
                    break;
            }
        }
    }
}
