using System;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // PreloadAsync through the navigator on a manual fake stage: answers, lease lifetimes and error routing.
    [TestFixture]
    public sealed class PreloadLeaseTests
    {
        private const string Shop = "shop";
        private const string NoLeaseListed = "preload leases: 0";
        private const string OneShopLeaseListed = "preload leases: 1 shop";

        private NavigatorHarness _harness;

        [TearDown]
        public void TearDown()
        {
            if (_harness != null)
                _harness.Dispose();
            _harness = null;
        }

        // A navigator with one registered row whose preloads wait until the test completes them.
        private NavigatorHarness CreateHarness()
        {
            _harness = new NavigatorHarness();
            _harness.Stage.PreloadMode = StageMode.Manual;
            _harness.Row(Shop);
            return _harness;
        }

        [Test]
        public void PreloadAsync_UnknownId_ReturnsFalseAndLogsUIDIRECTOR101AsError()
        {
            var harness = CreateHarness();

            var result = harness.Navigator.PreloadAsync("missing", harness.Owner).ResultNow();

            Assert.That(result, Is.False, "result");
            Assert.That(
                harness.Diagnostics.Entries,
                Has.Exactly(1).Matches<DiagnosticEntry>(entry => entry.Id == DiagnosticIds.UnknownViewId && entry.IsError),
                harness.Diagnostics.Describe());
            Assert.That(harness.Stage.CountOf(StageCallKind.Preload), Is.EqualTo(0), harness.Stage.DescribeCalls());
        }

        [Test]
        public void PreloadAsync_NullId_ThrowsArgumentNullException()
        {
            var harness = CreateHarness();

            Assert.That(() => { _ = harness.Navigator.PreloadAsync(null, harness.Owner); }, Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void PreloadAsync_OwnerAlreadyCancelled_ReturnsFalseAndAsksNoStage()
        {
            var harness = CreateHarness();
            var owner = harness.NewOwner();
            owner.Cancel();

            var result = harness.Navigator.PreloadAsync(Shop, owner.Token).ResultNow();

            Assert.That(result, Is.False, "result");
            Assert.That(harness.Stage.CountOf(StageCallKind.Preload), Is.EqualTo(0), harness.Stage.DescribeCalls());
        }

        [Test]
        public void PreloadAsync_OwnerEndsDuringLoad_ReturnsFalseAndCancelsTheStageToken()
        {
            var harness = CreateHarness();
            var owner = harness.NewOwner();
            var task = harness.Navigator.PreloadAsync(Shop, owner.Token);

            owner.Cancel();

            var stageToken = harness.Stage.CallsOf(StageCallKind.Preload)[0].Token;
            Assert.That(task.ResultNow(), Is.False, "result");
            Assert.That(stageToken.IsCancellationRequested, Is.True, "the token the stage received is cancelled");
            Assert.That(harness.Describe(), Does.Contain(NoLeaseListed), "no lease is listed");
        }

        [Test]
        public void PreloadAsync_OwnerEndsAfterStageAnsweredLate_ReturnsFalseAndDisposesTheLateLeaseOnce()
        {
            var harness = CreateHarness();
            harness.Stage.PreloadIgnoresCancellation = true;
            var owner = harness.NewOwner();
            var task = harness.Navigator.PreloadAsync(Shop, owner.Token);
            owner.Cancel();
            Assert.That(task.IsPending(), Is.True, "the stage ignores the cancel, so the preload still waits");

            harness.Stage.CompletePreload();

            Assert.That(task.ResultNow(), Is.False, "result");
            Assert.That(harness.Stage.Leases[0].DisposeCount, Is.EqualTo(1), "late lease dispose count");
            Assert.That(harness.Describe(), Does.Contain(NoLeaseListed), "no lease is listed");
            Assert.That(harness.Errors, Is.Empty, "a late lease is not an error");
        }

        [Test]
        public void PreloadAsync_LateLeaseDisposeThrows_ReturnsFalseAndRoutesAProviderError()
        {
            var harness = CreateHarness();
            harness.Stage.PreloadIgnoresCancellation = true;
            var fault = new InvalidOperationException("late lease dispose failed");
            harness.Stage.ThrowOnNextLeaseDispose(fault);
            var owner = harness.NewOwner();
            var task = harness.Navigator.PreloadAsync(Shop, owner.Token);
            owner.Cancel();

            harness.Stage.CompletePreload();

            Assert.That(task.ResultNow(), Is.False, "result");
            Assert.That(harness.Stage.Leases[0].DisposeCount, Is.EqualTo(1), "late lease dispose count");
            Assert.That(harness.Errors.Count, Is.EqualTo(1), "routed error count");
            Assert.That(harness.Errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(harness.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(harness.Errors[0].Context.ViewId, Is.EqualTo(Shop), "error view id");
        }

        [Test]
        public void PreloadAsync_StageThrows_ReturnsFalseAndRoutesAProviderError()
        {
            var harness = CreateHarness();
            var fault = new InvalidOperationException("preload failed");
            var task = harness.Navigator.PreloadAsync(Shop, harness.Owner);

            harness.Stage.FailPreload(fault);

            Assert.That(task.ResultNow(), Is.False, "result");
            Assert.That(harness.Errors.Count, Is.EqualTo(1), "routed error count");
            Assert.That(harness.Errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(harness.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(harness.Errors[0].Context.ViewId, Is.EqualTo(Shop), "error view id");
            Assert.That(harness.Describe(), Does.Contain(NoLeaseListed), "no lease is listed");
        }

        [Test]
        public void PreloadAsync_StageReturnsNull_ReturnsFalse()
        {
            var harness = CreateHarness();
            harness.Stage.ReturnNullOnNextPreload();
            var task = harness.Navigator.PreloadAsync(Shop, harness.Owner);

            harness.Stage.CompletePreload();

            Assert.That(task.ResultNow(), Is.False, "result");
            Assert.That(harness.Describe(), Does.Contain(NoLeaseListed), "no lease is listed");
            Assert.That(harness.Errors, Is.Empty, "a null lease is not an error");
        }

        [Test]
        public void PreloadAsync_Success_ReturnsTrueAndKeepsTheLeaseUntilTheOwnerEnds()
        {
            var harness = CreateHarness();
            var owner = harness.NewOwner();
            var task = harness.Navigator.PreloadAsync(Shop, owner.Token);
            Assert.That(harness.Describe(), Does.Contain(OneShopLeaseListed), "the loading lease is listed");

            harness.Stage.CompletePreload();

            var lease = harness.Stage.Leases[0];
            Assert.That(task.ResultNow(), Is.True, "result");
            Assert.That(lease.DisposeCount, Is.EqualTo(0), "dispose count while the owner lives");
            Assert.That(harness.Describe(), Does.Contain(OneShopLeaseListed), "the held lease is listed");

            owner.Cancel();

            Assert.That(lease.DisposeCount, Is.EqualTo(1), "dispose count after the owner ended");
            Assert.That(harness.Describe(), Does.Contain(NoLeaseListed), "no lease is listed after the owner ended");
        }

        [Test]
        public void PreloadAsync_Success_RootTeardownDisposesTheLeaseOnce()
        {
            var harness = CreateHarness();
            var owner = harness.NewOwner();
            var task = harness.Navigator.PreloadAsync(Shop, owner.Token);
            harness.Stage.CompletePreload();
            var lease = harness.Stage.Leases[0];
            Assert.That(task.ResultNow(), Is.True, "the preload succeeded");

            harness.Navigator.Teardown();

            Assert.That(lease.DisposeCount, Is.EqualTo(1), "dispose count after teardown");

            owner.Cancel();

            Assert.That(lease.DisposeCount, Is.EqualTo(1), "dispose count after the owner also ended");
        }

        [Test]
        public void PreloadAsync_RootTeardownDuringLoad_ReturnsFalseAndCancelsTheStageToken()
        {
            var harness = CreateHarness();
            var owner = harness.NewOwner();
            var task = harness.Navigator.PreloadAsync(Shop, owner.Token);

            harness.Navigator.Teardown();

            var stageToken = harness.Stage.CallsOf(StageCallKind.Preload)[0].Token;
            Assert.That(task.ResultNow(), Is.False, "result");
            Assert.That(stageToken.IsCancellationRequested, Is.True, "the token the stage received is cancelled");
            Assert.That(harness.Describe(), Does.Contain(NoLeaseListed), "no lease is listed");
        }

        [Test]
        public void PreloadAsync_RootTeardownThenStageAnswersLate_ReturnsFalseAndDisposesTheLateLeaseOnce()
        {
            var harness = CreateHarness();
            harness.Stage.PreloadIgnoresCancellation = true;
            var owner = harness.NewOwner();
            var task = harness.Navigator.PreloadAsync(Shop, owner.Token);
            harness.Navigator.Teardown();
            Assert.That(task.IsPending(), Is.True, "the stage ignores the cancel, so the preload still waits");

            harness.Stage.CompletePreload();

            Assert.That(task.ResultNow(), Is.False, "result");
            Assert.That(harness.Stage.Leases[0].DisposeCount, Is.EqualTo(1), "late lease dispose count");
        }

        [Test]
        public void PreloadAsync_LeaseDisposeThrows_IsRoutedAndNotThrown()
        {
            var harness = CreateHarness();
            var owner = harness.NewOwner();
            var task = harness.Navigator.PreloadAsync(Shop, owner.Token);
            harness.Stage.CompletePreload();
            Assert.That(task.ResultNow(), Is.True, "the preload succeeded");
            var fault = new InvalidOperationException("lease dispose failed");
            var lease = harness.Stage.Leases[0];
            lease.ThrowOnDispose = fault;

            Assert.That(() => owner.Cancel(), Throws.Nothing);

            Assert.That(lease.DisposeCount, Is.EqualTo(1), "dispose count");
            Assert.That(harness.Errors.Count, Is.EqualTo(1), "routed error count");
            Assert.That(harness.Errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(harness.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(harness.Errors[0].Context.ViewId, Is.EqualTo(Shop), "error view id");
        }

        [Test]
        public void PreloadAsync_LeaseDisposeThrowsOnTeardown_IsRoutedAndNotThrown()
        {
            var harness = CreateHarness();
            var owner = harness.NewOwner();
            var task = harness.Navigator.PreloadAsync(Shop, owner.Token);
            harness.Stage.CompletePreload();
            Assert.That(task.ResultNow(), Is.True, "the preload succeeded");
            var fault = new InvalidOperationException("lease dispose failed");
            var lease = harness.Stage.Leases[0];
            lease.ThrowOnDispose = fault;

            Assert.That(() => harness.Navigator.Teardown(), Throws.Nothing);

            Assert.That(lease.DisposeCount, Is.EqualTo(1), "dispose count");
            Assert.That(harness.Errors.Count, Is.EqualTo(1), "routed error count");
            Assert.That(harness.Errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(harness.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(harness.Errors[0].Context.ViewId, Is.EqualTo(Shop), "error view id");
        }

        [Test]
        public void PreloadAsync_TwoPreloadsOfOneId_AreIndependent()
        {
            var harness = CreateHarness();
            var firstOwner = harness.NewOwner();
            var secondOwner = harness.NewOwner();
            var first = harness.Navigator.PreloadAsync(Shop, firstOwner.Token);
            var second = harness.Navigator.PreloadAsync(Shop, secondOwner.Token);
            harness.Stage.CompletePreload();
            harness.Stage.CompletePreload();
            Assert.That(first.ResultNow() && second.ResultNow(), Is.True, "both preloads succeeded");

            firstOwner.Cancel();

            Assert.That(harness.Stage.Leases[0].DisposeCount, Is.EqualTo(1), "the first owner's lease");
            Assert.That(harness.Stage.Leases[1].DisposeCount, Is.EqualTo(0), "the second owner's lease");
        }

        [Test]
        public void PreloadAsync_AfterTeardown_ReturnsFalse()
        {
            var harness = CreateHarness();
            harness.Navigator.Teardown();

            var result = harness.Navigator.PreloadAsync(Shop, harness.Owner).ResultNow();

            Assert.That(result, Is.False, "result");
            Assert.That(harness.Stage.CountOf(StageCallKind.Preload), Is.EqualTo(0), harness.Stage.DescribeCalls());
        }
    }
}
