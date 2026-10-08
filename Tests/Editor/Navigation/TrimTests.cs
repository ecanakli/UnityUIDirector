using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Navigator.Trim on the fake stage: which kept instances it releases and what it leaves alone.
    [TestFixture]
    public sealed class TrimTests
    {
        private const string Kept = "kept";
        private const string Other = "other";
        private const string Blocker = "blocker";
        private const string Shop = "shop";

        private NavigatorHarness _harness;

        [TearDown]
        public void TearDown()
        {
            if (_harness != null)
                _harness.Dispose();
            _harness = null;
        }

        private NavigatorHarness CreateHarness()
        {
            _harness = new NavigatorHarness();
            return _harness;
        }

        // A KeepInstance popup row opened and completed once, so its instance sits idle in the cache.
        private static FakeInstance KeepIdle(NavigatorHarness harness, string id)
        {
            harness.Row(id, ViewLayers.Popups, retention: ViewRetention.KeepInstance).Map<NoArgs, bool>(id);
            var task = LifecycleDriver.Reach(harness, id, ViewPhase.Open, harness.Owner);
            LifecycleDriver.ViewOf(harness, id).Complete(true);
            Assert.That(task.ResultNow().Reason, Is.EqualTo(CloseReason.Completed), "Control: the request completed.");

            var kept = harness.Stage.LastInstanceOf(id);
            Assert.That(kept.Stashed, Is.True, "Control: the row keeps its idle instance.");
            return kept;
        }

        [Test]
        public void Trim_IdleKeptInstances_ReleasesEachOnce()
        {
            var harness = CreateHarness();
            var kept = KeepIdle(harness, Kept);
            var other = KeepIdle(harness, Other);

            harness.Navigator.Trim();

            Assert.That(kept.ReleaseCount, Is.EqualTo(1), "release count of the first kept instance");
            Assert.That(other.ReleaseCount, Is.EqualTo(1), "release count of the second kept instance");
            Assert.That(harness.Stage.StashedInstances, Is.Empty, "no instance is left stashed");
            Assert.That(harness.Describe(), Does.Contain("kept instances: 0"), "the cache is empty");
        }

        [Test]
        public void Trim_Twice_ReleasesOnce()
        {
            var harness = CreateHarness();
            var kept = KeepIdle(harness, Kept);

            harness.Navigator.Trim();
            harness.Navigator.Trim();

            Assert.That(kept.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void Trim_AfterTeardown_DoesNothing()
        {
            var harness = CreateHarness();
            var kept = KeepIdle(harness, Kept);
            harness.Navigator.Teardown();
            Assert.That(kept.ReleaseCount, Is.EqualTo(1), "Control: teardown released the kept instance.");
            harness.Stage.ClearCalls();

            harness.Navigator.Trim();

            Assert.That(harness.Stage.CountOf(StageCallKind.Release), Is.Zero, harness.Stage.DescribeCalls());
            Assert.That(kept.ReleaseCount, Is.EqualTo(1), "no second release");
        }

        [Test]
        public void Trim_RequestWaitingForTheKeptId_LoadsAFreshInstance()
        {
            var harness = CreateHarness();
            var kept = KeepIdle(harness, Kept);
            LifecycleDriver.AddRows(harness, ViewLayers.Popups, Blocker);
            LifecycleDriver.Reach(harness, Kept, ViewPhase.Waiting, harness.Owner, Blocker);

            harness.Navigator.Trim();
            harness.Stage.AcquireMode = StageMode.Auto;
            harness.Stage.CompleteAcquire();

            var fresh = harness.Stage.LastInstanceOf(Kept);
            Assert.That(kept.ReleaseCount, Is.EqualTo(1), "the trimmed instance is released once");
            Assert.That(fresh, Is.Not.SameAs(kept), "the waiting request is not given the released instance");
            Assert.That(
                harness.Stage.CallsOf(StageCallKind.Acquire),
                Has.Exactly(2).Matches<StageCall>(call => call.ViewId == Kept),
                "the kept id is loaded again\n" + harness.Stage.DescribeCalls());
            Assert.That(LifecycleDriver.HandleOf(harness, Kept).Phase, Is.EqualTo(ViewPhase.Open), "the waiting request opens");
        }

        [Test]
        public void Trim_AllowMultipleRow_ReleasesEveryKeptInstance()
        {
            var harness = CreateHarness();
            harness.Row(Kept, ViewLayers.Popups, retention: ViewRetention.KeepInstance, allowMultiple: true).Map<NoArgs, bool>(Kept);
            LifecycleDriver.Show(harness, Kept, harness.Owner);
            LifecycleDriver.Show(harness, Kept, harness.Owner);
            Assert.That(harness.Stage.Instances.Count, Is.EqualTo(2), "Control: two instances of the row are open.\n" + harness.Stage.DescribeCalls());
            ((FakeView<NoArgs, bool>)harness.Stage.Instances[0].View).Complete(true);
            ((FakeView<NoArgs, bool>)harness.Stage.Instances[1].View).Complete(true);
            harness.Stage.ClearCalls();

            harness.Navigator.Trim();

            // The cache keeps one slot per id, so the second close released its instance and Trim releases the one kept.
            Assert.That(harness.Stage.CountOf(StageCallKind.Release), Is.EqualTo(1), "releases by Trim\n" + harness.Stage.DescribeCalls());
            Assert.That(harness.Stage.StashedInstances, Is.Empty, "no instance is left stashed");
            harness.Stage.AssertAllReleased();
        }

        [Test]
        public void Trim_PreloadLeaseHeld_LeaseIsUntouched()
        {
            var harness = CreateHarness();
            var kept = KeepIdle(harness, Kept);
            harness.Row(Shop);
            Assert.That(harness.Navigator.PreloadAsync(Shop, harness.Owner).ResultNow(), Is.True, "Control: the preload succeeded.");
            var lease = harness.Stage.Leases[0];

            harness.Navigator.Trim();

            Assert.That(kept.ReleaseCount, Is.EqualTo(1), "Control: Trim released the kept instance.");
            Assert.That(lease.DisposeCount, Is.Zero, "lease dispose count");
            Assert.That(harness.Describe(), Does.Contain("preload leases: 1 shop"), "the lease is still listed");
        }
    }
}
