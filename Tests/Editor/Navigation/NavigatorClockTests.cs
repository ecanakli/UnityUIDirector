using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // The navigator's clock driven directly: the step cap, firing order, re-entrancy, the per-tick guard and fault isolation.
    [TestFixture]
    public sealed class NavigatorClockTests
    {
        private RecordingDiagnostics _diagnostics;

        [SetUp]
        public void SetUp()
        {
            _diagnostics = new RecordingDiagnostics();
        }

        private NavigatorClock CreateClock()
        {
            return new NavigatorClock(_diagnostics.Diagnostics);
        }

        private static void AddName(object name, List<string> order)
        {
            order.Add((string)name);
        }

        // Step cap

        [Test]
        public void Tick_DeltaAboveCap_AdvancesOneCapStepOnly([Values(0.5f, 60f, float.PositiveInfinity)] float delta)
        {
            var clock = CreateClock();
            var fired = false;
            clock.Schedule(15.0, _ => fired = true, null);

            clock.Tick(delta);

            Assert.That(clock.Now, Is.EqualTo((double)NavigatorClock.MaxStepSeconds), "Now after one capped tick");
            Assert.That(fired, Is.False, "a timer set for 15 s must not fire after one capped tick");
        }

        [Test]
        public void Tick_DeltaAtOrBelowCap_AdvancesByTheDelta([Values(0.016f, 0.1f)] float delta)
        {
            var clock = CreateClock();

            clock.Tick(delta);

            Assert.That(clock.Now, Is.EqualTo((double)delta));
        }

        [Test]
        public void Tick_ZeroNegativeOrNaNDelta_AdvancesNothing([Values(0f, -1f, float.NaN)] float delta)
        {
            var clock = CreateClock();

            clock.Tick(delta);

            Assert.That(clock.Now, Is.EqualTo(0.0));
        }

        // Firing order

        [Test]
        public void Tick_TimersDueTogether_FireEarliestFirstThenLowestId()
        {
            var clock = CreateClock();
            var order = new List<string>();
            clock.Schedule(0.05, name => AddName(name, order), "A");
            clock.Schedule(0.02, name => AddName(name, order), "B");
            clock.Schedule(0.05, name => AddName(name, order), "C");
            clock.Schedule(0.02, name => AddName(name, order), "D");

            clock.Tick(NavigatorClock.MaxStepSeconds);

            Assert.That(order, Is.EqualTo(new[] { "B", "D", "A", "C" }));
        }

        [Test]
        public void Tick_TimerDueExactlyAtNow_Fires()
        {
            const float delay = 0.05f;
            var clock = CreateClock();
            var fired = false;
            clock.Schedule(delay, _ => fired = true, null);

            clock.Tick(delay);

            Assert.That(fired, Is.True, "a timer whose due time equals Now must fire; Now is " + clock.Now);
        }

        [Test]
        public void Tick_DueTimer_FiresExactlyOnceAcrossLaterTicks()
        {
            var clock = CreateClock();
            var fireCount = 0;
            clock.Schedule(0.05, _ => fireCount++, null);

            clock.Tick(0.016f);
            var afterEarlyTick = fireCount;
            clock.Tick(0.1f);
            var afterDueTick = fireCount;
            clock.Tick(0.1f);

            Assert.That(afterEarlyTick, Is.Zero, "fires before the delay has accumulated");
            Assert.That(afterDueTick, Is.EqualTo(1), "fires on the tick the timer becomes due");
            Assert.That(fireCount, Is.EqualTo(1), "fires after a later tick");
            Assert.That(clock.HasTimers, Is.False, "pending timers after the timer ran");
        }

        // Re-entrancy

        [Test]
        public void Schedule_InsideACallbackWithZeroDelay_FiresInTheSameTick()
        {
            var clock = CreateClock();
            var innerFired = false;
            clock.Schedule(0.0, _ => clock.Schedule(0.0, __ => innerFired = true, null), null);

            clock.Tick(0.016f);

            Assert.That(innerFired, Is.True);
        }

        [Test]
        public void Schedule_ZeroDelaySelfRescheduling_StopsAtMaxFiresPerTickAndContinuesOnTheNextTick()
        {
            var clock = CreateClock();
            var count = 0;
            Action<object> again = null;
            again = _ =>
            {
                count++;
                clock.Schedule(0.0, again, null);
            };
            clock.Schedule(0.0, again, null);

            clock.Tick(0.016f);
            var afterFirstTick = count;
            clock.Tick(0.016f);

            Assert.That(afterFirstTick, Is.EqualTo(NavigatorClock.MaxFiresPerTick), "callbacks after the first tick");
            Assert.That(count, Is.GreaterThan(afterFirstTick), "the second tick must run the timer again");
        }

        [Test]
        public void Cancel_InsideACallbackOfAnotherDueTimer_DoesNotFireIt()
        {
            var clock = CreateClock();
            var cancelled = false;
            var secondFired = false;
            var secondId = 0;
            clock.Schedule(0.01, _ => cancelled = clock.Cancel(secondId), null);
            secondId = clock.Schedule(0.02, _ => secondFired = true, null);

            clock.Tick(NavigatorClock.MaxStepSeconds);

            Assert.That(cancelled, Is.True, "Cancel inside a callback finds the pending timer");
            Assert.That(secondFired, Is.False, "the cancelled timer must not fire in the same tick");
        }

        // Cancel and clear

        [Test]
        public void Cancel_PendingTimer_ReturnsTrueAndTimerDoesNotFire()
        {
            var clock = CreateClock();
            var fired = false;
            var id = clock.Schedule(0.05, _ => fired = true, null);

            var cancelled = clock.Cancel(id);
            clock.Tick(NavigatorClock.MaxStepSeconds);

            Assert.That(cancelled, Is.True, "Cancel of a pending timer");
            Assert.That(fired, Is.False, "a cancelled timer must not fire");
            Assert.That(clock.HasTimers, Is.False, "pending timers after Cancel");
        }

        [Test]
        public void Cancel_AlreadyCancelledId_ReturnsFalse()
        {
            var clock = CreateClock();
            var id = clock.Schedule(0.05, _ => { }, null);
            clock.Cancel(id);

            Assert.That(clock.Cancel(id), Is.False, "second Cancel of the same id");
        }

        [Test]
        public void Cancel_UnknownZeroOrAlreadyFiredId_ReturnsFalse()
        {
            var clock = CreateClock();
            var firedId = clock.Schedule(0.0, _ => { }, null);
            clock.Tick(0.016f);

            Assert.That(clock.Cancel(0), Is.False, "zero id");
            Assert.That(clock.Cancel(firedId + 1000), Is.False, "unknown id");
            Assert.That(clock.Cancel(firedId), Is.False, "already fired id");
        }

        [Test]
        public void Clear_DropsPendingTimersWithoutRunningThem()
        {
            var clock = CreateClock();
            var fireCount = 0;
            clock.Schedule(0.0, _ => fireCount++, null);
            clock.Schedule(0.05, _ => fireCount++, null);

            clock.Clear();
            clock.Tick(NavigatorClock.MaxStepSeconds);

            Assert.That(fireCount, Is.Zero, "callbacks run after Clear");
            Assert.That(clock.HasTimers, Is.False, "pending timers after Clear");
        }

        // Faults

        [Test]
        public void Tick_ThrowingCallback_IsReportedAsAFaultAndLaterTimersStillFire()
        {
            var clock = CreateClock();
            var laterFired = false;
            clock.Schedule(0.01, _ => throw new InvalidOperationException("timer test fault"), null);
            clock.Schedule(0.02, _ => laterFired = true, null);

            clock.Tick(NavigatorClock.MaxStepSeconds);

            Assert.That(_diagnostics.FaultCount, Is.EqualTo(1), _diagnostics.Describe());
            Assert.That(laterFired, Is.True, "the timer after the throwing one");
        }
    }
}
