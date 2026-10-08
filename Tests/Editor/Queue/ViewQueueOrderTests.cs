using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // When and in which order the queue presents.
    public sealed class ViewQueueOrderTests
    {
        private const string PresentFaultedDetail = "The navigator failed while the item was presented.";

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

        // Timing

        [Test]
        public void Enqueue_PresentsNothingBeforeTheNextTick_EvenWhenTheLayerIsClear()
        {
            _rig.Enqueue("a");

            Assert.That(_rig.Navigator.Calls, Is.Empty, "A trigger schedules a pump on the next tick, never presents inline.");
            Assert.That(_rig.Ticker.PendingTicks, Is.GreaterThan(0), "Enqueue schedules a pump.");

            _rig.Ticker.Tick();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void EnqueuesInOneTick_ArePresentedByPriority_NotByCallOrder()
        {
            _rig.Enqueue("low", 0);
            _rig.Enqueue("mid", 5);
            _rig.Enqueue("high", 10);

            _rig.Ticker.Tick();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "high" }), "Same-frame enqueues are ordered by priority.");
        }

        [Test]
        public void Items_ArePresentedByPriority_ThenInEnqueueOrder()
        {
            _rig.Enqueue("a1", 1);
            _rig.Enqueue("b5", 5);
            _rig.Enqueue("c5", 5);
            _rig.Enqueue("d1", 1);
            _rig.Enqueue("e9", 9);

            _rig.DrainPresentingEach();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "e9", "b5", "c5", "a1", "d1" }), "Q4: highest priority first, ties in enqueue order.");
        }

        [Test]
        public void AnItemEnqueuedWhileAnotherIsPresented_WithAHigherPriority_GoesBeforeOlderItems()
        {
            _rig.Enqueue("first", 0);
            _rig.Ticker.Tick();
            _rig.Enqueue("older", 0);
            _rig.Enqueue("urgent", 50);

            _rig.Navigator.LastCall.Close(CloseReason.Dismissed);
            _rig.Ticker.Tick();

            Assert.That(_rig.Navigator.LastCall.ViewId, Is.EqualTo("urgent"), "Q4: among the items present at the pump, the highest priority wins.");
        }

        [Test]
        public void NegativePriorities_GoAfterTheDefault()
        {
            _rig.Enqueue("later", -5);
            _rig.Enqueue("normal");

            _rig.DrainPresentingEach();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "normal", "later" }));
        }

        // What the navigator receives

        [Test]
        public void ShowAsync_GetsTheIdReservedAtEnqueue_TheArguments_TheOwner_AndThePriorityAndSourceInOpenOptions()
        {
            var owner = _rig.NewOwner();
            var key = ViewKey.Create<string, bool>("ask");

            var id = _rig.Queue.Enqueue(key, "Delete?", owner.Token, new QueueOptions { Priority = 7, Source = "inbox" });

            Assert.That(_rig.Navigator.ReservedCount, Is.EqualTo(1), "The id is reserved at enqueue.");
            Assert.That(id, Is.EqualTo(_rig.Navigator.LastReservedId));

            _rig.Ticker.Tick();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1));
            var call = _rig.Navigator.Calls[0];
            Assert.That(call.ViewId, Is.EqualTo("ask"));
            Assert.That(call.Args, Is.EqualTo("Delete?"));
            Assert.That(call.Owner, Is.EqualTo(owner.Token), "The item's owner is the request's owner.");
            Assert.That(call.ResultType, Is.EqualTo(typeof(bool)));
            Assert.That(call.Options.RequestId, Is.EqualTo(id), "The item is presented with the id reserved at enqueue.");
            Assert.That(call.Options.Priority, Is.EqualTo(7), "The priority travels in OpenOptions.");
            Assert.That(call.Options.Source, Is.EqualTo("inbox"));
            Assert.That(call.Options.RejectWhenBusy, Is.False);
            Assert.That(_rig.Navigator.ReservedCount, Is.EqualTo(1), "Presentation reserves no second id.");
        }

        [Test]
        public void Presentation_PassesTheQueuesLayerAsTheExpectedLayer()
        {
            _rig.Enqueue("a");

            _rig.Ticker.Tick();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1), "one presentation");
            Assert.That(_rig.Navigator.Calls[0].Options.ExpectedLayerId, Is.EqualTo(ViewLayers.Popups.Id), "expected layer id");
        }

        [Test]
        public void EachEnqueue_ReservesItsOwnId()
        {
            var a = _rig.Enqueue("a");
            var b = _rig.Enqueue("b");
            var c = _rig.Enqueue("c");

            Assert.That(new[] { a, b, c }, Is.Unique);
            Assert.That(_rig.Navigator.ReservedCount, Is.EqualTo(3));

            _rig.DrainPresentingEach();

            var ids = new List<int>();
            for (var i = 0; i < _rig.Navigator.Calls.Count; i++)
                ids.Add(_rig.Navigator.Calls[i].RequestId);
            Assert.That(ids, Is.EqualTo(new[] { a, b, c }));
        }

        [Test]
        public void TheQueue_PresentsThroughShowAsyncOnly()
        {
            _rig.Enqueue("a");
            _rig.DrainPresentingEach();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1));
            Assert.That(_rig.Navigator.OpenCount, Is.Zero);
            Assert.That(_rig.Navigator.CloseAllCount, Is.Zero);
        }

        // One item at a time and a clear layer

        [Test]
        public void WhileAnItemIsPresented_NothingElseIsPresented_EvenAfterSettled()
        {
            _rig.Enqueue("a");
            _rig.Enqueue("b");
            _rig.Ticker.Tick();

            _rig.Navigator.PublishSettled();
            _rig.Ticker.TickUntilIdle();
            _rig.Enqueue("c", 100);
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }), "Q1: at most one item is presenting.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(2), "The presenting item is not counted as waiting.");
        }

        [Test]
        public void ThePresentedItemsEnd_SchedulesAPump_AndTheNextItemIsPresentedOnTheNextTick()
        {
            _rig.Enqueue("a");
            _rig.Enqueue("b");
            _rig.Ticker.Tick();
            Assert.That(_rig.Ticker.PendingTicks, Is.Zero);

            _rig.Navigator.LastCall.Close(CloseReason.Dismissed);

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }), "The next item is not presented inside the closing request's completion.");
            Assert.That(_rig.Ticker.PendingTicks, Is.GreaterThan(0), "An item ending is a trigger.");

            _rig.Ticker.Tick();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void AContinuationThatOpensAPopup_AfterTheClose_KeepsTheNextItemWaiting()
        {
            var first = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            _rig.Enqueue("b");
            _rig.Ticker.Tick();

            _rig.Navigator.LastCall.Close(CloseReason.Dismissed);

            // The opener's continuation ran already; it opens a follow-up popup, so the layer is no longer clear.
            Assert.That(first.ResumeCount, Is.EqualTo(1), "The continuation runs before the queue presents.");
            _rig.Navigator.SetLayerClear(_rig.Layer, false);
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }), "The queued item waits for the follow-up popup.");

            _rig.Navigator.SetLayerClear(_rig.Layer, true);
            _rig.Navigator.PublishSettled();
            _rig.Ticker.Tick();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a", "b" }));
        }

        // Found by the 2,000-seed run (seed 707): a request ended by its owner, a loss or navigation clears the layer without Settled.
        [TestCase(ViewEventKind.Closed)]
        [TestCase(ViewEventKind.NotShown)]
        public void ALayerClearedWithoutSettled_IsNoticedThroughTheEndOfTheRequestThatHeldIt(ViewEventKind ending)
        {
            _rig.Navigator.SetLayerClear(_rig.Layer, false);
            _rig.Enqueue("a");
            _rig.Ticker.TickUntilIdle();
            Assume.That(_rig.PresentedIds(), Is.Empty, "control: nothing is presented while the layer is not clear");

            _rig.Navigator.SetLayerClear(_rig.Layer, true);
            var closed = ending == ViewEventKind.Closed;
            var ended = new ViewEvent(
                ending, 999, "opened-by-hand", _rig.Layer.Id, null, 0,
                closed ? CloseReason.OwnerEnded : CloseReason.NotShown,
                closed ? RejectReason.None : RejectReason.OwnerEnded,
                null, 0f, 0f);
            _rig.Navigator.Publish(in ended);
            _rig.Ticker.Tick();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }), "An item ending is a trigger, and so is the request that kept the layer busy.");
        }

        [Test]
        public void WhileTheNavigatorIsBusy_NothingIsPresented()
        {
            _rig.Navigator.IsBusy = true;
            _rig.Enqueue("a");

            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Navigator.Calls, Is.Empty, "Q2: an item is presented only when the layer is clear.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(1), "The item keeps waiting.");
        }

        [Test]
        public void WhileAModalLayerAboveIsOpen_NothingIsPresentedUnderneath()
        {
            // A System overlay is open: the navigator reports the popup layer as not clear.
            _rig.Navigator.SetLayerClear(ViewLayers.Popups, false);
            _rig.Enqueue("a");

            _rig.Ticker.TickUntilIdle();
            _rig.Navigator.PublishSettled();
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Navigator.Calls, Is.Empty, "Nothing is presented underneath a modal layer.");
            Assert.That(_rig.Navigator.IsLayerClearCount, Is.GreaterThan(0), "The pump asks the navigator about the layer.");
        }

        [Test]
        public void Settled_SchedulesAPump_AndAClearLayerThenPresents()
        {
            _rig.Navigator.IsBusy = true;
            _rig.Enqueue("a");
            _rig.Ticker.TickUntilIdle();
            Assert.That(_rig.Ticker.PendingTicks, Is.Zero);

            _rig.Navigator.IsBusy = false;
            _rig.Navigator.PublishSettled();

            Assert.That(_rig.Ticker.PendingTicks, Is.GreaterThan(0), "Settled is a trigger.");
            Assert.That(_rig.Navigator.Calls, Is.Empty, "The pump runs on the next tick, not inside the event.");

            _rig.Ticker.Tick();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void ALayerOtherThanTheQueuesNotBeingClear_DoesNotBlockIt()
        {
            // The fake reports per layer, so only the queue's own layer stays clear.
            _rig.Navigator.SetLayerClear(ViewLayers.Screens, false);
            _rig.Navigator.SetLayerClear(ViewLayers.System, false);
            _rig.Enqueue("a");

            _rig.Ticker.Tick();

            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }), "Q2: the queue asks only about its own layer.");
        }

        public enum QueueBlocker
        {
            Hold,
            NavigatorBusy,
            LayerNotClear,
            AnotherItemPresenting
        }

        [Test]
        public void ABlockedPump_AsksNoGateAndEndsNothing_UntilTheQueueIsFree([Values] QueueBlocker blocker)
        {
            var gate = new ScriptedGate(decide: r => r.AtPresentation && r.ViewId == "denied" ? GateDecision.Deny("not now") : GateDecision.Allow);
            _rig.Queue.AddGate(gate, CancellationToken.None);
            var unblock = Block(blocker);
            var deniedId = _rig.Enqueue("denied");
            var waitingBefore = _rig.Queue.Count;

            _rig.Ticker.TickUntilIdle();
            _rig.Navigator.PublishSettled();
            _rig.Ticker.TickUntilIdle();

            Assert.That(gate.Requests, Has.None.Matches<GateRequest>(r => r.AtPresentation && r.ViewId == "denied"), "A blocked pump does nothing, so no gate is asked.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(waitingBefore), "Queued items wait.");
            Assert.That(_rig.Observer.CountOf(ViewEventKind.NotShown), Is.Zero, "Nothing ends while the queue is blocked.");

            unblock();
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Observer.KindsFor(deniedId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }), "Once free, the walk reaches the gate.");
            Assert.That(_rig.Observer.EventsFor(deniedId)[1].Rejected, Is.EqualTo(RejectReason.Denied));
        }

        // Re-entrant gates at presentation: the check and ShowAsync are one synchronous block

        [Test]
        public void AGateThatOpensAViewAtPresentation_GetsNoShowAsync_AndTheItemWaitsForTheLayer()
        {
            var opened = false;
            var gate = new ScriptedGate(decide: r =>
            {
                if (r.AtPresentation && !opened)
                {
                    // An immediate open from the gate makes the layer busy.
                    opened = true;
                    _rig.Navigator.SetLayerClear(_rig.Layer, false);
                }

                return GateDecision.Allow;
            });
            _rig.Queue.AddGate(gate, CancellationToken.None);
            var id = _rig.Enqueue("a");

            _rig.Ticker.TickUntilIdle();

            Assert.That(opened, Is.True, "The gate ran at presentation.");
            Assert.That(_rig.Navigator.Calls, Is.Empty, "Q2: the layer is checked again right before ShowAsync.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(1), "The item keeps waiting.");
            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "The item is not denied.");

            _rig.Navigator.SetLayerClear(_rig.Layer, true);
            _rig.Navigator.PublishSettled();
            _rig.Ticker.Tick();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1));
            Assert.That(_rig.Navigator.Calls[0].RequestId, Is.EqualTo(id));
        }

        [Test]
        public void AGateThatHoldsTheQueueAtPresentation_GetsNoShowAsync_UntilTheHoldEnds()
        {
            var hold = default(ViewRegistration);
            var held = false;
            var gate = new ScriptedGate(decide: r =>
            {
                if (r.AtPresentation && !held)
                {
                    held = true;
                    hold = _rig.Queue.Hold("taken by a gate", CancellationToken.None);
                }

                return GateDecision.Allow;
            });
            _rig.Queue.AddGate(gate, CancellationToken.None);
            var id = _rig.Enqueue("a");

            _rig.Ticker.TickUntilIdle();

            Assert.That(held, Is.True, "The gate ran at presentation.");
            Assert.That(_rig.Navigator.Calls, Is.Empty, "Q2: an item is presented only when no hold exists.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(1));
            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued }));

            hold.Remove();
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1));
            Assert.That(_rig.Navigator.Calls[0].RequestId, Is.EqualTo(id));
        }

        [Test]
        public void AGateThatClearsTheQueueAtPresentation_GetsNoShowAsync_AndTheItemEndsOnceAsCancelled()
        {
            var gate = new ScriptedGate(decide: r =>
            {
                if (r.AtPresentation)
                    _rig.Queue.Clear();

                return GateDecision.Allow;
            });
            _rig.Queue.AddGate(gate, CancellationToken.None);
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;

            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Navigator.Calls, Is.Empty, "A cleared item is not presented.");
            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }), "Q3: exactly one end.");
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "I4");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(_rig.Queue.Count, Is.Zero);
        }

        // The presented item ends through its ShowAsync result only

        [TestCase(RejectReason.Busy)]
        [TestCase(RejectReason.Duplicate)]
        public void APresentedItemTheNavigatorEndsUnshown_IsNotRetried_AndTheNextItemFollowsOnTheNextTick(RejectReason reason)
        {
            var first = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var nextId = _rig.Enqueue("b");
            _rig.Ticker.Tick();

            _rig.Navigator.LastCall.NotShown(reason);

            Assert.That(first.ResumeCount, Is.EqualTo(1));
            Assert.That(first.Result.Rejected, Is.EqualTo(reason));
            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1), "The next item waits for the next tick.");

            _rig.Ticker.Tick();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(2));
            Assert.That(_rig.Navigator.Calls[1].RequestId, Is.EqualTo(nextId), "The queue moves on to the next waiting item.");

            _rig.Navigator.LastCall.Close(CloseReason.Dismissed);
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(2), "Q3: the rejected item is not presented again.");
            Assert.That(_rig.Observer.CountOf(ViewEventKind.NotShown), Is.Zero, "The navigator reported that end, not the queue.");
            Assert.That(_rig.Queue.Count, Is.Zero);
        }

        [Test]
        public void NavigatorEventsOtherThanSettled_DoNotEndOrAdvanceThePresentedItem()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            _rig.Enqueue("b");
            _rig.Ticker.Tick();
            var call = _rig.Navigator.LastCall;

            PublishNavigatorEvent(ViewEventKind.Requested, call);
            PublishNavigatorEvent(ViewEventKind.Shown, call);
            PublishNavigatorEvent(ViewEventKind.Closed, call);
            PublishNavigatorEvent(ViewEventKind.NotShown, call);
            _rig.Ticker.TickUntilIdle();

            Assert.That(probe.IsPending, Is.True, "The presented item ends through its ShowAsync result only.");
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a" }), "Q1: the item is still presenting.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(1));

            call.Close(CloseReason.Dismissed);
            _rig.Ticker.Tick();

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(_rig.PresentedIds(), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void APresentationWhoseShowAsyncFaults_CompletesTheAwaiterOnceAsNotShown_AndTheNextItemFollowsOnTheNextTick()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var nextId = _rig.Enqueue("b");
            _rig.Ticker.Tick();

            _rig.Navigator.LastCall.Fault(new InvalidOperationException("navigator broke"));

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "I4: EnqueueAsync completes with data in every case.");
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.NotShown));
            Assert.That(_rig.Diagnostics.Total + _rig.Routed.Count, Is.GreaterThan(0), "The top-level catch reports the error.");
            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1), "The next item waits for the next tick.");

            _rig.Ticker.Tick();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(2), "Q1: the queue left Presenting.");
            Assert.That(_rig.Navigator.Calls[1].RequestId, Is.EqualTo(nextId));

            _rig.Ticker.TickUntilIdle();
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "I4: exactly once.");
        }

        [Test]
        public void APresentationWhoseShowAsyncThrows_CompletesTheAwaiterOnceAsNotShown_AndTheNextItemFollowsOnTheNextTick()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var nextId = _rig.Enqueue("b");
            _rig.Navigator.ThrowOnNextShowAsync = new InvalidOperationException("navigator broke");

            _rig.Ticker.Tick();

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "I4: EnqueueAsync completes with data in every case.");
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.NotShown));
            Assert.That(_rig.Diagnostics.Total + _rig.Routed.Count, Is.GreaterThan(0), "The error is reported.");
            Assert.That(_rig.Navigator.Calls, Is.Empty, "The next item waits for the next tick.");

            _rig.Ticker.Tick();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1), "Q1: the queue is not stuck in Presenting.");
            Assert.That(_rig.Navigator.Calls[0].RequestId, Is.EqualTo(nextId));

            _rig.Ticker.TickUntilIdle();
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "I4: exactly once.");
        }

        [Test]
        public void APresentationWhoseShowAsyncFaults_PublishesQueueNotShownFaulted()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;
            _rig.Ticker.Tick();

            _rig.Navigator.LastCall.Fault(new InvalidOperationException("navigator broke"));

            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }), "An asynchronous fault ends the item with a queue NotShown, as a synchronous throw does.");
            var notShown = _rig.Observer.EventsFor(id)[1];
            Assert.That(notShown.Rejected, Is.EqualTo(RejectReason.Faulted), "event reject reason");
            Assert.That(notShown.Detail, Is.EqualTo(PresentFaultedDetail), "event detail");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "awaiter resumes");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Faulted), "awaiter reject reason");
            Assert.That(probe.Result.Detail, Is.EqualTo(PresentFaultedDetail), "awaiter detail");
        }

        [TestCase(ViewEventKind.Closed)]
        [TestCase(ViewEventKind.NotShown)]
        public void AFaultAfterTheNavigatorEndedTheRequest_PublishesNoQueueEnd(ViewEventKind ending)
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;
            var nextId = _rig.Enqueue("b");
            _rig.Ticker.Tick();
            var call = _rig.Navigator.LastCall;

            PublishNavigatorEvent(ending, call);
            call.Fault(new InvalidOperationException("navigator broke"));
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "The navigator already published the end, so the queue publishes none.");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The awaiter completes exactly once.");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Faulted), "The awaiter still learns the presentation faulted.");
            Assert.That(probe.Result.Detail, Is.EqualTo(PresentFaultedDetail), "awaiter detail");
            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(2), "The queue leaves Presenting even when it publishes no end.");
            Assert.That(_rig.Navigator.Calls[1].RequestId, Is.EqualTo(nextId), "The next waiting item is presented.");
        }

        [TestCase(ViewEventKind.Closed)]
        [TestCase(ViewEventKind.NotShown)]
        public void AFaultAfterAnotherRequestEnded_PublishesTheQueueNotShown(ViewEventKind ending)
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;
            _rig.Ticker.Tick();
            var call = _rig.Navigator.LastCall;

            // A request the queue never presented, such as a popup opened by hand.
            var closed = ending == ViewEventKind.Closed;
            var otherEnded = new ViewEvent(
                ending, 999, "opened-by-hand", _rig.Layer.Id, null, 0,
                closed ? CloseReason.Dismissed : CloseReason.NotShown,
                closed ? RejectReason.None : RejectReason.Busy,
                null, 0f, 0f);
            _rig.Navigator.Publish(in otherEnded);
            call.Fault(new InvalidOperationException("navigator broke"));
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }), "Only an end for the presented request suppresses the queue's end.");
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.Faulted), "event reject reason");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The awaiter completes exactly once.");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Faulted), "awaiter reject reason");
        }

        [Test]
        public void ASettledNavigatorBeforeTheFault_DoesNotSuppressTheQueueNotShown()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;
            _rig.Ticker.Tick();
            var call = _rig.Navigator.LastCall;

            // Carries the presented id, so only the kind keeps Settled from counting as an end.
            PublishNavigatorEvent(ViewEventKind.Settled, call);
            call.Fault(new InvalidOperationException("navigator broke"));
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }), "Settled does not end the request, so the queue publishes its end.");
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.Faulted), "event reject reason");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The awaiter completes exactly once.");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Faulted), "awaiter reject reason");
        }

        [Test]
        public void AFaultOffTheMainThread_PublishesAndCompletesOnTheMainThread()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;
            var nextId = _rig.Enqueue("b");
            _rig.Ticker.Tick();
            var call = _rig.Navigator.LastCall;

            _rig.MainThread.IsMainThread = false;
            call.Fault(new InvalidOperationException("navigator broke"));
            _rig.MainThread.IsMainThread = true;
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Observer.CountOf(ViewEventKind.NotShown), Is.Zero, "Nothing is published off the main thread.");
            Assert.That(probe.IsPending, Is.True, "The awaiter waits for the main thread.");
            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1), "Q1: the item is still presenting until the main thread ends it.");
            Assert.That(_rig.MainThread.PendingCount, Is.EqualTo(1), "The end is posted to the main thread.");

            _rig.MainThread.Flush();

            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }), "One queue end on the main thread.");
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.Faulted), "event reject reason");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The awaiter resumes once.");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Faulted), "awaiter reject reason");

            _rig.Ticker.Tick();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(2), "The next item follows on the next tick.");
            Assert.That(_rig.Navigator.Calls[1].RequestId, Is.EqualTo(nextId));
        }

        [Test]
        public void AFaultOffTheMainThread_ThenTheNavigatorEndsTheRequest_PublishesNoQueueEnd()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;
            _rig.Ticker.Tick();
            var call = _rig.Navigator.LastCall;

            _rig.MainThread.IsMainThread = false;
            call.Fault(new InvalidOperationException("navigator broke"));
            _rig.MainThread.IsMainThread = true;
            PublishNavigatorEvent(ViewEventKind.Closed, call);
            Assume.That(probe.IsPending, Is.True, "control: the end is still posted when the navigator's end arrives");

            _rig.MainThread.Flush();

            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "A navigator end that arrives before the posted end is still seen on the main thread.");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The awaiter completes exactly once.");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Faulted), "awaiter reject reason");
        }

        [Test]
        public void AFaultAfterTheQueueLifetimeEnded_CompletesTheAwaiterOnceAsFaulted_AndThrowsNothing()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            var id = _rig.Navigator.LastReservedId;
            _rig.Enqueue("b");
            _rig.Ticker.Tick();
            var call = _rig.Navigator.LastCall;

            _rig.EndLifetime();
            var faultsBefore = _rig.Diagnostics.FaultCount;

            Assert.That(() =>
            {
                call.Fault(new InvalidOperationException("navigator broke"));
                _rig.Ticker.TickUntilIdle();
            }, Throws.Nothing, "A fault after the lifetime ended throws nothing.");

            Assert.That(_rig.Diagnostics.FaultCount - faultsBefore, Is.EqualTo(1), "Only the presentation fault is reported; ending the item raises nothing.");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The awaiter completes exactly once.");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Faulted), "awaiter reject reason");
            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1), "An ended queue presents nothing more.");
            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "The queue's observers were removed with its lifetime.");
        }

        [Test]
        public void AnEnqueuedItemWhoseShowAsyncFaults_EndsWithAQueueEvent()
        {
            var id = _rig.Enqueue("a");
            _rig.Ticker.Tick();

            _rig.Navigator.LastCall.Fault(new InvalidOperationException("navigator broke"));

            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }), "Without an awaiter, the queue event is the only report of the end.");
            Assert.That(_rig.Observer.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.Faulted));
        }

        [Test]
        public void TheQueue_AddsOneObserverToTheNavigator()
        {
            Assert.That(_rig.Navigator.ObserverCount, Is.EqualTo(1), "One navigator observer for the queue's lifetime.");
        }

        // EnqueueAsync

        [Test]
        public void EnqueueAsync_WhenPresented_CompletesOnceWithTheNavigatorsResult()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create<string, bool>("ask"), "Delete?", CancellationToken.None));
            _rig.Ticker.Tick();
            Assert.That(probe.IsPending, Is.True, "The awaiter waits for the view, not for the presentation.");

            var call = _rig.Navigator.LastCall;
            call.Complete(true);

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "I4: exactly once.");
            Assert.That(probe.Result.RequestId, Is.EqualTo(call.RequestId));
            Assert.That(probe.Result.WasShown, Is.True);
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(probe.Result.HasValue, Is.True);
            Assert.That(probe.Result.Value, Is.True);

            _rig.Queue.Clear();
            _rig.Ticker.TickUntilIdle();
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "I4: nothing completes it a second time.");
        }

        [Test]
        public void EnqueueAsync_WhenTheNavigatorEndsTheRequestUnshown_CompletesWithThatResult_AndTheQueuePublishesNoNotShown()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            _rig.Ticker.Tick();

            _rig.Navigator.LastCall.NotShown(RejectReason.Duplicate, "already open");

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Duplicate), "The navigator's outcome is the awaiter's outcome.");
            Assert.That(probe.Result.Detail, Is.EqualTo("already open"));
            Assert.That(_rig.Observer.CountOf(ViewEventKind.NotShown), Is.Zero, "After presentation the navigator reports the end, not the queue.");
        }

        [Test]
        public void EnqueueAsync_ForAKeyWithoutResult_CompletesWithTheCloseReason()
        {
            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), CancellationToken.None));
            _rig.Ticker.Tick();

            _rig.Navigator.LastCall.Close(CloseReason.Backdrop);

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Result.WasShown, Is.True);
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.Backdrop));
            Assert.That(probe.Result.HasValue, Is.False);
        }

        // Events

        [Test]
        public void Enqueued_IsPublishedToQueueObservers_WithIdViewLayerPriorityAndSource()
        {
            var id = _rig.Enqueue("a", 42, "shop.offer");

            Assert.That(_rig.Observer.AllKinds(), Is.EqualTo(new[] { ViewEventKind.Enqueued }));
            var enqueued = _rig.Observer.Events[0];
            Assert.That(enqueued.RequestId, Is.EqualTo(id));
            Assert.That(enqueued.ViewId, Is.EqualTo("a"));
            Assert.That(enqueued.LayerId, Is.EqualTo(ViewLayers.Popups.Id));
            Assert.That(enqueued.Priority, Is.EqualTo(42), "Enqueued carries the priority.");
            Assert.That(enqueued.Source, Is.EqualTo("shop.offer"));
        }

        [Test]
        public void TheQueuePublishesOnlyToItsOwnObservers_AndNothingAfterPresentation()
        {
            var navigatorSide = new RecordingObserver();
            _rig.Navigator.AddObserver(navigatorSide, CancellationToken.None);

            _rig.Enqueue("a", 3, "s");
            _rig.Ticker.Tick();
            _rig.Navigator.LastCall.Close(CloseReason.Dismissed);
            _rig.Ticker.TickUntilIdle();

            Assert.That(_rig.Observer.AllKinds(), Is.EqualTo(new[] { ViewEventKind.Enqueued }),
                "Requested, Shown and Closed come from the navigator to the navigator's observers.");
            Assert.That(navigatorSide.Count, Is.Zero, "The queue never publishes through the navigator.");
        }

        [Test]
        public void NotShownFromTheQueue_CarriesThePriorityAndSource()
        {
            var owner = _rig.NewOwner();
            var id = _rig.Enqueue("a", 12, "daily", owner.Token);

            owner.Cancel();

            var notShown = _rig.Observer.LastOf(ViewEventKind.NotShown);
            Assert.That(notShown.RequestId, Is.EqualTo(id));
            Assert.That(notShown.Priority, Is.EqualTo(12));
            Assert.That(notShown.Source, Is.EqualTo("daily"));
            Assert.That(notShown.LayerId, Is.EqualTo(ViewLayers.Popups.Id));
            Assert.That(notShown.Reason, Is.EqualTo(CloseReason.NotShown));
        }

        [Test]
        public void AQueueObserverThatThrows_IsRouted_AndLaterObserversAndTheQueueCarryOn()
        {
            _rig.Observer.ThrowOnEvent = new InvalidOperationException("observer broke");
            var second = new RecordingObserver();
            _rig.Queue.AddObserver(second, CancellationToken.None);

            var id = _rig.Enqueue("a");

            Assert.That(_rig.Routed.Count, Is.EqualTo(1), "An observer that throws is routed.");
            Assert.That(_rig.Routed[0].Source, Is.EqualTo(ViewErrorSource.Observer), "A throwing queue observer is routed with the Observer source.");
            Assert.That(_rig.Routed[0].RequestId, Is.EqualTo(id));
            Assert.That(second.KindsFor(id), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "The other observers still run.");
            Assert.That(_rig.Queue.Count, Is.EqualTo(1), "The item still entered the queue.");

            _rig.Ticker.Tick();

            Assert.That(_rig.Navigator.Calls.Count, Is.EqualTo(1));
            Assert.That(_rig.Navigator.Calls[0].RequestId, Is.EqualTo(id));

            var b = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("b"), default(NoArgs), CancellationToken.None));
            var bId = _rig.Navigator.LastReservedId;
            var c = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("c"), default(NoArgs), CancellationToken.None));
            var cId = _rig.Navigator.LastReservedId;

            _rig.Queue.Clear();

            Assert.That(b.ResumeCount, Is.EqualTo(1), "Q3: a throwing NotShown observer does not stop Clear.");
            Assert.That(b.Result.Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(c.ResumeCount, Is.EqualTo(1));
            Assert.That(c.Result.Rejected, Is.EqualTo(RejectReason.Cancelled));
            Assert.That(second.KindsFor(bId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(second.KindsFor(cId), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }));
            Assert.That(_rig.Queue.Count, Is.Zero);
            Assert.That(_rig.Routed.Count, Is.EqualTo(5), "Enqueued a, b, c and NotShown b, c were each routed.");
            Assert.That(_rig.Routed, Has.All.Matches<ViewErrorContext>(context => context.Source == ViewErrorSource.Observer));
        }

        [Test]
        public void AQueueObserver_StopsReceivingEvents_WhenItsOwnerEnds()
        {
            var owner = _rig.NewOwner();
            var observer = new RecordingObserver();
            var registration = _rig.Queue.AddObserver(observer, owner.Token);
            _rig.Enqueue("a");

            owner.Cancel();
            _rig.Enqueue("b");
            _rig.Queue.Clear();

            Assert.That(registration.IsActive, Is.False, "The registration ends with its owner.");
            Assert.That(observer.AllKinds(), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "Neither Enqueued nor NotShown reach it after its owner ended.");
            Assert.That(_rig.Observer.CountOf(ViewEventKind.NotShown), Is.EqualTo(2), "The events were published.");
        }

        [Test]
        public void AQueueObserver_StopsReceivingEvents_WhenItsRegistrationIsRemoved()
        {
            var observer = new RecordingObserver();
            var registration = _rig.Queue.AddObserver(observer, CancellationToken.None);
            _rig.Enqueue("a");

            registration.Remove();
            _rig.Enqueue("b");
            _rig.Queue.Clear();

            Assert.That(registration.IsActive, Is.False);
            Assert.That(observer.AllKinds(), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "An observer removal takes effect at once.");
            Assert.That(_rig.Observer.CountOf(ViewEventKind.NotShown), Is.EqualTo(2), "The events were published.");
        }

        [Test]
        public void AQueueObserverAddedDuringADispatch_ReceivesEventsFromTheNextOne()
        {
            var late = new RecordingObserver();
            var added = false;
            _rig.Queue.AddObserver(new QueueActionObserver(e =>
            {
                if (added)
                    return;

                added = true;
                _rig.Queue.AddObserver(late, CancellationToken.None);
            }), CancellationToken.None);

            var firstId = _rig.Enqueue("a");

            Assert.That(added, Is.True);
            Assert.That(late.Count, Is.Zero, "An observer added during a dispatch does not receive that event.");

            var secondId = _rig.Enqueue("b");

            Assert.That(late.KindsFor(secondId), Is.EqualTo(new[] { ViewEventKind.Enqueued }), "A late observer receives events from the next dispatch.");
            Assert.That(late.KindsFor(firstId), Is.Empty);
        }

        [Test]
        public void AQueueObserverRemovedDuringADispatch_GetsNothingAfterTheRemoval()
        {
            var target = new RecordingObserver();
            var targetRegistration = default(ViewRegistration);
            _rig.Queue.AddObserver(new QueueActionObserver(e => targetRegistration.Remove()), CancellationToken.None);
            targetRegistration = _rig.Queue.AddObserver(target, CancellationToken.None);

            _rig.Enqueue("a");
            _rig.Enqueue("b");

            Assert.That(targetRegistration.IsActive, Is.False);
            Assert.That(target.Count, Is.Zero, "An observer removal takes effect at once, also inside the dispatch that made it.");
        }

        [Test]
        public void AnObserverThatEndsTheItemsOwnerOnEnqueued_LeavesEnqueuedBeforeNotShown_ForEveryObserver()
        {
            var owner = _rig.NewOwner();
            _rig.Queue.AddObserver(new QueueActionObserver(e =>
            {
                if (e.Kind == ViewEventKind.Enqueued)
                    owner.Cancel();
            }), CancellationToken.None);
            var later = new RecordingObserver();
            _rig.Queue.AddObserver(later, CancellationToken.None);

            var probe = _rig.Watch(_rig.Queue.EnqueueAsync(ViewKey.Create("a"), default(NoArgs), owner.Token));
            var id = _rig.Navigator.LastReservedId;
            _rig.Ticker.TickUntilIdle();

            var expected = new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown };
            Assert.That(_rig.Observer.KindsFor(id), Is.EqualTo(expected), "I6: the owner callback is registered after Enqueued.");
            Assert.That(later.KindsFor(id), Is.EqualTo(expected), "An observer after the cancelling one still sees Enqueued first.");
            Assert.That(later.EventsFor(id)[1].Rejected, Is.EqualTo(RejectReason.OwnerEnded));
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "I4");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.OwnerEnded));
            Assert.That(_rig.Navigator.Calls, Is.Empty);
            Assert.That(_rig.Queue.Count, Is.Zero);
        }

        // Puts the queue in one blocked state and returns what frees it.
        private Action Block(QueueBlocker blocker)
        {
            switch (blocker)
            {
                case QueueBlocker.Hold:
                {
                    var hold = _rig.Queue.Hold("blocker", CancellationToken.None);
                    return () => hold.Remove();
                }

                case QueueBlocker.NavigatorBusy:
                    _rig.Navigator.IsBusy = true;
                    return () =>
                    {
                        _rig.Navigator.IsBusy = false;
                        _rig.Navigator.PublishSettled();
                    };

                case QueueBlocker.LayerNotClear:
                    _rig.Navigator.SetLayerClear(_rig.Layer, false);
                    return () =>
                    {
                        _rig.Navigator.SetLayerClear(_rig.Layer, true);
                        _rig.Navigator.PublishSettled();
                    };

                case QueueBlocker.AnotherItemPresenting:
                {
                    _rig.Enqueue("presenting");
                    _rig.Ticker.Tick();
                    var call = _rig.Navigator.LastCall;
                    return () => call.Close(CloseReason.Dismissed);
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(blocker));
            }
        }

        // Publishes a navigator event for the presented request, as the real navigator would.
        private void PublishNavigatorEvent(ViewEventKind kind, FakeShowCall call)
        {
            var reason = kind == ViewEventKind.Closed ? CloseReason.Dismissed
                : kind == ViewEventKind.NotShown ? CloseReason.NotShown
                : default(CloseReason);
            var rejected = kind == ViewEventKind.NotShown ? RejectReason.Busy : RejectReason.None;
            var viewEvent = new ViewEvent(kind, call.RequestId, call.ViewId, _rig.Layer.Id, call.Options.Source, call.Options.Priority, reason, rejected, null, 0f, 0f);
            _rig.Navigator.Publish(in viewEvent);
        }

        // Startup scenario with an onboarding gate, at each moment

        [Test]
        public void Scenario64_MaintenanceFirst_ThenWhatIsNew_ThenFeedback()
        {
            using (var rig = new QueueRig(true))
            {
                var root = rig.Navigator.RootToken;
                var onboarding = new OnboardingGate();
                rig.Queue.AddGate(onboarding, root);

                // EnqueueAll runs while Home is still loading.
                rig.Navigator.IsBusy = true;
                var newsId = rig.Queue.Enqueue(Notice, new NoticeArgs("What is new", "Two new guides were added."), root,
                    new QueueOptions { Priority = 10, Source = "startup.news" });
                var maintenanceId = rig.Queue.Enqueue(Notice, new NoticeArgs("Maintenance", "The service pauses at 02:00."), root,
                    new QueueOptions { Priority = 100, Source = "startup.maintenance" });
                var feedback = rig.Watch(rig.Queue.EnqueueAsync(Confirm,
                    new ConfirmArgs("Feedback", "Would you like to tell us what to improve?"), root,
                    new QueueOptions { Priority = 1, Source = "startup.feedback" }));

                Assert.That(rig.Observer.CountOf(ViewEventKind.Enqueued), Is.EqualTo(3), "Both pass the gate at enqueue and publish Enqueued.");
                Assert.That(onboarding.AtEnqueue, Is.EqualTo(3));
                Assert.That(rig.Ticker.PendingTicks, Is.GreaterThan(0), "A pump is scheduled for the next tick.");

                // Next tick: Home is opening, so the popup layer is not clear.
                rig.Ticker.Tick();
                Assert.That(rig.Navigator.Calls, Is.Empty);

                // Home is shown.
                rig.Navigator.IsBusy = false;
                rig.Navigator.PublishSettled();
                rig.Ticker.Tick();

                Assert.That(rig.Navigator.Calls.Count, Is.EqualTo(1));
                var maintenance = rig.Navigator.Calls[0];
                Assert.That(maintenance.RequestId, Is.EqualTo(maintenanceId), "Maintenance (100) goes first although it was enqueued second.");
                Assert.That(((NoticeArgs)maintenance.Args).Title, Is.EqualTo("Maintenance"));
                Assert.That(maintenance.Options.Source, Is.EqualTo("startup.maintenance"));
                Assert.That(onboarding.AtPresentation, Is.GreaterThanOrEqualTo(1), "It passes the gate again before presentation.");
                Assert.That(onboarding.PresentationIds, Is.EqualTo(new[] { maintenanceId }), "The gate is asked once at presentation, about Maintenance only.");

                // The player closes it; the next pump presents What is new.
                maintenance.Close(CloseReason.Dismissed);
                rig.Ticker.Tick();
                Assert.That(rig.Navigator.Calls.Count, Is.EqualTo(2));
                Assert.That(rig.Navigator.Calls[1].RequestId, Is.EqualTo(newsId));

                // The feedback confirm follows.
                rig.Navigator.Calls[1].Close(CloseReason.Dismissed);
                rig.Ticker.Tick();
                Assert.That(rig.Navigator.Calls.Count, Is.EqualTo(3));
                var confirm = rig.Navigator.Calls[2];
                Assert.That(confirm.ViewId, Is.EqualTo(Confirm.Id));
                Assert.That(confirm.Options.Priority, Is.EqualTo(1));

                confirm.Complete(true);

                Assert.That(feedback.ResumeCount, Is.EqualTo(1));
                Assert.That(feedback.Result.HasValue && feedback.Result.Value, Is.True, "AskForFeedbackAsync answers true.");
                Assert.That(rig.Observer.CountOf(ViewEventKind.NotShown), Is.Zero);
            }
        }

        [Test]
        public void Scenario64_OnboardingStartsBeforeWhatIsNew_DeniesItAtPresentation_AndTheQueueContinues()
        {
            using (var rig = new QueueRig(true))
            {
                var root = rig.Navigator.RootToken;
                var onboarding = new OnboardingGate();
                rig.Queue.AddGate(onboarding, root);

                var newsId = rig.Queue.Enqueue(Notice, new NoticeArgs("What is new", "Two new guides were added."), root,
                    new QueueOptions { Priority = 10, Source = "startup.news" });
                rig.Queue.Enqueue(Notice, new NoticeArgs("Maintenance", "The service pauses at 02:00."), root,
                    new QueueOptions { Priority = 100, Source = "startup.maintenance" });
                var feedback = rig.Watch(rig.Queue.EnqueueAsync(Confirm,
                    new ConfirmArgs("Feedback", "Would you like to tell us what to improve?"), root,
                    new QueueOptions { Priority = 1, Source = "startup.feedback" }));

                rig.Ticker.Tick();
                Assert.That(rig.Navigator.Calls.Count, Is.EqualTo(1), "Maintenance is presented.");

                onboarding.IsRunning = true;
                rig.Navigator.Calls[0].Close(CloseReason.Dismissed);
                rig.Ticker.Tick();

                var newsEvents = rig.Observer.EventsFor(newsId);
                Assert.That(newsEvents.Length, Is.EqualTo(2));
                Assert.That(newsEvents[1].Kind, Is.EqualTo(ViewEventKind.NotShown));
                Assert.That(newsEvents[1].Rejected, Is.EqualTo(RejectReason.Denied));
                Assert.That(newsEvents[1].Detail, Is.EqualTo("onboarding is running"), "The gate's reason is the detail.");
                Assert.That(newsEvents[1].Source, Is.EqualTo("startup.news"));

                // The walk continued to the feedback confirm, which the gate denies too.
                Assert.That(feedback.ResumeCount, Is.EqualTo(1));
                Assert.That(feedback.Result.Rejected, Is.EqualTo(RejectReason.Denied));
                Assert.That(feedback.Result.HasValue && feedback.Result.Value, Is.False, "AskForFeedbackAsync answers false.");
                Assert.That(rig.Navigator.Calls.Count, Is.EqualTo(1));
                Assert.That(rig.Queue.Count, Is.Zero);

                // An urgent notice still passes while onboarding runs.
                rig.Queue.Enqueue(Notice, new NoticeArgs("Outage", "Now."), root, new QueueOptions { Priority = 100, Source = "ops" });
                rig.Ticker.Tick();
                Assert.That(rig.Navigator.Calls.Count, Is.EqualTo(2));
                Assert.That(rig.Navigator.Calls[1].Options.Source, Is.EqualTo("ops"));
            }
        }

        [Test]
        public void Scenario64_AHeldQueue_AndAPopupOpenedByHand_KeepItemsWaiting_UntilTheyEnd()
        {
            using (var rig = new QueueRig(true))
            {
                var root = rig.Navigator.RootToken;

                // A game flow needs the screen for itself.
                var flow = rig.NewOwner();
                rig.Queue.Hold("checkout flow", flow.Token);
                rig.Queue.Enqueue(Notice, new NoticeArgs("What is new", "Two new guides were added."), root,
                    new QueueOptions { Priority = 10, Source = "startup.news" });
                rig.Ticker.TickUntilIdle();
                Assert.That(rig.Navigator.Calls, Is.Empty, "Queued items wait while the flow holds the queue.");
                Assert.That(rig.Navigator.HoldInputCount, Is.Zero, "A queue hold does not touch the navigator, so the flow's immediate opens are not affected.");
                Assert.That(rig.Navigator.OpenCount, Is.Zero);

                // The player opens a popup by hand before the flow ends.
                rig.Navigator.SetLayerClear(ViewLayers.Popups, false);
                flow.Cancel();
                rig.Ticker.TickUntilIdle();
                Assert.That(rig.Queue.IsHeld, Is.False, "The hold ends with its token.");
                Assert.That(rig.Navigator.Calls, Is.Empty, "The layer is not clear, so the queue waits.");

                // The hand-opened popup closes; the navigator settles.
                rig.Navigator.SetLayerClear(ViewLayers.Popups, true);
                rig.Navigator.PublishSettled();
                rig.Ticker.Tick();

                Assert.That(rig.Navigator.Calls.Count, Is.EqualTo(1));
                Assert.That(rig.Navigator.Calls[0].Options.Source, Is.EqualTo("startup.news"));
            }
        }

        // Scenario types

        private static readonly ViewKey<NoticeArgs, NoResult> Notice = ViewKey.Create<NoticeArgs>("notice");
        private static readonly ViewKey<ConfirmArgs, bool> Confirm = ViewKey.Create<ConfirmArgs, bool>("confirm");

        private readonly struct NoticeArgs
        {
            public NoticeArgs(string title, string body)
            {
                Title = title;
                Body = body;
            }

            public string Title { get; }

            public string Body { get; }
        }

        private readonly struct ConfirmArgs
        {
            public ConfirmArgs(string title, string question)
            {
                Title = title;
                Question = question;
            }

            public string Title { get; }

            public string Question { get; }
        }

        // Only urgent notices pass while onboarding runs.
        private sealed class OnboardingGate : IViewGate
        {
            public readonly List<int> PresentationIds = new List<int>();
            public bool IsRunning;
            public int AtEnqueue;
            public int AtPresentation;

            public GateDecision Evaluate(in GateRequest request)
            {
                if (request.AtPresentation)
                {
                    AtPresentation++;
                    PresentationIds.Add(request.RequestId);
                }
                else
                {
                    AtEnqueue++;
                }

                return IsRunning && request.Priority < 100
                    ? GateDecision.Deny("onboarding is running")
                    : GateDecision.Allow;
            }
        }
    }

    // A queue on the popup layer over a FakeNavigator, with a manual ticker, a fake main thread and recording diagnostics.
    internal sealed class QueueRig : IDisposable
    {
        private readonly List<CancellationTokenSource> _owners = new List<CancellationTokenSource>();
        private readonly CancellationTokenSource _lifetime;
        private readonly bool _rootLifetime;

        public QueueRig(bool rootLifetime = false)
        {
            Navigator = new FakeNavigator();
            Ticker = new ManualTicker();
            MainThread = new FakeMainThread();
            Diagnostics = new RecordingDiagnostics();
            Observer = new RecordingObserver();
            Routed = new List<ViewErrorContext>();
            _rootLifetime = rootLifetime;
            _lifetime = new CancellationTokenSource();

            var lifetime = rootLifetime ? Navigator.RootToken : _lifetime.Token;
            Queue = new ViewQueue(
                Navigator,
                ViewLayers.Popups,
                lifetime,
                "test",
                Ticker,
                MainThread,
                new ErrorRouter(OnError, Diagnostics.Diagnostics),
                Diagnostics.Diagnostics);

            ObserverRegistration = Queue.AddObserver(Observer, CancellationToken.None);
        }

        public FakeNavigator Navigator { get; }

        public ManualTicker Ticker { get; }

        public FakeMainThread MainThread { get; }

        public RecordingDiagnostics Diagnostics { get; }

        // The queue's own observer, added with CancellationToken.None.
        public RecordingObserver Observer { get; }

        public ViewRegistration ObserverRegistration { get; }

        // Contexts of every exception routed to the game's handler.
        public List<ViewErrorContext> Routed { get; }

        public ViewQueue Queue { get; }

        public ViewLayer Layer
        {
            get { return ViewLayers.Popups; }
        }

        public CancellationTokenSource NewOwner()
        {
            var owner = new CancellationTokenSource();
            _owners.Add(owner);
            return owner;
        }

        // Ends the queue's lifetime.
        public void EndLifetime()
        {
            if (_rootLifetime)
                Navigator.CancelRoot();
            else
                _lifetime.Cancel();
        }

        public int Enqueue(string viewId, int priority = 0, string source = null, CancellationToken owner = default(CancellationToken))
        {
            return Queue.Enqueue(ViewKey.Create(viewId), default(NoArgs), owner, new QueueOptions { Priority = priority, Source = source });
        }

        public string[] PresentedIds()
        {
            var ids = new string[Navigator.Calls.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = Navigator.Calls[i].ViewId;
            return ids;
        }

        // Ticks, then closes whatever was presented, until nothing more is presented.
        public void DrainPresentingEach(int maxItems = 100)
        {
            for (var i = 0; i < maxItems; i++)
            {
                var before = Navigator.Calls.Count;
                Ticker.TickUntilIdle();
                if (Navigator.Calls.Count == before)
                    return;

                Navigator.LastCall.Close(CloseReason.Dismissed);
            }
        }

        public ResultProbe<TResult> Watch<TResult>(UniTask<ViewResult<TResult>> task)
        {
            var probe = new ResultProbe<TResult>();
            AwaitInto(task, probe).Forget();
            return probe;
        }

        public void Dispose()
        {
            MainThread.IsMainThread = true;
            EndLifetime();
            MainThread.Flush();

            for (var i = 0; i < _owners.Count; i++)
                _owners[i].Dispose();
            _lifetime.Dispose();
        }

        private static async UniTaskVoid AwaitInto<TResult>(UniTask<ViewResult<TResult>> task, ResultProbe<TResult> probe)
        {
            var result = await task;
            probe.Result = result;
            probe.ResumeCount++;
        }

        private void OnError(Exception exception, in ViewErrorContext context)
        {
            Routed.Add(context);
        }
    }

    // Records how often an awaiter resumed and with what.
    internal sealed class ResultProbe<TResult>
    {
        public int ResumeCount;
        public ViewResult<TResult> Result;

        public bool IsPending
        {
            get { return ResumeCount == 0; }
        }
    }

    // A gate the test scripts: it records each request, appends its name to a shared log and answers through a function.
    internal sealed class ScriptedGate : IViewGate
    {
        private readonly string _name;
        private readonly List<string> _log;
        private readonly Func<GateRequest, GateDecision> _decide;

        public ScriptedGate(string name = null, List<string> log = null, Func<GateRequest, GateDecision> decide = null)
        {
            _name = name;
            _log = log;
            _decide = decide;
        }

        public List<GateRequest> Requests { get; } = new List<GateRequest>();

        // When set, thrown instead of answering.
        public Exception Throw { get; set; }

        public GateDecision Evaluate(in GateRequest request)
        {
            Requests.Add(request);
            if (_log != null)
                _log.Add(_name);

            if (Throw != null)
                throw Throw;

            return _decide == null ? GateDecision.Allow : _decide(request);
        }
    }

    // A queue observer that hands every event to a delegate, for calls made from inside a dispatch.
    internal sealed class QueueActionObserver : IViewObserver
    {
        private readonly Action<ViewEvent> _action;

        public QueueActionObserver(Action<ViewEvent> action)
        {
            _action = action;
        }

        public void OnViewEvent(in ViewEvent viewEvent)
        {
            _action(viewEvent);
        }
    }
}
