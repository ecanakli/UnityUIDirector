using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // A stage step that throws while a view instance is prepared ends the request once and routes the exception with context.
    public sealed class StageFaultTests
    {
        // The preparation step after inject that throws.
        public enum PrepareStep
        {
            Bind,
            Place,
            Relink
        }

        private const string Detail = "detail";

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

        // Shows detail with its inject failing on the given exception.
        private UniTask<ViewResult<NoResult>> ShowDetailWithInjectThrowing(Exception fault)
        {
            _h.Row(Detail);
            _h.Stage.FailNext(StageCallKind.Inject, fault);
            return _h.Navigator.ShowAsync(ViewKey.Create(Detail), _h.Owner);
        }

        // Bind and Place fail on a first open; Relink fails on a reopen in place with no screen above.
        private UniTask<ViewResult<NoResult>> ShowDetailWithStepThrowing(PrepareStep step, Exception fault)
        {
            _h.Row("home").Row(Detail).Map<string, NoResult>(Detail);
            var key = ViewKey.Create<string>(Detail);
            NavigationTestKit.Open(_h, "home");

            if (step != PrepareStep.Relink)
            {
                _h.Stage.FailNext(step == PrepareStep.Bind ? StageCallKind.ResolveView : StageCallKind.Place, fault);
                return _h.Navigator.ShowAsync(key, "first", _h.Owner);
            }

            _h.Navigator.ShowAsync(key, "first", _h.Owner);
            _h.Stage.FailNext(StageCallKind.Relink, fault);
            return _h.Navigator.ShowAsync(key, "second", _h.Owner);
        }

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

        // Inject

        [Test]
        public void InjectThrows_RoutesTheExceptionOnceAsInject_WithTheViewIdAndRequestId()
        {
            var fault = new InvalidOperationException("inject failed");

            var result = ShowDetailWithInjectThrowing(fault).ResultNow();

            Assert.That(_h.Errors.Count, Is.EqualTo(1), "One routed error.\n" + _h.Stage.DescribeCalls());
            var error = _h.Errors[0];
            Assert.That(error.Exception, Is.SameAs(fault), "routed exception");
            Assert.That(error.Context.Source, Is.EqualTo(ViewErrorSource.Inject), "source");
            Assert.That(error.Context.ViewId, Is.EqualTo(Detail), "view id");
            Assert.That(result.RequestId, Is.Not.Zero, "The result carries the request id.");
            Assert.That(error.Context.RequestId, Is.EqualTo(result.RequestId), "request id");
        }

        [Test]
        public void InjectThrows_EndsTheRequestNotShownWithLoadFailed_AndLogsUIDIRECTOR104()
        {
            var fault = new InvalidOperationException("inject failed");

            var result = ShowDetailWithInjectThrowing(fault).ResultNow();

            Assert.That(result.WasShown, Is.False, "was shown");
            Assert.That(result.Reason, Is.EqualTo(CloseReason.NotShown), "reason");
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.LoadFailed), "reject reason");
            Assert.That(result.Detail, Is.EqualTo(fault.Message), "detail");
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.LoadFailed), Is.EqualTo(1), _h.Diagnostics.Describe());
            Assert.That(
                _h.Diagnostics.Entries,
                Has.Some.Matches<DiagnosticEntry>(entry => entry.Id == DiagnosticIds.LoadFailed
                    && entry.Message.StartsWith("Injecting '" + Detail + "' failed", StringComparison.Ordinal)),
                _h.Diagnostics.Describe());
        }

        [Test]
        public void InjectThrows_ReleasesTheInstanceOnce_AndLeavesNoShieldHolder()
        {
            ShowDetailWithInjectThrowing(new InvalidOperationException("inject failed")).ResultNow();

            var instance = _h.Stage.LastInstanceOf(Detail);
            Assert.That(instance, Is.Not.Null, "The instance was acquired.\n" + _h.Stage.DescribeCalls());
            Assert.That(instance.ReleaseCount, Is.EqualTo(1), "released once\n" + _h.Stage.DescribeCalls());
            Assert.That(_h.ShieldCount, Is.Zero, "Shield holders left.\n" + _h.Describe());
            Assert.That(_h.ViewOf<FakeView<NoArgs, NoResult>>(Detail).OpenCount, Is.Zero, "OnOpen never ran.");
        }

        [Test]
        public void InjectThrows_TheNextOpenOnTheSameLayerIsShown()
        {
            _h.Row("other");
            ShowDetailWithInjectThrowing(new InvalidOperationException("inject failed")).ResultNow();

            var other = NavigationTestKit.Open(_h, "other");

            Assert.That(other.Phase, Is.EqualTo(ViewPhase.Open), _h.Describe());
        }

        // A queued inject fault that is never consumed shows the step was skipped.

        [Test]
        public void ReopenInPlace_DoesNotInjectAgain()
        {
            _h.Row("home").Row(Detail).Map<string, NoResult>(Detail);
            var key = ViewKey.Create<string>(Detail);
            NavigationTestKit.Open(_h, "home");
            _h.Navigator.ShowAsync(key, "first", _h.Owner);
            _h.Stage.ClearCalls();
            _h.Stage.FailNext(StageCallKind.Inject, new InvalidOperationException("inject must not run"));

            var second = _h.Navigator.Open(key, "second", _h.Owner);

            Assert.That(_h.Stage.CountOf(StageCallKind.Relink), Is.EqualTo(1), "Control: the reopen relinked the live instance.\n" + _h.Stage.DescribeCalls());
            Assert.That(_h.Stage.CountOf(StageCallKind.Inject), Is.Zero, "A reopen in place does not inject.\n" + _h.Stage.DescribeCalls());
            Assert.That(_h.Errors, Is.Empty, "No routed error.");
            Assert.That(second.Phase, Is.EqualTo(ViewPhase.Open), _h.Describe());
        }

        [Test]
        public void KeptInstanceReused_DoesNotInjectAgain()
        {
            const string kept = "kept";
            var instance = KeepIdle(kept);
            _h.Stage.ClearCalls();
            _h.Stage.FailNext(StageCallKind.Inject, new InvalidOperationException("inject must not run"));

            var handle = _h.Navigator.Open(LifecycleDriver.KeyOf(kept), _h.Owner);

            Assert.That(_h.Stage.CountOf(StageCallKind.Acquire), Is.Zero, "Control: the kept instance is reused.\n" + _h.Stage.DescribeCalls());
            Assert.That(_h.Stage.LastInstanceOf(kept), Is.SameAs(instance), "Control: no new instance.");
            Assert.That(_h.Stage.CountOf(StageCallKind.Inject), Is.Zero, "A kept instance is not injected again.\n" + _h.Stage.DescribeCalls());
            Assert.That(_h.Errors, Is.Empty, "No routed error.");
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open), _h.Describe());
        }

        // Bind, place and relink

        [TestCase(PrepareStep.Bind)]
        [TestCase(PrepareStep.Place)]
        [TestCase(PrepareStep.Relink)]
        public void PreparingThrows_RoutesTheExceptionAsProvider(PrepareStep step)
        {
            var fault = new InvalidOperationException(step + " failed");

            var result = ShowDetailWithStepThrowing(step, fault).ResultNow();

            Assert.That(_h.Errors.Count, Is.EqualTo(1), "One routed error.\n" + _h.Stage.DescribeCalls());
            Assert.That(_h.Errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(_h.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider), "source");
            Assert.That(result.WasShown, Is.False, "was shown");
            Assert.That(result.Reason, Is.EqualTo(CloseReason.NotShown), "reason");
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.LoadFailed), "reject reason");
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.LoadFailed), Is.EqualTo(1), _h.Diagnostics.Describe());
            Assert.That(
                _h.Diagnostics.Entries,
                Has.Some.Matches<DiagnosticEntry>(entry => entry.Id == DiagnosticIds.LoadFailed
                    && entry.Message.StartsWith("Preparing '" + Detail + "' failed", StringComparison.Ordinal)),
                _h.Diagnostics.Describe());
        }

        // On Relink the released instance is the one that was on screen.
        [TestCase(PrepareStep.Bind)]
        [TestCase(PrepareStep.Place)]
        [TestCase(PrepareStep.Relink)]
        public void PreparingThrows_ReleasesTheInstanceOnce_AndLeavesNoShieldHolder(PrepareStep step)
        {
            ShowDetailWithStepThrowing(step, new InvalidOperationException(step + " failed")).ResultNow();

            var instance = _h.Stage.LastInstanceOf(Detail);
            Assert.That(instance, Is.Not.Null, "The instance was acquired.\n" + _h.Stage.DescribeCalls());
            Assert.That(NavigationTestKit.InstanceCount(_h.Stage, Detail), Is.EqualTo(1), "One instance throughout.");
            Assert.That(instance.ReleaseCount, Is.EqualTo(1), "released once\n" + _h.Stage.DescribeCalls());
            Assert.That(_h.ShieldCount, Is.Zero, "Shield holders left.\n" + _h.Describe());
        }

        [TestCase(PrepareStep.Bind)]
        [TestCase(PrepareStep.Place)]
        [TestCase(PrepareStep.Relink)]
        public void PreparingThrows_TheNextOpenOnTheSameLayerIsShown(PrepareStep step)
        {
            _h.Row("other");
            ShowDetailWithStepThrowing(step, new InvalidOperationException(step + " failed")).ResultNow();

            var other = NavigationTestKit.Open(_h, "other");

            Assert.That(other.Phase, Is.EqualTo(ViewPhase.Open), _h.Describe());
        }

        [Test]
        public void RelinkingThrows_TheReplacedRequestStillEndsAsNavigated()
        {
            _h.Row("home").Row(Detail).Map<string, NoResult>(Detail);
            var key = ViewKey.Create<string>(Detail);
            NavigationTestKit.Open(_h, "home");
            var first = _h.Navigator.ShowAsync(key, "first", _h.Owner);
            _h.Stage.FailNext(StageCallKind.Relink, new InvalidOperationException("Relink failed"));

            _h.Navigator.ShowAsync(key, "second", _h.Owner).ResultNow();

            var firstResult = first.ResultNow();
            Assert.That(firstResult.WasShown, Is.True, "The replaced request was shown.");
            Assert.That(firstResult.Reason, Is.EqualTo(CloseReason.Navigated), "The replaced request ends as Navigated, not with the failure.");
        }
    }
}
