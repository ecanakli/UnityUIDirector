using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.UIDirector.Tests
{
    // PlayerLoopQueueScheduler without a navigator clock: ticks and timers on the real player loop.
    [TestFixture]
    public sealed class PlayerLoopQueueSchedulerTests
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 1f;
        private const double ShortDelaySeconds = 0.05;

        // Clearly longer than every delay these tests schedule, to prove a callback never runs.
        private const float QuietWindowSeconds = 0.3f;
        private const int QuietWindowFrames = 5;

        private RecordingDiagnostics _diagnostics;
        private CancellationTokenSource _lifetime;
        private int _runs;
        private Action<object> _countRun;
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            _diagnostics = new RecordingDiagnostics();
            _lifetime = new CancellationTokenSource();
            _runs = 0;
            _countRun = _ => _runs++;
        }

        [TearDown]
        public void TearDown()
        {
            // Runs on failure too, so a failed time-scale test cannot leave the run at scale 0.
            Time.timeScale = _timeScale;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        [UnityTest]
        public IEnumerator NextTick_RunsOnALaterFrame_NeverSynchronously()
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            var ranAtFrame = -1;
            var calledAtFrame = Time.frameCount;

            scheduler.NextTick(_ => ranAtFrame = Time.frameCount, null);
            var ranInTheCall = ranAtFrame != -1;
            yield return WaitFor(() => ranAtFrame != -1, "the tick to run");

            Assert.That(ranInTheCall, Is.False, "ran inside the NextTick call");
            Assert.That(ranAtFrame, Is.GreaterThan(calledAtFrame), "frame of the run");
        }

        [UnityTest]
        public IEnumerator NextTick_AfterTheLifetimeEnded_DoesNotRun()
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            _lifetime.Cancel();

            scheduler.NextTick(_countRun, null);
            yield return WaitQuietWindow();

            Assert.That(_runs, Is.Zero, "callback runs");
        }

        [UnityTest]
        public IEnumerator NextTick_LifetimeEndsBeforeTheTick_DoesNotRun()
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            scheduler.NextTick(_countRun, null);

            _lifetime.Cancel();
            yield return WaitQuietWindow();

            Assert.That(_runs, Is.Zero, "callback runs");
        }

        [UnityTest]
        public IEnumerator After_AtTimeScaleZero_StillFires()
        {
            Time.timeScale = 0f;
            var scheduler = CreateScheduler(_lifetime.Token);

            scheduler.After(0.1, _countRun, null);
            yield return WaitFor(() => _runs > 0, "the timer to fire at time scale 0");
            yield return WaitQuietWindow();

            Assert.That(_runs, Is.EqualTo(1), "callback runs");
        }

        [UnityTest]
        public IEnumerator Cancel_StopsTheTimer()
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            var id = scheduler.After(ShortDelaySeconds, _countRun, null);

            scheduler.Cancel(id);
            yield return WaitQuietWindow();

            Assert.That(_runs, Is.Zero, "callback runs");
        }

        [UnityTest]
        public IEnumerator Cancel_UnknownOrZeroId_DoesNothing([Values(0, 9999)] int timerId)
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            scheduler.After(ShortDelaySeconds, _countRun, null);

            scheduler.Cancel(timerId);
            yield return WaitFor(() => _runs > 0, "the pending timer to fire");

            Assert.That(_runs, Is.EqualTo(1), "the pending timer still runs");
            Assert.That(_diagnostics.Total, Is.Zero, _diagnostics.Describe());
        }

        [UnityTest]
        public IEnumerator LifetimeEnd_CancelsEveryPendingTimer()
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            scheduler.After(ShortDelaySeconds, _countRun, null);
            scheduler.After(ShortDelaySeconds * 2.0, _countRun, null);

            _lifetime.Cancel();
            yield return WaitQuietWindow();

            Assert.That(_runs, Is.Zero, "callback runs");
        }

        [UnityTest]
        public IEnumerator CallbackThrows_ReportsAFaultAndTheLoopContinues()
        {
            var scheduler = CreateScheduler(_lifetime.Token);

            scheduler.NextTick(_ => throw new InvalidOperationException("planted tick fault"), null);
            scheduler.NextTick(_countRun, null);
            yield return WaitFor(() => _runs > 0, "the tick after the throwing one");

            Assert.That(_runs, Is.EqualTo(1), "the next callback runs");
            Assert.That(_diagnostics.FaultCount, Is.EqualTo(1), _diagnostics.Describe());
            Assert.That(_diagnostics.Entries[0].Message, Does.Contain("queue tick").And.Contain("planted tick fault"), "fault message");
        }

        private PlayerLoopQueueScheduler CreateScheduler(CancellationToken lifetime)
        {
            return new PlayerLoopQueueScheduler(lifetime, _diagnostics.Diagnostics);
        }

        // Bounded by frames and a real-time floor.
        private static IEnumerator WaitFor(Func<bool> condition, string what)
        {
            var started = Time.realtimeSinceStartup;
            for (var frame = 0; !condition(); frame++)
            {
                if (frame >= MaxFrames && Time.realtimeSinceStartup - started >= MinWaitSeconds)
                    Assert.Fail("Timed out after " + frame + " frames waiting for " + what + ".");

                yield return null;
            }
        }

        // A fixed window, the only way to show that something does not happen.
        private static IEnumerator WaitQuietWindow()
        {
            var started = Time.realtimeSinceStartup;
            for (var frame = 0; frame < QuietWindowFrames || Time.realtimeSinceStartup - started < QuietWindowSeconds; frame++)
                yield return null;
        }
    }
}
