using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // ViewQueue rules: holds, gates, item ends, lifetime, threads and arguments.
    public sealed class ViewQueueRulesTests
    {
        private QueueRig _rig;

        [SetUp]
        public void SetUp()
        {
            _rig = new QueueRig();
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        // Holds

        [Test]
        public void Holds_AreCounted_AndNothingIsPresentedWhileAnyRemains()
        {
            var first = _rig.Queue.Hold("first", CancellationToken.None);
            var second = _rig.Queue.Hold("second", CancellationToken.None);
            _rig.Enqueue("a");

            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Queue.IsHeld, Is.True);
            Assert.That(_rig.Navigator.Calls, Is.Empty, "Q2: nothing is presented while a hold exists.");

            first.Remove();
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Queue.IsHeld, Is.True, "One hold is left.");
            Assert.That(_rig.Navigator.Calls, Is.Empty);

            second.Remove();
            Assert.That(_rig.Queue.IsHeld, Is.False);
            Assert.That(_rig.Ticker.PendingTicks, Is.GreaterThan(0), "A removed hold schedules a pump.");

            _rig.Ticker.Tick();
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void AHold_EndsWithItsOwner_AndTheQueueResumes()
        {
            var owner = _rig.NewOwner();
            var hold = _rig.Queue.Hold("flow", owner.Token);
            _rig.Enqueue("a");
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Navigator.Calls, Is.Empty);

            owner.Cancel();

            Assert.That(hold.IsActive, Is.False, "The registration ends with its owner.");
            Assert.That(_rig.Queue.IsHeld, Is.False);
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void AHoldTakenWhileAnItemIsPresented_KeepsTheNextOneWaiting()
        {
            _rig.Enqueue("a");
            _rig.Enqueue("b");
            _rig.Ticker.Tick();

            var hold = _rig.Queue.Hold("cutscene", CancellationToken.None);
            _rig.Navigator.LastCall.Close(CloseReason.Dismissed);
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }), "A hold does not touch the presented view but stops the next one.");

            hold.Remove();
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void HoldCount_IsZero_AfterEveryHoldOwnerEnds()
        {
            var owners = new List<CancellationTokenSource>();
            for (var i = 0; i < 4; i++)
            {
                var owner = _rig.NewOwner();
                owners.Add(owner);
                _rig.Queue.Hold("hold " + i, owner.Token);
            }

            // One owner holds twice.
            _rig.Queue.Hold("again", owners[2].Token);

            for (var i = owners.Count - 1; i >= 0; i--)
                owners[i].Cancel();

            Assert.That(_rig.Queue.IsHeld, Is.False, "Q5: the hold count is 0 when every hold owner has ended.");
        }

        [Test]
        public void AHoldOlderThanSixtySeconds_LogsUIDIRECTOR111_Once()
        {
            _rig.Queue.Hold("stuck flow", CancellationToken.None);

            _rig.Ticker.Advance(59.5);
            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR111"), Is.Zero);

            _rig.Ticker.Advance(1.0);
            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR111"), Is.EqualTo(1), "A hold older than 60 s is reported.");

            _rig.Ticker.Advance(600.0);
            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR111"), Is.EqualTo(1), "An old hold is reported once per hold.");
        }

        [Test]
        public void AHoldOlderThanSixtySeconds_WarningNamesTheCallingMemberAndLine()
        {
            _rig.Queue.Hold("stuck flow", CancellationToken.None);

            _rig.Ticker.Advance(61.0);

            var message = FirstMessageOf("UIDIRECTOR111");
            Assert.That(message, Does.Contain(nameof(AHoldOlderThanSixtySeconds_WarningNamesTheCallingMemberAndLine)), "calling member. " + _rig.Diagnostics.Describe());
            Assert.That(message, Does.Contain(" (line "), "calling line");
            Assert.That(message, Does.Not.Contain("an unknown call site"), "a known call site");
        }

        [Test]
        public void Hold_RecordsTheCallSiteInTheCapturedState()
        {
            _rig.Queue.Hold("flow", CancellationToken.None);
            var state = new QueueState();

            _rig.Queue.CaptureState(state);

            Assert.That(state.Holds.Count, Is.EqualTo(1), "holds");
            Assert.That(state.Holds[0].CallSite, Does.Contain(nameof(Hold_RecordsTheCallSiteInTheCapturedState)), "call site");
        }

        [Test]
        public void Hold_WithAnExplicitMember_RecordsIt()
        {
            _rig.Queue.Hold("r", CancellationToken.None, "Flow", 12);
            var state = new QueueState();

            _rig.Queue.CaptureState(state);

            Assert.That(state.Holds.Count, Is.EqualTo(1), "holds");
            Assert.That(state.Holds[0].CallSite, Is.EqualTo("Flow (line 12)"), "call site");
        }

        [Test]
        public void AHoldRemovedBeforeSixtySeconds_LogsNothing()
        {
            var hold = _rig.Queue.Hold("short flow", CancellationToken.None);
            _rig.Ticker.Advance(30.0);

            hold.Remove();
            _rig.Ticker.Advance(120.0);

            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR111"), Is.Zero);
        }

        [Test]
        public void TwoHoldsOlderThanSixtySeconds_LogUIDIRECTOR111_OnceEach_AsAWarning()
        {
            _rig.Queue.Hold("first flow", CancellationToken.None);
            _rig.Ticker.Advance(30.0);
            _rig.Queue.Hold("second flow", CancellationToken.None);

            _rig.Ticker.Advance(31.0);
            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR111"), Is.EqualTo(1), "The first hold is 61 s old, the second 31 s.");

            _rig.Ticker.Advance(30.0);
            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR111"), Is.EqualTo(2), "Reported once per hold, not once per queue.");

            _rig.Ticker.Advance(600.0);
            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR111"), Is.EqualTo(2), "Never repeated.");
            Assert.That(_rig.Diagnostics.Entries, Has.None.Matches<DiagnosticEntry>(e => e.Id == "UIDIRECTOR111" && e.IsError),
                "UIDIRECTOR111 is a warning. " + _rig.Diagnostics.Describe());
        }

        [Test]
        public void AHoldRemovedTwice_OrEndedByItsOwnerAfterRemove_ReleasesOnlyItself()
        {
            var firstOwner = _rig.NewOwner();
            var first = _rig.Queue.Hold("first", firstOwner.Token);
            var second = _rig.Queue.Hold("second", CancellationToken.None);
            _rig.Enqueue("a");

            first.Remove();
            Assert.That(() => first.Remove(), Throws.Nothing, "Remove never throws.");
            Assert.That(() => firstOwner.Cancel(), Throws.Nothing);
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Queue.IsHeld, Is.True, "Q5: the second hold still exists; the first ends once.");
            Assert.That(_rig.Navigator.Calls, Is.Empty);

            second.Remove();
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Queue.IsHeld, Is.False);
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void Remove_OnAnEmptyRegistration_DoesNotThrow()
        {
            var ended = _rig.NewOwner();
            ended.Cancel();
            var lateHold = _rig.Queue.Hold("late", ended.Token);
            var lateGate = _rig.Queue.AddGate(new ScriptedGate(), ended.Token);
            var lateObserver = _rig.Queue.AddObserver(new RecordingObserver(), ended.Token);
            _rig.EndLifetime();
            var afterHold = _rig.Queue.Hold("after", CancellationToken.None);
            var afterGate = _rig.Queue.AddGate(new ScriptedGate(), CancellationToken.None);
            var afterObserver = _rig.Queue.AddObserver(new RecordingObserver(), CancellationToken.None);

            Assert.That(() =>
            {
                lateHold.Remove();
                lateHold.Remove();
                lateGate.Remove();
                lateObserver.Remove();
                afterHold.Remove();
                afterGate.Remove();
                afterObserver.Remove();
            }, Throws.Nothing, "ViewRegistration.Remove never throws.");
            Assert.That(_rig.Queue.IsHeld, Is.False);
        }

        [Test]
        public void AHoldRemovedOffTheMainThread_IsPosted_AndTheQueueResumesWhenTheMainThreadRunsIt()
        {
            var hold = _rig.Queue.Hold("flow", CancellationToken.None);
            _rig.Enqueue("a");
            _rig.Ticker.TickUntilIdle();

            _rig.MainThread.IsMainThread = false;
            hold.Remove();

            Assert.That(_rig.MainThread.PendingCount, Is.EqualTo(1), "Remove off the main thread is posted to the main thread.");
            Assert.That(_rig.Queue.IsHeld, Is.True, "Nothing changes until the main thread runs the post.");
            Assert.That(_rig.Ticker.PendingTicks, Is.Zero, "No pump is scheduled off the main thread.");

            _rig.MainThread.Flush();
            _rig.MainThread.IsMainThread = true;

            Assert.That(_rig.Queue.IsHeld, Is.False);
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void AHoldOwnerCancelledOffTheMainThread_IsPosted_WithUIDIRECTOR113()
        {
            var owner = _rig.NewOwner();
            _rig.Queue.Hold("flow", owner.Token);
            _rig.Enqueue("a");
            _rig.Ticker.TickUntilIdle();

            _rig.MainThread.IsMainThread = false;
            owner.Cancel();

            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR113"), Is.EqualTo(1), "An owner callback off the main thread logs UIDIRECTOR113.");
            Assert.That(_rig.MainThread.PendingCount, Is.EqualTo(1), "An off-thread removal is posted to the main thread.");
            Assert.That(_rig.Queue.IsHeld, Is.True);
            Assert.That(_rig.Ticker.PendingTicks, Is.Zero);

            _rig.MainThread.Flush();
            _rig.MainThread.IsMainThread = true;

            Assert.That(_rig.Queue.IsHeld, Is.False);
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void Hold_WithAnOwnerAlreadyEnded_TakesNoHold()
        {
            var owner = _rig.NewOwner();
            owner.Cancel();

            var hold = _rig.Queue.Hold("late", owner.Token);

            Assert.That(hold.IsActive, Is.False);
            Assert.That(_rig.Queue.IsHeld, Is.False);
        }

        // Gates

        [Test]
        public void AGateDenyAtEnqueue_PublishesOnlyNotShownDenied_WithTheReservedId()
        {
            var gate = new ScriptedGate(decide: r => r.AtPresentation ? GateDecision.Allow : GateDecision.Deny("not now"));
            _rig.Queue.AddGate(gate, CancellationToken.None);

            var id = _rig.Enqueue("a", 4, "promo");

            Assert.That(id, Is.EqualTo(_rig.Navigator.LastReservedId), "The denied item still has a reserved id.");
            Assert.That(_rig.Observer.AllKinds(), Is.EqualTo(new[] { ViewEventKind.NotShown }), "No Enqueued for an item that never entered the queue.");
            var notShown = _rig.Observer.Events[0];
            Assert.That(notShown.RequestId, Is.EqualTo(id));
            Assert.That(notShown.Rejected, Is.EqualTo(RejectReason.Denied));
            Assert.That(notShown.Detail, Is.EqualTo("not now"), "The gate's reason is the detail.");
            Assert.That(notShown.Priority, Is.EqualTo(4));
            Assert.That(notShown.Source, Is.EqualTo("promo"));
            Assert.That(_rig.Queue.Count, Is.Zero);

            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Navigator.Calls, Is.Empty);
        }

        [Test]
        public void EnqueueAsync_DeniedAtEnqueue_CompletesOnceAsNotShownDenied()
        {
            _rig.Queue.AddGate(new ScriptedGate(decide: r => GateDecision.Deny("closed")), CancellationToken.None);

            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create<string, bool>("ask"), "Delete?", CancellationToken.None));

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "I4: the outcome is data.");
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.NotShown));
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Denied));
            Assert.That(probe.Result.Detail, Is.EqualTo("closed"));
            Assert.That(probe.Result.RequestId, Is.EqualTo(_rig.Navigator.LastReservedId));
        }

        [Test]
        public void Gates_AreAskedAtEnqueue_AndAgainRightBeforePresentation_AboutTheItem()
        {
            var gate = new ScriptedGate();
            _rig.Queue.AddGate(gate, CancellationToken.None);

            var id = _rig.Enqueue("a", 8, "src");
            Assert.That(gate.Requests.Count, Is.EqualTo(1));
            var atEnqueue = gate.Requests[0];
            Assert.That(atEnqueue.AtPresentation, Is.False);
            Assert.That(atEnqueue.RequestId, Is.EqualTo(id));
            Assert.That(atEnqueue.ViewId, Is.EqualTo("a"));
            Assert.That(atEnqueue.Layer, Is.SameAs(ViewLayers.Popups));
            Assert.That(atEnqueue.Priority, Is.EqualTo(8));
            Assert.That(atEnqueue.Source, Is.EqualTo("src"));

            _rig.Ticker.Tick();

            Assert.That(gate.Requests.Count, Is.EqualTo(2), "Gates run twice.");
            Assert.That(gate.Requests[1].AtPresentation, Is.True);
            Assert.That(gate.Requests[1].RequestId, Is.EqualTo(id));
            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public void AGateDenyAtPresentation_EndsThatItem_AndTheWalkPresentsTheNext()
        {
            var gate = new ScriptedGate(decide: r => r.AtPresentation && r.ViewId == "head" ? GateDecision.Deny("blocked") : GateDecision.Allow);
            _rig.Queue.AddGate(gate, CancellationToken.None);
            var head = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("head"), default(NoArgs), CancellationToken.None, new QueueOptions { Priority = 10 }));
            var nextId = _rig.Enqueue("next", 1);

            _rig.Ticker.Tick();

            Assert.That(head.ResumeCount, Is.EqualTo(1));
            Assert.That(head.Result.Rejected, Is.EqualTo(RejectReason.Denied));
            Assert.That(head.Result.Detail, Is.EqualTo("blocked"));
            Assert.That(_rig.Observer.KindsFor(head.Result.RequestId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1), "A denied head does not block; the walk continues in the same pump.");
            Assert.That(_rig.Navigator.Calls[0].RequestId, Is.EqualTo(nextId));
        }

        [Test]
        public void Gates_RunInAscendingOrder()
        {
            var log = new List<string>();
            _rig.Queue.AddGate(new ScriptedGate("five", log), CancellationToken.None, 5);
            _rig.Queue.AddGate(new ScriptedGate("minus", log), CancellationToken.None, -1);
            _rig.Queue.AddGate(new ScriptedGate("zero", log), CancellationToken.None);

            _rig.Enqueue("a");

            Assert.That(log, Is.EqualTo(new[] { "minus", "zero", "five" }), "Gates run in ascending order.");
        }

        [Test]
        public void TheFirstDeny_EndsTheItem_AndLaterGatesAreNotAsked()
        {
            var log = new List<string>();
            _rig.Queue.AddGate(new ScriptedGate("first", log), CancellationToken.None, 0);
            _rig.Queue.AddGate(new ScriptedGate("denier", log, r => GateDecision.Deny("denier says no")), CancellationToken.None, 1);
            _rig.Queue.AddGate(new ScriptedGate("last", log, r => GateDecision.Deny("last says no")), CancellationToken.None, 2);

            _rig.Enqueue("a");

            Assert.That(log, Is.EqualTo(new[] { "first", "denier" }));
            Assert.That(_rig.Observer.LastOf(ViewEventKind.NotShown).Detail, Is.EqualTo("denier says no"));
        }

        [Test]
        public void AThrowingGate_IsRouted_LogsUIDIRECTOR119_AndCountsAsDeny()
        {
            var gate = new ScriptedGate { Throw = new InvalidOperationException("gate broke") };
            _rig.Queue.AddGate(gate, CancellationToken.None);

            var id = _rig.Enqueue("a");

            Assert.That(_rig.Routed.Count, Is.EqualTo(1), "The exception goes to the game's handler.");
            Assert.That(_rig.Routed[0].Source, Is.EqualTo(ViewErrorSource.Gate));
            Assert.That(_rig.Routed[0].RequestId, Is.EqualTo(id));
            Assert.That(_rig.Routed[0].ViewId, Is.EqualTo("a"));
            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR119"), Is.EqualTo(1));
            Assert.That(_rig.Observer.AllKinds(), Is.EqualTo(new[] { ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.Events[0].Rejected, Is.EqualTo(RejectReason.Denied), "A throwing gate counts as Deny.");
        }

        [Test]
        public void AGateThatThrowsAtPresentation_DeniesThatItem_AndTheNextIsPresented()
        {
            var gate = new ScriptedGate(decide: r =>
            {
                if (r.AtPresentation && r.ViewId == "head")
                    throw new InvalidOperationException("gate broke");
                return GateDecision.Allow;
            });
            _rig.Queue.AddGate(gate, CancellationToken.None);
            var headId = _rig.Enqueue("head", 10);
            _rig.Enqueue("next", 1);

            _rig.Ticker.Tick();

            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR119"), Is.EqualTo(1));
            Assert.That(_rig.Routed.Count, Is.EqualTo(1));
            Assert.That(_rig.Observer.KindsFor(headId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.EventsFor(headId)[1].Rejected, Is.EqualTo(RejectReason.Denied));
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "next" }));
        }

        [Test]
        public void AGateRemovedOrEndedByItsOwner_IsNoLongerAsked_AndItsRemovalSchedulesAPump()
        {
            var denier = new ScriptedGate(decide: r => r.AtPresentation ? GateDecision.Deny("wait") : GateDecision.Allow);
            var owner = _rig.NewOwner();
            var registration = _rig.Queue.AddGate(denier, owner.Token);
            var held = _rig.Queue.Hold("keep waiting", CancellationToken.None);
            _rig.Enqueue("a");
            _rig.Ticker.TickUntilIdle();

            owner.Cancel();

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_rig.Ticker.PendingTicks, Is.GreaterThan(0), "A removed gate is a trigger.");

            var askedBefore = denier.Requests.Count;
            held.Remove();
            _rig.Ticker.TickUntilIdle();

            Assert.That(denier.Requests.Count, Is.EqualTo(askedBefore), "An ended gate is not asked.");
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void AGateRegistrationRemoved_IsNoLongerAsked_AndItsRemovalSchedulesAPump()
        {
            var denier = new ScriptedGate(decide: r => r.AtPresentation ? GateDecision.Deny("wait") : GateDecision.Allow);
            var registration = _rig.Queue.AddGate(denier, CancellationToken.None);
            var held = _rig.Queue.Hold("keep waiting", CancellationToken.None);
            _rig.Enqueue("a");
            _rig.Ticker.TickUntilIdle();

            registration.Remove();

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_rig.Ticker.PendingTicks, Is.GreaterThan(0), "A removed gate is a trigger.");

            var askedBefore = denier.Requests.Count;
            held.Remove();
            _rig.Ticker.TickUntilIdle();

            Assert.That(denier.Requests.Count, Is.EqualTo(askedBefore), "A removed gate is not asked.");
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void AddGate_SchedulesAPump()
        {
            _rig.Navigator.IsBusy = true;
            _rig.Enqueue("a");
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Ticker.PendingTicks, Is.Zero);

            _rig.Queue.AddGate(new ScriptedGate(), CancellationToken.None);

            Assert.That(_rig.Ticker.PendingTicks, Is.GreaterThan(0), "A gate added is a trigger.");
        }

        [Test]
        public void Gates_WithTheSameOrder_RunInAddOrder_AtEnqueueAndAtPresentation()
        {
            var log = new List<string>();
            _rig.Queue.AddGate(new ScriptedGate("last", log), CancellationToken.None, 3);
            _rig.Queue.AddGate(new ScriptedGate("a", log), CancellationToken.None, 0);
            _rig.Queue.AddGate(new ScriptedGate("first", log), CancellationToken.None, -1);
            _rig.Queue.AddGate(new ScriptedGate("b", log), CancellationToken.None, 0);
            _rig.Queue.AddGate(new ScriptedGate("c", log), CancellationToken.None, 0);
            var expected = new[] { "first", "a", "b", "c", "last" };

            _rig.Enqueue("item");

            Assert.That(log, Is.EqualTo(expected), "Gates run in ascending order; ties keep add order.");

            log.Clear();
            _rig.Ticker.Tick();

            Assert.That(log, Is.EqualTo(expected), "Gates run in the same order right before presentation.");
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "item" }));
        }

        [Test]
        public void AThrowingGate_StopsLaterGates_AndItsNotShownDetailNamesIt()
        {
            var log = new List<string>();
            _rig.Queue.AddGate(new ScriptedGate("before", log), CancellationToken.None, 0);
            _rig.Queue.AddGate(new ExplodingGate(log), CancellationToken.None, 1);
            var after = new ScriptedGate("after", log);
            _rig.Queue.AddGate(after, CancellationToken.None, 2);

            _rig.Enqueue("a");

            Assert.That(log, Is.EqualTo(new[] { "before", nameof(ExplodingGate) }), "A throwing gate counts as the first Deny.");
            Assert.That(after.Requests, Is.Empty, "Later gates are not asked.");
            Assert.That(_rig.Routed.Count, Is.EqualTo(1));
            var notShown = _rig.Observer.LastOf(ViewEventKind.NotShown);
            Assert.That(notShown.Rejected, Is.EqualTo(RejectReason.Denied));
            Assert.That(notShown.Detail, Does.Contain(nameof(ExplodingGate)), "The detail names the throwing gate.");
        }

        [Test]
        public void AGateReturningDefault_LetsTheItemThrough()
        {
            var gate = new ScriptedGate(decide: r => default(GateDecision));
            _rig.Queue.AddGate(gate, CancellationToken.None);

            var id = _rig.Enqueue("a");

            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "default(GateDecision) at enqueue lets the item in.");

            _rig.Ticker.Tick();

            Assert.That(gate.Requests.Count, Is.EqualTo(2), "The gate was asked at enqueue and at presentation.");
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }), "default(GateDecision) at presentation lets the item be presented.");
            Assert.That(_rig.Observer.CountOf(ViewEventKind.NotShown), Is.Zero, "No item ends as NotShown.");
        }

        // Item owner

        [Test]
        public void AnOwnerEndingWhileTheItemWaits_EndsItAsNotShownOwnerEnded()
        {
            var owner = _rig.NewOwner();
            _rig.Navigator.IsBusy = true;
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), owner.Token));
            var id = _rig.Navigator.LastReservedId;
            _rig.Ticker.TickUntilIdle();

            owner.Cancel();

            Assert.That(_rig.Queue.Count, Is.Zero);
            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.OwnerEnded));
            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.OwnerEnded));

            _rig.Navigator.IsBusy = false;
            _rig.Navigator.PublishSettled();
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Navigator.Calls, Is.Empty);
        }

        [Test]
        public void AnOwnerAlreadyEndedAtEnqueue_EndsTheItemAsNotShownOwnerEnded_AndNothingIsPresented()
        {
            var owner = _rig.NewOwner();
            owner.Cancel();

            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), owner.Token));

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.OwnerEnded));
            Assert.That(_rig.Observer.LastOf(ViewEventKind.NotShown).Rejected, Is.EqualTo(RejectReason.OwnerEnded));
            Assert.That(_rig.Queue.Count, Is.Zero);

            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Navigator.Calls, Is.Empty);
        }

        [Test]
        public void AnOwnerEndingAfterPresentation_IsLeftToTheNavigator()
        {
            var owner = _rig.NewOwner();
            var id = _rig.Enqueue("a", 0, null, owner.Token);
            _rig.Ticker.Tick();

            owner.Cancel();

            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "The navigator reports the end of a presented item.");
            Assert.That(_rig.Navigator.LastCall.IsCompleted, Is.False, "The queue does not end a presented request.");
        }

        [Test]
        public void AnOwnerCancelledAfterItsItemEnded_EndsNothingASecondTime()
        {
            var gate = new ScriptedGate(decide: r =>
                (r.AtPresentation && r.ViewId == "deniedLater") || (!r.AtPresentation && r.ViewId == "deniedAtEnqueue")
                    ? GateDecision.Deny("no")
                    : GateDecision.Allow);
            _rig.Queue.AddGate(gate, CancellationToken.None);
            var shownOwner = _rig.NewOwner();
            var deniedLaterOwner = _rig.NewOwner();
            var clearedOwner = _rig.NewOwner();
            var deniedAtEnqueueOwner = _rig.NewOwner();

            var shown = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("shown"), default(NoArgs), shownOwner.Token, new QueueOptions { Priority = 5 }));
            var shownId = _rig.Navigator.LastReservedId;
            var deniedLater = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("deniedLater"), default(NoArgs), deniedLaterOwner.Token, new QueueOptions { Priority = 10 }));
            var deniedLaterId = _rig.Navigator.LastReservedId;
            var cleared = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("cleared"), default(NoArgs), clearedOwner.Token));
            var clearedId = _rig.Navigator.LastReservedId;
            var deniedAtEnqueue = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("deniedAtEnqueue"), default(NoArgs), deniedAtEnqueueOwner.Token));
            var deniedAtEnqueueId = _rig.Navigator.LastReservedId;

            // deniedLater is denied at presentation, shown is presented, closed, and cleared is cleared.
            _rig.Ticker.Tick();
            _rig.Navigator.LastCall.Close(CloseReason.Dismissed);
            _rig.Queue.Clear();

            shownOwner.Cancel();
            deniedLaterOwner.Cancel();
            clearedOwner.Cancel();
            deniedAtEnqueueOwner.Cancel();
            _rig.Ticker.TickUntilIdle();

            Assert.That(QueueNotShownCount(shownId), Is.Zero, "The navigator ended the presented item.");
            Assert.That(QueueNotShownCount(deniedLaterId), Is.EqualTo(1), "Q3: the denied item ends once.");
            Assert.That(QueueNotShownCount(clearedId), Is.EqualTo(1), "Q3: the cleared item ends once.");
            Assert.That(QueueNotShownCount(deniedAtEnqueueId), Is.EqualTo(1), "Q3: the item denied at enqueue ends once.");
            Assert.That(shown.ResumeCount, Is.EqualTo(1), "I4");
            Assert.That(deniedLater.ResumeCount, Is.EqualTo(1), "I4");
            Assert.That(cleared.ResumeCount, Is.EqualTo(1), "I4");
            Assert.That(deniedAtEnqueue.ResumeCount, Is.EqualTo(1), "I4");
            Assert.That(shown.Result.WasShown, Is.True);
            Assert.That(deniedLater.Result.Rejected, Is.EqualTo(RejectReason.Denied), "The first end stands.");
            Assert.That(cleared.Result.Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(deniedAtEnqueue.Result.Rejected, Is.EqualTo(RejectReason.Denied));
            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1));
        }

        // Clear

        [Test]
        public void Clear_EndsEveryWaitingItemAsNotShownCancelled_AndLeavesThePresentingOne()
        {
            var presented = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("shown"), default(NoArgs), CancellationToken.None, new QueueOptions { Priority = 9 }));
            _rig.Ticker.Tick();
            var b = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("b"), default(NoArgs), CancellationToken.None));
            var bId = _rig.Navigator.LastReservedId;
            var cId = _rig.Enqueue("c");

            _rig.Queue.Clear();

            Assert.That(_rig.Queue.Count, Is.Zero);
            Assert.That(_rig.Observer.KindsFor(bId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.KindsFor(cId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.EventsFor(cId)[1].Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(b.ResumeCount, Is.EqualTo(1));
            Assert.That(b.Result.Rejected, Is.EqualTo(RejectReason.Cancelled));

            Assert.That(presented.IsPending, Is.True, "Clear leaves the presenting item to the navigator.");
            Assert.That(_rig.Navigator.LastCall.IsCompleted, Is.False);
            Assert.That(_rig.Navigator.CloseAllCount, Is.Zero);

            _rig.Navigator.LastCall.Close(CloseReason.Dismissed);
            Assert.That(presented.ResumeCount, Is.EqualTo(1));
            Assert.That(presented.Result.WasShown, Is.True);
        }

        [Test]
        public void TheQueue_KeepsWorking_AfterClear()
        {
            _rig.Enqueue("a");
            _rig.Queue.Clear();
            _rig.Ticker.TickUntilIdle();

            _rig.Enqueue("b");
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "b" }));
        }

        [Test]
        public void Clear_LatchesEveryWaitingItemFirst_AndAnItemEnqueuedWhileItReportsSurvives()
        {
            var aId = _rig.Enqueue("a");
            var bId = _rig.Enqueue("b");
            var cId = _rig.Enqueue("c");
            var countsSeen = new List<int>();
            var lateId = 0;
            _rig.Queue.AddObserver(new QueueActionObserver(e =>
            {
                if (e.Kind != ViewEventKind.NotShown || lateId != 0)
                    return;

                countsSeen.Add(_rig.Queue.Count);
                lateId = _rig.Enqueue("late");
            }), CancellationToken.None);

            _rig.Queue.Clear();

            Assert.That(countsSeen, Is.EqualTo(new[] { 0 }), "Every waiting item is latched before the first one is reported.");
            Assert.That(_rig.Observer.KindsFor(aId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.KindsFor(bId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.KindsFor(cId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.EventsFor(cId)[1].Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(_rig.Observer.KindsFor(lateId), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "An item enqueued while Clear reports is not cleared.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(1));

            _rig.Ticker.Tick();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1));
            Assert.That(_rig.Navigator.Calls[0].RequestId, Is.EqualTo(lateId), "It is presented on the next tick.");
        }

        // Lifetime

        [Test]
        public void LifetimeEnd_EndsWaitingItemsAsCancelled_EndsRegistrations_AndLeavesThePresentedView()
        {
            var presented = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("shown"), default(NoArgs), CancellationToken.None, new QueueOptions { Priority = 9 }));
            _rig.Ticker.Tick();
            var gate = _rig.Queue.AddGate(new ScriptedGate(), CancellationToken.None);
            var hold = _rig.Queue.Hold("flow", CancellationToken.None);
            var waiting = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("b"), default(NoArgs), CancellationToken.None));
            var waitingId = _rig.Navigator.LastReservedId;

            _rig.EndLifetime();

            Assert.That(waiting.ResumeCount, Is.EqualTo(1));
            Assert.That(waiting.Result.Rejected, Is.EqualTo(RejectReason.Cancelled), "Waiting items end as NotShown / Cancelled.");
            Assert.That(_rig.Observer.KindsFor(waitingId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Queue.Count, Is.Zero);

            Assert.That(gate.IsActive, Is.False, "Registrations end with the queue's lifetime.");
            Assert.That(hold.IsActive, Is.False);
            Assert.That(_rig.ObserverRegistration.IsActive, Is.False);
            Assert.That(_rig.Queue.IsHeld, Is.False);
            Assert.That(_rig.Navigator.ObserverCount, Is.Zero, "The queue's navigator observer ends with the lifetime.");

            Assert.That(presented.IsPending, Is.True, "A presented view is left to its own owner.");
            Assert.That(_rig.Navigator.LastCall.IsCompleted, Is.False);
            Assert.That(_rig.Navigator.CloseAllCount, Is.Zero);

            _rig.Navigator.LastCall.Close(CloseReason.Dismissed);
            Assert.That(presented.ResumeCount, Is.EqualTo(1), "The presented item's awaiter still gets the navigator's result.");
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public void AfterTheLifetime_CallsAreInert()
        {
            _rig.EndLifetime();
            _rig.Ticker.TickUntilIdle();
            var reservedBefore = _rig.Navigator.ReservedCount;

            var id = _rig.Enqueue("a");
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("b"), default(NoArgs), CancellationToken.None));
            var hold = _rig.Queue.Hold("late", CancellationToken.None);
            var gate = _rig.Queue.AddGate(new ScriptedGate(), CancellationToken.None);
            var observer = _rig.Queue.AddObserver(new RecordingObserver(), CancellationToken.None);

            Assert.That(id, Is.EqualTo(reservedBefore + 1), "After the lifetime ended, Enqueue still returns a reserved id.");
            Assert.That(_rig.Navigator.ReservedCount, Is.EqualTo(reservedBefore + 2));
            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Cancelled), "After the lifetime ended, EnqueueAsync completes as NotShown / Cancelled.");
            Assert.That(hold.IsActive, Is.False, "After the lifetime ended, a hold is an empty registration.");
            Assert.That(gate.IsActive, Is.False);
            Assert.That(observer.IsActive, Is.False);
            Assert.That(_rig.Queue.IsHeld, Is.False);
            Assert.That(_rig.Queue.Count, Is.Zero);

            _rig.Navigator.PublishSettled();
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Navigator.Calls, Is.Empty);
            Assert.That(() => _rig.Queue.Clear(), Throws.Nothing);
        }

        [Test]
        public void AQueueCreatedWithAnEndedLifetime_IsInertFromTheStart()
        {
            var navigator = new FakeNavigator();
            var ticker = new ManualTicker();
            var diagnostics = new RecordingDiagnostics();
            var queue = new ViewQueue(
                navigator,
                ViewLayers.Popups,
                new CancellationToken(true),
                "ended",
                ticker,
                new FakeMainThread(),
                new ErrorRouter(null, diagnostics.Diagnostics),
                diagnostics.Diagnostics);

            var probe = _rig.Watch(queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            ticker.TickUntilIdle();

            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(navigator.Calls, Is.Empty);
            Assert.That(navigator.ObserverCount, Is.Zero);
        }

        [Test]
        public void RootEnd_WithAnIndependentLifetime_EndsWaitingItemsAsNotShownRootGone()
        {
            _rig.Navigator.IsBusy = true;
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;
            _rig.Ticker.TickUntilIdle();

            _rig.Navigator.CancelRoot();

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The waiting item ends once when the root ends.");
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.RootGone), "A root end ends waiting items as NotShown / RootGone.");
            Assert.That(probe.Result.Detail, Is.EqualTo("The root was destroyed."));
            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.RootGone));
            Assert.That(_rig.Queue.Count, Is.Zero);
        }

        [Test]
        public void RootEnd_WithAnIndependentLifetime_EndsEveryRegistration()
        {
            var gate = _rig.Queue.AddGate(new ScriptedGate(), CancellationToken.None);
            var hold = _rig.Queue.Hold("flow", CancellationToken.None);
            Assert.That(_rig.Navigator.ObserverCount, Is.EqualTo(1), "Precondition: the queue observes the navigator.");
            Assert.That(gate.IsActive, Is.True, "Precondition: the gate is registered.");
            Assert.That(hold.IsActive, Is.True, "Precondition: the hold is registered.");

            _rig.Navigator.CancelRoot();

            Assert.That(gate.IsActive, Is.False, "Registrations end with the navigator's root.");
            Assert.That(hold.IsActive, Is.False);
            Assert.That(_rig.ObserverRegistration.IsActive, Is.False);
            Assert.That(_rig.Queue.IsHeld, Is.False);
            Assert.That(_rig.Navigator.ObserverCount, Is.Zero, "The queue's navigator observer ends with the root.");
        }

        [Test]
        public void RootEnd_LeavesThePresentedItemToTheNavigator()
        {
            var presented = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("shown"), default(NoArgs), CancellationToken.None, new QueueOptions { Priority = 9 }));
            var presentedId = _rig.Navigator.LastReservedId;
            _rig.Ticker.Tick();
            var waitingId = _rig.Enqueue("waiting");

            _rig.Navigator.CancelRoot();

            Assert.That(presented.IsPending, Is.True, "The queue does not end a presented item when the root ends.");
            Assert.That(_rig.Navigator.LastCall.IsCompleted, Is.False);
            Assert.That(_rig.Observer.KindsFor(presentedId), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "The queue publishes no NotShown for the presented item.");
            Assert.That(_rig.Observer.KindsFor(waitingId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }), "The waiting item ends when the root ends.");
            Assert.That(_rig.Observer.EventsFor(waitingId)[1].Rejected, Is.EqualTo(RejectReason.RootGone));
            Assert.That(_rig.Queue.Count, Is.Zero);

            _rig.Navigator.LastCall.Close(CloseReason.Destroyed);

            Assert.That(presented.ResumeCount, Is.EqualTo(1), "The presented item's awaiter resumes once, through the navigator.");
            Assert.That(presented.Result.Reason, Is.EqualTo(CloseReason.Destroyed), "The awaiter gets the navigator's result.");
        }

        [Test]
        public void Enqueue_AfterRootEnd_CompletesAtOnceAsNotShownRootGone()
        {
            _rig.Navigator.CancelRoot();

            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "After the root ended, EnqueueAsync completes in the call.");
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.RootGone));
            Assert.That(probe.Result.Detail, Is.EqualTo("The root was destroyed."));

            _rig.Navigator.PublishSettled();
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Navigator.Calls, Is.Empty, "Nothing is presented after the root ended.");
        }

        [Test]
        public void Enqueue_WhenAGateEndsTheRoot_CompletesOnceAsNotShownRootGone()
        {
            _rig.Queue.AddGate(new ScriptedGate(decide: r =>
            {
                _rig.Navigator.CancelRoot();
                return GateDecision.Allow;
            }), CancellationToken.None);

            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "An item whose gate ended the root completes once, in the call.");
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.RootGone), "The end reason follows the root token's state.");
            Assert.That(probe.Result.Detail, Is.EqualTo("The root was destroyed."));
            Assert.That(_rig.Observer.KindsFor(id), Has.No.Member(ViewEventKind.Enqueued), "The item never entered the queue.");
            Assert.That(_rig.Queue.Count, Is.Zero);

            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Navigator.Calls, Is.Empty, "Nothing is presented after the root ended.");
        }

        [Test]
        public void Enqueue_WhenAGateEndsTheLifetime_CompletesOnceAsNotShownCancelled()
        {
            _rig.Queue.AddGate(new ScriptedGate(decide: r =>
            {
                _rig.EndLifetime();
                return GateDecision.Allow;
            }), CancellationToken.None);

            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "An item whose gate ended the lifetime completes once, in the call.");
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Cancelled), "With the root alive, the end reason is Cancelled.");
            Assert.That(_rig.Observer.KindsFor(id), Has.No.Member(ViewEventKind.Enqueued), "The item never entered the queue.");
            Assert.That(_rig.Queue.Count, Is.Zero);

            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Navigator.Calls, Is.Empty, "Nothing is presented after the lifetime ended.");
        }

        [Test]
        public void Constructor_OnAnEndedLifetimeAndAnEndedRoot_GivesAnInertQueueThatRejectsRootGone()
        {
            var navigator = new FakeNavigator();
            navigator.CancelRoot();
            var ticker = new ManualTicker();
            var diagnostics = new RecordingDiagnostics();
            var queue = new ViewQueue(
                navigator,
                ViewLayers.Popups,
                new CancellationToken(true),
                "ended",
                ticker,
                new FakeMainThread(),
                new ErrorRouter(null, diagnostics.Diagnostics),
                diagnostics.Diagnostics);

            var probe = _rig.Watch(queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            ticker.TickUntilIdle();

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.RootGone), "With both tokens ended, the root decides the reason.");
            Assert.That(probe.Result.Detail, Is.EqualTo("The root was destroyed."));
            Assert.That(navigator.ObserverCount, Is.Zero, "An inert queue registers nothing with the navigator.");
            Assert.That(navigator.Calls, Is.Empty);
        }

        [Test]
        public void Constructor_OnANavigatorWhoseRootEnded_GivesAnInertQueue()
        {
            var navigator = new FakeNavigator();
            navigator.CancelRoot();
            var ticker = new ManualTicker();
            var diagnostics = new RecordingDiagnostics();

            using (var lifetime = new CancellationTokenSource())
            {
                var queue = new ViewQueue(
                    navigator,
                    ViewLayers.Popups,
                    lifetime.Token,
                    "late",
                    ticker,
                    new FakeMainThread(),
                    new ErrorRouter(null, diagnostics.Diagnostics),
                    diagnostics.Diagnostics);

                var probe = _rig.Watch(queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
                ticker.TickUntilIdle();

                Assert.That(probe.ResumeCount, Is.EqualTo(1));
                Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.RootGone), "A queue built after the root ended is inert from the start.");
                Assert.That(navigator.ObserverCount, Is.Zero, "An inert queue registers nothing with the navigator.");
                Assert.That(navigator.Calls, Is.Empty);
            }
        }

        [Test]
        public void LifetimeEnd_WhenTheLifetimeIsTheRootToken_EndsWaitingItemsAsRootGone()
        {
            var rig = new QueueRig(true);
            try
            {
                rig.Navigator.IsBusy = true;
                var probe = rig.Watch(rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));

                rig.EndLifetime();

                Assert.That(probe.ResumeCount, Is.EqualTo(1));
                Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.RootGone), "A queue whose lifetime is the root token ends its waiting items as RootGone.");
                Assert.That(probe.Result.Detail, Is.EqualTo("The root was destroyed."));
            }
            finally
            {
                rig.Dispose();
            }
        }

        [Test]
        public void LifetimeEnd_WhenTheLifetimeIsLinkedToTheRootToken_EndsWaitingItemsAsRootGone()
        {
            var navigator = new FakeNavigator();
            var linked = CancellationTokenSource.CreateLinkedTokenSource(navigator.RootToken);
            try
            {
                var diagnostics = new RecordingDiagnostics();
                var queue = new ViewQueue(
                    navigator,
                    ViewLayers.Popups,
                    linked.Token,
                    "linked",
                    new ManualTicker(),
                    new FakeMainThread(),
                    new ErrorRouter(null, diagnostics.Diagnostics),
                    diagnostics.Diagnostics);
                navigator.IsBusy = true;
                var probe = _rig.Watch(queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));

                navigator.CancelRoot();

                Assert.That(probe.ResumeCount, Is.EqualTo(1), "The waiting item ends once, whichever callback runs first.");
                Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.RootGone), "A lifetime linked to the root token ends waiting items as RootGone.");
                Assert.That(probe.Result.Detail, Is.EqualTo("The root was destroyed."));
                Assert.That(queue.Count, Is.Zero);
            }
            finally
            {
                linked.Dispose();
            }
        }

        // Covers the inert guard only; the DropsEveryCallbackIntoTheQueue tests witness the disposal.
        [Test]
        public void LifetimeEnd_ThenRootEnd_IsANoOp()
        {
            _rig.Navigator.IsBusy = true;
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            _rig.EndLifetime();
            var eventsBefore = _rig.Observer.Count;
            var diagnosticsBefore = _rig.Diagnostics.Total;

            _rig.Navigator.CancelRoot();

            Assert.That(_rig.Observer.Count, Is.EqualTo(eventsBefore), "A root end after the lifetime end publishes no queue event.");
            Assert.That(_rig.Routed, Is.Empty, "No error is routed.");
            Assert.That(_rig.Diagnostics.Total, Is.EqualTo(diagnosticsBefore), "No diagnostic is logged. " + _rig.Diagnostics.Describe());
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The waiting item ended once.");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Cancelled), "The first end stands.");
        }

        [Test]
        public void LifetimeEnd_DropsEveryCallbackIntoTheQueue()
        {
            var navigator = new FakeNavigator();
            using (var lifetime = new CancellationTokenSource())
            {
                var queue = NewQueue(navigator, lifetime.Token);
                Assume.That(queue.HasEndRegistrations, Is.True, "control: both tokens hold a callback while the queue lives");
                Assume.That(navigator.ObserverCount, Is.EqualTo(1), "control: the queue observes its navigator");

                lifetime.Cancel();

                Assert.That(queue.HasEndRegistrations, Is.False, "Neither the root token nor the lifetime keeps a callback into an ended queue.");
                Assert.That(navigator.ObserverCount, Is.Zero, "The navigator no longer holds the queue's observer.");
            }
        }

        [Test]
        public void RootEnd_DropsEveryCallbackIntoTheQueue()
        {
            var navigator = new FakeNavigator();
            using (var lifetime = new CancellationTokenSource())
            {
                var queue = NewQueue(navigator, lifetime.Token);
                Assume.That(queue.HasEndRegistrations, Is.True, "control: both tokens hold a callback while the queue lives");

                navigator.CancelRoot();

                Assert.That(queue.HasEndRegistrations, Is.False, "Neither the lifetime nor the root token keeps a callback into an ended queue.");
                Assert.That(navigator.ObserverCount, Is.Zero, "The navigator observer bound to the lifetime is removed.");
            }
        }

        // The Editor's conservative stack scan can keep a dead queue alive, so these run by hand only.
        [Test, Explicit("Garbage collection witness; conservative stack scanning makes it layout dependent.")]
        public void LifetimeEnd_ReleasesTheQueueFromTheRootToken()
        {
            var navigator = new FakeNavigator();

            var queue = BuildQueueAndEndItsLifetime(navigator);
            CollectGarbage(queue);

            Assert.That(queue.IsAlive, Is.False, "A queue whose lifetime ended is not kept alive by the navigator's root token.");
            GC.KeepAlive(navigator);
        }

        [Test, Explicit("Garbage collection witness; conservative stack scanning makes it layout dependent.")]
        public void RootEnd_ReleasesTheQueueFromTheLifetimeToken()
        {
            using (var lifetime = new CancellationTokenSource())
            {
                var queue = BuildQueueAndEndItsRoot(lifetime.Token);
                CollectGarbage(queue);

                Assert.That(queue.IsAlive, Is.False, "A queue whose root ended is not kept alive by its lifetime token.");
                GC.KeepAlive(lifetime);
            }
        }

        [Test, Explicit("Control for the garbage collection witnesses.")]
        public void AQueueWhoseLifetimeHasNotEnded_IsKeptAliveByTheRootToken()
        {
            CancellationToken rootToken;
            var queue = BuildQueueReachableOnlyFromTheRootToken(out rootToken);
            CollectGarbage(queue);

            Assert.That(queue.IsAlive, Is.True, "A live queue is kept alive by its registration on the root token.");
            GC.KeepAlive(rootToken);
        }

        [Test, Explicit("Control for the garbage collection witnesses.")]
        public void AQueueWhoseRootHasNotEnded_IsKeptAliveByTheLifetimeToken()
        {
            using (var lifetime = new CancellationTokenSource())
            {
                var queue = BuildQueueReachableOnlyFromTheLifetime(lifetime.Token);
                CollectGarbage(queue);

                Assert.That(queue.IsAlive, Is.True, "A live queue is kept alive by its registration on the lifetime token.");
                GC.KeepAlive(lifetime);
            }
        }

        // Every item ends once

        [Test]
        public void EveryEnqueuedItem_EndsExactlyOnce_OnEveryPath()
        {
            var denyAtPresentation = new ScriptedGate(decide: r => r.AtPresentation && r.ViewId == "denied" ? GateDecision.Deny("no") : GateDecision.Allow);
            _rig.Queue.AddGate(denyAtPresentation, CancellationToken.None);
            var owner = _rig.NewOwner();

            var probes = new Dictionary<int, ResultProbe<NoResult>>();
            void Add(string viewId, int priority, CancellationToken itemOwner)
            {
                var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create(viewId), default(NoArgs), itemOwner, new QueueOptions { Priority = priority }));
                probes.Add(_rig.Navigator.LastReservedId, probe);
            }

            Add("shown1", 9, CancellationToken.None);
            Add("denied", 8, CancellationToken.None);
            Add("shown2", 7, CancellationToken.None);
            Add("owned", 1, owner.Token);
            Add("cleared", 0, CancellationToken.None);

            _rig.Ticker.Tick();
            _rig.Navigator.LastCall.Close(CloseReason.Dismissed);
            _rig.Ticker.Tick();
            owner.Cancel();
            _rig.Navigator.LastCall.NotShown(RejectReason.Faulted);
            _rig.Queue.Clear();
            _rig.EndLifetime();
            _rig.Ticker.TickUntilIdle();

            foreach (var pair in probes)
            {
                var id = pair.Key;
                var queueEnds = 0;
                var kinds = _rig.Observer.KindsFor(id);
                for (var i = 0; i < kinds.Length; i++)
                {
                    if (kinds[i] == ViewEventKind.NotShown)
                        queueEnds++;
                }

                var navigatorEnds = 0;
                for (var i = 0; i < _rig.Navigator.Calls.Count; i++)
                {
                    if (_rig.Navigator.Calls[i].RequestId == id)
                        navigatorEnds++;
                }

                Assert.That(queueEnds + navigatorEnds, Is.EqualTo(1), "Q3: request " + id + " ends exactly once, in the queue or through the navigator.");
                Assert.That(kinds.Length, Is.GreaterThan(0));
                Assert.That(kinds[0], Is.EqualTo(ViewEventKind.Enqueued), "I6: the queue's sequence starts with Enqueued.");
                Assert.That(pair.Value.ResumeCount, Is.EqualTo(1), "I4: request " + id + " completes its awaiter exactly once.");
            }

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(2), "shown1 and shown2 reached the navigator.");
        }

        // Threads

        [Test]
        public void CallsOffTheMainThread_Throw_AndChangeNothing()
        {
            var observer = new RecordingObserver();
            var gate = new ScriptedGate();
            _rig.MainThread.IsMainThread = false;

            Assert.Throws<InvalidOperationException>(() => _rig.Enqueue("a"));
            Assert.Throws<InvalidOperationException>(() => _rig.Queue.EnqueueAsync(ViewKey.Create("b"), default(NoArgs), CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => _rig.Queue.Hold("h", CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => _rig.Queue.AddGate(gate, CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => _rig.Queue.AddObserver(observer, CancellationToken.None));

            _rig.MainThread.IsMainThread = true;
            Assert.That(_rig.Navigator.ReservedCount, Is.Zero, "A refused call reserves no id.");
            Assert.That(_rig.Observer.Count, Is.Zero);
            Assert.That(_rig.Queue.Count, Is.Zero);
            Assert.That(_rig.Queue.IsHeld, Is.False);

            _rig.Enqueue("c");
            Assert.That(gate.Requests, Is.Empty, "The refused gate was not added.");
            Assert.That(observer.Count, Is.Zero, "The refused observer was not added.");
        }

        [Test]
        public void Clear_OffTheMainThread_IsPostedAndEndsWaitingItemsOnlyAfterFlush()
        {
            _rig.Navigator.IsBusy = true;
            var first = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var firstId = _rig.Navigator.LastReservedId;
            var secondId = _rig.Enqueue("b");
            _rig.MainThread.IsMainThread = false;

            _rig.Queue.Clear();

            Assert.That(_rig.MainThread.PendingCount, Is.EqualTo(1), "An off-thread Clear is posted to the main thread.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(2), "Both items still wait before the flush.");
            Assert.That(first.IsPending, Is.True, "No awaiter completes before the flush.");
            Assert.That(_rig.Observer.KindsFor(firstId), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "No NotShown is published before the flush.");
            Assert.That(_rig.Observer.KindsFor(secondId), Is.EqualTo(new[] { ViewEventKind.Enqueued }));

            _rig.MainThread.Flush();
            _rig.MainThread.IsMainThread = true;

            Assert.That(_rig.Queue.Count, Is.Zero);
            Assert.That(first.ResumeCount, Is.EqualTo(1));
            Assert.That(first.Result.Rejected, Is.EqualTo(RejectReason.Cancelled), "After the flush, waiting items end as NotShown / Cancelled.");
            Assert.That(_rig.Observer.KindsFor(firstId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.KindsFor(secondId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.EventsFor(secondId)[1].Rejected, Is.EqualTo(RejectReason.Cancelled));
        }

        [Test]
        public void RootEnd_OffTheMainThread_IsPostedAndEndsOnTheMainThread()
        {
            _rig.Navigator.IsBusy = true;
            var id = _rig.Enqueue("a");

            _rig.MainThread.IsMainThread = false;
            _rig.Navigator.CancelRoot();

            Assert.That(_rig.MainThread.PendingCount, Is.EqualTo(1), "An off-thread root end is posted to the main thread.");
            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR113"), Is.EqualTo(1), "A root token cancelled off the main thread logs UIDIRECTOR113.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(1), "Nothing ends until the main thread runs the post.");

            _rig.MainThread.Flush();
            _rig.MainThread.IsMainThread = true;

            Assert.That(_rig.Queue.Count, Is.Zero);
            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.RootGone));
        }

        [Test]
        public void BothTokensCancelledOffTheMainThread_EndWaitingItemsOnce()
        {
            _rig.Navigator.IsBusy = true;
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;

            _rig.MainThread.IsMainThread = false;
            _rig.EndLifetime();
            _rig.Navigator.CancelRoot();

            Assert.That(_rig.MainThread.PendingCount, Is.EqualTo(2), "Each off-thread token end is posted to the main thread.");
            Assert.That(probe.IsPending, Is.True, "Nothing ends until the main thread runs the posts.");

            _rig.MainThread.Flush();
            _rig.MainThread.IsMainThread = true;

            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }), "The second post ends nothing again.");
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.RootGone), "With the root ended, the reason is RootGone.");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The awaiter resumes once.");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.RootGone));
            Assert.That(_rig.Queue.Count, Is.Zero);
            Assert.That(_rig.Routed, Is.Empty, "No error is routed.");
            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR119"), Is.Zero, _rig.Diagnostics.Describe());
            Assert.That(_rig.Diagnostics.FaultCount, Is.Zero, "No internal fault is reported. " + _rig.Diagnostics.Describe());
        }

        [Test]
        public void AnItemOwnerCancelledOffTheMainThread_IsPosted_WithUIDIRECTOR113()
        {
            _rig.Navigator.IsBusy = true;
            var owner = _rig.NewOwner();
            var id = _rig.Enqueue("a", 0, null, owner.Token);

            _rig.MainThread.IsMainThread = false;
            owner.Cancel();

            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR113"), Is.EqualTo(1));
            Assert.That(_rig.MainThread.PendingCount, Is.EqualTo(1), "An off-thread end is posted to the main thread.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(1));

            _rig.MainThread.Flush();
            _rig.MainThread.IsMainThread = true;

            Assert.That(_rig.Queue.Count, Is.Zero);
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.OwnerEnded));
        }

        [Test]
        public void ALifetimeCancelledOffTheMainThread_IsPosted()
        {
            _rig.Navigator.IsBusy = true;
            var id = _rig.Enqueue("a");

            _rig.MainThread.IsMainThread = false;
            _rig.EndLifetime();

            Assert.That(_rig.MainThread.PendingCount, Is.EqualTo(1), "An off-thread lifetime end is posted to the main thread.");
            Assert.That(_rig.Diagnostics.Count("UIDIRECTOR113"), Is.GreaterThan(0), "An owner callback off the main thread logs UIDIRECTOR113.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(1));

            _rig.MainThread.Flush();
            _rig.MainThread.IsMainThread = true;

            Assert.That(_rig.Queue.Count, Is.Zero);
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.Cancelled));
        }

        // Arguments

        [Test]
        public void NullArguments_ThrowArgumentNullException_AndChangeNothing()
        {
            var key = default(ViewKey<NoArgs, NoResult>);

            Assert.Throws<ArgumentNullException>(() => _rig.Queue.Hold(null, CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => _rig.Queue.AddGate(null, CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => _rig.Queue.AddObserver(null, CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => _rig.Queue.Enqueue(key, default(NoArgs), CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => _rig.Queue.EnqueueAsync(key, default(NoArgs), CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => _rig.Queue.Describe(null));

            Assert.That(_rig.Navigator.ReservedCount, Is.Zero, "No id is reserved.");
            Assert.That(_rig.Observer.Count, Is.Zero, "No event is published.");
            Assert.That(_rig.Queue.Count, Is.Zero);
            Assert.That(_rig.Queue.IsHeld, Is.False);
            Assert.That(_rig.Ticker.PendingTicks, Is.Zero, "No pump is scheduled.");
        }

        [Test]
        public void TheConstructor_WithANullNavigatorOrLayer_ThrowsArgumentNullException()
        {
            var navigator = new FakeNavigator();
            var diagnostics = new RecordingDiagnostics();
            var errors = new ErrorRouter(null, diagnostics.Diagnostics);

            Assert.Throws<ArgumentNullException>(() => new ViewQueue(
                null, ViewLayers.Popups, CancellationToken.None, "q", new ManualTicker(), new FakeMainThread(), errors, diagnostics.Diagnostics));
            Assert.Throws<ArgumentNullException>(() => new ViewQueue(
                navigator, null, CancellationToken.None, "q", new ManualTicker(), new FakeMainThread(), errors, diagnostics.Diagnostics));

            Assert.That(navigator.ObserverCount, Is.Zero, "Nothing was registered with the navigator.");
        }

        // How many NotShown events the queue published for the request id.
        private int QueueNotShownCount(int requestId)
        {
            var count = 0;
            var events = _rig.Observer.EventsFor(requestId);
            for (var i = 0; i < events.Length; i++)
            {
                if (events[i].Kind == ViewEventKind.NotShown)
                    count++;
            }

            return count;
        }

        // The message of the first diagnostic with the id, or null when none was logged.
        private string FirstMessageOf(string id)
        {
            var entries = _rig.Diagnostics.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Id == id)
                    return entries[i].Message;
            }

            return null;
        }

        // A queue over the navigator with test fakes; nothing outside it keeps a reference.
        private static ViewQueue NewQueue(FakeNavigator navigator, CancellationToken lifetime)
        {
            var diagnostics = new Diagnostics();
            return new ViewQueue(
                navigator,
                ViewLayers.Popups,
                lifetime,
                "released",
                new ManualTicker(),
                new FakeMainThread(),
                new ErrorRouter(null, diagnostics),
                diagnostics);
        }

        // Not inlined, so no local of the caller's frame still points at the queue.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference BuildQueueAndEndItsLifetime(FakeNavigator navigator)
        {
            var lifetime = new CancellationTokenSource();
            var queue = NewQueue(navigator, lifetime.Token);
            lifetime.Cancel();
            lifetime.Dispose();
            return new WeakReference(queue);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference BuildQueueAndEndItsRoot(CancellationToken lifetime)
        {
            var navigator = new FakeNavigator();
            var queue = NewQueue(navigator, lifetime);
            navigator.CancelRoot();
            return new WeakReference(queue);
        }

        // The queue's only reference is the root token: no lifetime registration and the navigator is dropped.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference BuildQueueReachableOnlyFromTheRootToken(out CancellationToken rootToken)
        {
            var navigator = new FakeNavigator();
            rootToken = navigator.RootToken;
            return new WeakReference(NewQueue(navigator, CancellationToken.None));
        }

        // The queue's only reference is the lifetime token: the navigator and its root are dropped.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference BuildQueueReachableOnlyFromTheLifetime(CancellationToken lifetime)
        {
            return new WeakReference(NewQueue(new FakeNavigator(), lifetime));
        }

        // Retries, because the Editor's conservative stack scan can keep a dead object alive for a round.
        private static void CollectGarbage(WeakReference target)
        {
            for (var round = 0; round < 5 && target.IsAlive; round++)
            {
                ScrubStack(64);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        // Overwrites stale stack slots with plain integers before a collection.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long ScrubStack(int depth)
        {
            long a = depth;
            long b = depth * 3L;
            long c = depth ^ 0x5A5A;
            long d = depth + 17L;
            if (depth > 0)
                return ScrubStack(depth - 1) + a + b + c + d;

            return a + b + c + d;
        }

        // A gate that always throws; its distinct type name is what the NotShown detail must name.
        private sealed class ExplodingGate : IViewGate
        {
            private readonly List<string> _log;

            public ExplodingGate(List<string> log)
            {
                _log = log;
            }

            public GateDecision Evaluate(in GateRequest request)
            {
                _log.Add(nameof(ExplodingGate));
                throw new InvalidOperationException("The exploding gate broke.");
            }
        }
    }
}
