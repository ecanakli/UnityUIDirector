using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Every awaiter completes exactly once, and only its own token can cancel it.
    public sealed class AwaiterContractTests
    {
        private NavigatorHarness _harness;

        [SetUp]
        public void SetUp()
        {
            _harness = new NavigatorHarness();
        }

        [TearDown]
        public void TearDown()
        {
            _harness.MainThread.IsMainThread = true;
            _harness.Dispose();
        }

        // ShowAsync completes with data, exactly once, on every path

        [TestCase(ContractClosePath.ViewComplete, CloseReason.Completed)]
        [TestCase(ContractClosePath.ViewClose, CloseReason.Dismissed)]
        [TestCase(ContractClosePath.HandleClose, CloseReason.Code)]
        [TestCase(ContractClosePath.Back, CloseReason.Back)]
        [TestCase(ContractClosePath.Backdrop, CloseReason.Backdrop)]
        [TestCase(ContractClosePath.CloseAll, CloseReason.Code)]
        [TestCase(ContractClosePath.OwnerCancel, CloseReason.OwnerEnded)]
        [TestCase(ContractClosePath.InstanceDestroyed, CloseReason.Destroyed)]
        [TestCase(ContractClosePath.InstanceDeactivated, CloseReason.Destroyed)]
        [TestCase(ContractClosePath.Teardown, CloseReason.Destroyed)]
        public void ShowAsync_OfAShownView_CompletesExactlyOnce_WithTheCloseReasonAsData(ContractClosePath path, CloseReason expected)
        {
            var run = ContractShownRun.Start(_harness);
            var probe = ContractAwait.Watch(run.Result);
            Assert.That(probe.IsPending, Is.True, "The awaiter must not complete while the view is open.");

            run.End(path);

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Error, Is.Null, "ShowAsync never throws for what happens to the view.");
            Assert.That(probe.Result.WasShown, Is.True);
            Assert.That(probe.Result.Reason, Is.EqualTo(expected));
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.None));
            Assert.That(probe.Result.HasValue, Is.EqualTo(path == ContractClosePath.ViewComplete), "Only Complete(value) carries a value.");

            // Nothing that follows may complete it again.
            _harness.Dispose();
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "Teardown must not complete an awaiter twice.");
        }

        [Test]
        public void ShowAsync_CompleteWithValue_CarriesTheValue_AndHasValueIsTrue()
        {
            _harness.Row("ask", ViewLayers.Popups).Map<string, bool>("ask");
            var task = _harness.Navigator.ShowAsync(ViewKey.Create<string, bool>("ask"), "Delete?", _harness.Owner);
            var probe = ContractAwait.Watch(task);

            _harness.ViewOf<FakeView<string, bool>>("ask").Complete(true);

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Result.HasValue, Is.True);
            Assert.That(probe.Result.Value, Is.True);
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.Completed));
        }

        [TestCase(ContractNotShownPath.UnknownKey, RejectReason.UnknownKey)]
        [TestCase(ContractNotShownPath.OwnerAlreadyCancelled, RejectReason.OwnerEnded)]
        [TestCase(ContractNotShownPath.OnOpenThrows, RejectReason.Faulted)]
        [TestCase(ContractNotShownPath.OnOpenDeclines, RejectReason.Declined)]
        [TestCase(ContractNotShownPath.LoadFails, RejectReason.LoadFailed)]
        [TestCase(ContractNotShownPath.OwnerEndsWhileLoading, RejectReason.OwnerEnded)]
        [TestCase(ContractNotShownPath.HandleClosedWhileLoading, RejectReason.Cancelled)]
        [TestCase(ContractNotShownPath.TeardownWhileLoading, RejectReason.RootGone)]
        [TestCase(ContractNotShownPath.TeardownWhileWaiting, RejectReason.RootGone)]
        [TestCase(ContractNotShownPath.ShowAfterTeardown, RejectReason.RootGone)]
        public void ShowAsync_OfARequestThatIsNeverShown_CompletesExactlyOnce_WithTheRejectReasonAsData(ContractNotShownPath path, RejectReason expected)
        {
            var run = ContractNotShownRun.Start(_harness, path);
            var probe = ContractAwait.Watch(run.Result);

            run.Finish();

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Error, Is.Null, "ShowAsync never throws for what happens to the request.");
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.NotShown));
            Assert.That(probe.Result.Rejected, Is.EqualTo(expected));
            Assert.That(probe.Result.HasValue, Is.False);

            _harness.Dispose();
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "Teardown must not complete an awaiter twice.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ShowAsync_WhenTheOwnerEnds_Succeeds_ItIsNeverCancelled(bool shownFirst)
        {
            // A manual load keeps the request in Loading, so the owner ends it before it is shown.
            if (!shownFirst)
                _harness.Stage.AcquireMode = StageMode.Manual;

            var run = ContractShownRun.Start(_harness);

            run.Owner.Cancel();

            Assert.That(run.Result.Status, Is.EqualTo(UniTaskStatus.Succeeded), "The owner token never cancels an awaiter.");
            Assert.That(run.Result.ResultNow().Reason, Is.EqualTo(shownFirst ? CloseReason.OwnerEnded : CloseReason.NotShown));
        }

        // The owner decides the view, whoever awaits

        [Test]
        public void OwnerEnded_CompletesEveryAwaiterWithData_EvenWhenTheAwaitingCodeIsNotTheOwner()
        {
            _harness.Row("home");
            var ownerA = _harness.NewOwner();
            var ownerB = _harness.NewOwner();

            var handle = _harness.Navigator.OpenView("home", ownerA.Token);
            var waiter = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));
            var joiner = ContractAwait.Watch(_harness.Navigator.Show("home", ownerB.Token));

            ownerA.Cancel();

            Assert.That(waiter.ResumeCount, Is.EqualTo(1));
            Assert.That(waiter.Error, Is.Null);
            Assert.That(waiter.Result.WasShown, Is.True);
            Assert.That(waiter.Result.Reason, Is.EqualTo(CloseReason.OwnerEnded));

            Assert.That(joiner.ResumeCount, Is.EqualTo(1));
            Assert.That(joiner.Error, Is.Null);
            Assert.That(joiner.Result.Reason, Is.EqualTo(CloseReason.OwnerEnded));
            Assert.That(ownerB.IsCancellationRequested, Is.False, "The joiner's own token was never touched.");
        }

        // WaitForCloseAsync before and after the end

        [Test]
        public void WaitForCloseAsync_BeforeTheEnd_StaysPending_ThenCompletesWithTheOutcome()
        {
            var run = ContractShownRun.Start(_harness);
            ViewHandle handle;
            Assert.That(_harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle), Is.True);

            var waiter = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));

            Assert.That(waiter.IsPending, Is.True);
            Assert.That(handle.IsAlive, Is.True);
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open));

            run.View.Complete(default(NoResult));

            Assert.That(waiter.ResumeCount, Is.EqualTo(1));
            Assert.That(waiter.Result.WasShown, Is.True);
            Assert.That(waiter.Result.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(waiter.Result.Rejected, Is.EqualTo(RejectReason.None));
            Assert.That(handle.IsAlive, Is.False);
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Ended));
        }

        [Test]
        public void WaitForCloseAsync_AfterTheEnd_ReturnsTheStoredOutcomeAtOnce_EveryTime()
        {
            var run = ContractShownRun.Start(_harness);
            ViewHandle handle;
            _harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle);
            run.View.Close();

            var late = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));
            var later = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));

            Assert.That(late.ResumeCount, Is.EqualTo(1), "A late wait completes at once.");
            Assert.That(later.ResumeCount, Is.EqualTo(1), "A second late wait completes at once.");
            Assert.That(late.Result.Reason, Is.EqualTo(CloseReason.Dismissed));
            Assert.That(later.Result.Reason, Is.EqualTo(CloseReason.Dismissed));
            Assert.That(late.Result.WasShown, Is.True);
        }

        [Test]
        public void WaitForCloseAsync_AfterARequestEndedUnshown_ReturnsNotShownWithTheReason()
        {
            _harness.Row("shop", ViewLayers.Popups);
            _harness.Stage.AcquireMode = StageMode.Manual;
            var owner = _harness.NewOwner();
            var handle = _harness.Navigator.OpenView("shop", owner.Token);

            owner.Cancel();
            var late = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));
            _harness.Stage.CompleteAcquire();

            Assert.That(late.ResumeCount, Is.EqualTo(1));
            Assert.That(late.Result.WasShown, Is.False);
            Assert.That(late.Result.Reason, Is.EqualTo(CloseReason.NotShown));
            Assert.That(late.Result.Rejected, Is.EqualTo(RejectReason.OwnerEnded));
        }

        [Test]
        public void WaitForCloseAsync_ManyCallers_AllCompleteExactlyOnce_WithTheSameOutcome()
        {
            var run = ContractShownRun.Start(_harness);
            ViewHandle handle;
            _harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle);
            var waiterSource = new CancellationTokenSource();

            var first = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));
            var second = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));
            var third = ContractAwait.Watch(handle.WaitForCloseAsync(waiterSource.Token));

            run.View.Close();
            waiterSource.Cancel();
            waiterSource.Dispose();

            Assert.That(first.ResumeCount, Is.EqualTo(1));
            Assert.That(second.ResumeCount, Is.EqualTo(1));
            Assert.That(third.ResumeCount, Is.EqualTo(1), "A waiter token cancelled after the end changes nothing.");
            Assert.That(third.Error, Is.Null);
            Assert.That(first.Result.Reason, Is.EqualTo(CloseReason.Dismissed));
            Assert.That(second.Result.Reason, Is.EqualTo(CloseReason.Dismissed));
            Assert.That(third.Result.Reason, Is.EqualTo(CloseReason.Dismissed));
        }

        // Waiter cancellation

        [Test]
        public void WaitForCloseAsync_WaiterCancelled_ThrowsOnlyForThatWaiter_AndTheViewIsUntouched()
        {
            var run = ContractShownRun.Start(_harness);
            ViewHandle handle;
            _harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle);
            var cancelled = new CancellationTokenSource();

            var shower = ContractAwait.Watch(run.Result);
            var cancellable = ContractAwait.Watch(handle.WaitForCloseAsync(cancelled.Token));
            var plain = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));

            cancelled.Cancel();

            Assert.That(cancellable.ResumeCount, Is.EqualTo(1));
            Assert.That(cancellable.IsCanceled, Is.True, "Only the cancelled waiter throws OperationCanceledException.");
            Assert.That(plain.IsPending, Is.True, "Another waiter must not be cancelled.");
            Assert.That(shower.IsPending, Is.True, "The ShowAsync awaiter must not be cancelled.");
            Assert.That(handle.IsAlive, Is.True, "A waiter cancellation leaves the view untouched.");
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_harness.Stage.LastInstanceOf(ContractShownRun.DefaultId).ReleaseCount, Is.Zero);

            run.View.Close();

            Assert.That(plain.ResumeCount, Is.EqualTo(1));
            Assert.That(plain.Error, Is.Null);
            Assert.That(shower.ResumeCount, Is.EqualTo(1));
            Assert.That(shower.Error, Is.Null);
            Assert.That(cancellable.ResumeCount, Is.EqualTo(1), "The cancelled waiter is not completed a second time by the end.");
            cancelled.Dispose();
        }

        [TestCase(1)]
        [TestCase(1000)]
        public void WaitForCloseAsync_WaitersCancelledOnALiveRequest_LeaveNoEntry(int cycles)
        {
            ContractShownRun.Start(_harness);
            ViewHandle handle;
            _harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle);
            var waiters = new List<ContractAwaitProbe<ViewClosed>>(cycles);
            var tokens = new List<CancellationToken>(cycles);

            for (var i = 0; i < cycles; i++)
            {
                using (var source = new CancellationTokenSource())
                {
                    tokens.Add(source.Token);
                    waiters.Add(ContractAwait.Watch(handle.WaitForCloseAsync(source.Token)));
                    source.Cancel();
                }
            }

            Assert.That(handle.Record.CloseWaiterCount, Is.Zero, "A cancelled waiter removes its own entry from the request.");
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open), "Cancelling waiters leaves the view open.");
            Assert.That(_harness.Errors, Is.Empty, "Cancelling a waiter routes no error.");

            for (var i = 0; i < cycles; i++)
            {
                var waiter = waiters[i];
                Assert.That(waiter.ResumeCount, Is.EqualTo(1), "Cycle " + i + ": the cancelled wait resumes once.");
                Assert.That(waiter.IsCanceled, Is.True, "Cycle " + i + ": the cancelled wait ends with OperationCanceledException.");
                Assert.That(((OperationCanceledException)waiter.Error).CancellationToken, Is.EqualTo(tokens[i]), "Cycle " + i + ": the exception carries the waiter's own token.");
            }
        }

        [Test]
        public void WaitForCloseAsync_WaiterCancelledOffTheMainThread_LeavesNoEntry_AndThrowsOnlyForThatWaiter()
        {
            var run = ContractShownRun.Start(_harness);
            ViewHandle handle;
            _harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle);
            var source = new CancellationTokenSource();
            var resumeThreadId = -1;
            var cancellable = ContractAwait.Watch(
                handle.WaitForCloseAsync(source.Token),
                () => resumeThreadId = Thread.CurrentThread.ManagedThreadId);
            var plain = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));
            Exception workerError = null;
            var worker = new Thread(() =>
            {
                try
                {
                    source.Cancel();
                }
                catch (Exception exception)
                {
                    workerError = exception;
                }
            });

            worker.Start();
            var joined = worker.Join(5000);

            Assert.That(joined, Is.True, "The worker thread finishes the cancel within the deadline.");
            Assert.That(workerError, Is.Null, "Cancelling the waiter token never throws into the cancelling thread.");
            Assert.That(resumeThreadId, Is.EqualTo(worker.ManagedThreadId), "The cancelled continuation runs on the cancelling thread.");
            Assert.That(resumeThreadId, Is.Not.EqualTo(Thread.CurrentThread.ManagedThreadId), "The cancelled continuation does not run on the test thread.");
            Assert.That(handle.Record.CloseWaiterCount, Is.EqualTo(1), "Only the plain waiter keeps an entry.");
            Assert.That(cancellable.ResumeCount, Is.EqualTo(1), "The cancelled waiter resumes once.");
            Assert.That(cancellable.IsCanceled, Is.True, "The cancelled waiter ends with OperationCanceledException.");
            Assert.That(plain.IsPending, Is.True, "The plain waiter is not cancelled.");

            run.View.Close();

            Assert.That(handle.Record.CloseWaiterCount, Is.Zero, "The end clears the remaining entry.");
            Assert.That(plain.ResumeCount, Is.EqualTo(1), "The plain waiter resumes once.");
            Assert.That(plain.Error, Is.Null, "The plain waiter completes with data.");
            Assert.That(plain.Result.Reason, Is.EqualTo(CloseReason.Dismissed), "The plain waiter receives the outcome.");
            Assert.That(cancellable.ResumeCount, Is.EqualTo(1), "The end does not complete the cancelled waiter again.");
            Assert.That(_harness.Errors, Is.Empty, "An off-thread cancel routes no error.");
            source.Dispose();
        }

        [Test]
        public void WaitForCloseAsync_WaiterCancelledWhileTheViewCloses_CompletesExactlyOnce_AndLeavesNoEntry()
        {
            const int iterations = 200;

            for (var i = 0; i < iterations; i++)
            {
                var label = "Iteration " + i + ": ";
                var id = "race-" + i;
                var run = ContractShownRun.Start(_harness, id: id);
                ViewHandle handle;
                Assert.That(_harness.Navigator.TryGetOpen(id, out handle), Is.True, label + "the view is open.");

                using (var source = new CancellationTokenSource())
                using (var gate = new ManualResetEventSlim(false))
                {
                    var cancellable = ContractAwait.Watch(handle.WaitForCloseAsync(source.Token));
                    var plain = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));
                    Exception workerError = null;
                    var worker = new Thread(() =>
                    {
                        try
                        {
                            gate.Wait();
                            source.Cancel();
                        }
                        catch (Exception exception)
                        {
                            workerError = exception;
                        }
                    });

                    worker.Start();
                    gate.Set();
                    run.View.Close();
                    var joined = worker.Join(5000);

                    // Which side wins differs per run, so only order-independent facts are asserted.
                    Assert.That(joined, Is.True, label + "the cancel and the close finish without a deadlock.");
                    Assert.That(workerError, Is.Null, label + "cancelling the waiter token never throws into the cancelling thread.");
                    Assert.That(cancellable.ResumeCount, Is.EqualTo(1), label + "the cancellable waiter resumes exactly once.");
                    var gotOutcome = cancellable.Error == null && cancellable.Result.Reason == CloseReason.Dismissed;
                    Assert.That(gotOutcome || cancellable.IsCanceled, Is.True, label + "the cancellable waiter ends with either the outcome or the cancellation.");
                    Assert.That(plain.ResumeCount, Is.EqualTo(1), label + "the plain waiter resumes exactly once.");
                    Assert.That(plain.Error, Is.Null, label + "the plain waiter completes with data.");
                    Assert.That(plain.Result.Reason, Is.EqualTo(CloseReason.Dismissed), label + "the plain waiter receives the outcome.");
                    Assert.That(handle.Record.CloseWaiterCount, Is.Zero, label + "no entry remains after the race.");
                }
            }

            Assert.That(_harness.Errors, Is.Empty, "A cancel racing the close routes no error.");
        }

        [Test]
        public void WaitForCloseAsync_UncancelledWaiters_AreCountedUntilTheEnd_ThenCleared()
        {
            var run = ContractShownRun.Start(_harness);
            ViewHandle handle;
            _harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle);
            var first = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));
            var second = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));
            var countWhileOpen = handle.Record.CloseWaiterCount;

            run.View.Close();

            Assert.That(countWhileOpen, Is.EqualTo(2), "Both waiters are held while the view is open.");
            Assert.That(handle.Record.CloseWaiterCount, Is.Zero, "The end clears every entry.");
            Assert.That(first.ResumeCount, Is.EqualTo(1), "The first waiter resumes once.");
            Assert.That(second.ResumeCount, Is.EqualTo(1), "The second waiter resumes once.");
            Assert.That(first.Result.Reason, Is.EqualTo(CloseReason.Dismissed), "The first waiter receives the outcome.");
            Assert.That(second.Result.Reason, Is.EqualTo(CloseReason.Dismissed), "The second waiter receives the outcome.");
        }

        // Covers the observable result only; it cannot tell whether the end released the registration.
        [Test]
        public void WaitForCloseAsync_WaiterCancelledAfterTheEnd_KeepsItsCompletedResult()
        {
            var run = ContractShownRun.Start(_harness);
            ViewHandle handle;
            _harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle);
            var source = new CancellationTokenSource();
            var waiter = ContractAwait.Watch(handle.WaitForCloseAsync(source.Token));
            run.View.Close();

            source.Cancel();

            Assert.That(waiter.ResumeCount, Is.EqualTo(1), "A cancel after the end does not resume the waiter again.");
            Assert.That(waiter.Error, Is.Null, "A cancel after the end does not turn the result into an error.");
            Assert.That(waiter.Result.Reason, Is.EqualTo(CloseReason.Dismissed), "The waiter keeps the outcome.");
            Assert.That(handle.Record.CloseWaiterCount, Is.Zero, "No entry remains after the end.");
            Assert.That(_harness.Errors, Is.Empty, "A late cancel routes no error.");
            source.Dispose();
        }

        [Test]
        public void WaitForCloseAsync_WithAnAlreadyCancelledWaiter_OnALiveRequest_Throws_AndLeavesTheViewAlone()
        {
            var run = ContractShownRun.Start(_harness);
            ViewHandle handle;
            _harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle);
            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            var task = handle.WaitForCloseAsync(cancelled.Token);

            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.That(handle.IsAlive, Is.True);
            Assert.That(run.Result.IsPending(), Is.True);
            Assert.That(handle.Record.CloseWaiterCount, Is.Zero, "An already-cancelled waiter never adds an entry.");
            Assert.That(_harness.Errors, Is.Empty, "An already-cancelled waiter routes no error.");
            cancelled.Dispose();
        }

        // Empty handle

        [Test]
        public void WaitForCloseAsync_OnTheHandleOfARejectedOpen_CompletesAtOnce_AsNotShown()
        {
            var handle = _harness.Navigator.OpenView("missing", _harness.Owner);

            var waiter = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None));

            Assert.That(handle.IsAlive, Is.False);
            Assert.That(waiter.ResumeCount, Is.EqualTo(1));
            Assert.That(waiter.Error, Is.Null);
            Assert.That(waiter.Result.WasShown, Is.False);
            Assert.That(waiter.Result.Reason, Is.EqualTo(CloseReason.NotShown));
            Assert.DoesNotThrow(() => handle.Close(), "Close on an inert handle never throws.");
        }

        // Join order

        [Test]
        public void ShowAsyncAwaiters_CompleteInJoinOrder_BeforeTheWaitForCloseAwaiters()
        {
            _harness.Row("home");
            var order = new List<string>();
            var handle = _harness.Navigator.OpenView("home", _harness.Owner);

            // The wait registers first, so the order below comes from the close rules and not from registration.
            var wait = ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None), () => order.Add("wait"));
            var show1 = ContractAwait.Watch(_harness.Navigator.Show("home", _harness.Owner), () => order.Add("show 1"));
            var show2 = ContractAwait.Watch(_harness.Navigator.Show("home", _harness.Owner), () => order.Add("show 2"));

            handle.Close();

            Assert.That(order, Is.EqualTo(new[] { "show 1", "show 2", "wait" }), "ShowAsync awaiters resume in join order, then WaitForCloseAsync awaiters.");
            Assert.That(show1.Result.RequestId, Is.EqualTo(handle.RequestId), "A joiner receives the outcome of the existing request.");
            Assert.That(show2.Result.RequestId, Is.EqualTo(handle.RequestId));
            Assert.That(wait.ResumeCount + show1.ResumeCount + show2.ResumeCount, Is.EqualTo(3));
        }

        // Teardown

        [Test]
        public void Teardown_CompletesEveryAwaiterExactlyOnce_InEveryPhase_AndALateLoadCannotCompleteItAgain()
        {
            var shown = ContractShownRun.Start(_harness, id: "shown");
            ViewHandle shownHandle;
            _harness.Navigator.TryGetOpen("shown", out shownHandle);
            var shownWait = ContractAwait.Watch(shownHandle.WaitForCloseAsync(CancellationToken.None));
            var shownShow = ContractAwait.Watch(shown.Result);

            var loading = ContractNotShownRun.Start(_harness, ContractNotShownPath.TeardownWhileWaiting);
            var loadingShow = ContractAwait.Watch(loading.Companion);
            var waitingShow = ContractAwait.Watch(loading.Result);

            loading.Finish();

            Assert.That(shownShow.ResumeCount, Is.EqualTo(1));
            Assert.That(shownWait.ResumeCount, Is.EqualTo(1));
            Assert.That(loadingShow.ResumeCount, Is.EqualTo(1));
            Assert.That(waitingShow.ResumeCount, Is.EqualTo(1));

            Assert.That(shownShow.Result.Reason, Is.EqualTo(CloseReason.Destroyed), "I11: a shown view ends as Destroyed.");
            Assert.That(shownWait.Result.Reason, Is.EqualTo(CloseReason.Destroyed));
            Assert.That(loadingShow.Result.Rejected, Is.EqualTo(RejectReason.RootGone));
            Assert.That(waitingShow.Result.Rejected, Is.EqualTo(RejectReason.RootGone));

            Assert.That(_harness.Navigator.RootToken.IsCancellationRequested, Is.True, "I11: the root token is cancelled.");
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Diagnostics.FaultCount, Is.Zero, "A late load answer must not fault the package.");
            _harness.Stage.AssertAllReleased();
        }

        [Test]
        public void ShowAsync_AfterTeardown_CompletesAtOnce_WithRootGone_AndDoesNotThrow()
        {
            _harness.Row("home");
            _harness.Navigator.Teardown();

            UniTask<ViewResult<NoResult>> task = default(UniTask<ViewResult<NoResult>>);
            Assert.DoesNotThrow(() => task = _harness.Navigator.Show("home", _harness.Owner));

            var result = task.ResultNow();
            Assert.That(result.WasShown, Is.False);
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.RootGone));
        }
    }
}
