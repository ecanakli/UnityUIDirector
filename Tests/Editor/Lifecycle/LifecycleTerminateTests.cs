using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Terminate causes end a request at once; a latched outcome survives.
    public sealed class LifecycleTerminateTests
    {
        public enum Cause
        {
            OwnerEnded,
            ExternalDestroy,
            ExternalDeactivate,
            Teardown
        }

        public enum Latch
        {
            Complete,
            ViewClose,
            HandleClose
        }

        public enum QueuedOperation
        {
            Close,
            PopTo,
            CloseAll
        }

        private const string Target = LifecycleDriver.Target;
        private const string Blocker = LifecycleDriver.Blocker;

        private NavigatorHarness _h;
        private FakeVisibleView<NoArgs, bool> _view;
        private int _openScopeEnds;

        [SetUp]
        public void SetUp()
        {
            _h = new NavigatorHarness();
            _view = null;
            _openScopeEnds = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _h.Dispose();
        }

        // A popup row whose view counts the ends of its open scope and keeps the visible hook.
        private void MapTracked(string id)
        {
            _h.Row(id, ViewLayers.Popups);
            _h.Map(id, () => _view = new FakeVisibleView<NoArgs, bool>
            {
                BindOnOpen = scope => scope.OnEnd(() => _openScopeEnds++)
            });
        }

        private void Terminate(Cause cause, CancellationTokenSource owner, FakeInstance instance)
        {
            switch (cause)
            {
                case Cause.OwnerEnded:
                    owner.Cancel();
                    break;
                case Cause.ExternalDestroy:
                    _h.Stage.RaiseInstanceLost(instance, true);
                    break;
                case Cause.ExternalDeactivate:
                    _h.Stage.RaiseInstanceLost(instance, false);
                    break;
                case Cause.Teardown:
                    _h.Navigator.Teardown();
                    break;
            }
        }

        private static CloseReason ExpectedReason(Cause cause)
        {
            return cause == Cause.OwnerEnded ? CloseReason.OwnerEnded : CloseReason.Destroyed;
        }

        private static bool IsExternalLoss(Cause cause)
        {
            return cause == Cause.ExternalDestroy || cause == Cause.ExternalDeactivate;
        }

        private void ApplyLatch(Latch latch)
        {
            switch (latch)
            {
                case Latch.Complete:
                    LifecycleDriver.ViewOf(_h, Target).Complete(true);
                    break;
                case Latch.ViewClose:
                    LifecycleDriver.ViewOf(_h, Target).Close();
                    break;
                case Latch.HandleClose:
                    LifecycleDriver.HandleOf(_h, Target).Close();
                    break;
            }
        }

        private static CloseReason ReasonOf(Latch latch)
        {
            switch (latch)
            {
                case Latch.Complete:
                    return CloseReason.Completed;
                case Latch.ViewClose:
                    return CloseReason.Dismissed;
                default:
                    return CloseReason.Code;
            }
        }

        // The request ended as shown with the reason, every awaiter got it once, and nothing is left behind.
        private void AssertTerminatedAsShown(
            UniTask<ViewResult<bool>> task,
            UniTask<ViewClosed> waiter,
            ViewHandle handle,
            FakeInstance instance,
            CloseReason reason,
            bool hasValue)
        {
            var result = task.ResultNow();
            Assert.That(result.WasShown, Is.True, "WasShown");
            Assert.That(result.Reason, Is.EqualTo(reason), "Reason");
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.None), "Rejected");
            Assert.That(result.HasValue, Is.EqualTo(hasValue), "HasValue");
            if (hasValue)
                Assert.That(result.Value, Is.True, "Value");

            var closed = waiter.ResultNow();
            Assert.That(closed.WasShown, Is.True, "WaitForCloseAsync WasShown");
            Assert.That(closed.Reason, Is.EqualTo(reason), "WaitForCloseAsync Reason");

            Assert.That(handle.IsAlive, Is.False);
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Ended));
            Assert.That(_h.RequestCount(ViewLayers.Popups), Is.Zero, _h.Describe());
            Assert.That(_h.ShieldCount, Is.Zero, _h.Describe());
            Assert.That(_h.Stage.ShieldBlocking, Is.False);
            Assert.That(_h.Stage.IsBackdropActive(ViewLayers.Popups), Is.False, "The backdrop goes with the view.");
            Assert.That(_h.Navigator.IsBusy, Is.False);

            var kinds = _h.Observer.KindsFor(result.RequestId);
            Assert.That(kinds[kinds.Length - 1], Is.EqualTo(ViewEventKind.Closed), "The last event of the request is Closed.");
            var terminalEvents = 0;
            for (var i = 0; i < kinds.Length; i++)
            {
                Assert.That(kinds[i], Is.Not.EqualTo(ViewEventKind.NotShown), "A shown request never reports NotShown.");
                if (kinds[i] == ViewEventKind.Closed)
                    terminalEvents++;
            }

            Assert.That(terminalEvents, Is.EqualTo(1), "Closed is published exactly once.");
            Assert.That(_h.Observer.LastOf(ViewEventKind.Closed).Reason, Is.EqualTo(reason), "Closed event reason");

            Assert.That(instance.ReleaseCount, Is.EqualTo(1), "The instance is released exactly once.");
            _h.Stage.AssertAllReleased();

            Assert.That(_openScopeEnds, Is.EqualTo(1), "The open scope ends exactly once.");
            Assert.That(_view.OpenCount, Is.EqualTo(1), "OnOpen ran once.");
            Assert.That(_view.LastVisible == null || _view.LastVisible.IsEnded, Is.True, "No visible scope is left open.");
        }

        // Owner ended, instance destroyed, instance deactivated, root destroyed: in every on-screen phase

        [Test]
        public void Terminate_EndsAShownRequestAtOnce_AndTheAwaitersGetTheCause(
            [Values(ViewPhase.Opening, ViewPhase.Open)] ViewPhase phase,
            [Values] Cause cause)
        {
            MapTracked(Target);
            var owner = _h.NewOwner();
            var task = LifecycleDriver.Reach(_h, Target, phase, owner.Token);
            var handle = LifecycleDriver.HandleOf(_h, Target);
            var waiter = handle.WaitForCloseAsync(CancellationToken.None);
            var instance = _h.Stage.LastInstanceOf(Target);

            Terminate(cause, owner, instance);

            AssertTerminatedAsShown(task, waiter, handle, instance, ExpectedReason(cause), false);
        }

        [Test]
        public void Terminate_CancelsTheRunningTransitionStep(
            [Values(ViewPhase.Opening, ViewPhase.Closing)] ViewPhase phase,
            [Values] Cause cause)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var owner = _h.NewOwner();
            LifecycleDriver.Reach(_h, Target, phase, owner.Token);
            var instance = _h.Stage.LastInstanceOf(Target);
            var plays = _h.Stage.CallsOf(StageCallKind.Play);
            var running = plays[plays.Length - 1];
            Assert.That(running.Token.IsCancellationRequested, Is.False);

            Terminate(cause, owner, instance);

            Assert.That(running.Token.IsCancellationRequested, Is.True, "The running step is cancelled by the terminate cause.");
        }

        [Test]
        public void Terminate_SnapsTheLiveView_BeforeItIsFinalized(
            [Values(ViewPhase.Opening, ViewPhase.Open, ViewPhase.Closing)] ViewPhase phase,
            [Values(Cause.OwnerEnded, Cause.ExternalDeactivate)] Cause cause)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var owner = _h.NewOwner();
            LifecycleDriver.Reach(_h, Target, phase, owner.Token);
            var instance = _h.Stage.LastInstanceOf(Target);
            _h.Stage.ClearCalls();

            Terminate(cause, owner, instance);

            var snap = LifecycleDriver.IndexOfCall(_h.Stage, StageCallKind.Snap, instance);
            var release = LifecycleDriver.IndexOfCall(_h.Stage, StageCallKind.Release, instance);
            Assert.That(snap, Is.GreaterThanOrEqualTo(0), "Running step cancelled, then snap.\n" + _h.Stage.DescribeCalls());
            Assert.That(release, Is.GreaterThan(snap), "Snap comes before finalize.\n" + _h.Stage.DescribeCalls());
        }

        [Test]
        public void ExternalLoss_LogsUIDIRECTOR107Once_ButOwnerEndAndTeardownDoNot(
            [Values(ViewPhase.Opening, ViewPhase.Open, ViewPhase.Closing)] ViewPhase phase,
            [Values] Cause cause)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var owner = _h.NewOwner();
            LifecycleDriver.Reach(_h, Target, phase, owner.Token);
            var instance = _h.Stage.LastInstanceOf(Target);

            Terminate(cause, owner, instance);

            Assert.That(_h.Diagnostics.Count(DiagnosticIds.ViewLostOutsidePackage), Is.EqualTo(IsExternalLoss(cause) ? 1 : 0));
        }

        [Test]
        public void TerminateDuringOpening_KeepsTheEventSequenceOfI6([Values] Cause cause)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var owner = _h.NewOwner();
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Opening, owner.Token);
            var instance = _h.Stage.LastInstanceOf(Target);

            Terminate(cause, owner, instance);

            var kinds = _h.Observer.KindsFor(task.ResultNow().RequestId);
            Assert.That(
                kinds,
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed })
                    .Or.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.NotShown }),
                "I6: per request id the sequence is Requested, Shown, Closed or Requested, NotShown.");
        }

        // A latched outcome survives a terminate, in Opening (close pending) and in Closing

        [Test]
        public void LatchedOutcome_SurvivesATerminate_AwaitersGetTheLatchedOutcome(
            [Values(ViewPhase.Opening, ViewPhase.Closing)] ViewPhase phase,
            [Values] Cause cause,
            [Values] Latch latch)
        {
            MapTracked(Target);
            var owner = _h.NewOwner();
            var task = LifecycleDriver.Reach(_h, Target, phase, owner.Token, null, view => ApplyLatch(latch));
            if (phase == ViewPhase.Opening)
                ApplyLatch(latch);

            var handle = LifecycleDriver.HandleOf(_h, Target);
            var waiter = handle.WaitForCloseAsync(CancellationToken.None);
            var instance = _h.Stage.LastInstanceOf(Target);

            Terminate(cause, owner, instance);

            AssertTerminatedAsShown(task, waiter, handle, instance, ReasonOf(latch), latch == Latch.Complete);
        }

        // The next request on the layer is not stuck behind a terminated one

        [Test]
        public void TerminateOfTheRunningOpen_LetsTheNextRequestRun(
            [Values(Cause.OwnerEnded, Cause.ExternalDestroy, Cause.ExternalDeactivate)] Cause cause)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target, "next");
            var owner = _h.NewOwner();
            LifecycleDriver.Reach(_h, Target, ViewPhase.Opening, owner.Token);
            var instance = _h.Stage.LastInstanceOf(Target);
            var nextTask = LifecycleDriver.Show(_h, "next", _h.Owner);
            Assert.That(LifecycleDriver.HandleOf(_h, "next").Phase, Is.EqualTo(ViewPhase.Waiting));

            Terminate(cause, owner, instance);

            Assert.That(LifecycleDriver.HandleOf(_h, "next").Phase, Is.EqualTo(ViewPhase.Opening), "The runner moved on to the next request.");
            _h.Stage.CompletePlay(_h.Stage.PendingPlayCount - 1);

            Assert.That(LifecycleDriver.HandleOf(_h, "next").Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(nextTask.IsPending(), Is.True);
            Assert.That(_h.ShieldCount, Is.Zero);
        }

        [Test]
        public void Terminate_LeavesTheLayerUsable_ForANewRequestOfTheSameId(
            [Values(ViewPhase.Opening, ViewPhase.Open, ViewPhase.Closing)] ViewPhase phase,
            [Values(Cause.OwnerEnded, Cause.ExternalDestroy, Cause.ExternalDeactivate)] Cause cause)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var owner = _h.NewOwner();
            LifecycleDriver.Reach(_h, Target, phase, owner.Token);
            var instance = _h.Stage.LastInstanceOf(Target);

            Terminate(cause, owner, instance);
            _h.Stage.PlayMode = StageMode.Auto;
            var again = LifecycleDriver.Show(_h, Target, _h.Owner);

            Assert.That(again.IsPending(), Is.True, "The new request is accepted and shown.");
            Assert.That(LifecycleDriver.HandleOf(_h, Target).Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_h.Observer.CountOf(ViewEventKind.NotShown), Is.Zero, "No duplicate: the old request is gone.");
        }

        // Teardown with requests in every phase

        [Test]
        public void Teardown_EndsARequestInEveryPhase_AndCompletesItsAwaiters(
            [Values(ViewPhase.Waiting, ViewPhase.Loading, ViewPhase.Opening, ViewPhase.Open, ViewPhase.Closing)] ViewPhase phase)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Blocker, Target);
            var task = LifecycleDriver.Reach(_h, Target, phase, _h.Owner, Blocker);
            var handle = LifecycleDriver.HandleOf(_h, Target);
            var waiter = handle.WaitForCloseAsync(CancellationToken.None);

            _h.Navigator.Teardown();

            var result = task.ResultNow();
            var closed = waiter.ResultNow();
            if (phase >= ViewPhase.Opening)
            {
                // A latched Complete in Closing survives; the others end as Destroyed.
                var expected = phase == ViewPhase.Closing ? CloseReason.Completed : CloseReason.Destroyed;
                Assert.That(result.WasShown, Is.True);
                Assert.That(result.Reason, Is.EqualTo(expected));
                Assert.That(closed.WasShown, Is.True);
                Assert.That(closed.Reason, Is.EqualTo(expected));
            }
            else
            {
                LifecycleDriver.AssertNotShown(result, RejectReason.RootGone);
                Assert.That(closed.WasShown, Is.False);
                Assert.That(closed.Rejected, Is.EqualTo(RejectReason.RootGone));
            }

            Assert.That(_h.Navigator.RootToken.IsCancellationRequested, Is.True, "RootToken is cancelled.");
            Assert.That(handle.IsAlive, Is.False);
            Assert.That(_h.RequestCount(ViewLayers.Popups), Is.Zero);
            Assert.That(_h.ShieldCount, Is.Zero);
            Assert.That(_h.Stage.ShieldBlocking, Is.False);
            Assert.That(_h.Navigator.IsBusy, Is.False);

            // Loads that were still running answer late; the stage releases those instances itself.
            while (_h.Stage.PendingAcquireCount > 0)
                _h.Stage.CompleteAcquire();

            _h.Stage.AssertAllReleased();

            var terminalEvents = 0;
            var kinds = _h.Observer.KindsFor(result.RequestId);
            for (var i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] == ViewEventKind.Closed || kinds[i] == ViewEventKind.NotShown)
                    terminalEvents++;
            }

            Assert.That(terminalEvents, Is.EqualTo(1), "Exactly one terminal event for the request.");
        }

        [Test]
        public void Teardown_WithRequestsOnEveryLayer_CompletesEveryAwaiter_AndLeavesNothing()
        {
            _h.Row("s");
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "p1", "p2");
            LifecycleDriver.AddRows(_h, ViewLayers.System, "y");
            var screenTask = _h.Navigator.ShowAsync(ViewKey.Create("s"), _h.Owner);
            _h.Stage.AcquireMode = StageMode.Manual;
            var loading = LifecycleDriver.Show(_h, "p1", _h.Owner);
            var waiting = LifecycleDriver.Show(_h, "p2", _h.Owner);
            var system = LifecycleDriver.Show(_h, "y", _h.Owner);
            var screenWaiter = LifecycleDriver.HandleOf(_h, "s").WaitForCloseAsync(CancellationToken.None);
            var waitingWaiter = LifecycleDriver.HandleOf(_h, "p2").WaitForCloseAsync(CancellationToken.None);

            _h.Navigator.Teardown();

            var screen = screenTask.ResultNow();
            Assert.That(screen.WasShown, Is.True);
            Assert.That(screen.Reason, Is.EqualTo(CloseReason.Destroyed));
            LifecycleDriver.AssertNotShown(loading.ResultNow(), RejectReason.RootGone);
            LifecycleDriver.AssertNotShown(waiting.ResultNow(), RejectReason.RootGone);
            LifecycleDriver.AssertNotShown(system.ResultNow(), RejectReason.RootGone);
            Assert.That(screenWaiter.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed));
            Assert.That(waitingWaiter.ResultNow().Rejected, Is.EqualTo(RejectReason.RootGone));

            Assert.That(_h.Navigator.RootToken.IsCancellationRequested, Is.True);
            LifecycleDriver.AssertClean(_h, ViewLayers.Screens, ViewLayers.Popups, ViewLayers.System);

            while (_h.Stage.PendingAcquireCount > 0)
                _h.Stage.CompleteAcquire();

            _h.Stage.AssertAllReleased();
        }

        [Test]
        public void Teardown_CalledTwice_DoesNothingTheSecondTime()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            _h.Navigator.Teardown();
            var eventsBefore = _h.Observer.Count;
            var callsBefore = _h.Stage.Calls.Count;

            _h.Navigator.Teardown();

            Assert.That(_h.Observer.Count, Is.EqualTo(eventsBefore));
            Assert.That(_h.Stage.Calls.Count, Is.EqualTo(callsBefore));
        }

        // An operation queued behind a running show never starts once teardown begins

        // How often an awaiter resumed, and the value it resumed with.
        private sealed class Resumes<T>
        {
            public int Count;
            public T Result;
        }

        private static Resumes<T> Track<T>(UniTask<T> task)
        {
            var resumes = new Resumes<T>();
            AwaitAsync(task, resumes).Forget();
            return resumes;
        }

        private static async UniTaskVoid AwaitAsync<T>(UniTask<T> task, Resumes<T> resumes)
        {
            resumes.Result = await task;
            resumes.Count++;
        }

        // Popups: p1 Open with a handle close queued while p2's show transition is still pending.
        private void QueueCloseBehindARunningShow(List<Resumes<ViewResult<bool>>> shows)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "p1", "p2");
            shows.Add(Track(LifecycleDriver.Show(_h, "p1", _h.Owner)));
            _h.Stage.PlayMode = StageMode.Manual;
            shows.Add(Track(LifecycleDriver.Show(_h, "p2", _h.Owner)));
            Assert.That(LifecycleDriver.HandleOf(_h, "p2").Phase, Is.EqualTo(ViewPhase.Opening), "Control: p2's show is running.");

            LifecycleDriver.HandleOf(_h, "p1").Close();

            Assert.That(LifecycleDriver.HandleOf(_h, "p1").Phase, Is.EqualTo(ViewPhase.Open), "Control: p1's close waits behind the running show.");
        }

        // Screens: a and b Open, c Opening with its show pending, and a pop-to a queued behind it.
        private void QueuePopToBehindARunningShow(List<Resumes<ViewResult<bool>>> shows)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Screens, "a", "b", "c");
            shows.Add(Track(LifecycleDriver.Show(_h, "a", _h.Owner)));
            shows.Add(Track(LifecycleDriver.Show(_h, "b", _h.Owner)));
            _h.Stage.PlayMode = StageMode.Manual;
            shows.Add(Track(LifecycleDriver.Show(_h, "c", _h.Owner)));
            Assert.That(LifecycleDriver.HandleOf(_h, "c").Phase, Is.EqualTo(ViewPhase.Opening), "Control: c's show is running.");

            Assert.That(_h.Navigator.CloseTo("a"), Is.True, "Control: the pop-to is accepted.");

            Assert.That(LifecycleDriver.HandleOf(_h, "b").Phase, Is.EqualTo(ViewPhase.Open), "Control: the pop-to waits behind the running show.");
        }

        // Popups: p1 Open and a close-all of the layer queued while p2's show transition is still pending.
        private void QueueCloseAllBehindARunningShow(List<Resumes<ViewResult<bool>>> shows)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "p1", "p2");
            shows.Add(Track(LifecycleDriver.Show(_h, "p1", _h.Owner)));
            _h.Stage.PlayMode = StageMode.Manual;
            shows.Add(Track(LifecycleDriver.Show(_h, "p2", _h.Owner)));
            Assert.That(LifecycleDriver.HandleOf(_h, "p2").Phase, Is.EqualTo(ViewPhase.Opening), "Control: p2's show is running.");

            _h.Navigator.CloseAll(ViewLayers.Popups);

            Assert.That(LifecycleDriver.HandleOf(_h, "p1").Phase, Is.EqualTo(ViewPhase.Open), "Control: the close-all waits behind the running show.");
        }

        [Test]
        public void Teardown_WithAnOperationQueuedBehindARunningShow_StartsNoTransitionForIt([Values] QueuedOperation queued)
        {
            var shows = new List<Resumes<ViewResult<bool>>>();
            string[] live;
            string queuedFor;
            ViewLayer layer;
            if (queued == QueuedOperation.Close)
            {
                QueueCloseBehindARunningShow(shows);
                live = new[] { "p1", "p2" };
                queuedFor = "p1";
                layer = ViewLayers.Popups;
            }
            else if (queued == QueuedOperation.CloseAll)
            {
                QueueCloseAllBehindARunningShow(shows);
                live = new[] { "p1", "p2" };
                queuedFor = "p1";
                layer = ViewLayers.Popups;
            }
            else
            {
                QueuePopToBehindARunningShow(shows);
                live = new[] { "a", "b", "c" };
                queuedFor = "b";
                layer = ViewLayers.Screens;
            }

            TeardownAndAssertNoTransitionStartsFor(shows, live, queuedFor, layer);
        }

        [Test]
        public void Teardown_WithACloseQueuedBehindARunningLoad_StartsNoTransitionForIt()
        {
            var shows = new List<Resumes<ViewResult<bool>>>();
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "p1", "p2");
            shows.Add(Track(LifecycleDriver.Show(_h, "p1", _h.Owner)));
            _h.Stage.AcquireMode = StageMode.Manual;
            shows.Add(Track(LifecycleDriver.Show(_h, "p2", _h.Owner)));
            Assert.That(LifecycleDriver.HandleOf(_h, "p2").Phase, Is.EqualTo(ViewPhase.Loading), "Control: p2's load is running.");

            LifecycleDriver.HandleOf(_h, "p1").Close();

            Assert.That(LifecycleDriver.HandleOf(_h, "p1").Phase, Is.EqualTo(ViewPhase.Open), "Control: p1's close waits behind the running load.");

            TeardownAndAssertNoTransitionStartsFor(shows, new[] { "p1", "p2" }, "p1", ViewLayers.Popups);

            Assert.That(shows[0].Result.WasShown, Is.True, "p1 ends as shown.");
            LifecycleDriver.AssertNotShown(shows[1].Result, RejectReason.RootGone);
        }

        // Tears the navigator down and checks the queued operation never ran while every request ended once.
        private void TeardownAndAssertNoTransitionStartsFor(
            List<Resumes<ViewResult<bool>>> shows,
            string[] live,
            string queuedFor,
            ViewLayer layer)
        {
            var queuedInstance = _h.Stage.LastInstanceOf(queuedFor);
            Assert.That(LifecycleDriver.IndexOfPlay(_h.Stage, queuedInstance, false), Is.EqualTo(-1), "Control: no hide transition ran before teardown.\n" + _h.Stage.DescribeCalls());

            var handles = new ViewHandle[live.Length];
            var waiters = new List<Resumes<ViewClosed>>();
            for (var i = 0; i < live.Length; i++)
            {
                handles[i] = LifecycleDriver.HandleOf(_h, live[i]);
                waiters.Add(Track(handles[i].WaitForCloseAsync(CancellationToken.None)));
            }

            var teardownStart = _h.Stage.Calls.Count;

            _h.Navigator.Teardown();

            var calls = _h.Stage.Calls;
            for (var i = teardownStart; i < calls.Count; i++)
            {
                var call = calls[i];
                var hidesQueued = call.Kind == StageCallKind.Play && !call.On && ReferenceEquals(call.Instance, queuedInstance);
                Assert.That(hidesQueued, Is.False, "Teardown starts no hide transition for '" + queuedFor + "'.\n" + _h.Stage.DescribeCalls());
            }

            for (var i = 0; i < shows.Count; i++)
            {
                Assert.That(shows[i].Count, Is.EqualTo(1), "The show awaiter of '" + live[i] + "' resumes exactly once.");

                var terminalEvents = 0;
                var kinds = _h.Observer.KindsFor(shows[i].Result.RequestId);
                for (var k = 0; k < kinds.Length; k++)
                {
                    if (kinds[k] == ViewEventKind.Closed || kinds[k] == ViewEventKind.NotShown)
                        terminalEvents++;
                }

                Assert.That(terminalEvents, Is.EqualTo(1), "Exactly one terminal event for '" + live[i] + "'.");
            }

            for (var i = 0; i < waiters.Count; i++)
            {
                Assert.That(waiters[i].Count, Is.EqualTo(1), "WaitForCloseAsync of '" + live[i] + "' resumes exactly once.");
                Assert.That(handles[i].IsAlive, Is.False, "The request of '" + live[i] + "' ended.");
            }

            Assert.That(_h.RequestCount(layer), Is.Zero, _h.Describe());
            Assert.That(_h.ShieldCount, Is.Zero, _h.Describe());

            // Loads that were still running answer late; the stage releases those instances itself.
            while (_h.Stage.PendingAcquireCount > 0)
                _h.Stage.CompleteAcquire();

            _h.Stage.AssertAllReleased();
        }

        // External loss reports

        [Test]
        public void TwoLostReportsForOneInstance_EndTheRequestOnce()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            var instance = _h.Stage.LastInstanceOf(Target);

            // A destroyed object is disabled first, then destroyed: two reports for one instance.
            _h.Stage.RaiseInstanceLost(instance, false);
            _h.Stage.RaiseInstanceLost(instance, true);

            Assert.That(_h.Observer.CountOf(ViewEventKind.Closed), Is.EqualTo(1));
            Assert.That(instance.ReleaseCount, Is.EqualTo(1));
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.ViewLostOutsidePackage), Is.EqualTo(1));
        }

        [Test]
        public void InstanceLost_ForAnInstanceNoRequestOwnsAnyMore_IsIgnored()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            var instance = _h.Stage.LastInstanceOf(Target);
            LifecycleDriver.ViewOf(_h, Target).Complete(true);
            Assert.That(task.ResultNow().Reason, Is.EqualTo(CloseReason.Completed));
            var eventsBefore = _h.Observer.Count;

            _h.Stage.RaiseInstanceLost(instance, true);

            Assert.That(_h.Observer.Count, Is.EqualTo(eventsBefore));
            Assert.That(instance.ReleaseCount, Is.EqualTo(1), "Not released a second time.");
            Assert.That(_h.Diagnostics.Has(DiagnosticIds.ViewLostOutsidePackage), Is.False);
        }

        [Test]
        public void KeptIdleInstance_DestroyedFromOutside_IsDroppedFromTheCacheAndReleased()
        {
            _h.Row(Target, ViewLayers.Popups, retention: ViewRetention.KeepInstance).Map<NoArgs, bool>(Target);
            var first = LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            var kept = _h.Stage.LastInstanceOf(Target);
            LifecycleDriver.ViewOf(_h, Target).Complete(true);
            Assert.That(first.ResultNow().Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(kept.Stashed, Is.True, "The row keeps its instance.");

            _h.Stage.RaiseInstanceLost(kept, true);

            Assert.That(kept.ReleaseCount, Is.EqualTo(1), "The dead instance is released.");
            Assert.That(_h.Diagnostics.Has(DiagnosticIds.ViewLostOutsidePackage), Is.False, "No open view was lost.");

            // The next open loads a fresh instance instead of reusing the dead one.
            LifecycleDriver.Show(_h, Target, _h.Owner);
            Assert.That(_h.Stage.Instances.Count, Is.EqualTo(2));
            Assert.That(_h.Stage.CountOf(StageCallKind.Acquire), Is.EqualTo(2));
        }

        // Release faults of kept instances are routed with the view id

        // A KeepInstance popup row opened and completed once, so its instance sits idle in the cache.
        private FakeInstance KeepIdle(string id)
        {
            _h.Row(id, ViewLayers.Popups, retention: ViewRetention.KeepInstance).Map<NoArgs, bool>(id);
            var task = LifecycleDriver.Reach(_h, id, ViewPhase.Open, _h.Owner);
            LifecycleDriver.ViewOf(_h, id).Complete(true);
            Assert.That(task.ResultNow().Reason, Is.EqualTo(CloseReason.Completed), "Control: the request completed.");

            var kept = _h.Stage.LastInstanceOf(id);
            Assert.That(kept.Stashed, Is.True, "Control: the row keeps its idle instance.");
            return kept;
        }

        [Test]
        public void Trim_KeptInstanceReleaseFaults_RoutesProviderErrorWithViewId()
        {
            KeepIdle(Target);
            var fault = new InvalidOperationException("kept release failed");
            _h.Stage.FailNext(StageCallKind.Release, fault);

            Assert.That(() => _h.Navigator.Trim(), Throws.Nothing);

            Assert.That(_h.Errors.Count, Is.EqualTo(1), "routed error count");
            Assert.That(_h.Errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(_h.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(_h.Errors[0].Context.ViewId, Is.EqualTo(Target), "error view id");
            Assert.That(_h.Errors[0].Context.RequestId, Is.Zero, "A kept instance belongs to no request.");
            Assert.That(_h.Diagnostics.FaultCount, Is.Zero, "The fault is routed, not reported as an internal fault.\n" + _h.Diagnostics.Describe());
        }

        [Test]
        public void Trim_AfterAReleaseFault_DoesNotReleaseTheInstanceAgain()
        {
            KeepIdle(Target);
            _h.Stage.FailNext(StageCallKind.Release);
            _h.Navigator.Trim();
            Assert.That(_h.Errors.Count, Is.EqualTo(1), "Control: the first release fault is routed.");

            Assert.That(_h.Describe(), Does.Contain("kept instances: 0"), "A faulted instance counts as released and leaves the cache.");

            _h.Navigator.Trim();

            Assert.That(_h.Errors.Count, Is.EqualTo(1), "The second Trim routes no further error.");
            Assert.That(_h.Stage.CountOf(StageCallKind.Release), Is.EqualTo(1), "The faulted instance is not released again.\n" + _h.Stage.DescribeCalls());
        }

        [Test]
        public void Teardown_KeptInstanceReleaseFaults_RoutesProviderErrorWithViewId()
        {
            KeepIdle(Target);
            var fault = new InvalidOperationException("kept release failed at teardown");
            _h.Stage.FailNext(StageCallKind.Release, fault);

            Assert.That(() => _h.Navigator.Teardown(), Throws.Nothing);

            Assert.That(_h.Errors.Count, Is.EqualTo(1), "routed error count");
            Assert.That(_h.Errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(_h.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(_h.Errors[0].Context.ViewId, Is.EqualTo(Target), "error view id");
            Assert.That(_h.Errors[0].Context.RequestId, Is.Zero, "A kept instance belongs to no request.");
            Assert.That(_h.Diagnostics.FaultCount, Is.Zero, "The fault is routed, not reported as an internal fault.\n" + _h.Diagnostics.Describe());
        }

        [Test]
        public void Trim_FirstKeptInstanceReleaseFaults_StillReleasesTheOthers()
        {
            var first = KeepIdle("first");
            var second = KeepIdle("second");
            _h.Stage.ClearCalls();
            _h.Stage.FailNext(StageCallKind.Release);

            _h.Navigator.Trim();

            var releases = _h.Stage.CallsOf(StageCallKind.Release);
            Assert.That(releases, Has.Exactly(1).Matches<StageCall>(call => ReferenceEquals(call.Instance, first)), "Release calls for the first instance.\n" + _h.Stage.DescribeCalls());
            Assert.That(releases, Has.Exactly(1).Matches<StageCall>(call => ReferenceEquals(call.Instance, second)), "Release calls for the second instance.\n" + _h.Stage.DescribeCalls());
            Assert.That(first.ReleaseCount + second.ReleaseCount, Is.EqualTo(1), "The instance whose release did not fault is released.");
            Assert.That(_h.Errors.Count, Is.EqualTo(1), "routed error count");
        }

        [Test]
        public void KeptIdleInstanceLost_ReleaseFaults_RoutesProviderErrorWithViewId()
        {
            var kept = KeepIdle(Target);
            var fault = new InvalidOperationException("lost kept release failed");
            _h.Stage.FailNext(StageCallKind.Release, fault);

            Assert.That(() => _h.Stage.RaiseInstanceLost(kept, true), Throws.Nothing);

            Assert.That(_h.Errors.Count, Is.EqualTo(1), "routed error count");
            Assert.That(_h.Errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(_h.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(_h.Errors[0].Context.ViewId, Is.EqualTo(Target), "error view id");
            Assert.That(_h.Errors[0].Context.RequestId, Is.Zero, "A kept instance belongs to no request.");
            Assert.That(_h.Diagnostics.FaultCount, Is.Zero, "The fault is routed, not reported as an internal fault.\n" + _h.Diagnostics.Describe());
        }
    }
}
