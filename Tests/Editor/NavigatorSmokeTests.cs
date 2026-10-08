using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // A few end-to-end cases that prove the test utilities drive the navigator.
    public sealed class NavigatorSmokeTests
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

        [Test]
        public void Popup_WithTypedArgs_CompletesWithValue()
        {
            var key = ViewKey.Create<string, bool>("confirm");
            _harness.Row("confirm", ViewLayers.Popups).Map<string, bool>("confirm");

            var task = _harness.Navigator.ShowAsync(key, "Delete?", _harness.Owner);

            // The view is open and waits for its own Complete.
            Assert.That(task.IsPending(), Is.True);
            var view = _harness.ViewOf<FakeView<string, bool>>("confirm");
            Assert.That(view.LastArgs, Is.EqualTo("Delete?"));

            view.Complete(true);

            var result = task.ResultNow();
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(result.HasValue, Is.True);
            Assert.That(result.Value, Is.True);

            var kinds = _harness.Observer.KindsFor(result.RequestId);
            Assert.That(kinds, Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }));

            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Stage.ShieldBlocking, Is.False);
            Assert.That(_harness.Stage.Instances.Count, Is.EqualTo(1));
            _harness.Stage.AssertAllReleased();
        }

        [Test]
        public void Back_PopsScreensDownToTheRoot_ThenReturnsFalse()
        {
            _harness.Row("a").Row("b").Row("c");
            var navigator = _harness.Navigator;

            navigator.Open(ViewKey.Create("a"), _harness.Owner);
            navigator.Open(ViewKey.Create("b"), _harness.Owner);
            navigator.Open(ViewKey.Create("c"), _harness.Owner);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(3));

            Assert.That(navigator.Back(), Is.True);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2));
            Assert.That(navigator.IsOpen("c"), Is.False);

            Assert.That(navigator.Back(), Is.True);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1));
            Assert.That(navigator.IsOpen("b"), Is.False);

            // The bottom screen is the root: nothing handles back.
            Assert.That(navigator.Back(), Is.False);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1));
            Assert.That(navigator.IsOpen("a"), Is.True);
        }

        [Test]
        public void OwnerEndsWhileLoading_ReportsOwnerEnded_AndReleasesTheLateInstance()
        {
            _harness.Row("shop");
            _harness.Stage.AcquireMode = StageMode.Manual;
            var owner = _harness.NewOwner();

            var task = _harness.Navigator.ShowAsync(ViewKey.Create("shop"), owner.Token);

            // The load never answers until the test says so.
            Assert.That(task.IsPending(), Is.True);
            Assert.That(_harness.Stage.PendingAcquireCount, Is.EqualTo(1));

            owner.Cancel();

            var result = task.ResultNow();
            Assert.That(result.WasShown, Is.False);
            Assert.That(result.Reason, Is.EqualTo(CloseReason.NotShown));
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.OwnerEnded));

            // The load finishes after the request ended; the stage releases that instance itself.
            _harness.Stage.CompleteAcquire();

            Assert.That(_harness.Stage.LastInstanceOf("shop").ReleasedLate, Is.True);
            _harness.Stage.AssertAllReleased();
        }
    }
}
