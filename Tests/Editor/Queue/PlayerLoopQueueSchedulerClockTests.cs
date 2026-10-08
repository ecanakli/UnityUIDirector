using System;
using System.Threading;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // PlayerLoopQueueScheduler with a navigator clock: After and Cancel only, on a clock ticked by hand.
    [TestFixture]
    public sealed class PlayerLoopQueueSchedulerClockTests
    {
        private const double WarnDelaySeconds = 60.0;

        private RecordingDiagnostics _diagnostics;
        private NavigatorClock _clock;
        private CancellationTokenSource _lifetime;
        private int _runs;
        private Action<object> _countRun;

        [SetUp]
        public void SetUp()
        {
            _diagnostics = new RecordingDiagnostics();
            _clock = new NavigatorClock(_diagnostics.Diagnostics);
            _lifetime = new CancellationTokenSource();
            _runs = 0;
            _countRun = _ => _runs++;
        }

        [TearDown]
        public void TearDown()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        [Test]
        public void After_OnANavigatorClock_AddsOneClockTimer()
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            Assume.That(_clock.TimerCount, Is.Zero, "control: the clock starts empty");

            var id = scheduler.After(WarnDelaySeconds, _countRun, null);

            Assert.That(_clock.TimerCount, Is.EqualTo(1), "clock timers");
            Assert.That(id, Is.Not.Zero, "timer id");
        }

        [Test]
        public void After_OnANavigatorClock_DoesNotRunTheCallbackBeforeTheDelay()
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            scheduler.After(WarnDelaySeconds, _countRun, null);

            TickClock(599);

            Assert.That(_runs, Is.Zero, "callback runs after 59.9 clock seconds");
        }

        [TestCase(600)]
        [TestCase(1200)]
        public void After_OnANavigatorClock_RunsTheCallbackOnceAtTheDelay(int ticks)
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            scheduler.After(WarnDelaySeconds, _countRun, null);

            TickClock(ticks);

            Assert.That(_runs, Is.EqualTo(1), "callback runs");
            Assert.That(_clock.TimerCount, Is.Zero, "a timer that ran leaves the clock");
        }

        [Test]
        public void After_OnANavigatorClockWithANullCallback_SchedulesNothingAndReturnsZero()
        {
            var scheduler = CreateScheduler(_lifetime.Token);

            var id = scheduler.After(WarnDelaySeconds, null, null);

            Assert.That(id, Is.Zero, "timer id");
            Assert.That(_clock.TimerCount, Is.Zero, "clock timers");
        }

        [Test]
        public void Cancel_OnANavigatorClock_LeavesATimerScheduledDirectlyOnTheClock()
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            var schedulerRuns = 0;
            _clock.Schedule(1.0, _countRun, null);
            var id = scheduler.After(WarnDelaySeconds, _ => schedulerRuns++, null);

            scheduler.Cancel(id);
            var timersAfterCancel = _clock.TimerCount;
            TickClock(10);

            Assert.That(timersAfterCancel, Is.EqualTo(1), "only the scheduler's own timer left the clock");
            Assert.That(_runs, Is.EqualTo(1), "the timer scheduled directly on the clock ran at 1 s");
            Assert.That(schedulerRuns, Is.Zero, "the cancelled timer did not run");
        }

        [Test]
        public void Cancel_OnANavigatorClock_RemovesTheTimerAndTheCallbackNeverRuns()
        {
            var scheduler = CreateScheduler(_lifetime.Token);
            var id = scheduler.After(WarnDelaySeconds, _countRun, null);

            scheduler.Cancel(id);
            TickClock(1200);

            Assert.That(_clock.TimerCount, Is.Zero, "clock timers");
            Assert.That(_runs, Is.Zero, "callback runs after 120 clock seconds");
        }

        [Test]
        public void After_OnANavigatorClockWithAnEndedLifetime_SchedulesNothingAndReturnsZero()
        {
            var scheduler = CreateScheduler(new CancellationToken(true));

            var id = scheduler.After(WarnDelaySeconds, _countRun, null);

            Assert.That(id, Is.Zero, "timer id");
            Assert.That(_clock.TimerCount, Is.Zero, "clock timers");
        }

        [Test]
        public void Cancel_OnANavigatorClockWithAnIdItDidNotIssue_LeavesOtherClockTimers()
        {
            var scheduler = CreateScheduler(new CancellationToken(true));
            _clock.Schedule(1.0, _countRun, null);
            var unscheduled = scheduler.After(WarnDelaySeconds, _countRun, null);

            scheduler.Cancel(unscheduled);

            Assert.That(_clock.TimerCount, Is.EqualTo(1), "the timer scheduled directly on the clock survives");
        }

        private PlayerLoopQueueScheduler CreateScheduler(CancellationToken lifetime)
        {
            return new PlayerLoopQueueScheduler(lifetime, _diagnostics.Diagnostics, _clock);
        }

        // One tick per root Update at the clock's largest step.
        private void TickClock(int ticks)
        {
            for (var i = 0; i < ticks; i++)
                _clock.Tick(NavigatorClock.MaxStepSeconds);
        }
    }
}
