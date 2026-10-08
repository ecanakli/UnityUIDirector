using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Ecanakli.UIDirector.Tests.Probes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // Asset-key rows on a real stage with a provider the test completes by hand: missing provider, late and failed loads, and releases.
    [TestFixture]
    public sealed class StageProviderTests
    {
        private const string RemoteViewId = "remote";
        private const string RemoteAssetKey = "remote-key";

        // Frames a stray second release would need to show up before a count is read.
        private const int SettleFrames = 3;

        private readonly List<RoutedError> _errors = new List<RoutedError>();
        private readonly List<Object> _created = new List<Object>();
        private StageWorld _world;
        private LogRecorder _log;
        private CancellationTokenSource _owner;

        public enum OnOpenFailure
        {
            Decline,
            Throw
        }

        [SetUp]
        public void SetUp()
        {
            _errors.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_world != null)
            {
                _world.Dispose();
                _world = null;
            }

            if (_owner != null)
            {
                _owner.Dispose();
                _owner = null;
            }

            for (var i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    Object.Destroy(_created[i]);
            }

            _created.Clear();

            // Destroy is deferred; the next test must not see this test's objects.
            yield return null;

            // Last, so lines the teardown writes are still let through and the log setting is restored for the next test.
            if (_log != null)
            {
                _log.Dispose();
                _log = null;
            }
        }

        // Missing provider

        // Only the UIDIRECTOR120 line is counted; which other lines a failed load writes is left open, so they are not asserted.
        [UnityTest]
        public IEnumerator Open_AssetKeyRowWithNoProvider_EndsLoadFailedAndLogsUIDIRECTOR120Once()
        {
            var log = StartLogRecorder(letFailingMessagesThrough: true);
            _world = new StageWorld();
            _world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);

            var handle = _world.Navigator.Open(ViewKey.Create(RemoteViewId), _world.Owner);
            var closed = handle.WaitForCloseAsync(CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => !closed.IsPending(), "the request to end");
            yield return null;

            var result = closed.ResultNow();
            Assert.That(result.Reason, Is.EqualTo(CloseReason.NotShown), "close reason");
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.LoadFailed), "reject reason");
            Assert.That(log.Count(LogType.Error, DiagnosticIds.NoProviderForAssetKey), Is.EqualTo(1), "UIDIRECTOR120 error lines:\n" + log.Describe());
            Assert.That(_world.Navigator.IsBusy, Is.False, "navigator busy after the request ended");
            Assert.That(_world.Stage.Shield.raycastTarget, Is.False, "shield blocking after the request ended");
        }

        [UnityTest]
        public IEnumerator PreloadAsync_NoProvider_ReturnsFalseAndLogsUIDIRECTOR120()
        {
            _world = new StageWorld();
            _world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            LogAssert.Expect(LogType.Error, new Regex(DiagnosticIds.NoProviderForAssetKey));

            var preload = _world.Navigator.PreloadAsync(RemoteViewId, _world.Owner);
            yield return TransitionConformance.WaitFor(() => !preload.IsPending(), "the preload to finish");

            Assert.That(preload.ResultNow(), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // Preload and provider lookup

        [UnityTest]
        public IEnumerator PreloadAsync_DirectPrefabRow_ReturnsTrueAtOnceAndTheLeaseDisposeIsANoOp()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Direct");
            world.Row(RemoteViewId, template);
            var owner = CreateOwner();

            var preload = world.Navigator.PreloadAsync(RemoteViewId, owner.Token);
            var pendingAtReturn = preload.IsPending();
            owner.Cancel();
            yield return null;

            Assert.That(pendingAtReturn, Is.False, "pending when PreloadAsync returned");
            Assert.That(preload.ResultNow(), Is.True, "preload result");
            Assert.That(provider.PreloadKeys, Is.Empty, "preloads asked of the provider");
            Assert.That(provider.LeaseDisposeCount, Is.EqualTo(0), "provider leases disposed");
            Assert.That((bool)template, Is.True, "prefab alive after the lease ended");
            Assert.That(_errors, Is.Empty, "routed errors");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PreloadAsync_AssetKeyRowWithProvider_AsksTheProviderAndDisposesTheLeaseOnceWhenTheOwnerEnds()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            var owner = CreateOwner();

            var preload = world.Navigator.PreloadAsync(RemoteViewId, owner.Token);
            yield return TransitionConformance.WaitFor(() => !preload.IsPending(), "the preload to finish");
            var disposedWhileHeld = provider.LeaseDisposeCount;
            owner.Cancel();
            var disposedAtOwnerEnd = provider.LeaseDisposeCount;
            yield return Frames(SettleFrames);

            Assert.That(preload.ResultNow(), Is.True, "preload result");
            Assert.That(provider.PreloadKeys, Is.EqualTo(new[] { RemoteAssetKey }), "preloads asked of the provider");
            Assert.That(disposedWhileHeld, Is.EqualTo(0), "leases disposed while the owner was alive");
            Assert.That(disposedAtOwnerEnd, Is.EqualTo(1), "leases disposed when the owner ended");
            Assert.That(provider.LeaseDisposeCount, Is.EqualTo(1), "leases disposed after the settle frames");
            Assert.That(_errors, Is.Empty, "routed errors");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PreloadAsync_ProviderPreloadThrows_ReturnsFalseAndRoutesProviderError()
        {
            var fault = new InvalidOperationException("The scripted preload failed.");
            var provider = CreateProvider();
            provider.PreloadFault = fault;
            var world = CreateWorld(provider);
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);

            var preload = world.Navigator.PreloadAsync(RemoteViewId, world.Owner);
            yield return TransitionConformance.WaitFor(() => !preload.IsPending(), "the preload to finish");

            Assert.That(preload.ResultNow(), Is.False, "preload result");
            Assert.That(_errors.Count, Is.EqualTo(1), "routed errors");
            Assert.That(_errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(_errors[0].Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(_errors[0].ViewId, Is.EqualTo(RemoteViewId), "error view id");
            Assert.That(_errors[0].RequestId, Is.EqualTo(0), "error request id of a preload");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_ProviderOnTheRootComponent_IsUsedWhenTheSetupHasNone()
        {
            ScriptedProvider provider = null;
            _world = new StageWorld(root => provider = root.gameObject.AddComponent<ScriptedProvider>());
            _world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);

            _world.Navigator.Open(ViewKey.Create(RemoteViewId), _world.Owner);
            yield return WaitForLoad(provider);

            Assert.That(provider.AcquireCount, Is.EqualTo(1), "loads asked of the root's provider");
            Assert.That(provider.LastLoad.AssetKey, Is.EqualTo(RemoteAssetKey), "asset key");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_ProviderInTheSetupAndOnTheRoot_UsesTheSetupProvider()
        {
            var setupProvider = CreateProvider();
            ScriptedProvider rootProvider = null;
            var world = CreateWorld(setupProvider, root => rootProvider = root.gameObject.AddComponent<ScriptedProvider>());
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);

            world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            yield return TransitionConformance.WaitFor(
                () => setupProvider.AcquireCount + rootProvider.AcquireCount > 0,
                "a provider to be asked for the view");
            yield return null;

            Assert.That(setupProvider.AcquireCount, Is.EqualTo(1), "loads asked of the setup's provider");
            Assert.That(rootProvider.AcquireCount, Is.EqualTo(0), "loads asked of the root's provider");
            LogAssert.NoUnexpectedReceived();
        }

        // Late results

        [UnityTest]
        public IEnumerator Open_ProviderResultAfterTheOwnerEnded_IsReleasedOnceAndTheRequestEndsOwnerEnded()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            var owner = CreateOwner();
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), owner.Token);
            yield return WaitForLoad(provider);
            var closed = handle.WaitForCloseAsync(CancellationToken.None);

            owner.Cancel();
            yield return TransitionConformance.WaitFor(() => !closed.IsPending(), "the request to end");
            var instance = provider.LastLoad.CompleteWithInstanceOf(template);
            yield return TransitionConformance.WaitFor(() => provider.ReleaseCount > 0, "the late result to be released");
            yield return Frames(SettleFrames);

            Assert.That(provider.ReleaseCount, Is.EqualTo(1), "releases");
            Assert.That(provider.Released[0], Is.SameAs(instance), "released object");
            Assert.That(closed.ResultNow().Rejected, Is.EqualTo(RejectReason.OwnerEnded), "reject reason");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_ProviderResultAfterTheLoadTimeout_IsReleasedOnce()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider, root => StageWorld.SetField(root, "_loadTimeoutSeconds", LayerRunner.MinTimeoutSeconds));
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            LogAssert.Expect(LogType.Error, new Regex(DiagnosticIds.LoadTimedOut));
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            yield return WaitForLoad(provider);
            Assume.That(world.Stage.Shield.raycastTarget, Is.True, "control: the shield is up while the load is pending");
            var closed = handle.WaitForCloseAsync(CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => !closed.IsPending(), "the load to time out");
            Assume.That(closed.ResultNow().Rejected, Is.EqualTo(RejectReason.LoadTimedOut), "control: the load timed out");
            yield return null;
            var shieldAfterTimeout = world.Stage.Shield.raycastTarget;

            var instance = provider.LastLoad.CompleteWithInstanceOf(template);
            yield return TransitionConformance.WaitFor(() => provider.ReleaseCount > 0, "the late result to be released");
            yield return Frames(SettleFrames);

            Assert.That(provider.ReleaseCount, Is.EqualTo(1), "releases");
            Assert.That(provider.Released[0], Is.SameAs(instance), "released object");
            Assert.That(shieldAfterTimeout, Is.False, "shield blocking after the load timed out");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_ProviderResultAfterTheRootWasDestroyed_IsReleasedOnce()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            yield return WaitForLoad(provider);
            var root = world.Root;

            Object.Destroy(root.gameObject);
            yield return TransitionConformance.WaitFor(() => root == null, "the root to be destroyed");

            // The staging parent went with the root, so the late result is built outside it.
            var instance = Object.Instantiate(template);
            _created.Add(instance);
            provider.LastLoad.Complete(instance);
            yield return Frames(SettleFrames);

            Assert.That(provider.ReleaseCount, Is.EqualTo(1), "releases");
            Assert.That(provider.Released[0], Is.SameAs(instance), "released object");
            Assert.That(_errors, Is.Empty, "routed errors");
            LogAssert.NoUnexpectedReceived();
        }

        // Failed loads

        [UnityTest]
        public IEnumerator Open_ProviderReturnsNull_EndsLoadFailedWith104()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            LogAssert.Expect(LogType.Error, new Regex(DiagnosticIds.LoadFailed));
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            yield return WaitForLoad(provider);
            Assume.That(world.Stage.Shield.raycastTarget, Is.True, "control: the shield is up while the load is pending");
            var closed = handle.WaitForCloseAsync(CancellationToken.None);

            provider.LastLoad.Complete(null);
            yield return TransitionConformance.WaitFor(() => !closed.IsPending(), "the request to end");
            yield return null;

            Assert.That(closed.ResultNow().Rejected, Is.EqualTo(RejectReason.LoadFailed), "reject reason");
            Assert.That(world.Stage.Shield.raycastTarget, Is.False, "shield blocking after the request ended");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_ProviderThrows_EndsLoadFailedAndRoutesOneProviderError()
        {
            var fault = new InvalidOperationException("The scripted load failed.");
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            LogAssert.Expect(LogType.Error, new Regex(DiagnosticIds.LoadFailed));
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            var requestId = handle.RequestId;
            yield return WaitForLoad(provider);
            Assume.That(world.Stage.Shield.raycastTarget, Is.True, "control: the shield is up while the load is pending");
            var closed = handle.WaitForCloseAsync(CancellationToken.None);

            provider.LastLoad.Fail(fault);
            yield return TransitionConformance.WaitFor(() => !closed.IsPending(), "the request to end");
            yield return null;

            Assert.That(closed.ResultNow().Rejected, Is.EqualTo(RejectReason.LoadFailed), "reject reason");
            Assert.That(_errors.Count, Is.EqualTo(1), "routed errors");
            Assert.That(_errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(_errors[0].Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(_errors[0].ViewId, Is.EqualTo(RemoteViewId), "error view id");
            Assert.That(_errors[0].RequestId, Is.EqualTo(requestId), "error request id");
            Assert.That(world.Stage.Shield.raycastTarget, Is.False, "shield blocking after the request ended");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_ProviderReturnsADestroyedObject_EndsLoadFailed()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            LogAssert.Expect(LogType.Error, new Regex(DiagnosticIds.LoadFailed));
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            yield return WaitForLoad(provider);
            var closed = handle.WaitForCloseAsync(CancellationToken.None);
            var destroyed = Object.Instantiate(template, provider.LastLoad.Parent, false);
            Object.DestroyImmediate(destroyed);

            provider.LastLoad.Complete(destroyed);
            yield return TransitionConformance.WaitFor(() => !closed.IsPending(), "the request to end");

            Assert.That(closed.ResultNow().Rejected, Is.EqualTo(RejectReason.LoadFailed));
            LogAssert.NoUnexpectedReceived();
        }

        // Releases

        [UnityTest]
        public IEnumerator Close_ReleaseOnClose_CallsProviderReleaseOnce()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            yield return WaitForLoad(provider);
            var instance = provider.LastLoad.CompleteWithInstanceOf(template);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");

            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");
            yield return Frames(SettleFrames);

            Assert.That(provider.ReleaseCount, Is.EqualTo(1), "releases");
            Assert.That(provider.Released[0], Is.SameAs(instance), "released object");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Release_ReleaseOnCloseViewClosed_HandsTheProviderAnInactiveInstance()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            yield return WaitForLoad(provider);
            provider.LastLoad.CompleteWithInstanceOf(template);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");

            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");
            yield return Frames(SettleFrames);

            Assert.That(provider.ActiveAtRelease, Is.EqualTo(new[] { false }), "active when the provider's Release ran");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Release_ProviderViewDestroyedOutside_CallsProviderReleaseOnce()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            yield return WaitForLoad(provider);
            var instance = provider.LastLoad.CompleteWithInstanceOf(template);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");
            var closed = handle.WaitForCloseAsync(CancellationToken.None);

            LogAssert.Expect(LogType.Warning, new Regex(DiagnosticIds.ViewLostOutsidePackage));
            Object.Destroy(instance);
            yield return TransitionConformance.WaitFor(() => !closed.IsPending(), "the request to end");
            yield return Frames(SettleFrames);
            Assume.That(closed.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed), "control: the loss ended the request");

            Assert.That(provider.ReleaseCount, Is.EqualTo(1), "releases");
            Assert.That(provider.Released[0], Is.SameAs(instance), "released object");
            Assert.That(_errors, Is.Empty, "routed errors");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Close_KeepInstance_DoesNotReleaseUntilTrim()
        {
            var provider = CreateProvider();
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, retention: ViewRetention.KeepInstance, assetKey: RemoteAssetKey);
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            yield return WaitForLoad(provider);
            var instance = provider.LastLoad.CompleteWithInstanceOf(template);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");

            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");
            yield return Frames(SettleFrames);
            var releasesAfterClose = provider.ReleaseCount;
            world.Navigator.Trim();
            yield return null;

            Assert.That(releasesAfterClose, Is.EqualTo(0), "releases after the close");
            Assert.That(provider.ReleaseCount, Is.EqualTo(1), "releases after the trim");
            Assert.That(provider.Released[0], Is.SameAs(instance), "released object");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Release_ProviderReleaseThrows_RoutesProviderErrorWithViewAndRequestId()
        {
            var fault = new InvalidOperationException("The scripted release failed.");
            var provider = CreateProvider();
            provider.ReleaseFault = fault;
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            var requestId = handle.RequestId;
            yield return WaitForLoad(provider);
            provider.LastLoad.CompleteWithInstanceOf(template);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");

            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");
            yield return null;

            Assert.That(provider.ReleaseCount, Is.EqualTo(1), "releases");
            Assert.That(_errors.Count, Is.EqualTo(1), "routed errors");
            Assert.That(_errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(_errors[0].Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(_errors[0].ViewId, Is.EqualTo(RemoteViewId), "error view id");
            Assert.That(_errors[0].RequestId, Is.EqualTo(requestId), "error request id");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Release_ProviderReleaseThrows_EndsRequestWithItsCloseReason()
        {
            var provider = CreateProvider();
            provider.ReleaseFault = new InvalidOperationException("The scripted release failed.");
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            var shown = world.Navigator.ShowAsync(ViewKey.Create(RemoteViewId), world.Owner);
            yield return WaitForLoad(provider);
            provider.LastLoad.CompleteWithInstanceOf(template);
            var handle = default(ViewHandle);
            yield return TransitionConformance.WaitFor(
                () => world.Navigator.TryGetOpen(RemoteViewId, out handle) && handle.Phase == ViewPhase.Open,
                "the view to open");

            handle.Close();
            yield return TransitionConformance.WaitFor(() => !shown.IsPending(), "ShowAsync to complete");

            var result = shown.ResultNow();
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Code), "close reason");
            Assert.That(result.WasShown, Is.True, "was shown");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AcquireAsync_LateResultReleaseThrows_RoutesProviderErrorWithViewId()
        {
            var fault = new InvalidOperationException("The scripted release failed.");
            var provider = CreateProvider();
            provider.ReleaseFault = fault;
            var world = CreateWorld(provider);
            var template = world.Template<RecordingView>("Remote");
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            var owner = CreateOwner();
            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), owner.Token);
            yield return WaitForLoad(provider);
            var closed = handle.WaitForCloseAsync(CancellationToken.None);
            owner.Cancel();
            yield return TransitionConformance.WaitFor(() => !closed.IsPending(), "the request to end");

            provider.LastLoad.CompleteWithInstanceOf(template);
            yield return TransitionConformance.WaitFor(() => provider.ReleaseCount > 0, "the late result to be released");
            yield return null;

            Assert.That(_errors.Count, Is.EqualTo(1), "routed errors");
            Assert.That(_errors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(_errors[0].Source, Is.EqualTo(ViewErrorSource.Provider), "error source");
            Assert.That(_errors[0].ViewId, Is.EqualTo(RemoteViewId), "error view id");
            Assert.That(_errors[0].RequestId, Is.EqualTo(0), "error request id of a load nobody waits for");
            LogAssert.NoUnexpectedReceived();
        }

        // A request that ends in OnOpen gets no hidden state, so only the release keeps the object off screen.
        [UnityTest]
        public IEnumerator Release_OnOpenFailedWithProviderReleaseThrowing_LeavesTheInstanceInactiveAndNotHittable([Values] OnOpenFailure failure)
        {
            Assume.That(ProbeUi.ScreenIsUsable(), Is.True, "screen is " + Screen.width + "x" + Screen.height);
            var fault = new InvalidOperationException("The scripted release failed.");
            var provider = CreateProvider();
            provider.ReleaseFault = fault;
            var world = CreateWorld(provider);
            var template = failure == OnOpenFailure.Decline
                ? world.Template<DecliningView>("Declining", AddFullScreenImage)
                : world.Template<ThrowingView>("Throwing", AddFullScreenImage);
            var expectedReject = failure == OnOpenFailure.Decline ? RejectReason.Declined : RejectReason.Faulted;
            world.Row(RemoteViewId, null, assetKey: RemoteAssetKey);
            if (failure == OnOpenFailure.Throw)
                LogAssert.Expect(LogType.Error, new Regex(DiagnosticIds.OnOpenThrew));

            var handle = world.Navigator.Open(ViewKey.Create(RemoteViewId), world.Owner);
            var requestId = handle.RequestId;
            yield return WaitForLoad(provider);
            var closed = handle.WaitForCloseAsync(CancellationToken.None);

            var instance = provider.LastLoad.CompleteWithInstanceOf(template);
            yield return TransitionConformance.WaitFor(() => !closed.IsPending(), "the request to end in OnOpen");
            Assume.That(closed.ResultNow().Rejected, Is.EqualTo(expectedReject), "control: OnOpen ended the request");
            yield return SettleRender();
            var hits = RaycastAtScreenCentre(world);
            var providerErrors = ErrorsFrom(ViewErrorSource.Provider);

            Assert.That(providerErrors.Count, Is.EqualTo(1), "routed provider errors");
            Assert.That(providerErrors[0].Exception, Is.SameAs(fault), "routed exception");
            Assert.That(providerErrors[0].ViewId, Is.EqualTo(RemoteViewId), "error view id");
            Assert.That(providerErrors[0].RequestId, Is.EqualTo(requestId), "error request id");
            Assert.That(provider.ActiveAtRelease, Is.EqualTo(new[] { false }), "active when the provider's Release ran");
            Assert.That((bool)instance, Is.True, "instance left behind by the failed release");
            Assert.That(instance.activeSelf, Is.False, "instance active after the failed release");
            Assert.That(hits, Has.None.Matches<RaycastResult>(hit => hit.gameObject.transform.IsChildOf(instance.transform)), "raycast hits on the instance");
            LogAssert.NoUnexpectedReceived();

            // Control: the same object, active again, is hit; otherwise this run cannot raycast and the check above proved nothing.
            instance.SetActive(true);
            yield return SettleRender();
            var controlHits = RaycastAtScreenCentre(world);
            Assume.That(
                controlHits,
                Has.Some.Matches<RaycastResult>(hit => hit.gameObject.transform.IsChildOf(instance.transform)),
                "control: an active instance is hit");
        }

        // Helpers

        private ScriptedProvider CreateProvider()
        {
            var holder = new GameObject("ScriptedProvider");
            _created.Add(holder);
            return holder.AddComponent<ScriptedProvider>();
        }

        private StageWorld CreateWorld(IViewProvider provider, Action<ViewRoot> configure = null)
        {
            var setup = new ViewRootSetup
            {
                AssetProvider = provider,
                ErrorHandler = OnError
            };

            _world = new StageWorld(configure, setup);
            return _world;
        }

        private CancellationTokenSource CreateOwner()
        {
            _owner = new CancellationTokenSource();
            return _owner;
        }

        private LogRecorder StartLogRecorder(bool letFailingMessagesThrough)
        {
            _log = new LogRecorder(letFailingMessagesThrough);
            return _log;
        }

        private void OnError(Exception exception, in ViewErrorContext context)
        {
            _errors.Add(new RoutedError(exception, context.Source, context.ViewId, context.RequestId));
        }

        private List<RoutedError> ErrorsFrom(ViewErrorSource source)
        {
            var matching = new List<RoutedError>();
            for (var i = 0; i < _errors.Count; i++)
            {
                if (_errors[i].Source == source)
                    matching.Add(_errors[i]);
            }

            return matching;
        }

        private static IEnumerator WaitForLoad(ScriptedProvider provider)
        {
            yield return TransitionConformance.WaitFor(() => provider.AcquireCount > 0, "the provider to be asked for the view");
        }

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }

        // Layout and rendered frames, so graphics have a depth the raycaster accepts.
        private static IEnumerator SettleRender()
        {
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
        }

        private static List<RaycastResult> RaycastAtScreenCentre(StageWorld world)
        {
            var eventSystem = world.EventSystemObject.GetComponent<EventSystem>();
            var pointer = new PointerEventData(eventSystem)
            {
                position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            };

            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, hits);
            return hits;
        }

        // A stretched raycast target over the whole view.
        private static void AddFullScreenImage(GameObject template)
        {
            var child = new GameObject("Hit", typeof(RectTransform));
            child.transform.SetParent(template.transform, false);
            var rect = (RectTransform)child.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            child.AddComponent<Image>().raycastTarget = true;
        }

        private readonly struct RoutedError
        {
            public readonly Exception Exception;
            public readonly ViewErrorSource Source;
            public readonly string ViewId;
            public readonly int RequestId;

            public RoutedError(Exception exception, ViewErrorSource source, string viewId, int requestId)
            {
                Exception = exception;
                Source = source;
                ViewId = viewId;
                RequestId = requestId;
            }
        }

        [AddComponentMenu("")]
        private sealed class DecliningView : ViewBehaviour
        {
            protected override void OnOpen(NoArgs args, ViewScope open)
            {
                Decline("Declined by the test.");
            }
        }

        [AddComponentMenu("")]
        private sealed class ThrowingView : ViewBehaviour
        {
            protected override void OnOpen(NoArgs args, ViewScope open)
            {
                throw new InvalidOperationException("OnOpen failed in the test.");
            }
        }
    }
}
