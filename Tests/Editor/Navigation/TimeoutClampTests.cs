using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // A load or transition timeout below the minimum, or NaN, acts as the minimum instead of firing at the first tick.
    [TestFixture]
    public sealed class TimeoutClampTests
    {
        private const string Target = LifecycleDriver.Target;
        private const float BeforeMinimumSeconds = 0.05f;
        // Far past both default timeouts, so a capped infinity would have fired.
        private const float LongWaitSeconds = 100f;

        private NavigatorHarness _harness;

        [TearDown]
        public void TearDown()
        {
            if (_harness != null)
                _harness.Dispose();

            _harness = null;
        }

        private NavigatorHarness CreateHarness(
            float loadTimeoutSeconds = LayerRunner.DefaultLoadTimeoutSeconds,
            float transitionTimeoutSeconds = LayerRunner.DefaultTransitionTimeoutSeconds)
        {
            _harness = new NavigatorHarness(loadTimeoutSeconds: loadTimeoutSeconds, transitionTimeoutSeconds: transitionTimeoutSeconds);
            LifecycleDriver.AddRows(_harness, ViewLayers.Popups, Target);
            return _harness;
        }

        [Test]
        public void LoadTimeout_ZeroNegativeOrNaN_ActsAsTheMinimum([Values(0f, -1f, float.NegativeInfinity, float.NaN)] float value)
        {
            AssertLoadTimeoutActsAsTheMinimum(value);
        }

        // Values at or below BeforeMinimumSeconds, so an unclamped timeout would already have fired at the first check.
        [Test]
        public void LoadTimeout_PositiveBelowTheMinimum_ActsAsTheMinimum([Values(0.02f, 0.05f)] float value)
        {
            AssertLoadTimeoutActsAsTheMinimum(value);
        }

        [Test]
        public void TransitionTimeout_ZeroNegativeOrNaN_ActsAsTheMinimum([Values(0f, -1f, float.NegativeInfinity, float.NaN)] float value)
        {
            AssertTransitionTimeoutActsAsTheMinimum(value);
        }

        [Test]
        public void TransitionTimeout_PositiveBelowTheMinimum_ActsAsTheMinimum([Values(0.02f, 0.05f)] float value)
        {
            AssertTransitionTimeoutActsAsTheMinimum(value);
        }

        [Test]
        public void LoadTimeout_PositiveInfinity_NeverTimesOut()
        {
            var harness = CreateHarness(loadTimeoutSeconds: float.PositiveInfinity);
            var task = LifecycleDriver.Reach(harness, Target, ViewPhase.Loading, harness.Owner);

            harness.Advance(LongWaitSeconds);

            Assert.That(task.IsPending(), Is.True, "Still loading with an infinite load timeout.\n" + harness.Describe());
            Assert.That(LifecycleDriver.HandleOf(harness, Target).Phase, Is.EqualTo(ViewPhase.Loading), "phase with an infinite load timeout");
            Assert.That(harness.Diagnostics.Count(DiagnosticIds.LoadTimedOut), Is.Zero, "load timeout diagnostics with an infinite load timeout");
        }

        [Test]
        public void TransitionTimeout_PositiveInfinity_NeverSnaps()
        {
            var harness = CreateHarness(transitionTimeoutSeconds: float.PositiveInfinity);
            LifecycleDriver.Reach(harness, Target, ViewPhase.Opening, harness.Owner);
            var handle = LifecycleDriver.HandleOf(harness, Target);

            harness.Advance(LongWaitSeconds);

            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Opening), "Still opening with an infinite transition timeout.\n" + harness.Describe());
            Assert.That(harness.Diagnostics.Count(DiagnosticIds.TransitionTimedOut), Is.Zero, "transition timeout diagnostics with an infinite transition timeout");
        }

        [Test]
        public void LoadTimeout_AtTheMinimum_StillTimesOutAtPointOneSeconds()
        {
            var harness = CreateHarness(loadTimeoutSeconds: LayerRunner.MinTimeoutSeconds);
            var task = LifecycleDriver.Reach(harness, Target, ViewPhase.Loading, harness.Owner);

            harness.Advance(0.09f);
            Assert.That(task.IsPending(), Is.True, "Still loading just before 0.1 s.\n" + harness.Describe());

            harness.Advance(0.02f);

            LifecycleDriver.AssertNotShown(task.ResultNow(), RejectReason.LoadTimedOut);
        }

        private void AssertLoadTimeoutActsAsTheMinimum(float value)
        {
            var harness = CreateHarness(loadTimeoutSeconds: value);
            var task = LifecycleDriver.Reach(harness, Target, ViewPhase.Loading, harness.Owner);

            harness.Advance(BeforeMinimumSeconds);
            Assert.That(task.IsPending(), Is.True, "Still loading before the 0.1 s minimum.\n" + harness.Describe());
            Assert.That(LifecycleDriver.HandleOf(harness, Target).Phase, Is.EqualTo(ViewPhase.Loading), "phase before the minimum");

            harness.Advance(LayerRunner.MinTimeoutSeconds);

            LifecycleDriver.AssertNotShown(task.ResultNow(), RejectReason.LoadTimedOut);
            Assert.That(harness.Diagnostics.Count(DiagnosticIds.LoadTimedOut), Is.EqualTo(1), "load timeout diagnostics");
        }

        private void AssertTransitionTimeoutActsAsTheMinimum(float value)
        {
            var harness = CreateHarness(transitionTimeoutSeconds: value);
            LifecycleDriver.Reach(harness, Target, ViewPhase.Opening, harness.Owner);
            var handle = LifecycleDriver.HandleOf(harness, Target);

            harness.Advance(BeforeMinimumSeconds);
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Opening), "Still opening before the 0.1 s minimum.\n" + harness.Describe());
            Assert.That(harness.Diagnostics.Count(DiagnosticIds.TransitionTimedOut), Is.Zero, "transition timeout diagnostics before the minimum");

            harness.Advance(LayerRunner.MinTimeoutSeconds);

            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open), "phase after the minimum");
            Assert.That(harness.Diagnostics.Count(DiagnosticIds.TransitionTimedOut), Is.EqualTo(1), "transition timeout diagnostics after the minimum");
        }
    }
}
