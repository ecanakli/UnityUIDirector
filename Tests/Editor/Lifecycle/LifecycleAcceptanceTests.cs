using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Acceptance: every rejection at acceptance, in order, the burst default and the sort band rule.
    public sealed class LifecycleAcceptanceTests
    {
        // Where the close-all of the first popup stands when the same id opens again.
        public enum CloseAllWindow
        {
            TopHidePlaying,
            QueuedBehindARunningOpen
        }

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

        private CancellationToken CancelledOwner()
        {
            var source = _h.NewOwner();
            source.Cancel();
            return source.Token;
        }

        private int Acquires
        {
            get { return _h.Stage.CountOf(StageCallKind.Acquire); }
        }

        // Popups p0..p5: p0 is loading and p1..p4 wait, so the layer sits exactly at its waiting cap.
        private void FillWaitingCap()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "p0", "p1", "p2", "p3", "p4", "p5");
            _h.Stage.AcquireMode = StageMode.Manual;
            for (var i = 0; i < 5; i++)
                _h.Navigator.Open(LifecycleDriver.KeyOf("p" + i), _h.Owner);
        }

        // The target popup is shown, plays are manual, and a close-all of the popups layer is in the given window.
        private UniTask<ViewResult<bool>> ReachCloseAllWindow(CloseAllWindow window)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, LifecycleDriver.Target);

            UniTask<ViewResult<bool>> first;
            if (window == CloseAllWindow.QueuedBehindARunningOpen)
            {
                first = LifecycleDriver.Reach(_h, LifecycleDriver.Target, ViewPhase.Opening, _h.Owner);
            }
            else
            {
                first = LifecycleDriver.Reach(_h, LifecycleDriver.Target, ViewPhase.Open, _h.Owner);
                _h.Stage.PlayMode = StageMode.Manual;
            }

            _h.Navigator.CloseAll(ViewLayers.Popups);
            Assert.That(_h.Stage.PendingPlayCount, Is.EqualTo(1), "A show or hide of the first popup is pending.\n" + _h.Describe());
            return first;
        }

        // Completes every pending play, including the ones the completions start.
        private void CompletePendingPlays()
        {
            for (var completed = 0; _h.Stage.PendingPlayCount > 0; completed++)
            {
                Assert.That(completed, Is.LessThan(100), "Plays keep starting.\n" + _h.Stage.DescribeCalls());
                _h.Stage.CompletePlay();
            }
        }

        // The operation is over: no shield holder, the shield is down and nothing runs or waits.
        private void AssertSettled()
        {
            Assert.That(_h.ShieldCount, Is.Zero, "Shield holders left.\n" + _h.Describe());
            Assert.That(_h.Stage.ShieldBlocking, Is.False, "The shield is still up.");
            Assert.That(_h.Navigator.IsBusy, Is.False, "navigator busy");
        }

        // Order of the checks: root gone, owner cancelled, unknown key, duplicate, RejectWhenBusy, waiting cap

        [Test]
        public void RootGone_IsCheckedFirst_EvenWhenTheOwnerIsCancelledAndTheKeyIsUnknown()
        {
            var owner = CancelledOwner();
            _h.Navigator.Teardown();

            var result = _h.Navigator.ShowAsync(ViewKey.Create("missing"), owner).ResultNow();

            LifecycleDriver.AssertNotShown(result, RejectReason.RootGone);
            Assert.That(_h.Diagnostics.Has(DiagnosticIds.UnknownViewId), Is.False, "The key is never looked up after the root is gone.");
            Assert.That(Acquires, Is.Zero);
        }

        [Test]
        public void RootGone_RejectsAnOpenWithAnInertHandle()
        {
            _h.Row("a");
            _h.Navigator.Teardown();

            var handle = _h.Navigator.Open(ViewKey.Create("a"), _h.Owner);

            Assert.That(handle.IsAlive, Is.False);
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Ended));
            Assert.That(Acquires, Is.Zero);
        }

        [Test]
        public void OwnerAlreadyCancelled_RejectsAsOwnerEnded_BeforeTheKeyIsLookedUp()
        {
            var owner = CancelledOwner();

            var result = _h.Navigator.ShowAsync(ViewKey.Create("missing"), owner).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.OwnerEnded, "missing", 0, 0);
            Assert.That(_h.Diagnostics.Has(DiagnosticIds.UnknownViewId), Is.False, "The key is never looked up for a cancelled owner.");
        }

        [Test]
        public void OwnerAlreadyCancelled_RejectsAKnownKey_WithoutShieldLoadOrRecord()
        {
            _h.Row("a");
            var owner = CancelledOwner();

            var result = _h.Navigator.ShowAsync(ViewKey.Create("a"), owner).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.OwnerEnded, "a", 0, 0);
            Assert.That(_h.RequestCount(ViewLayers.Screens), Is.Zero);
            Assert.That(_h.Stage.Instances, Is.Empty);
            Assert.That(_h.Observer.CountOf(ViewEventKind.Requested), Is.Zero);
        }

        [Test]
        public void OwnerAlreadyCancelled_WinsOverDuplicate()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, LifecycleDriver.Target);
            LifecycleDriver.Reach(_h, LifecycleDriver.Target, ViewPhase.Loading, _h.Owner);
            var shieldBefore = _h.ShieldCount;
            var acquiresBefore = Acquires;

            var result = LifecycleDriver.Show(_h, LifecycleDriver.Target, CancelledOwner()).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.OwnerEnded, LifecycleDriver.Target, shieldBefore, acquiresBefore);
        }

        [Test]
        public void UnknownKey_RejectsAndLogsUIDIRECTOR101()
        {
            var result = _h.Navigator.ShowAsync(ViewKey.Create("missing"), _h.Owner).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.UnknownKey, "missing", 0, 0);
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.UnknownViewId), Is.EqualTo(1));
        }

        [Test]
        public void UnknownKey_WinsOverRejectWhenBusy_WhileTheNavigatorIsBusy()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, LifecycleDriver.Target);
            LifecycleDriver.Reach(_h, LifecycleDriver.Target, ViewPhase.Loading, _h.Owner);
            var shieldBefore = _h.ShieldCount;
            var acquiresBefore = Acquires;
            var options = new OpenOptions { RejectWhenBusy = true };

            var result = _h.Navigator.ShowAsync(ViewKey.Create("missing"), _h.Owner, options).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.UnknownKey, "missing", shieldBefore, acquiresBefore);
        }

        [Test]
        public void Duplicate_OnANonHistoryLayer_EndsTheNewRequestAsDuplicate_WhileTheFirstIsInFlightOrOpen(
            [Values(ViewPhase.Loading, ViewPhase.Open)] ViewPhase phase)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, LifecycleDriver.Target);
            LifecycleDriver.Reach(_h, LifecycleDriver.Target, phase, _h.Owner);
            var first = LifecycleDriver.HandleOf(_h, LifecycleDriver.Target);
            var shieldBefore = _h.ShieldCount;
            var acquiresBefore = Acquires;

            var result = LifecycleDriver.Show(_h, LifecycleDriver.Target, _h.Owner).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.Duplicate, LifecycleDriver.Target, shieldBefore, acquiresBefore);
            Assert.That(first.IsAlive, Is.True, "The first request is untouched.");
            Assert.That(first.Phase, Is.EqualTo(phase));
            Assert.That(_h.RequestCount(ViewLayers.Popups), Is.EqualTo(1));
        }

        [Test]
        public void Duplicate_WinsOverRejectWhenBusy()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, LifecycleDriver.Target);
            LifecycleDriver.Reach(_h, LifecycleDriver.Target, ViewPhase.Loading, _h.Owner);
            var shieldBefore = _h.ShieldCount;
            var acquiresBefore = Acquires;
            var options = new OpenOptions { RejectWhenBusy = true };

            var result = LifecycleDriver.Show(_h, LifecycleDriver.Target, _h.Owner, options).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.Duplicate, LifecycleDriver.Target, shieldBefore, acquiresBefore);
        }

        [Test]
        public void Duplicate_WinsOverTheWaitingCap()
        {
            FillWaitingCap();
            var shieldBefore = _h.ShieldCount;
            var acquiresBefore = Acquires;

            var result = LifecycleDriver.Show(_h, "p1", _h.Owner).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.Duplicate, "p1", shieldBefore, acquiresBefore);
            Assert.That(_h.Diagnostics.Has(DiagnosticIds.WaitingCapReached), Is.False, "The cap check comes after the duplicate check.");
        }

        [Test]
        public void RejectWhenBusy_WinsOverTheWaitingCap()
        {
            FillWaitingCap();
            var shieldBefore = _h.ShieldCount;
            var acquiresBefore = Acquires;
            var options = new OpenOptions { RejectWhenBusy = true };

            var result = LifecycleDriver.Show(_h, "p5", _h.Owner, options).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.Busy, "p5", shieldBefore, acquiresBefore);
            Assert.That(_h.Diagnostics.Has(DiagnosticIds.WaitingCapReached), Is.False, "RejectWhenBusy is checked before the cap, so no cap diagnostic.");
        }

        // Duplicates

        [Test]
        public void Duplicate_IsOnlyCountedAgainstLiveRequests_AnIdThatEndedCanOpenAgain()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, LifecycleDriver.Target);
            var firstTask = LifecycleDriver.Reach(_h, LifecycleDriver.Target, ViewPhase.Open, _h.Owner);
            LifecycleDriver.ViewOf(_h, LifecycleDriver.Target).Complete(true);
            Assert.That(firstTask.ResultNow().Reason, Is.EqualTo(CloseReason.Completed));

            var secondTask = LifecycleDriver.Show(_h, LifecycleDriver.Target, _h.Owner);

            Assert.That(secondTask.IsPending(), Is.True, "The second open is accepted and shown.");
            Assert.That(LifecycleDriver.HandleOf(_h, LifecycleDriver.Target).Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_h.Stage.Instances.Count, Is.EqualTo(2));
        }

        [Test]
        public void Duplicate_OnAHistoryLayer_IsNotRejected_TheCallerReturnsToTheExistingRequest()
        {
            _h.Row("s");
            var first = _h.Navigator.ShowAsync(ViewKey.Create("s"), _h.Owner);

            var second = _h.Navigator.ShowAsync(ViewKey.Create("s"), _h.Owner);

            Assert.That(first.IsPending(), Is.True);
            Assert.That(second.IsPending(), Is.True, "The second caller joins the open request.");
            Assert.That(_h.Observer.CountOf(ViewEventKind.NotShown), Is.Zero);
            Assert.That(_h.Stage.Instances.Count, Is.EqualTo(1), "A screen is not instantiated twice.");
        }

        [Test]
        public void Duplicate_OfAPopupThatCloseAllIsRemoving_IsAcceptedAndOpensAfterTheClose([Values] CloseAllWindow window)
        {
            var first = ReachCloseAllWindow(window);

            var second = LifecycleDriver.Show(_h, LifecycleDriver.Target, _h.Owner);

            Assert.That(second.IsPending(), Is.True, "The second open is accepted, not refused as a duplicate.");
            Assert.That(_h.Observer.CountOf(ViewEventKind.NotShown), Is.Zero, "No NotShown is published.");

            CompletePendingPlays();

            var firstResult = first.ResultNow();
            Assert.That(firstResult.WasShown, Is.True, "first was shown");
            Assert.That(firstResult.Reason, Is.EqualTo(CloseReason.Code), "first reason");
            Assert.That(_h.RequestCount(ViewLayers.Popups), Is.EqualTo(1), "Only the new request remains.\n" + _h.Describe());
            var current = LifecycleDriver.HandleOf(_h, LifecycleDriver.Target);
            Assert.That(current.RequestId, Is.Not.EqualTo(firstResult.RequestId), "the remaining request is the new one");
            Assert.That(current.Phase, Is.EqualTo(ViewPhase.Open), "new request phase");
            Assert.That(second.IsPending(), Is.True, "second pending");
            AssertSettled();
        }

        [Test]
        public void AllowMultiple_AcceptsASecondRequestForTheSameIdOnANonHistoryLayer()
        {
            _h.Row("p", ViewLayers.Popups, allowMultiple: true).Map<NoArgs, bool>("p");

            var first = _h.Navigator.Open(LifecycleDriver.KeyOf("p"), _h.Owner);
            var second = _h.Navigator.Open(LifecycleDriver.KeyOf("p"), _h.Owner);

            Assert.That(first.IsAlive, Is.True);
            Assert.That(second.IsAlive, Is.True);
            Assert.That(second.RequestId, Is.Not.EqualTo(first.RequestId));
            Assert.That(_h.RequestCount(ViewLayers.Popups), Is.EqualTo(2));
            Assert.That(_h.Stage.Instances.Count, Is.EqualTo(2));
            Assert.That(_h.Observer.CountOf(ViewEventKind.NotShown), Is.Zero);
        }

        // RejectWhenBusy

        [Test]
        public void RejectWhenBusy_WhileAnOperationRuns_EndsAsBusy_WithoutTheCapDiagnostic()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "a", "b");
            _h.Stage.AcquireMode = StageMode.Manual;
            _h.Navigator.Open(LifecycleDriver.KeyOf("a"), _h.Owner);
            var shieldBefore = _h.ShieldCount;
            var acquiresBefore = Acquires;
            var options = new OpenOptions { RejectWhenBusy = true };

            var result = LifecycleDriver.Show(_h, "b", _h.Owner, options).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.Busy, "b", shieldBefore, acquiresBefore);
            Assert.That(_h.Diagnostics.Has(DiagnosticIds.WaitingCapReached), Is.False);
            Assert.That(_h.RequestCount(ViewLayers.Popups), Is.EqualTo(1));
        }

        [Test]
        public void RejectWhenBusy_WhileAnInputHoldExists_EndsAsBusy_AndAcceptsAgainOnceTheHoldIsRemoved()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, LifecycleDriver.Target);
            var hold = _h.Navigator.HoldInput("test hold", _h.Owner);
            var shieldBefore = _h.ShieldCount;
            var options = new OpenOptions { RejectWhenBusy = true };

            Assert.That(_h.Navigator.IsBusy, Is.True);
            var rejected = LifecycleDriver.Show(_h, LifecycleDriver.Target, _h.Owner, options).ResultNow();
            LifecycleDriver.AssertRejectedAtAcceptance(_h, rejected, RejectReason.Busy, LifecycleDriver.Target, shieldBefore, 0);

            hold.Remove();
            Assert.That(_h.Navigator.IsBusy, Is.False);

            var accepted = LifecycleDriver.Show(_h, LifecycleDriver.Target, _h.Owner, options);
            Assert.That(accepted.IsPending(), Is.True, "Nothing is busy any more, so the open is accepted and shown.");
            Assert.That(LifecycleDriver.HandleOf(_h, LifecycleDriver.Target).Phase, Is.EqualTo(ViewPhase.Open));
        }

        [Test]
        public void RejectWhenBusy_IsNavigatorWide_AnOperationOnAnotherLayerCounts()
        {
            _h.Row("s");
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "p");
            _h.Stage.AcquireMode = StageMode.Manual;
            _h.Navigator.Open(ViewKey.Create("s"), _h.Owner);
            var shieldBefore = _h.ShieldCount;
            var acquiresBefore = Acquires;
            var options = new OpenOptions { RejectWhenBusy = true };

            var result = LifecycleDriver.Show(_h, "p", _h.Owner, options).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.Busy, "p", shieldBefore, acquiresBefore);
        }

        [Test]
        public void RejectWhenBusy_WhenNothingIsBusy_IsAcceptedAndShown()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, LifecycleDriver.Target);
            var options = new OpenOptions { RejectWhenBusy = true };

            var task = LifecycleDriver.Show(_h, LifecycleDriver.Target, _h.Owner, options);

            Assert.That(task.IsPending(), Is.True);
            var handle = LifecycleDriver.HandleOf(_h, LifecycleDriver.Target);
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_h.Observer.KindsFor(handle.RequestId), Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }));
        }

        [Test]
        public void BusyNavigator_WithoutRejectWhenBusy_AcceptsAndSerializesTheRequest()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "a", "b");
            _h.Stage.AcquireMode = StageMode.Manual;
            _h.Navigator.Open(LifecycleDriver.KeyOf("a"), _h.Owner);

            var second = _h.Navigator.Open(LifecycleDriver.KeyOf("b"), _h.Owner);

            Assert.That(second.IsAlive, Is.True);
            Assert.That(second.Phase, Is.EqualTo(ViewPhase.Waiting));
            Assert.That(_h.Observer.CountOf(ViewEventKind.NotShown), Is.Zero);
        }

        // Waiting cap

        [Test]
        public void WaitingCap_EndsTheFifthWaitingRequestAsBusy_AndLogsUIDIRECTOR112()
        {
            FillWaitingCap();
            var shieldBefore = _h.ShieldCount;
            var acquiresBefore = Acquires;

            var result = LifecycleDriver.Show(_h, "p5", _h.Owner).ResultNow();

            LifecycleDriver.AssertRejectedAtAcceptance(_h, result, RejectReason.Busy, "p5", shieldBefore, acquiresBefore);
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.WaitingCapReached), Is.EqualTo(1));
            Assert.That(_h.RequestCount(ViewLayers.Popups), Is.EqualTo(5));
        }

        [Test]
        public void WaitingCap_CountsOnlyWaitingRequests_TheLoadingOneDoesNotCount()
        {
            FillWaitingCap();

            Assert.That(LifecycleDriver.HandleOf(_h, "p0").Phase, Is.EqualTo(ViewPhase.Loading));
            for (var i = 1; i <= 4; i++)
                Assert.That(LifecycleDriver.HandleOf(_h, "p" + i).Phase, Is.EqualTo(ViewPhase.Waiting), "p" + i);

            Assert.That(_h.Observer.CountOf(ViewEventKind.NotShown), Is.Zero, "Five requests were accepted: one loading and four waiting.");
        }

        [Test]
        public void WaitingCap_IsPerLayer_AnotherLayerStillAccepts()
        {
            FillWaitingCap();
            _h.Row("s");

            var handle = _h.Navigator.Open(ViewKey.Create("s"), _h.Owner);

            Assert.That(handle.IsAlive, Is.True);
            Assert.That(_h.Observer.CountOf(ViewEventKind.NotShown), Is.Zero);
        }

        [Test]
        public void WaitingCap_FreesASlot_WhenAWaitingRequestIsWithdrawn()
        {
            FillWaitingCap();
            LifecycleDriver.HandleOf(_h, "p1").Close();

            var handle = _h.Navigator.Open(LifecycleDriver.KeyOf("p5"), _h.Owner);

            Assert.That(handle.IsAlive, Is.True);
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Waiting));
            Assert.That(_h.Diagnostics.Has(DiagnosticIds.WaitingCapReached), Is.False);
        }

        // Sort band: the whole band stays below 29999 and an overflow stops the open

        [Test]
        public void SortBand_JustBelowTheLimit_StillAcceptsAndShowsTheOpen()
        {
            using (var h = new NavigatorHarness(sortingBase: 26998))
            {
                h.Row("s");

                var task = h.Navigator.ShowAsync(ViewKey.Create("s"), h.Owner);

                Assert.That(task.IsPending(), Is.True);
                Assert.That(h.Observer.CountOf(ViewEventKind.Shown), Is.EqualTo(1));
                Assert.That(h.Diagnostics.Has(DiagnosticIds.SortBandOverflow), Is.False);
            }
        }

        [TestCase(26999)]
        [TestCase(29000)]
        [TestCase(32768)]
        public void SortBandOverflow_StopsTheOpen_NothingIsLoadedShieldedOrWrapped(int sortingBase)
        {
            using (var h = new NavigatorHarness(sortingBase: sortingBase))
            {
                h.Row("s");

                var result = h.Navigator.ShowAsync(ViewKey.Create("s"), h.Owner).ResultNow();

                LifecycleDriver.AssertRejectedAtAcceptance(h, result, RejectReason.Busy, "s", 0, 0);
                Assert.That(h.RequestCount(ViewLayers.Screens), Is.Zero);
                Assert.That(h.Stage.Instances, Is.Empty);
                Assert.That(h.Observer.CountOf(ViewEventKind.Requested), Is.Zero);
            }
        }

        [Test]
        public void SortBandOverflow_IsLoggedAsAnError_NotAWarning()
        {
            using (var h = new NavigatorHarness(sortingBase: 29000))
            {
                h.Row("s");

                h.Navigator.ShowAsync(ViewKey.Create("s"), h.Owner).ResultNow();

                DiagnosticEntry entry;
                Assert.That(LifecycleDriver.TryFindDiagnostic(h.Diagnostics, DiagnosticIds.SortBandOverflow, out entry), Is.True);
                Assert.That(entry.IsError, Is.True, "UIDIRECTOR115 is an error that stops the open.");
                Assert.That(h.Diagnostics.Count(DiagnosticIds.SortBandOverflow), Is.EqualTo(1));
            }
        }

        // Ids and handles of rejected requests

        [Test]
        public void RejectedRequest_KeepsTheReservedRequestId()
        {
            var reserved = _h.Navigator.ReserveRequestId();
            var options = new OpenOptions { RequestId = reserved };

            var result = _h.Navigator.ShowAsync(ViewKey.Create("missing"), _h.Owner, options).ResultNow();

            Assert.That(result.RequestId, Is.EqualTo(reserved));
            Assert.That(_h.Observer.KindsFor(reserved), Is.EqualTo(new[] { ViewEventKind.NotShown }));
        }

        [Test]
        public void AcceptedRequest_KeepsTheReservedRequestId()
        {
            _h.Row("a");
            var reserved = _h.Navigator.ReserveRequestId();
            var options = new OpenOptions { RequestId = reserved };

            var handle = _h.Navigator.Open(ViewKey.Create("a"), _h.Owner, options);

            Assert.That(handle.RequestId, Is.EqualTo(reserved));
            Assert.That(_h.Observer.KindsFor(reserved), Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }));
        }

        [Test]
        public void RejectedRequest_CarriesTheSourceTagOnItsNotShownEvent()
        {
            var options = new OpenOptions { Source = "tap-handler" };

            var result = _h.Navigator.ShowAsync(ViewKey.Create("missing"), _h.Owner, options).ResultNow();

            var published = _h.Observer.EventsFor(result.RequestId)[0];
            Assert.That(published.Source, Is.EqualTo("tap-handler"));
        }

        [Test]
        public void RejectedOpen_ReturnsAnInertHandle()
        {
            var handle = _h.Navigator.Open(ViewKey.Create("missing"), _h.Owner);

            Assert.That(handle.IsAlive, Is.False);
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Ended));
            Assert.That(_h.Observer.CountOf(ViewEventKind.NotShown), Is.EqualTo(1));
        }

        [Test]
        public void RejectedShowAsync_CompletesAtOnce_WithoutWaitingForAnything()
        {
            var task = _h.Navigator.ShowAsync(ViewKey.Create("missing"), _h.Owner);

            Assert.That(task.IsPending(), Is.False);
            task.ResultNow();
        }
    }
}
