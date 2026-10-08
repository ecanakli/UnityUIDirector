using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Load indicator delay passed through the navigator: zero or less shows on the first tick, a positive delay waits.
    public sealed class LoadIndicatorDelayTests
    {
        private const float OneFrameSeconds = 0.016f;

        private NavigatorHarness _harness;

        [TearDown]
        public void TearDown()
        {
            _harness?.Dispose();
            _harness = null;
        }

        private NavigatorHarness CreateHarness(float loadIndicatorDelaySeconds)
        {
            _harness = new NavigatorHarness(loadIndicatorDelaySeconds: loadIndicatorDelaySeconds);
            _harness.Row("a");
            _harness.Stage.AcquireMode = StageMode.Manual;
            return _harness;
        }

        [Test]
        public void LoadIndicator_DelayZero_ShowsOnTheNextTick()
        {
            var harness = CreateHarness(0f);
            NavigationTestKit.Open(harness, "a");
            Assert.That(harness.Stage.LoadIndicatorVisible, Is.False, "Not shown inside the Open call, before any tick.");

            harness.Clock.Tick(OneFrameSeconds);
            Assert.That(harness.Stage.LoadIndicatorVisible, Is.True, "Shown on the first tick while the acquire is pending.");

            harness.Stage.CompleteAcquire();
            Assert.That(harness.Stage.LoadIndicatorVisible, Is.False, "Hidden once the load ends.");
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void LoadIndicator_DelayNegativeOrNaN_BehavesLikeZero(float delaySeconds)
        {
            var harness = CreateHarness(delaySeconds);
            NavigationTestKit.Open(harness, "a");

            harness.Clock.Tick(OneFrameSeconds);

            Assert.That(harness.Stage.LoadIndicatorVisible, Is.True, "A delay of " + delaySeconds + " should show the indicator on the first tick.");
        }

        [Test]
        public void LoadIndicator_LoadEndsBeforeTheDelay_NeverShows()
        {
            var harness = CreateHarness(0f);
            NavigationTestKit.Open(harness, "a");

            harness.Stage.CompleteAcquire();
            harness.Advance(1f);

            Assert.That(harness.Stage.CountOf(StageCallKind.SetLoadIndicator), Is.Zero, "A load that ends before the first tick never touches the indicator.");
            Assert.That(harness.Stage.LoadIndicatorVisible, Is.False, "The indicator stays hidden.");
        }

        [Test]
        public void LoadIndicator_PositiveDelay_ShowsOnlyAfterTheDelay()
        {
            var harness = CreateHarness(0.1f);
            NavigationTestKit.Open(harness, "a");

            harness.Advance(0.05f);
            Assert.That(harness.Stage.LoadIndicatorVisible, Is.False, "Hidden before the 0.1 s delay has passed.");
            Assert.That(harness.Stage.CountOf(StageCallKind.SetLoadIndicator), Is.Zero, "The indicator is not touched before the delay.");

            harness.Advance(0.1f);
            Assert.That(harness.Stage.LoadIndicatorVisible, Is.True, "Shown once the delay has passed while the acquire is pending.");
        }
    }
}
