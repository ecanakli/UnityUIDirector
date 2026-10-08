using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests
{
    // Catalog registration refuses bad rows, and removal leaves nothing behind.
    public sealed class CatalogContractTests
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
            _harness.Dispose();

            for (var i = 0; i < _catalogs.Count; i++)
                UnityEngine.Object.DestroyImmediate(_catalogs[i]);
        }

        // Duplicate id

        [Test]
        public void DuplicateId_AcrossCatalogs_RefusesTheSecondRow_WithUIDIRECTOR108()
        {
            var first = NewCatalog(ContractRows.Entry("dup", ViewLayers.Screens.Id));
            var second = NewCatalog(ContractRows.Entry("dup", ViewLayers.Popups.Id));
            Map("dup");

            _harness.Navigator.AddCatalog(first, _harness.Owner);
            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR108"), Is.Zero);

            _harness.Navigator.AddCatalog(second, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR108"), Is.EqualTo(1), "The second row is refused and reported once.");

            _harness.Navigator.OpenView("dup", _harness.Owner);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1), "The first row stays in force.");
            Assert.That(_harness.RequestCount(ViewLayers.Popups), Is.Zero, "The refused row must not be used.");
        }

        [Test]
        public void DuplicateId_InsideOneCatalog_RefusesTheSecondRow_WithUIDIRECTOR108()
        {
            var catalog = NewCatalog(
                ContractRows.Entry("dup", ViewLayers.Screens.Id),
                ContractRows.Entry("dup", ViewLayers.Popups.Id));
            Map("dup");

            _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR108"), Is.EqualTo(1));
            _harness.Navigator.OpenView("dup", _harness.Owner);
            Assert.That(_harness.RequestCount(ViewLayers.Screens), Is.EqualTo(1));
        }

        [Test]
        public void RegisteringTheSameCatalogTwice_RefusesEveryRowOfTheSecondRegistration()
        {
            var catalog = NewCatalog(ContractRows.Entry("a", ViewLayers.Screens.Id), ContractRows.Entry("b", ViewLayers.Screens.Id));

            _harness.Navigator.AddCatalog(catalog, _harness.Owner);
            _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR108"), Is.EqualTo(2));
        }

        [Test]
        public void RefusedDuplicate_IsNotAnError_ForTheViewThatWasRegisteredFirst()
        {
            var first = NewCatalog(ContractRows.Entry("dup", ViewLayers.Popups.Id));
            var second = NewCatalog(ContractRows.Entry("dup", ViewLayers.Popups.Id));
            Map("dup");
            _harness.Navigator.AddCatalog(first, _harness.Owner);
            _harness.Navigator.AddCatalog(second, _harness.Owner);

            var result = _harness.Navigator.Show("dup", _harness.Owner);

            Assert.That(result.IsPending(), Is.True, "The first row opens normally.");
            Assert.That(_harness.Stage.CountOf(StageCallKind.Acquire), Is.EqualTo(1));
        }

        // Unknown layer

        [Test]
        public void RowWithAnUnknownLayer_IsRefused_WithUIDIRECTOR109_AndItsIdIsUnknown()
        {
            var catalog = NewCatalog(ContractRows.Entry("lost", "nowhere"));
            Map("lost");

            _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR109"), Is.EqualTo(1));

            var result = _harness.Navigator.Show("lost", _harness.Owner).ResultNow();
            Assert.That(result.WasShown, Is.False);
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.UnknownKey), "A refused row was never registered.");
            Assert.That(_harness.Stage.CountOf(StageCallKind.Acquire), Is.Zero, "Nothing is loaded for a refused row.");
        }

        [Test]
        public void ARefusedRow_DoesNotStopTheOtherRowsOfTheSameCatalog()
        {
            var catalog = NewCatalog(
                ContractRows.Entry("lost", "nowhere"),
                ContractRows.Entry("good", ViewLayers.Popups.Id));
            Map("lost");
            Map("good");

            _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR109"), Is.EqualTo(1));
            var task = _harness.Navigator.Show("good", _harness.Owner);
            Assert.That(task.IsPending(), Is.True, "The good row is open and waiting for its close.");
            Assert.That(_harness.Navigator.IsOpen("good"), Is.True);
        }

        // Empty id

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("\t \n")]
        public void RowWithAnEmptyId_IsRefused_WithUIDIRECTOR125(string id)
        {
            var catalog = NewCatalog(ContractRows.Entry(id, ViewLayers.Popups.Id));

            _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR125"), Is.EqualTo(1), "The row is refused and reported once.");
            Assert.That(_harness.Describe(), Does.Contain("rows: 0\n"), "A refused row is not registered.");
        }

        [Test]
        public void RowWithAnEmptyId_DoesNotStopTheOtherRowsOfTheSameCatalog()
        {
            var catalog = NewCatalog(
                ContractRows.Entry(" ", ViewLayers.Popups.Id),
                ContractRows.Entry("good", ViewLayers.Popups.Id));
            Map("good");

            _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR125"), Is.EqualTo(1));
            var task = _harness.Navigator.Show("good", _harness.Owner);
            Assert.That(task.IsPending(), Is.True, "The good row is open and waiting for its close.");
            Assert.That(_harness.Navigator.IsOpen("good"), Is.True);
        }

        [Test]
        public void RowWithAnEmptyIdAndAnUnknownLayer_ReportsOnlyUIDIRECTOR125()
        {
            var catalog = NewCatalog(ContractRows.Entry(" ", "nowhere"));

            _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR125"), Is.EqualTo(1), "The empty id is checked first.");
            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR109"), Is.Zero, "A row refused for its id is not checked further.");
        }

        [Test]
        public void TwoRowsWithAnEmptyId_AreEachRefusedWithUIDIRECTOR125_AndNeverAsDuplicates()
        {
            var catalog = NewCatalog(
                ContractRows.Entry("", ViewLayers.Popups.Id),
                ContractRows.Entry(" ", ViewLayers.Popups.Id));

            _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR125"), Is.EqualTo(2), "Each blank row is reported for its id.");
            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR108"), Is.Zero, "A blank row claims no id, so nothing collides.");
            Assert.That(_harness.Describe(), Does.Contain("rows: 0\n"), "Neither blank row is registered.");
        }

        [Test]
        public void NullRow_IsSkippedSilently_AndTheOtherRowsStillRegister()
        {
            var catalog = NewCatalog((ViewEntry)null, ContractRows.Entry("good", ViewLayers.Popups.Id));
            Map("good");

            _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR125"), Is.Zero, "A null row is not reported as an empty id.");
            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR109"), Is.Zero);
            Assert.That(_harness.Diagnostics.Total, Is.Zero, "A null row is skipped without any diagnostic.");
            Assert.That(_harness.Describe(), Does.Contain("rows: 1\n"), "Only the good row is registered.");
            Assert.That(_harness.Navigator.Show("good", _harness.Owner).IsPending(), Is.True, "The good row is open and waiting for its close.");
        }

        [Test]
        public void AddCatalog_WithANullCatalog_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _harness.Navigator.AddCatalog(null, _harness.Owner));
        }

        [Test]
        public void AddCatalog_WithAnAlreadyCancelledOwner_RegistersNothing()
        {
            var catalog = NewCatalog(ContractRows.Entry("a", ViewLayers.Popups.Id));
            Map("a");
            var owner = _harness.NewOwner();
            owner.Cancel();

            var registration = _harness.Navigator.AddCatalog(catalog, owner.Token);

            Assert.That(registration.IsActive, Is.False);
            var result = _harness.Navigator.Show("a", _harness.Owner).ResultNow();
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.UnknownKey));
        }

        // Removal

        [Test]
        public void RemovingACatalog_MakesItsIdsUnknown_AndIsSafeToRepeat()
        {
            var catalog = NewCatalog(ContractRows.Entry("a", ViewLayers.Popups.Id));
            Map("a");
            var registration = _harness.Navigator.AddCatalog(catalog, _harness.Owner);
            Assert.That(registration.IsActive, Is.True);

            registration.Remove();
            Assert.DoesNotThrow(() => registration.Remove());

            Assert.That(registration.IsActive, Is.False);
            var result = _harness.Navigator.Show("a", _harness.Owner).ResultNow();
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.UnknownKey));
        }

        [Test]
        public void ACatalogWhoseOwnerEnds_IsRemoved_LikeAnExplicitRemove()
        {
            var catalog = NewCatalog(ContractRows.Entry("a", ViewLayers.Popups.Id));
            Map("a");
            var owner = _harness.NewOwner();
            var registration = _harness.Navigator.AddCatalog(catalog, owner.Token);

            owner.Cancel();

            Assert.That(registration.IsActive, Is.False);
            Assert.That(_harness.Navigator.Show("a", _harness.Owner).ResultNow().Rejected, Is.EqualTo(RejectReason.UnknownKey));
        }

        [Test]
        public void RemovingACatalog_FreesItsIds_SoTheSameRowsRegisterAgainWithoutUIDIRECTOR108()
        {
            var catalog = NewCatalog(ContractRows.Entry("a", ViewLayers.Popups.Id));
            Map("a");
            var registration = _harness.Navigator.AddCatalog(catalog, _harness.Owner);
            registration.Remove();

            _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR108"), Is.Zero);
            Assert.That(_harness.Navigator.Show("a", _harness.Owner).IsPending(), Is.True);
        }

        [Test]
        public void RemovingACatalog_WithAnOpenView_LeavesTheViewOpen_AndItEndsNormally()
        {
            var catalog = NewCatalog(ContractRows.Entry("a", ViewLayers.Popups.Id));
            var view = new ContractView();
            _harness.Stage.SetView("a", () => view);
            var registration = _harness.Navigator.AddCatalog(catalog, _harness.Owner);
            var probe = ContractAwait.Watch(_harness.Navigator.Show("a", _harness.Owner));

            registration.Remove();

            Assert.That(_harness.Navigator.IsOpen("a"), Is.True, "Removal only affects new opens.");
            Assert.That(probe.IsPending, Is.True);
            Assert.That(_harness.Stage.LastInstanceOf("a").ReleaseCount, Is.Zero);

            view.Complete(default(NoResult));

            Assert.That(probe.ResumeCount, Is.EqualTo(1));
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(_harness.Stage.LastInstanceOf("a").ReleaseCount, Is.EqualTo(1), "The instance is released exactly once.");
            Assert.That(_harness.Errors, Is.Empty);
            Assert.That(_harness.Navigator.Show("a", _harness.Owner).ResultNow().Rejected, Is.EqualTo(RejectReason.UnknownKey), "A new open of the removed id is unknown.");
        }

        [Test]
        public void RemovingACatalog_WithWaitingRequests_CompletesEveryAwaiterOnce_AndLeaksNoInstance()
        {
            var catalog = NewCatalog(
                ContractRows.Entry("a", ViewLayers.Popups.Id),
                ContractRows.Entry("b", ViewLayers.Popups.Id),
                ContractRows.Entry("c", ViewLayers.Popups.Id));
            Map("a");
            Map("b");
            Map("c");
            var registration = _harness.Navigator.AddCatalog(catalog, _harness.Owner);

            // a loads, b and c wait behind it.
            _harness.Stage.AcquireMode = StageMode.Manual;
            var a = ContractAwait.Watch(_harness.Navigator.Show("a", _harness.Owner));
            var b = ContractAwait.Watch(_harness.Navigator.Show("b", _harness.Owner));
            var c = ContractAwait.Watch(_harness.Navigator.Show("c", _harness.Owner));

            registration.Remove();

            // Whatever the package decides for the waiting ones, drive everything to the end.
            while (_harness.Stage.PendingAcquireCount > 0)
                _harness.Stage.CompleteAcquire();
            _harness.Stage.AcquireMode = StageMode.Auto;
            _harness.Navigator.CloseAll(ViewLayers.Popups);
            _harness.Dispose();

            Assert.That(a.ResumeCount, Is.EqualTo(1), "Every awaiter completes exactly once.");
            Assert.That(b.ResumeCount, Is.EqualTo(1), "Every awaiter completes exactly once.");
            Assert.That(c.ResumeCount, Is.EqualTo(1), "Every awaiter completes exactly once.");
            Assert.That(a.Error, Is.Null);
            Assert.That(b.Error, Is.Null);
            Assert.That(c.Error, Is.Null);
            Assert.That(_harness.Errors, Is.Empty);
            Assert.That(_harness.Diagnostics.FaultCount, Is.Zero);
            _harness.Stage.AssertAllReleased();
        }

        [Test]
        public void RemovingACatalog_ReleasesTheKeptInstancesOfItsRows_AndOnlyThose()
        {
            var removed = NewCatalog(ContractRows.Entry("a", ViewLayers.Popups.Id, ViewRetention.KeepInstance));
            var kept = NewCatalog(ContractRows.Entry("b", ViewLayers.Popups.Id, ViewRetention.KeepInstance));
            var viewA = new ContractView();
            var viewB = new ContractView();
            _harness.Stage.SetView("a", () => viewA);
            _harness.Stage.SetView("b", () => viewB);
            var registration = _harness.Navigator.AddCatalog(removed, _harness.Owner);
            _harness.Navigator.AddCatalog(kept, _harness.Owner);

            _harness.Navigator.Show("a", _harness.Owner);
            viewA.Close();
            _harness.Navigator.Show("b", _harness.Owner);
            viewB.Close();
            var instanceA = _harness.Stage.LastInstanceOf("a");
            var instanceB = _harness.Stage.LastInstanceOf("b");
            Assert.That(instanceA.Stashed, Is.True, "Control: a KeepInstance view is stashed when it closes.");
            Assert.That(instanceB.Stashed, Is.True);

            registration.Remove();

            Assert.That(instanceA.ReleaseCount, Is.EqualTo(1), "Nothing can reuse the kept instance of a removed row, so it is released.");
            Assert.That(instanceA.Stashed, Is.False);
            Assert.That(instanceB.Stashed, Is.True, "The kept instance of another catalog is untouched.");
            Assert.That(instanceB.ReleaseCount, Is.Zero);
        }

        [Test]
        public void RemovingACatalog_KeptInstanceReleaseFaults_RoutesProviderErrorWithViewId()
        {
            var catalog = NewCatalog(ContractRows.Entry("a", ViewLayers.Popups.Id, ViewRetention.KeepInstance));
            var view = new ContractView();
            _harness.Stage.SetView("a", () => view);
            var registration = _harness.Navigator.AddCatalog(catalog, _harness.Owner);
            _harness.Navigator.Show("a", _harness.Owner);
            view.Close();
            Assert.That(_harness.Stage.LastInstanceOf("a").Stashed, Is.True, "Control: a KeepInstance view is stashed when it closes.");
            var fault = new InvalidOperationException("kept release failed");
            _harness.Stage.FailNext(StageCallKind.Release, fault);

            Assert.That(() => registration.Remove(), Throws.Nothing);

            Assert.That(_harness.Errors.Count, Is.EqualTo(1), "routed error count");
            Assert.That(_harness.Errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(_harness.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(_harness.Errors[0].Context.ViewId, Is.EqualTo("a"), "error view id");
            Assert.That(_harness.Errors[0].Context.RequestId, Is.Zero, "A kept instance belongs to no request.");
            Assert.That(_harness.Diagnostics.FaultCount, Is.Zero, "The fault is routed, not reported as an internal fault.\n" + _harness.Diagnostics.Describe());
        }

        [Test]
        public void RemovingACatalog_FirstKeptInstanceReleaseFaults_StillReleasesTheOthers()
        {
            var catalog = NewCatalog(
                ContractRows.Entry("a", ViewLayers.Popups.Id, ViewRetention.KeepInstance),
                ContractRows.Entry("b", ViewLayers.Popups.Id, ViewRetention.KeepInstance));
            var viewA = new ContractView();
            var viewB = new ContractView();
            _harness.Stage.SetView("a", () => viewA);
            _harness.Stage.SetView("b", () => viewB);
            var registration = _harness.Navigator.AddCatalog(catalog, _harness.Owner);
            _harness.Navigator.Show("a", _harness.Owner);
            viewA.Close();
            _harness.Navigator.Show("b", _harness.Owner);
            viewB.Close();
            var instanceA = _harness.Stage.LastInstanceOf("a");
            var instanceB = _harness.Stage.LastInstanceOf("b");
            Assert.That(instanceA.Stashed, Is.True, "Control: a KeepInstance view is stashed when it closes.");
            Assert.That(instanceB.Stashed, Is.True, "Control: a KeepInstance view is stashed when it closes.");
            _harness.Stage.ClearCalls();
            _harness.Stage.FailNext(StageCallKind.Release);

            registration.Remove();

            var releases = _harness.Stage.CallsOf(StageCallKind.Release);
            Assert.That(releases, Has.Exactly(1).Matches<StageCall>(call => ReferenceEquals(call.Instance, instanceA)), "Release calls for a.\n" + _harness.Stage.DescribeCalls());
            Assert.That(releases, Has.Exactly(1).Matches<StageCall>(call => ReferenceEquals(call.Instance, instanceB)), "Release calls for b.\n" + _harness.Stage.DescribeCalls());
            Assert.That(instanceA.ReleaseCount + instanceB.ReleaseCount, Is.EqualTo(1), "The instance whose release did not fault is released.");

            // The removal order follows the registry, so the faulted id is whichever was not released.
            var faultedId = instanceA.ReleaseCount == 0 ? "a" : "b";
            Assert.That(_harness.Errors.Count, Is.EqualTo(1), "routed error count");
            Assert.That(_harness.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(_harness.Errors[0].Context.ViewId, Is.EqualTo(faultedId), "The error names the view whose release faulted.");
            Assert.That(_harness.Errors[0].Context.RequestId, Is.Zero, "A kept instance belongs to no request.");
        }

        [Test]
        public void RemovingACatalog_WhileItsKeepInstanceViewIsOpen_ThenClosing_DoesNotKeepTheInstanceForever()
        {
            var catalog = NewCatalog(ContractRows.Entry("a", ViewLayers.Popups.Id, ViewRetention.KeepInstance));
            var view = new ContractView();
            _harness.Stage.SetView("a", () => view);
            var registration = _harness.Navigator.AddCatalog(catalog, _harness.Owner);
            _harness.Navigator.Show("a", _harness.Owner);
            var instance = _harness.Stage.LastInstanceOf("a");

            registration.Remove();
            view.Close();

            Assert.That(instance.Stashed, Is.False, "The id is unknown now: a kept instance could never be reused.");
            Assert.That(instance.ReleaseCount, Is.EqualTo(1), "It is released when the view closes.");
        }

        [Test]
        public void RemovingTheCatalogThatHadARefusedDuplicate_DoesNotRemoveTheOtherCatalogsRow()
        {
            var first = NewCatalog(ContractRows.Entry("dup", ViewLayers.Popups.Id));
            var second = NewCatalog(ContractRows.Entry("dup", ViewLayers.Popups.Id));
            Map("dup");
            _harness.Navigator.AddCatalog(first, _harness.Owner);
            var secondRegistration = _harness.Navigator.AddCatalog(second, _harness.Owner);

            secondRegistration.Remove();

            Assert.That(_harness.Navigator.Show("dup", _harness.Owner).IsPending(), Is.True, "The first catalog's row is still registered.");
        }

        [Test]
        public void RemovingOneCatalog_LeavesTheRowsOfAnotherCatalogOpenable()
        {
            var first = NewCatalog(ContractRows.Entry("a", ViewLayers.Popups.Id));
            var second = NewCatalog(ContractRows.Entry("b", ViewLayers.Popups.Id));
            Map("a");
            Map("b");
            var firstRegistration = _harness.Navigator.AddCatalog(first, _harness.Owner);
            _harness.Navigator.AddCatalog(second, _harness.Owner);

            firstRegistration.Remove();

            Assert.That(_harness.Navigator.Show("b", _harness.Owner).IsPending(), Is.True);
            Assert.That(_harness.Navigator.Show("a", _harness.Owner).ResultNow().Rejected, Is.EqualTo(RejectReason.UnknownKey));
        }

        // Helpers

        private ViewCatalog NewCatalog(params ViewEntry[] entries)
        {
            var catalog = ContractRows.Catalog(entries);
            _catalogs.Add(catalog);
            return catalog;
        }

        private void Map(string id)
        {
            _harness.Stage.SetView(id, () => new FakeView<NoArgs, NoResult>());
        }
    }
}
