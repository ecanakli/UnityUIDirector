using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Threading: the package is main-thread only; a stray thread is turned into a post.
    public sealed class ThreadingContractTests
    {
        private NavigatorHarness _harness;
        private List<ViewCatalog> _catalogs;

        [SetUp]
        public void SetUp()
        {
            _harness = new NavigatorHarness();
            _catalogs = new List<ViewCatalog>();
        }

        [TearDown]
        public void TearDown()
        {
            // Teardown removes registrations on the main thread; a fake that still says "other thread" would only post them.
            _harness.MainThread.IsMainThread = true;
            _harness.Dispose();

            for (var i = 0; i < _catalogs.Count; i++)
                UnityEngine.Object.DestroyImmediate(_catalogs[i]);
        }

        // Calls from another thread

        [Test]
        public void OpenAndShowAsync_OffTheMainThread_ThrowInvalidOperationException_AtTheCall_AndChangeNothing()
        {
            _harness.Row("p", ViewLayers.Popups);

            OffMainThread();
            Assert.Throws<InvalidOperationException>(() => _harness.Navigator.OpenView("p", _harness.Owner));
            Assert.Throws<InvalidOperationException>(() => _harness.Navigator.Show("p", _harness.Owner));
            OnMainThread();

            Assert.That(_harness.RequestCount(ViewLayers.Popups), Is.Zero, "A refused call leaves no request behind.");
            Assert.That(_harness.ShieldCount, Is.Zero);
            Assert.That(_harness.Observer.Count, Is.Zero, "A refused call publishes no event.");
            Assert.That(_harness.Stage.CountOf(StageCallKind.Acquire), Is.Zero);
        }

        [Test]
        public void ShowAsyncWithArguments_OffTheMainThread_Throws_AtTheCall()
        {
            _harness.Row("ask", ViewLayers.Popups).Map<string, bool>("ask");

            OffMainThread();

            Assert.Throws<InvalidOperationException>(
                () => _harness.Navigator.ShowAsync(ViewKey.Create<string, bool>("ask"), "Delete?", _harness.Owner));
        }

        [Test]
        public void RegistrationAndPreloadCalls_OffTheMainThread_ThrowInvalidOperationException()
        {
            var catalog = ContractRows.Catalog(ContractRows.Entry("a", ViewLayers.Popups.Id));
            _catalogs.Add(catalog);
            _harness.Row("p", ViewLayers.Popups);
            var observer = new RecordingObserver();

            OffMainThread();

            Assert.Throws<InvalidOperationException>(() => _harness.Navigator.HoldInput("test", _harness.Owner));
            Assert.Throws<InvalidOperationException>(() => _harness.Navigator.AddObserver(observer, _harness.Owner));
            Assert.Throws<InvalidOperationException>(() => _harness.Navigator.AddCatalog(catalog, _harness.Owner));
            Assert.Throws<InvalidOperationException>(() => _harness.Navigator.PreloadAsync("p", _harness.Owner));

            OnMainThread();
            Assert.That(_harness.ShieldCount, Is.Zero, "No hold was taken.");
        }

        [Test]
        public void TheNeverThrowCalls_OffTheMainThread_DoNotThrow()
        {
            _harness.Row("a");
            _harness.Navigator.OpenView("a", _harness.Owner);

            OffMainThread();

            Assert.DoesNotThrow(() => _harness.Navigator.Back());
            Assert.DoesNotThrow(() => _harness.Navigator.CloseTo("a"));
            Assert.DoesNotThrow(() => _harness.Navigator.CloseAll(ViewLayers.Screens));
            Assert.DoesNotThrow(() => _harness.Navigator.Trim());

            OnMainThread();
            _harness.MainThread.Flush();
            Assert.That(_harness.Errors, Is.Empty);
            Assert.That(_harness.Diagnostics.FaultCount, Is.Zero);
        }

        [Test]
        public void Back_OffTheMainThread_ReturnsFalseAndClosesNothing()
        {
            _harness.Row("a").Row("b");
            _harness.Navigator.OpenView("a", _harness.Owner);
            _harness.Navigator.OpenView("b", _harness.Owner);

            OffMainThread();
            var handled = _harness.Navigator.Back();
            OnMainThread();

            Assert.That(handled, Is.False, "Back off the main thread reports that it handled nothing.");
            Assert.That(_harness.Navigator.IsOpen("b"), Is.True, "The top screen stays open.");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(2), "The screen stack keeps its depth.");
            Assert.That(_harness.MainThread.PendingCount, Is.Zero, "Back is refused, not posted.");
            Assume.That(_harness.Navigator.Back(), Is.True, "control: on the main thread the same call closes the top screen");
        }

        [Test]
        public void CloseTo_OffTheMainThread_ReturnsFalseAndClosesNothing()
        {
            _harness.Row("a").Row("b").Row("c");
            _harness.Navigator.OpenView("a", _harness.Owner);
            _harness.Navigator.OpenView("b", _harness.Owner);
            _harness.Navigator.OpenView("c", _harness.Owner);

            OffMainThread();
            var popped = _harness.Navigator.CloseTo("a");
            OnMainThread();

            Assert.That(popped, Is.False, "CloseTo off the main thread reports that it popped nothing.");
            Assert.That(_harness.Navigator.IsOpen("b"), Is.True, "The screen between stays open.");
            Assert.That(_harness.Navigator.IsOpen("c"), Is.True, "The top screen stays open.");
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(3), "The screen stack keeps its depth.");
            Assert.That(_harness.MainThread.PendingCount, Is.Zero, "CloseTo is refused, not posted.");
            Assume.That(_harness.Navigator.CloseTo("a"), Is.True, "control: on the main thread the same call pops back to the target");
        }

        [Test]
        public void CloseAll_OffTheMainThread_IsPostedAndClosesTheViewOnlyAfterFlush()
        {
            _harness.Row("p", ViewLayers.Popups);
            _harness.Navigator.OpenView("p", _harness.Owner);

            OffMainThread();
            _harness.Navigator.CloseAll(ViewLayers.Popups);

            Assert.That(_harness.MainThread.PendingCount, Is.EqualTo(1), "CloseAll off the main thread is posted.");
            Assert.That(_harness.Navigator.IsOpen("p"), Is.True, "Nothing closes before the main thread runs the post.");

            OnMainThread();
            _harness.MainThread.Flush();

            Assert.That(_harness.Navigator.IsOpen("p"), Is.False, "The posted CloseAll closes the view on the main thread.");
            Assert.That(_harness.Errors, Is.Empty);
        }

        [Test]
        public void Trim_OffTheMainThread_IsPostedAndReleasesTheKeptInstanceOnlyAfterFlush()
        {
            _harness.Row("k", ViewLayers.Popups, ViewRetention.KeepInstance);
            var handle = _harness.Navigator.OpenView("k", _harness.Owner);
            handle.Close();
            var kept = _harness.Stage.LastInstanceOf("k");
            Assume.That(kept.Stashed, Is.True, "control: the closed instance is kept");

            OffMainThread();
            _harness.Navigator.Trim();

            Assert.That(_harness.MainThread.PendingCount, Is.EqualTo(1), "Trim off the main thread is posted.");
            Assert.That(kept.ReleaseCount, Is.Zero, "Nothing is released before the main thread runs the post.");

            OnMainThread();
            _harness.MainThread.Flush();

            Assert.That(kept.ReleaseCount, Is.EqualTo(1), "The posted Trim releases the kept instance once.");
        }

        [Test]
        public void CloseAll_PostedThenTeardown_DoesNotTouchTheDeadNavigator()
        {
            _harness.Row("p", ViewLayers.Popups);
            _harness.Navigator.OpenView("p", _harness.Owner);
            OffMainThread();
            _harness.Navigator.CloseAll(ViewLayers.Popups);
            Assume.That(_harness.MainThread.PendingCount, Is.EqualTo(1), "control: CloseAll was posted");
            OnMainThread();
            _harness.Navigator.Teardown();
            _harness.Stage.ClearCalls();

            var ran = 0;
            Assert.DoesNotThrow(() => ran = _harness.MainThread.Flush(), "A post that outlived the navigator runs without throwing.");

            Assert.That(ran, Is.EqualTo(1), "The post still runs; it just finds the navigator gone.");
            Assert.That(_harness.MainThread.PendingCount, Is.Zero, "The post schedules nothing further.");
            Assert.That(_harness.RequestCount(ViewLayers.Popups), Is.Zero, "The layer stays empty after teardown.");
            Assert.That(_harness.Stage.Calls, Is.Empty, "The post makes no stage call after teardown.\n" + _harness.Stage.DescribeCalls());
            Assert.That(_harness.Errors, Is.Empty);
            Assert.That(_harness.Diagnostics.FaultCount, Is.Zero);
        }

        [Test]
        public void Trim_PostedThenTeardown_ReleasesNothingTwice()
        {
            _harness.Row("k", ViewLayers.Popups, ViewRetention.KeepInstance);
            var handle = _harness.Navigator.OpenView("k", _harness.Owner);
            handle.Close();
            var kept = _harness.Stage.LastInstanceOf("k");
            Assume.That(kept.Stashed, Is.True, "control: the closed instance is kept");
            OffMainThread();
            _harness.Navigator.Trim();
            Assume.That(_harness.MainThread.PendingCount, Is.EqualTo(1), "control: Trim was posted");
            OnMainThread();
            _harness.Navigator.Teardown();
            Assert.That(kept.ReleaseCount, Is.EqualTo(1), "Teardown releases the kept instance once.");

            var ran = _harness.MainThread.Flush();

            Assert.That(ran, Is.EqualTo(1), "The posted Trim still runs after teardown.");
            Assert.That(kept.ReleaseCount, Is.EqualTo(1), "The posted Trim does not release the instance teardown already released.");
            Assert.That(_harness.Stage.CountOf(StageCallKind.Release), Is.EqualTo(1), "The posted Trim makes no release call of its own.");
            Assert.That(_harness.Diagnostics.FaultCount, Is.Zero);
        }

        [Test]
        public void RegistrationRemove_OffTheMainThread_IsPosted_AndRunsOnTheMainThread()
        {
            var observer = new RecordingObserver();
            var registration = _harness.Navigator.AddObserver(observer, _harness.Owner);

            OffMainThread();
            Assert.DoesNotThrow(() => registration.Remove(), "Remove is safe to call from any thread.");

            Assert.That(_harness.MainThread.PendingCount, Is.EqualTo(1), "Remove off the main thread is posted to the main thread.");
            Assert.That(registration.IsActive, Is.True, "Nothing is removed until the main thread runs the post.");

            _harness.MainThread.Flush();

            Assert.That(registration.IsActive, Is.False);
        }

        [Test]
        public void RegistrationRemove_OnTheMainThread_RunsAtOnce_AndPostsNothing()
        {
            var registration = _harness.Navigator.AddObserver(new RecordingObserver(), _harness.Owner);

            registration.Remove();

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_harness.MainThread.PostedCount, Is.Zero);
        }

        // Owner cancelled on another thread

        [Test]
        public void OwnerCancelledOffTheMainThread_OfAShownView_IsPosted_AndTheViewEndsOnTheMainThread_WithUIDIRECTOR113()
        {
            var scopeEndOnMainThread = false;
            var continuationOnMainThread = false;
            var run = ContractShownRun.Start(_harness, view => view.BindOnOpen = scope => scope.OnEnd(() => scopeEndOnMainThread = _harness.MainThread.IsMainThread));
            var probe = ContractAwait.Watch(run.Result, () => continuationOnMainThread = _harness.MainThread.IsMainThread);

            OffMainThread();
            run.Owner.Cancel();

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR113"), Is.EqualTo(1), "The off-thread cancel is reported.");
            Assert.That(_harness.MainThread.PendingCount, Is.EqualTo(1), "The end is posted, not run on the cancelling thread.");
            Assert.That(probe.IsPending, Is.True, "No awaiter completes on the cancelling thread.");
            Assert.That(_harness.Navigator.IsOpen(ContractShownRun.DefaultId), Is.True);

            _harness.MainThread.Flush();
            OnMainThread();

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Result.WasShown, Is.True);
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.OwnerEnded));
            Assert.That(scopeEndOnMainThread, Is.True, "The scopes end on the main thread.");
            Assert.That(continuationOnMainThread, Is.True, "The awaiter resumes on the main thread.");
            Assert.That(_harness.Navigator.IsOpen(ContractShownRun.DefaultId), Is.False);
            Assert.That(_harness.Errors, Is.Empty);
        }

        [Test]
        public void OwnerCancelledOffTheMainThread_WhileLoading_IsPosted_AndTheRequestEndsUnshownOnTheMainThread()
        {
            _harness.Stage.AcquireMode = StageMode.Manual;
            var run = ContractShownRun.Start(_harness);
            var probe = ContractAwait.Watch(run.Result);

            OffMainThread();
            run.Owner.Cancel();

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR113"), Is.EqualTo(1));
            Assert.That(_harness.MainThread.PendingCount, Is.EqualTo(1));
            Assert.That(probe.IsPending, Is.True);

            _harness.MainThread.Flush();
            OnMainThread();

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Result.WasShown, Is.False);
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.OwnerEnded));

            // The cancelled load answers late; the stage releases that instance.
            _harness.Stage.CompleteAcquire();
            _harness.Stage.AssertAllReleased();
            Assert.That(_harness.ShieldCount, Is.Zero);
        }

        [Test]
        public void OwnerCancelledOnTheMainThread_EndsTheViewAtOnce_WithoutPostingOrUIDIRECTOR113()
        {
            var run = ContractShownRun.Start(_harness);
            var probe = ContractAwait.Watch(run.Result);

            run.Owner.Cancel();

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The end is handled inside the cancel call.");
            Assert.That(_harness.MainThread.PostedCount, Is.Zero);
            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR113"), Is.Zero);
        }

        [Test]
        public void OwnerCancelledOffTheMainThread_AfterTheRequestEnded_PostsNothing_AndReportsNothing()
        {
            var run = ContractShownRun.Start(_harness);
            run.View.Close();

            OffMainThread();
            run.Owner.Cancel();
            OnMainThread();

            Assert.That(_harness.MainThread.PostedCount, Is.Zero, "The owner callback is gone once the request ended.");
            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR113"), Is.Zero);
        }

        [Test]
        public void ARegistrationOwnerCancelledOffTheMainThread_IsRemovedOnTheMainThread_WithUIDIRECTOR113()
        {
            var owner = _harness.NewOwner();
            var hold = _harness.Navigator.HoldInput("test", owner.Token);
            Assert.That(_harness.ShieldCount, Is.EqualTo(1));

            OffMainThread();
            owner.Cancel();

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR113"), Is.EqualTo(1));
            Assert.That(_harness.MainThread.PendingCount, Is.EqualTo(1), "The removal is posted.");
            Assert.That(_harness.ShieldCount, Is.EqualTo(1), "The hold stays until the main thread runs the post.");

            _harness.MainThread.Flush();
            OnMainThread();

            Assert.That(hold.IsActive, Is.False);
            Assert.That(_harness.ShieldCount, Is.Zero);
        }

        // Helpers

        private void OffMainThread()
        {
            _harness.MainThread.IsMainThread = false;
        }

        private void OnMainThread()
        {
            _harness.MainThread.IsMainThread = true;
        }
    }
}
