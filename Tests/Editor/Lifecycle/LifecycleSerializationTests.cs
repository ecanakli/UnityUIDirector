using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Serialization: one FIFO and one runner per layer, steps never interrupted, close-all withdraws waiting requests.
    public sealed class LifecycleSerializationTests
    {
        private const string Target = LifecycleDriver.Target;

        private NavigatorHarness _h;

        [SetUp]
        public void SetUp()
        {
            _h = new NavigatorHarness();
        }

        [TearDown]
        public void TearDown()
        {
            _h.Dispose();
        }

        // Every event except Settled as "Kind:viewId", in publish order.
        private string[] Trace()
        {
            var trace = new List<string>();
            var events = _h.Observer.Events;
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == ViewEventKind.Settled)
                    continue;

                trace.Add(events[i].Kind + ":" + events[i].ViewId);
            }

            return trace.ToArray();
        }

        private int AcquiresOf(string viewId)
        {
            var count = 0;
            var calls = _h.Stage.CallsOf(StageCallKind.Acquire);
            for (var i = 0; i < calls.Length; i++)
            {
                if (calls[i].ViewId == viewId)
                    count++;
            }

            return count;
        }

        // A close cause during Opening waits for the show transition and runs after it; nothing is cut short.
        private void AssertCloseRunsAfterTheOpen(Action<FakeView<NoArgs, bool>> latch, CloseReason expected)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Opening, _h.Owner);
            var handle = LifecycleDriver.HandleOf(_h, Target);
            var instance = _h.Stage.LastInstanceOf(Target);

            latch(LifecycleDriver.ViewOf(_h, Target));

            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Opening), "The open is not interrupted.");
            Assert.That(task.IsPending(), Is.True);
            Assert.That(_h.Stage.CountOf(StageCallKind.Play), Is.EqualTo(1), "No hide transition yet.");
            Assert.That(_h.Stage.CallsOf(StageCallKind.Play)[0].Token.IsCancellationRequested, Is.False, "The show step is not cancelled by a close request.");
            Assert.That(_h.Observer.CountOf(ViewEventKind.Closed), Is.Zero);
            Assert.That(_h.ShieldCount, Is.GreaterThan(0));

            // The show transition ends; the open finishes before the close starts.
            _h.Stage.CompletePlay();

            Assert.That(_h.Observer.CountOf(ViewEventKind.Shown), Is.EqualTo(1), "The open finished first.");
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Closing));
            Assert.That(_h.Stage.CountOf(StageCallKind.Play), Is.EqualTo(2));
            Assert.That(_h.ShieldCount, Is.GreaterThan(0), "I2: a close is in flight, so the shield is up.");

            _h.Stage.CompletePlay();

            var result = task.ResultNow();
            Assert.That(result.WasShown, Is.True);
            Assert.That(result.Reason, Is.EqualTo(expected));
            Assert.That(_h.Observer.KindsFor(result.RequestId), Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }));
            Assert.That(LifecycleDriver.IndexOfPlay(_h.Stage, instance, true), Is.LessThan(LifecycleDriver.IndexOfPlay(_h.Stage, instance, false)));
            LifecycleDriver.AssertClean(_h, ViewLayers.Popups);
            _h.Stage.AssertAllReleased();
        }

        [Test]
        public void CloseRequestedByTheViewDuringOpening_RunsAfterTheOpenFinishes()
        {
            AssertCloseRunsAfterTheOpen(view => view.Close(), CloseReason.Dismissed);
        }

        [Test]
        public void CompleteCalledDuringOpening_RunsAfterTheOpenFinishes()
        {
            AssertCloseRunsAfterTheOpen(view => view.Complete(true), CloseReason.Completed);
        }

        [Test]
        public void HandleCloseDuringOpening_RunsAfterTheOpenFinishes()
        {
            AssertCloseRunsAfterTheOpen(view => LifecycleDriver.HandleOf(_h, Target).Close(), CloseReason.Code);
        }

        [Test]
        public void CloseOfAnOpenView_QueuesBehindARunningOpenOnTheSameLayer()
        {
            var plain = LifecycleDriver.PlainLayer();
            using (var h = new NavigatorHarness(layers: new[] { plain }))
            {
                LifecycleDriver.AddRows(h, plain, "a", "b");
                var taskA = LifecycleDriver.Reach(h, "a", ViewPhase.Open, h.Owner);
                var instanceA = h.Stage.LastInstanceOf("a");

                // b starts opening; its show transition holds the layer's runner.
                h.Stage.PlayMode = StageMode.Manual;
                h.Navigator.Open(LifecycleDriver.KeyOf("b"), h.Owner);
                Assert.That(LifecycleDriver.HandleOf(h, "b").Phase, Is.EqualTo(ViewPhase.Opening));
                Assert.That(h.ShieldCount, Is.EqualTo(1));
                Assert.That(instanceA.Visual.Interactive, Is.True);

                LifecycleDriver.ViewOf(h, "a").Complete(true);

                // First cause latched: raycaster off, shield up, the close waits for the runner.
                Assert.That(LifecycleDriver.HandleOf(h, "a").Phase, Is.EqualTo(ViewPhase.Open));
                Assert.That(instanceA.Visual.Interactive, Is.False, "The raycaster is off once a close is pending.");
                Assert.That(h.ShieldCount, Is.EqualTo(2), "A pending close holds the shield too.");
                Assert.That(LifecycleDriver.IndexOfPlay(h.Stage, instanceA, false), Is.EqualTo(-1), "The close is queued behind the running open.");
                Assert.That(taskA.IsPending(), Is.True);

                // b's open finishes, then the runner takes a's close.
                h.Stage.CompletePlay();
                Assert.That(LifecycleDriver.HandleOf(h, "b").Phase, Is.EqualTo(ViewPhase.Open));
                Assert.That(LifecycleDriver.HandleOf(h, "a").Phase, Is.EqualTo(ViewPhase.Closing));

                h.Stage.CompletePlay();

                var result = taskA.ResultNow();
                Assert.That(result.Reason, Is.EqualTo(CloseReason.Completed));
                Assert.That(result.Value, Is.True);
                Assert.That(h.ShieldCount, Is.Zero);
            }
        }

        [Test]
        public void TwoOpens_OnOneLayer_RunInOrder_WhileTheFirstLoads()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "a", "b");
            _h.Stage.AcquireMode = StageMode.Manual;
            _h.Navigator.Open(LifecycleDriver.KeyOf("a"), _h.Owner);
            _h.Navigator.Open(LifecycleDriver.KeyOf("b"), _h.Owner);

            Assert.That(_h.Stage.PendingAcquireCount, Is.EqualTo(1), "At most one operation runs per layer.");
            Assert.That(AcquiresOf("b"), Is.Zero, "The second request waits for the first to finish.");

            _h.Stage.CompleteAcquire();
            Assert.That(AcquiresOf("b"), Is.EqualTo(1), "The second load starts after the first open finished.");
            Assert.That(LifecycleDriver.HandleOf(_h, "a").Phase, Is.EqualTo(ViewPhase.Open));

            _h.Stage.CompleteAcquire();
            Assert.That(LifecycleDriver.HandleOf(_h, "b").Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(Trace(), Is.EqualTo(new[] { "Requested:a", "Requested:b", "Shown:a", "Shown:b" }));
        }

        [Test]
        public void TwoOpens_OnOneLayer_RunInOrder_WhileTheFirstTransitionRuns()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "a", "b");
            _h.Stage.PlayMode = StageMode.Manual;
            _h.Navigator.Open(LifecycleDriver.KeyOf("a"), _h.Owner);
            _h.Navigator.Open(LifecycleDriver.KeyOf("b"), _h.Owner);

            Assert.That(LifecycleDriver.HandleOf(_h, "a").Phase, Is.EqualTo(ViewPhase.Opening));
            Assert.That(LifecycleDriver.HandleOf(_h, "b").Phase, Is.EqualTo(ViewPhase.Waiting));
            Assert.That(AcquiresOf("b"), Is.Zero);

            _h.Stage.CompletePlay();
            Assert.That(LifecycleDriver.HandleOf(_h, "a").Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(LifecycleDriver.HandleOf(_h, "b").Phase, Is.EqualTo(ViewPhase.Opening));

            _h.Stage.CompletePlay();
            Assert.That(Trace(), Is.EqualTo(new[] { "Requested:a", "Requested:b", "Shown:a", "Shown:b" }));
        }

        [Test]
        public void ThreeImmediateOpens_AreAcceptedAndShownInTheOrderTheyWereCalled()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "a", "b", "c");
            _h.Stage.AcquireMode = StageMode.Manual;
            _h.Navigator.Open(LifecycleDriver.KeyOf("a"), _h.Owner);
            _h.Navigator.Open(LifecycleDriver.KeyOf("b"), _h.Owner);
            _h.Navigator.Open(LifecycleDriver.KeyOf("c"), _h.Owner);

            Assert.That(_h.Observer.CountOf(ViewEventKind.NotShown), Is.Zero, "The burst default accepts and serializes.");

            _h.Stage.CompleteAcquire();
            _h.Stage.CompleteAcquire();
            _h.Stage.CompleteAcquire();

            Assert.That(
                Trace(),
                Is.EqualTo(new[] { "Requested:a", "Requested:b", "Requested:c", "Shown:a", "Shown:b", "Shown:c" }));
        }

        [Test]
        public void OpensOnDifferentLayers_RunConcurrently()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "p");
            _h.Row("s");
            _h.Stage.AcquireMode = StageMode.Manual;

            _h.Navigator.Open(LifecycleDriver.KeyOf("p"), _h.Owner);
            _h.Navigator.Open(ViewKey.Create("s"), _h.Owner);

            Assert.That(_h.Stage.PendingAcquireCount, Is.EqualTo(2), "Each layer has its own runner.");
            Assert.That(LifecycleDriver.HandleOf(_h, "p").Phase, Is.EqualTo(ViewPhase.Loading));
            Assert.That(LifecycleDriver.HandleOf(_h, "s").Phase, Is.EqualTo(ViewPhase.Loading));
        }

        // Close-all: withdraw waiting requests, hide the top visible view animated, finalize all by snap

        [Test]
        public void CloseAll_WithdrawsEveryWaitingRequest_BeforeTheyLoad_ThenClosesTheTopView()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "a", "b", "c", "d");
            var taskA = LifecycleDriver.Show(_h, "a", _h.Owner);
            _h.Stage.AcquireMode = StageMode.Manual;
            var taskB = LifecycleDriver.Show(_h, "b", _h.Owner);
            var taskC = LifecycleDriver.Show(_h, "c", _h.Owner);
            var taskD = LifecycleDriver.Show(_h, "d", _h.Owner);
            Assert.That(LifecycleDriver.HandleOf(_h, "b").Phase, Is.EqualTo(ViewPhase.Loading));
            Assert.That(LifecycleDriver.HandleOf(_h, "c").Phase, Is.EqualTo(ViewPhase.Waiting));

            _h.Navigator.CloseAll(ViewLayers.Popups);

            LifecycleDriver.AssertNotShown(taskB.ResultNow(), RejectReason.Cancelled);
            LifecycleDriver.AssertNotShown(taskC.ResultNow(), RejectReason.Cancelled);
            LifecycleDriver.AssertNotShown(taskD.ResultNow(), RejectReason.Cancelled);
            var resultA = taskA.ResultNow();
            Assert.That(resultA.WasShown, Is.True);
            Assert.That(resultA.Reason, Is.EqualTo(CloseReason.Code));

            var trace = Trace();
            var closedA = Array.IndexOf(trace, "Closed:a");
            Assert.That(closedA, Is.GreaterThanOrEqualTo(0));
            Assert.That(Array.IndexOf(trace, "NotShown:b"), Is.GreaterThanOrEqualTo(0));
            Assert.That(closedA, Is.GreaterThan(Array.IndexOf(trace, "NotShown:b")));
            Assert.That(closedA, Is.GreaterThan(Array.IndexOf(trace, "NotShown:c")));
            Assert.That(closedA, Is.GreaterThan(Array.IndexOf(trace, "NotShown:d")));
            Assert.That(_h.RequestCount(ViewLayers.Popups), Is.Zero);
            Assert.That(_h.ShieldCount, Is.Zero);

            // Last, so the checks above still run: the withdrawal comes first, the runner never starts the waiting loads.
            Assert.That(AcquiresOf("c"), Is.Zero, "A waiting request is withdrawn, never loaded.");
            Assert.That(AcquiresOf("d"), Is.Zero, "A waiting request is withdrawn, never loaded.");
        }

        [Test]
        public void CloseAll_WithdrawsWaitingRequestsAtCallTime_AndTheCloseRunsAfterTheOpenThatIsRunning()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "a", "b", "c");
            _h.Stage.PlayMode = StageMode.Manual;
            var taskA = LifecycleDriver.Show(_h, "a", _h.Owner);
            var taskB = LifecycleDriver.Show(_h, "b", _h.Owner);
            var taskC = LifecycleDriver.Show(_h, "c", _h.Owner);
            Assert.That(LifecycleDriver.HandleOf(_h, "a").Phase, Is.EqualTo(ViewPhase.Opening));

            _h.Navigator.CloseAll(ViewLayers.Popups);

            LifecycleDriver.AssertNotShown(taskB.ResultNow(), RejectReason.Cancelled);
            LifecycleDriver.AssertNotShown(taskC.ResultNow(), RejectReason.Cancelled);
            Assert.That(AcquiresOf("b"), Is.Zero);
            Assert.That(AcquiresOf("c"), Is.Zero);

            // The open that is running is never interrupted: the close of everything runs after it.
            Assert.That(LifecycleDriver.HandleOf(_h, "a").Phase, Is.EqualTo(ViewPhase.Opening));
            Assert.That(taskA.IsPending(), Is.True);

            _h.Stage.CompletePlay();
            Assert.That(LifecycleDriver.HandleOf(_h, "a").Phase, Is.EqualTo(ViewPhase.Closing));
            Assert.That(_h.ShieldCount, Is.GreaterThan(0), "I2: the close-all operation is in flight, so the shield is up.");

            _h.Stage.CompletePlay();
            var resultA = taskA.ResultNow();
            Assert.That(resultA.WasShown, Is.True);
            Assert.That(resultA.Reason, Is.EqualTo(CloseReason.Code));
            Assert.That(_h.ShieldCount, Is.Zero);
        }

        [Test]
        public void CloseAll_HidesTheTopViewAnimated_AndSnapsEveryOtherView()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "a", "b", "c");
            var taskA = LifecycleDriver.Show(_h, "a", _h.Owner);
            var taskB = LifecycleDriver.Show(_h, "b", _h.Owner);
            var taskC = LifecycleDriver.Show(_h, "c", _h.Owner);
            var instanceA = _h.Stage.LastInstanceOf("a");
            var instanceB = _h.Stage.LastInstanceOf("b");
            var instanceC = _h.Stage.LastInstanceOf("c");
            _h.Stage.ClearCalls();

            _h.Navigator.CloseAll(ViewLayers.Popups);

            // One animated hide, for the top view only.
            var plays = _h.Stage.CallsOf(StageCallKind.Play);
            Assert.That(plays.Length, Is.EqualTo(1));
            Assert.That(ReferenceEquals(plays[0].Instance, instanceC), Is.True);
            Assert.That(plays[0].On, Is.False);

            // The others are finalized by snap, and the top view is not snapped.
            Assert.That(LifecycleDriver.IndexOfCall(_h.Stage, StageCallKind.Snap, instanceA), Is.GreaterThanOrEqualTo(0));
            Assert.That(LifecycleDriver.IndexOfCall(_h.Stage, StageCallKind.Snap, instanceB), Is.GreaterThanOrEqualTo(0));
            Assert.That(LifecycleDriver.IndexOfCall(_h.Stage, StageCallKind.Snap, instanceC), Is.EqualTo(-1));

            // The top view is hidden before anything is finalized.
            var releases = _h.Stage.CallsOf(StageCallKind.Release);
            Assert.That(releases.Length, Is.EqualTo(3));
            var hideIndex = LifecycleDriver.IndexOfPlay(_h.Stage, instanceC, false);
            Assert.That(hideIndex, Is.LessThan(LifecycleDriver.IndexOfCall(_h.Stage, StageCallKind.Release, instanceA)));
            Assert.That(hideIndex, Is.LessThan(LifecycleDriver.IndexOfCall(_h.Stage, StageCallKind.Release, instanceB)));

            Assert.That(taskA.ResultNow().Reason, Is.EqualTo(CloseReason.Code));
            Assert.That(taskB.ResultNow().Reason, Is.EqualTo(CloseReason.Code));
            Assert.That(taskC.ResultNow().Reason, Is.EqualTo(CloseReason.Code));
            _h.Stage.AssertAllReleased();
            LifecycleDriver.AssertClean(_h, ViewLayers.Popups);
        }
    }
}
