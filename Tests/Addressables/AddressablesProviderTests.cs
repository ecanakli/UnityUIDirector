using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector.Tests.Probes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
#endif

namespace Ecanakli.UIDirector.Tests
{
    // AddressablesViewProvider against real Addressables content ("Use Asset Database", simulated load delay), directly and through a ViewRoot.
    [TestFixture]
    [Timeout(60000)]
    [PrebuildSetup(typeof(ProbeContentSetup))]
    [PostBuildCleanup(typeof(ProbeContentSetup))]
    public sealed class AddressablesProviderTests
    {
        private const float DeadlineSeconds = 10f;
        private const float ShortLoadTimeoutSeconds = 0.1f;
        private const float LongSimulatedLoadDelaySeconds = 1f;
        private const string MissingKey = "uidirector-provider-missing-key";

        private readonly List<Object> _created = new List<Object>();
        private readonly List<AsyncOperationHandle<GameObject>> _outsideHandles = new List<AsyncOperationHandle<GameObject>>();
        private CancellationTokenSource _owner;
        private GameObject _staging;
        private OpSnapshot _baseline;
        private string _environmentError;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AddressableTestView.ResetAwakeTotal();
            _owner = new CancellationTokenSource();
            _staging = Track(new GameObject("ProviderTestStaging"));
            _staging.SetActive(false);

            _environmentError = CheckEnvironment();
            if (_environmentError != null)
                yield break;

            // auto-released handle; an invalid handle reports IsDone, so the wait ends in both cases
            var init = Addressables.InitializeAsync();
            var wait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => init.IsDone, wait, 30f);

            if (wait.TimedOut)
                _environmentError = "Addressables initialization did not complete in 30 s";
            else if (!Addressables.ResourceLocators.Any())
                _environmentError = "Addressables initialized without a resource locator";

            // deferred callbacks run before the baseline is taken
            yield return null;
            yield return null;

            _baseline = OpSnapshot.Take();
            if (_environmentError == null && !_baseline.AnyAvailable)
                _environmentError = "The ResourceManager counters could not be read by reflection";
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _owner.Cancel();
            _owner.Dispose();

            // references a test took on the asset itself; a passing test has released them already
            foreach (var outside in _outsideHandles)
            {
                if (outside.IsValid())
                    Addressables.Release(outside);
            }

            _outsideHandles.Clear();

            // newest first, so a root is destroyed before the provider it releases through
            for (var i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.Destroy(_created[i]);
            }

            _created.Clear();
            yield return null;
            yield return null;
        }

        // Direct provider calls

        [UnityTest]
        public IEnumerator AcquireAsync_UnderInactiveParent_ReturnsInactiveInstanceWithoutAwake() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();

            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);

            Assert.That(instance, Is.Not.Null, "instance");
            var view = instance.GetComponent<AddressableTestView>();
            Assert.That(view, Is.Not.Null, "AddressableTestView on the instance");
            Assert.That(instance.transform.parent, Is.SameAs(_staging.transform), "parent");
            Assert.That(instance.activeInHierarchy, Is.False, "active in hierarchy");
            Assert.That(view.AwakeCount, Is.EqualTo(0), "Awake calls");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances");

            provider.Release(instance);
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator Release_AcquiredInstance_DestroysItAndReturnsCountsToBaseline() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);
            // the counters see the asset load, so the baseline check below has teeth
            Assert.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "ResourceManager counts while the instance is held");

            provider.Release(instance);
            var liveRightAfter = provider.LiveInstanceCount;
            await WaitUntilAsync(() => instance == null, "the released instance to be destroyed");

            Assert.That(liveRightAfter, Is.EqualTo(0), "live instances right after Release");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator AcquireAsync_CancelledBeforeCompletion_ThrowsAtOnceAndReleasesLateInstance() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            using var cancel = new CancellationTokenSource();
            var acquire = provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, cancel.Token);
            Assume.That(acquire.Status, Is.EqualTo(UniTaskStatus.Pending), "the load completed synchronously, so nothing was in flight");

            cancel.Cancel();
            var statusRightAfterCancel = acquire.Status;
            var thrown = await CatchAsync(() => acquire);

            Assert.That(statusRightAfterCancel, Is.EqualTo(UniTaskStatus.Canceled), "status right after Cancel");
            Assert.That(thrown, Is.InstanceOf<OperationCanceledException>(), "exception");

            await WaitRealtimeAsync(LateWindowSeconds());
            Assert.That(_staging.transform.childCount, Is.EqualTo(0), "instances under the parent after the late completion");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator AcquireAsync_TokenAlreadyCancelled_ThrowsAtOnceAndLeavesNothing() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            var acquire = provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, cancel.Token);
            var statusRightAfterCall = acquire.Status;
            var thrown = await CatchAsync(() => acquire);

            Assert.That(statusRightAfterCall, Is.EqualTo(UniTaskStatus.Canceled), "status right after the call");
            Assert.That(thrown, Is.InstanceOf<OperationCanceledException>(), "exception");

            await WaitRealtimeAsync(LateWindowSeconds());
            Assert.That(_staging.transform.childCount, Is.EqualTo(0), "instances under the parent");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator AcquireAsync_UnknownKey_ThrowsInvalidOperationExceptionNamingTheKey() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            // The editor logs a failed load through Addressables' exception handler
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(MissingKey)));

            var thrown = await CatchAsync(() => provider.AcquireAsync(MissingKey, _staging.transform, CancellationToken.None));

            // The inner exception is the operation's own, which for an unknown key is InvalidKeyException
            // An unknown-key operation is created already failed and sits in no ResourceManager cache, so its release is not observable here.
            Assert.That(thrown, Is.InstanceOf<InvalidOperationException>(), "exception");
            Assert.That(thrown.Message, Does.Contain(MissingKey), "message");
            Assert.That(thrown.InnerException, Is.Not.Null, "inner exception");
            Assert.That(thrown.InnerException, Is.InstanceOf<InvalidKeyException>(), "inner exception type");
            Assert.That(_staging.transform.childCount, Is.EqualTo(0), "instances under the parent");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator AcquireAsync_AddressOrGuidKey_ReturnsInactiveInstanceUnderTheParent([Values("address", "guid")] string keyForm) => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var key = KeyOf(keyForm);
            Assume.That(key, Is.Not.Null.And.Not.Empty, "the key of the provider view prefab could not be read");

            var instance = await provider.AcquireAsync(key, _staging.transform, CancellationToken.None);

            Assert.That(instance, Is.Not.Null, "instance");
            Assert.That(instance.transform.parent, Is.SameAs(_staging.transform), "parent");
            Assert.That(instance.activeInHierarchy, Is.False, "active in hierarchy");
            Assert.That(AddressableTestView.AwakeTotal, Is.EqualTo(0), "Awake calls");

            provider.Release(instance);
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator AcquireAsync_NullParent_ThrowsArgumentNullException() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();

            TestDelegate call = () => provider.AcquireAsync(ProbeContent.ProviderViewAddress, null, CancellationToken.None);

            Assert.That(call, Throws.TypeOf<ArgumentNullException>()
                .With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("inactiveParent"));
            // a started load adds its operation synchronously, so this shows that none started
            var afterCall = OpSnapshot.Take();

            Assert.That(_baseline.Diff(afterCall), Is.Empty, "ResourceManager counts right after the call " + _baseline + " -> " + afterCall);
            await AssertBaselineAsync(provider);
            Assert.That(AddressableTestView.AwakeTotal, Is.EqualTo(0), "Awake calls");
        });

        [UnityTest]
        public IEnumerator AcquireAsync_ParentAlreadyDestroyed_ThrowsInvalidOperationExceptionNamingTheKey() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var parent = CreateInactiveParent();
            var destroyedParent = parent.transform;
            Object.DestroyImmediate(parent);

            var acquire = provider.AcquireAsync(ProbeContent.ProviderViewAddress, destroyedParent, CancellationToken.None);
            var statusRightAfterCall = acquire.Status;
            // a started load adds its operation synchronously, so this shows that none started
            var afterCall = OpSnapshot.Take();
            var thrown = await CatchAsync(() => acquire);

            Assert.That(statusRightAfterCall, Is.EqualTo(UniTaskStatus.Faulted), "status right after the call");
            Assert.That(_baseline.Diff(afterCall), Is.Empty, "ResourceManager counts right after the call " + _baseline + " -> " + afterCall);
            Assert.That(thrown, Is.InstanceOf<InvalidOperationException>(), "exception");
            Assert.That(thrown.Message, Does.Contain(ProbeContent.ProviderViewAddress), "message");
            await AssertBaselineAsync(provider);
            Assert.That(AddressableTestView.AwakeTotal, Is.EqualTo(0), "Awake calls");
        });

        [UnityTest]
        public IEnumerator AcquireAsync_ParentDestroyedDuringLoad_ThrowsInvalidOperationExceptionNamingTheKey() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var parent = CreateInactiveParent();
            var acquire = provider.AcquireAsync(ProbeContent.ProviderViewAddress, parent.transform, CancellationToken.None);
            Assume.That(acquire.Status, Is.EqualTo(UniTaskStatus.Pending), "the load completed synchronously, so nothing was in flight");

            // DestroyImmediate, so the parent is gone before the simulated load delay ends
            Object.DestroyImmediate(parent);
            var thrown = await CatchAsync(() => acquire);

            Assert.That(thrown, Is.InstanceOf<InvalidOperationException>(), "exception");
            Assert.That(thrown.Message, Does.Contain(ProbeContent.ProviderViewAddress), "message");
            Assert.That(AddressableTestView.AwakeTotal, Is.EqualTo(0), "Awake calls");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator AcquireAsync_ParentDestroyedAndTokenCancelledDuringLoad_RunsNoAwakeAndReleasesTheLoad() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var parent = CreateInactiveParent();
            using var cancel = new CancellationTokenSource();
            var acquire = provider.AcquireAsync(ProbeContent.ProviderViewAddress, parent.transform, cancel.Token);
            Assume.That(acquire.Status, Is.EqualTo(UniTaskStatus.Pending), "the load completed synchronously, so nothing was in flight");

            Object.DestroyImmediate(parent);
            cancel.Cancel();
            var statusRightAfterCancel = acquire.Status;
            // the load still holds its operation, so the baseline check below waits for the late completion
            Assume.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "the load was no longer in flight after Cancel");
            var thrown = await CatchAsync(() => acquire);

            Assert.That(statusRightAfterCancel, Is.EqualTo(UniTaskStatus.Canceled), "status right after Cancel");
            Assert.That(thrown, Is.InstanceOf<OperationCanceledException>(), "exception");

            // the window is a lower bound; the baseline check waits until the late load ended and was released
            await WaitRealtimeAsync(LateWindowSeconds());
            await AssertBaselineAsync(provider);
            Assert.That(AddressableTestView.AwakeTotal, Is.EqualTo(0), "Awake calls after the late completion");
            Assert.That(ViewsInScenes(), Is.EqualTo(0), "AddressableTestView instances in the loaded scenes after the late completion");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(0), "live instances after the late completion");
        });

        [UnityTest]
        public IEnumerator Release_SameInstanceTwice_DoesNothingTheSecondTime() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);
            provider.Release(instance);

            Assert.That(() => provider.Release(instance), Throws.Nothing, "second release in the same frame");
            await WaitUntilAsync(() => instance == null, "the released instance to be destroyed");
            Assert.That(() => provider.Release(instance), Throws.Nothing, "release after the instance was destroyed");

            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator Release_NullInstance_DoesNothing() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);

            Assert.That(() => provider.Release(null), Throws.Nothing, "Release(null)");
            await UniTask.NextFrame();

            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances after Release(null)");
            Assert.That(instance != null, Is.True, "the acquired instance is still alive");

            provider.Release(instance);
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator Release_UnknownInstance_LeavesItAndTheLiveInstancesAlone() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);
            var foreign = Track(new GameObject("ForeignInstance"));

            Assert.That(() => provider.Release(foreign), Throws.Nothing, "Release(foreign)");
            await UniTask.NextFrame();

            Assert.That(foreign != null, Is.True, "the foreign object is still alive");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances");
            Assert.That(instance != null, Is.True, "the acquired instance is still alive");

            provider.Release(instance);
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator Release_InstanceDestroyedFromOutside_ReleasesItsHandle() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);
            Assert.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "ResourceManager counts while the instance is held");
            Object.Destroy(instance);
            await WaitUntilAsync(() => instance == null, "the instance to be destroyed from outside");
            var liveBeforeRelease = provider.LiveInstanceCount;

            // The asset load outlives a hand-destroyed instance until its handle is released
            Assert.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "ResourceManager counts after the outside destroy, before the release");
            Assert.That(() => provider.Release(instance), Throws.Nothing, "Release of a destroyed instance");

            Assert.That(liveBeforeRelease, Is.EqualTo(1), "live instances before the release");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(0), "live instances right after the release");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator AddressablesReleaseInstance_OnAProviderInstance_DoesNothing() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);
            // A tracked handle is registered by a deferred callback, so a frame passes first
            await UniTask.NextFrame();
            var trackedWhileHeld = OpSnapshot.Take().TrackedHandles;

            // The provider instantiates the loaded asset itself, so Addressables has no instance handle for it; only the provider releases it
            var releasedByGameObject = Addressables.ReleaseInstance(instance);
            await UniTask.NextFrame();

            Assert.That(releasedByGameObject, Is.False, "Addressables.ReleaseInstance(GameObject) on a provider instance");
            Assert.That(instance != null, Is.True, "the instance is still alive");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances");
            // The provider's asset load is the one tracked handle while the instance lives; the instance itself has none
            if (_baseline.TrackedHandles.HasValue && trackedWhileHeld.HasValue)
                Assert.That(trackedWhileHeld.Value, Is.EqualTo(_baseline.TrackedHandles.Value + 1), "tracked handles while the instance is held: only the provider's asset handle");

            provider.Release(instance);
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator PreloadAsync_ThenDispose_ReleasesTheAsset() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();

            var lease = await provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None);

            Assert.That(lease, Is.Not.Null, "lease");
            Assert.That(provider.LivePreloadCount, Is.EqualTo(1), "live preloads while held");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(0), "a preload creates no instance");
            Assert.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "ResourceManager counts while the asset is held");

            lease.Dispose();

            Assert.That(provider.LivePreloadCount, Is.EqualTo(0), "live preloads right after Dispose");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator PreloadAsync_LeaseDisposedTwice_DoesNotReleaseAnotherLease() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var first = await provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None);
            var second = await provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None);

            first.Dispose();
            Assert.That(() => first.Dispose(), Throws.Nothing, "second Dispose");

            // the second lease shares the asset operation, so a doubled release would end its reference
            Assert.That(provider.LivePreloadCount, Is.EqualTo(1), "live preloads after the first lease was disposed twice");
            Assert.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "ResourceManager counts while the second lease holds the asset");

            second.Dispose();
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator PreloadAsync_CancelledBeforeCompletion_ThrowsAtOnceAndReleasesLateLoad() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            using var cancel = new CancellationTokenSource();
            var preload = provider.PreloadAsync(ProbeContent.ProviderViewAddress, cancel.Token);
            Assume.That(preload.Status, Is.EqualTo(UniTaskStatus.Pending), "the load completed synchronously, so nothing was in flight");

            cancel.Cancel();
            var statusRightAfterCancel = preload.Status;
            var thrown = await CatchAsync(() => preload);

            Assert.That(statusRightAfterCancel, Is.EqualTo(UniTaskStatus.Canceled), "status right after Cancel");
            Assert.That(thrown, Is.InstanceOf<OperationCanceledException>(), "exception");

            await WaitRealtimeAsync(LateWindowSeconds());
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator PreloadAsync_TokenAlreadyCancelled_ThrowsAtOnceAndLeavesNothing() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            var preload = provider.PreloadAsync(ProbeContent.ProviderViewAddress, cancel.Token);
            var statusRightAfterCall = preload.Status;
            var thrown = await CatchAsync(() => preload);

            Assert.That(statusRightAfterCall, Is.EqualTo(UniTaskStatus.Canceled), "status right after the call");
            Assert.That(thrown, Is.InstanceOf<OperationCanceledException>(), "exception");

            await WaitRealtimeAsync(LateWindowSeconds());
            Assert.That(provider.LivePreloadCount, Is.EqualTo(0), "live preloads after the late window");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator PreloadAsync_UnknownKey_ThrowsInvalidOperationExceptionNamingTheKey() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            // The editor logs a failed load through Addressables' exception handler
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(MissingKey)));

            var thrown = await CatchAsync(() => provider.PreloadAsync(MissingKey, CancellationToken.None));

            // The inner exception is the operation's own, which for an unknown key is InvalidKeyException
            // An unknown-key operation is created already failed and sits in no ResourceManager cache, so its release is not observable here.
            Assert.That(thrown, Is.InstanceOf<InvalidOperationException>(), "exception");
            Assert.That(thrown.Message, Does.Contain(MissingKey), "message");
            Assert.That(thrown.InnerException, Is.Not.Null, "inner exception");
            Assert.That(thrown.InnerException, Is.InstanceOf<InvalidKeyException>(), "inner exception type");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator PreloadAsync_LeaseDisposedBeforeInstanceRelease_InstanceStaysLoaded() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var lease = await provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None);
            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);

            lease.Dispose();
            await UniTask.NextFrame();

            Assert.That(instance != null, Is.True, "the instance is still alive");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances");
            Assert.That(provider.LivePreloadCount, Is.EqualTo(0), "live preloads");

            provider.Release(instance);
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator Release_InstanceReleasedBeforeLeaseDispose_LeaseStaysLoaded() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var lease = await provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None);
            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);

            provider.Release(instance);
            await WaitUntilAsync(() => instance == null, "the released instance to be destroyed");

            Assert.That(provider.LivePreloadCount, Is.EqualTo(1), "live preloads");
            Assert.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "ResourceManager counts while the lease holds the asset");

            lease.Dispose();
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator OnDestroy_WithLiveInstanceAndLease_ReleasesBoth() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);
            await provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None);

            Object.Destroy(provider.gameObject);
            await WaitUntilAsync(() => provider == null, "the provider to be destroyed");

            Assert.That(provider.LiveInstanceCount, Is.EqualTo(0), "live instances right after OnDestroy");
            Assert.That(provider.LivePreloadCount, Is.EqualTo(0), "live preloads right after OnDestroy");
            await WaitUntilAsync(() => instance == null, "the instance to be destroyed by its release");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator OnDestroy_LaterCalls_ThrowObjectDisposedExceptionOrDoNothing() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var instance = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);
            var lease = await provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None);
            Object.Destroy(provider.gameObject);
            await WaitUntilAsync(() => provider == null, "the provider to be destroyed");

            var acquireError = await CatchAsync(() => provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None));
            var preloadError = await CatchAsync(() => provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None));

            Assert.That(acquireError, Is.InstanceOf<ObjectDisposedException>(), "AcquireAsync after OnDestroy");
            Assert.That(preloadError, Is.InstanceOf<ObjectDisposedException>(), "PreloadAsync after OnDestroy");
            Assert.That(() => provider.Release(instance), Throws.Nothing, "Release after OnDestroy");
            Assert.That(() => lease.Dispose(), Throws.Nothing, "lease Dispose after OnDestroy");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator OnDestroy_DuringInFlightAcquire_ReleasesTheLateInstance() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var acquire = provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);
            Assume.That(acquire.Status, Is.EqualTo(UniTaskStatus.Pending), "the load completed synchronously, so nothing was in flight");

            Object.Destroy(provider.gameObject);
            await WaitUntilAsync(() => provider == null, "the provider to be destroyed");
            await WaitUntilAsync(() => acquire.Status != UniTaskStatus.Pending, "the pending acquire to end");
            var thrown = await CatchAsync(() => acquire);

            // The provider owns the late instance; a caller could not release it once the provider is gone
            Assert.That(thrown, Is.InstanceOf<OperationCanceledException>().Or.InstanceOf<ObjectDisposedException>(), "how the pending acquire ended");
            await WaitRealtimeAsync(LateWindowSeconds());
            Assert.That(_staging.transform.childCount, Is.EqualTo(0), "instances under the parent after the late completion");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator OnDestroy_DuringInFlightPreload_ReleasesTheLateLoad() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var preload = provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None);
            Assume.That(preload.Status, Is.EqualTo(UniTaskStatus.Pending), "the load completed synchronously, so nothing was in flight");

            Object.Destroy(provider.gameObject);
            await WaitUntilAsync(() => provider == null, "the provider to be destroyed");
            await WaitUntilAsync(() => preload.Status != UniTaskStatus.Pending, "the pending preload to end");
            var thrown = await CatchAsync(() => preload);

            // The provider owns the late load; a caller could not dispose a lease it never received
            Assert.That(thrown, Is.InstanceOf<OperationCanceledException>().Or.InstanceOf<ObjectDisposedException>(), "how the pending preload ended");
            await WaitRealtimeAsync(LateWindowSeconds());
            Assert.That(provider.LivePreloadCount, Is.EqualTo(0), "live preloads after the late completion");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator OnDestroy_TwoInstancesTwoLeasesAndAnOutsideReference_ReleasesOnlyItsOwn() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            // an outside reference keeps the shared asset operation valid, so a doubled release would show
            var outside = await HoldAssetAsync(ProbeContent.ProviderViewAddress);
            var first = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);
            var second = await provider.AcquireAsync(ProbeContent.ProviderViewAddress, _staging.transform, CancellationToken.None);
            var firstLease = await provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None);
            var secondLease = await provider.PreloadAsync(ProbeContent.ProviderViewAddress, CancellationToken.None);
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(2), "live instances before OnDestroy");
            Assert.That(provider.LivePreloadCount, Is.EqualTo(2), "live preloads before OnDestroy");

            Object.Destroy(provider.gameObject);
            await WaitUntilAsync(() => provider == null, "the provider to be destroyed");

            Assert.That(provider.LiveInstanceCount, Is.EqualTo(0), "live instances right after OnDestroy");
            Assert.That(provider.LivePreloadCount, Is.EqualTo(0), "live preloads right after OnDestroy");
            await WaitUntilAsync(() => first == null && second == null, "both instances to be destroyed by their release");
            Assert.That(outside.IsValid(), Is.True, "the outside reference after OnDestroy");
            Assert.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "ResourceManager counts after OnDestroy while the outside reference holds the asset");

            // Later calls do nothing, so they must not release what the outside reference still holds
            Assert.That(() => firstLease.Dispose(), Throws.Nothing, "first lease Dispose after OnDestroy");
            Assert.That(() => secondLease.Dispose(), Throws.Nothing, "second lease Dispose after OnDestroy");
            Assert.That(() => provider.Release(first), Throws.Nothing, "Release of the first instance after OnDestroy");
            Assert.That(() => provider.Release(second), Throws.Nothing, "Release of the second instance after OnDestroy");
            await UniTask.NextFrame();

            Assert.That(outside.IsValid(), Is.True, "the outside reference after the later calls");
            Assert.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "ResourceManager counts after the later calls while the outside reference holds the asset");

            Addressables.Release(outside);
            await AssertBaselineAsync(provider);
        });

        // Through a real ViewRoot with an asset-key row

        [UnityTest]
        public IEnumerator OpenAndClose_AssetKeyRow_ReturnsCountsToBaseline() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            var key = AddRow(root, "addressable-view", ViewRetention.ReleaseOnClose);

            var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => handle.Phase == ViewPhase.Open, "the view to open");
            var liveWhileOpen = provider.LiveInstanceCount;
            var closing = handle.WaitForCloseAsync(CancellationToken.None);
            handle.Close();
            await closing;
            // Release order at close: scopes end -> instance released -> events, so the close awaiter already sees it released
            var liveAtCloseAwaiter = provider.LiveInstanceCount;
            await WaitUntilAsync(() => !handle.IsAlive, "the view to close");

            Assert.That(liveWhileOpen, Is.EqualTo(1), "live instances while open");
            Assert.That(liveAtCloseAwaiter, Is.EqualTo(0), "live instances when WaitForCloseAsync completes (ReleaseOnClose releases at finalize)");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator DestroyFromOutside_OpenView_ReleasesItsHandle() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            var key = AddRow(root, "addressable-view", ViewRetention.ReleaseOnClose);
            var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => handle.Phase == ViewPhase.Open, "the view to open");
            Assert.That(handle.TryGetInstance(out var instance), Is.True, "the open view has an instance");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances while open");
            var closing = handle.WaitForCloseAsync(CancellationToken.None);

            Object.Destroy(instance);
            // The stage forwards the loss on the next frame
            await closing;
            var liveAtCloseAwaiter = provider.LiveInstanceCount;
            await WaitUntilAsync(() => !handle.IsAlive, "the request of the destroyed view to end");

            Assert.That(liveAtCloseAwaiter, Is.EqualTo(0), "live instances when WaitForCloseAsync completes (released at finalize)");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator DestroyFromOutside_OpenKeptView_ReleasesInsteadOfKeeping() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            var key = AddRow(root, "kept-view", ViewRetention.KeepInstance);
            var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => handle.Phase == ViewPhase.Open, "the view to open");
            Assert.That(handle.TryGetInstance(out var instance), Is.True, "the open view has an instance");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances while open");
            var closing = handle.WaitForCloseAsync(CancellationToken.None);

            Object.Destroy(instance);
            // The stage forwards the loss on the next frame; a destroyed instance is released, never kept
            var closed = await closing;
            var liveAtCloseAwaiter = provider.LiveInstanceCount;
            await WaitUntilAsync(() => !handle.IsAlive, "the request of the destroyed view to end");

            Assert.That(closed.Reason, Is.EqualTo(CloseReason.Destroyed), closed.Detail);
            Assert.That(liveAtCloseAwaiter, Is.EqualTo(0), "live instances when WaitForCloseAsync completes (not parked)");
            // no Trim: a parked instance would keep the counts above the baseline; an unexpected fault log fails the test on its own
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator StagedInstanceDestroyedBeforeShown_EndsAsLoadFailedAndReleasesItsHandle() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            // A never-activated instance gets no OnDestroy, so ViewHost cannot report it; the bind step fails instead.
            // DestroyImmediate, because Object.Destroy would wait until after the view was shown.
            var root = CreateRoot(provider, inject: staged => Object.DestroyImmediate(staged));
            var key = AddRow(root, "addressable-view", ViewRetention.ReleaseOnClose);
            // the core's logs for a failed prepare: the routed exception, then UIDIRECTOR104
            LogAssert.Expect(LogType.Exception, new Regex("destroyed"));
            LogAssert.Expect(LogType.Error, new Regex(DiagnosticIds.LoadFailed));

            var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
            var closing = handle.WaitForCloseAsync(CancellationToken.None);
            await WaitUntilAsync(() => !handle.IsAlive, "the request to end");
            var closed = await closing;

            Assert.That(closed.Rejected, Is.EqualTo(RejectReason.LoadFailed), closed.Detail);
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(0), "live instances after the request ended");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator LoadTimeout_ShorterThanTheLoad_EndsAsLoadTimedOutAndReleasesLateInstance() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            // the timeout is the 0.1 s minimum, so the load must be clearly longer than it
            var previousDelay = SetSimulatedLoadDelay(LongSimulatedLoadDelaySeconds);
            try
            {
                var provider = CreateProvider();
                LogAssert.Expect(LogType.Error, new Regex(DiagnosticIds.LoadTimedOut));
                var root = CreateRoot(provider, ShortLoadTimeoutSeconds);
                var key = AddRow(root, "addressable-view", ViewRetention.ReleaseOnClose);

                var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
                var closing = handle.WaitForCloseAsync(CancellationToken.None);
                await WaitUntilAsync(() => !handle.IsAlive, "the request to end");
                var closed = await closing;

                Assert.That(closed.Rejected, Is.EqualTo(RejectReason.LoadTimedOut), closed.Detail);
                await WaitRealtimeAsync(LateWindowSeconds());
                await AssertBaselineAsync(provider);
            }
            finally
            {
                SetSimulatedLoadDelay(previousDelay);
            }
        });

        [UnityTest]
        public IEnumerator NavigatorPreloadAsync_OwnerEnds_ReleasesTheLease() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            AddRow(root, "preloaded-view", ViewRetention.ReleaseOnClose);
            using var preloadOwner = new CancellationTokenSource();

            var loaded = await root.Navigator.PreloadAsync("preloaded-view", preloadOwner.Token);
            var livePreloadsWhileHeld = provider.LivePreloadCount;
            var liveInstancesWhileHeld = provider.LiveInstanceCount;
            preloadOwner.Cancel();

            Assert.That(loaded, Is.True, "PreloadAsync result");
            Assert.That(livePreloadsWhileHeld, Is.EqualTo(1), "live preloads while the owner lives");
            Assert.That(liveInstancesWhileHeld, Is.EqualTo(0), "a preload creates no instance");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator NavigatorPreloadAsync_OwnerEndsDuringLoad_ReturnsFalseAndReleasesTheLateLoad() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            AddRow(root, "preloaded-view", ViewRetention.ReleaseOnClose);
            using var preloadOwner = new CancellationTokenSource();

            var preload = root.Navigator.PreloadAsync("preloaded-view", preloadOwner.Token);
            Assume.That(preload.Status, Is.EqualTo(UniTaskStatus.Pending), "the load completed synchronously, so nothing was in flight");
            preloadOwner.Cancel();
            var loaded = await preload;

            // PreloadAsync(id, owner) returns false, lease released; only the provider's late callback can release the load
            Assert.That(loaded, Is.False, "PreloadAsync result");
            await WaitRealtimeAsync(LateWindowSeconds());
            Assert.That(provider.LivePreloadCount, Is.EqualTo(0), "live preloads after the late completion");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator NavigatorPreloadAsync_RootDestroyedDuringLoad_ReturnsFalseAndReleasesTheLateLoad() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            AddRow(root, "preloaded-view", ViewRetention.ReleaseOnClose);
            using var preloadOwner = new CancellationTokenSource();

            var preload = root.Navigator.PreloadAsync("preloaded-view", preloadOwner.Token);
            Assume.That(preload.Status, Is.EqualTo(UniTaskStatus.Pending), "the load completed synchronously, so nothing was in flight");
            Object.Destroy(root.gameObject);
            await WaitUntilAsync(() => root == null, "the root to be destroyed");
            await WaitUntilAsync(() => preload.Status != UniTaskStatus.Pending, "the pending preload to end");
            var loaded = await preload;

            Assert.That(loaded, Is.False, "PreloadAsync result");
            await WaitRealtimeAsync(LateWindowSeconds());
            Assert.That(provider.LivePreloadCount, Is.EqualTo(0), "live preloads after the late completion");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator Trim_IdleKeptInstance_ReleasesItsHandle() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            var key = AddRow(root, "kept-view", ViewRetention.KeepInstance);
            var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => handle.Phase == ViewPhase.Open, "the view to open");
            handle.Close();
            await WaitUntilAsync(() => !handle.IsAlive, "the view to close");
            await UniTask.NextFrame();

            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances while the kept instance is idle");
            Assert.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "ResourceManager counts while the kept instance is idle");

            root.Navigator.Trim();

            Assert.That(provider.LiveInstanceCount, Is.EqualTo(0), "live instances right after Trim");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator Trim_DuringInFlightLoad_LeavesTheLoadAlone() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            var keptKey = AddRow(root, "kept-view", ViewRetention.KeepInstance);
            // a separate asset, so its load is in flight (simulated delay) while the first asset is held
            var key = AddRow(root, "addressable-view", ViewRetention.ReleaseOnClose, ProbeContent.ProviderViewAltAddress);

            // an idle kept instance gives Trim something to release; a navigator lease holds its asset
            var kept = root.Navigator.Open(keptKey, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => kept.Phase == ViewPhase.Open, "the kept view to open");
            Assert.That(kept.TryGetInstance(out var keptInstance), Is.True, "the kept view has an instance");
            kept.Close();
            await WaitUntilAsync(() => !kept.IsAlive, "the kept view to close");
            using var preloadOwner = new CancellationTokenSource();
            var preloaded = await root.Navigator.PreloadAsync("kept-view", preloadOwner.Token);
            Assert.That(preloaded, Is.True, "PreloadAsync result");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances while the kept instance is idle");

            var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => handle.Phase != ViewPhase.Waiting, "the request to start loading");
            Assume.That(handle.Phase, Is.EqualTo(ViewPhase.Loading), "the load finished before Trim could run during it");
            var liveBeforeTrim = provider.LiveInstanceCount;

            root.Navigator.Trim();

            // Trim: every idle kept instance goes at once; a request in flight and a preload lease stay
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(liveBeforeTrim - 1), "live instances right after Trim (the kept one released)");
            Assert.That(provider.LivePreloadCount, Is.EqualTo(1), "live preloads right after Trim");
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Loading), "the in-flight request right after Trim");
            await WaitUntilAsync(() => keptInstance == null, "the trimmed kept instance to be destroyed");
            await WaitUntilAsync(() => handle.Phase == ViewPhase.Open, "the view to open after Trim");

            Assert.That(handle.TryGetInstance(out var instance) && instance != null, Is.True, "the open view has a live instance");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances while open");

            handle.Close();
            await WaitUntilAsync(() => !handle.IsAlive, "the view to close");

            Assert.That(provider.LivePreloadCount, Is.EqualTo(1), "live preloads until the owner ends");
            Assert.That(_baseline.Moved(OpSnapshot.Take()), Is.True, "ResourceManager counts while the lease holds the asset");

            preloadOwner.Cancel();
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator DestroyRoot_WithOpenViewKeptInstanceAndLease_ReleasesEverything() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            var keptKey = AddRow(root, "kept-view", ViewRetention.KeepInstance);
            var openKey = AddRow(root, "open-view", ViewRetention.ReleaseOnClose);
            await OpenAndCloseAsync(root, keptKey);
            var open = root.Navigator.Open(openKey, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => open.Phase == ViewPhase.Open, "the second view to open");
            using var preloadOwner = new CancellationTokenSource();
            var loaded = await root.Navigator.PreloadAsync("open-view", preloadOwner.Token);
            Assert.That(loaded, Is.True, "PreloadAsync result");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(2), "live instances before the root is destroyed (kept and open)");
            Assert.That(provider.LivePreloadCount, Is.EqualTo(1), "live preloads before the root is destroyed");

            Object.Destroy(root.gameObject);
            await WaitUntilAsync(() => root == null, "the root to be destroyed");

            Assert.That(open.IsAlive, Is.False, "the open request ended with the root");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator DestroyRoot_DuringInFlightLoad_ReleasesTheLateInstance() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            var key = AddRow(root, "addressable-view", ViewRetention.ReleaseOnClose);
            var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => handle.Phase != ViewPhase.Waiting, "the request to start loading");
            Assume.That(handle.Phase, Is.EqualTo(ViewPhase.Loading), "the load finished before the root could be destroyed during it");

            Object.Destroy(root.gameObject);
            await WaitUntilAsync(() => root == null, "the root to be destroyed");
            await WaitRealtimeAsync(LateWindowSeconds());

            Assert.That(handle.IsAlive, Is.False, "the in-flight request ended with the root");
            await AssertBaselineAsync(provider);
        });

        [UnityTest]
        public IEnumerator DestroyRoot_DuringInFlightLoad_WakesNoLateInstance() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var provider = CreateProvider();
            var root = CreateRoot(provider);
            var key = AddRow(root, "addressable-view", ViewRetention.ReleaseOnClose);
            var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => handle.Phase != ViewPhase.Waiting, "the request to start loading");
            Assume.That(handle.Phase, Is.EqualTo(ViewPhase.Loading), "the load finished before the root could be destroyed during it");

            Object.Destroy(root.gameObject);
            await WaitUntilAsync(() => root == null, "the root to be destroyed");
            // the window is a lower bound; the baseline check waits until the late load ended and was released
            await WaitRealtimeAsync(LateWindowSeconds());
            await AssertBaselineAsync(provider);

            Assert.That(AddressableTestView.AwakeTotal, Is.EqualTo(0), "Awake calls after the late completion");
        });

        [UnityTest]
        public IEnumerator DestroyRoot_ProviderOnTheRoot_ReleasesEverything() => UniTask.ToCoroutine(async () =>
        {
            RequireEnvironment();
            var root = CreateRootCarryingProvider(out var provider);
            var key = AddRow(root, "addressable-view", ViewRetention.ReleaseOnClose);
            var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => handle.Phase == ViewPhase.Open, "the view to open");
            using var preloadOwner = new CancellationTokenSource();
            var loaded = await root.Navigator.PreloadAsync("addressable-view", preloadOwner.Token);
            Assert.That(loaded, Is.True, "PreloadAsync result");
            Assert.That(provider.LiveInstanceCount, Is.EqualTo(1), "live instances before the root is destroyed");
            Assert.That(provider.LivePreloadCount, Is.EqualTo(1), "live preloads before the root is destroyed");

            Object.Destroy(root.gameObject);
            await WaitUntilAsync(() => root == null, "the root to be destroyed");

            Assert.That(handle.IsAlive, Is.False, "the open request ended with the root");
            await AssertBaselineAsync(provider);
        });

        // Helpers

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        // A reference to the asset the test holds itself, outside any provider; TearDown releases it if the test did not.
        private async UniTask<AsyncOperationHandle<GameObject>> HoldAssetAsync(string key)
        {
            var handle = Addressables.LoadAssetAsync<GameObject>(key);
            _outsideHandles.Add(handle);
            await WaitUntilAsync(() => handle.IsDone, "the outside load of " + key);
            Assert.That(handle.Status, Is.EqualTo(AsyncOperationStatus.Succeeded), "the outside load of " + key);
            return handle;
        }

        private void RequireEnvironment()
        {
            if (_environmentError != null)
                Assert.Inconclusive(_environmentError);
        }

        // A parent of its own, so a test can destroy it without touching the shared staging object.
        private GameObject CreateInactiveParent()
        {
            var parent = Track(new GameObject("SeparateInactiveParent"));
            parent.SetActive(false);
            return parent;
        }

        // The provider view prefab's key in the given form: "address" or "guid".
        private static string KeyOf(string keyForm)
        {
            return keyForm == "guid" ? ProbeContentLookup.ProviderViewGuid() : ProbeContent.ProviderViewAddress;
        }

        private AddressablesViewProvider CreateProvider()
        {
            var holder = Track(new GameObject("TestAddressablesViewProvider"));
            return holder.AddComponent<AddressablesViewProvider>();
        }

        // A root with the provider passed in ViewRootSetup; a null timeout keeps the root's default, a null inject skips injection.
        private ViewRoot CreateRoot(AddressablesViewProvider provider, float? loadTimeoutSeconds = null, Action<GameObject> inject = null)
        {
            return BuildRoot(null, provider, loadTimeoutSeconds, inject);
        }

        // A root that finds the provider as a component on itself (ViewRootSetup.AssetProvider left null).
        private ViewRoot CreateRootCarryingProvider(out AddressablesViewProvider provider)
        {
            AddressablesViewProvider added = null;
            var root = BuildRoot(rootObject => added = rootObject.AddComponent<AddressablesViewProvider>(), null, null, null);
            provider = added;
            return root;
        }

        // Built inactive so the Inspector fields are set before Awake, like the PlayMode kit's StageWorld.
        private ViewRoot BuildRoot(Action<GameObject> configure, IViewProvider setupProvider, float? loadTimeoutSeconds, Action<GameObject> inject)
        {
            CreateEventSystem();

            var rootObject = Track(new GameObject("TestRoot", typeof(RectTransform)));
            rootObject.SetActive(false);
            var canvas = rootObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var root = rootObject.AddComponent<ViewRoot>();
            SetField(root, "_initializeOnAwake", false);
            if (loadTimeoutSeconds.HasValue)
                SetField(root, "_loadTimeoutSeconds", loadTimeoutSeconds.Value);
            if (configure != null)
                configure(rootObject);

            rootObject.SetActive(true);
            root.Initialize(new ViewRootSetup { AssetProvider = setupProvider, Inject = inject });
            return root;
        }

        // UnityEngine.UI is not referenced by this assembly, so the EventSystem is added by name.
        private void CreateEventSystem()
        {
            var eventSystemType = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");
            if (eventSystemType == null)
                return;

            var eventSystem = Track(new GameObject("TestEventSystem"));
            eventSystem.AddComponent(eventSystemType);
        }

        // Registers a one-row catalog whose row resolves a test prefab through its asset key.
        private ViewKey<NoArgs, NoResult> AddRow(ViewRoot root, string id, ViewRetention retention, string assetKey = ProbeContent.ProviderViewAddress)
        {
            var row = new ViewEntry(
                id,
                null,
                assetKey,
                ViewLayers.Screens.Id,
                retention,
                BackPolicy.LayerDefault,
                false,
                CoverPolicy.LayerDefault,
                CoveredMode.DisableCanvas,
                false);

            var catalog = Track(ViewCatalog.Create(new[] { row }));
            root.Navigator.AddCatalog(catalog, _owner.Token);
            return ViewKey.Create(id);
        }

        private async UniTask OpenAndCloseAsync(ViewRoot root, ViewKey<NoArgs, NoResult> key)
        {
            var handle = root.Navigator.Open(key, new NoArgs(), _owner.Token);
            await WaitUntilAsync(() => handle.Phase == ViewPhase.Open, "the view " + key.Id + " to open");
            handle.Close();
            await WaitUntilAsync(() => !handle.IsAlive, "the view " + key.Id + " to close");
        }

        // Waits (bounded) for the provider and the ResourceManager to settle, then asserts the baseline and an empty scene.
        private async UniTask AssertBaselineAsync(AddressablesViewProvider provider)
        {
            await SettleAsync(() =>
                provider.LiveInstanceCount == 0
                && provider.LivePreloadCount == 0
                && _baseline.Diff(OpSnapshot.Take()).Length == 0);

            // a release destroys its instance at the end of the frame
            await UniTask.NextFrame();
            var after = OpSnapshot.Take();

            Assert.That(provider.LiveInstanceCount, Is.EqualTo(0), "live instances at the end");
            Assert.That(provider.LivePreloadCount, Is.EqualTo(0), "live preloads at the end");
            Assert.That(_baseline.Diff(after), Is.Empty, "ResourceManager counts " + _baseline + " -> " + after);
            Assert.That(ViewsInScenes(), Is.EqualTo(0), "AddressableTestView instances left in the scene");
        }

        private static int ViewsInScenes()
        {
            var count = 0;
            foreach (var view in Resources.FindObjectsOfTypeAll<AddressableTestView>())
            {
                if (view != null && view.gameObject.scene.IsValid())
                    count++;
            }

            return count;
        }

        private static async UniTask<Exception> CatchAsync<T>(Func<UniTask<T>> call)
        {
            try
            {
                await call();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        // Fails the test after a real-time deadline instead of hanging the run.
        private static async UniTask WaitUntilAsync(Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail("Timed out after " + DeadlineSeconds + " s waiting for " + what);

                await UniTask.Yield();
            }
        }

        // Like WaitUntilAsync, but the caller asserts afterwards with its own messages.
        private static async UniTask SettleAsync(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (!condition() && Time.realtimeSinceStartup <= deadline)
                await UniTask.Yield();
        }

        // Proving that a late completion leaves nothing needs a fixed window clearly longer than the simulated load delay.
        private static async UniTask WaitRealtimeAsync(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                await UniTask.Yield();
        }

        private static float LateWindowSeconds()
        {
#if UNITY_EDITOR
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var delay = settings != null ? settings.SimulatedLoadDelay : 0f;
#else
            var delay = 0f;
#endif
            return Mathf.Max(0.1f, delay) * 3f;
        }

        // Sets the live "Use Asset Database" load delay and returns the previous one.
        private static float SetSimulatedLoadDelay(float seconds)
        {
#if UNITY_EDITOR
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return seconds;

            var previous = settings.SimulatedLoadDelay;
            settings.SimulatedLoadDelay = seconds;
            return previous;
#else
            return seconds;
#endif
        }

        private static string CheckEnvironment()
        {
#if UNITY_EDITOR
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return "No Addressables settings exist; the probe prebuild setup did not run";
            if (!(settings.ActivePlayModeDataBuilder is BuildScriptFastMode))
                return "The active play mode script is not Use Asset Database";

            var guid = ProbeContentLookup.ProviderViewGuid();
            if (string.IsNullOrEmpty(guid) || settings.FindAssetEntry(guid) == null)
                return "The provider view prefab is not an Addressables entry";

            var altGuid = ProbeContentLookup.ProviderViewAltGuid();
            if (string.IsNullOrEmpty(altGuid) || settings.FindAssetEntry(altGuid) == null)
                return "The second provider view prefab is not an Addressables entry";
            return null;
#else
            return "The provider tests need the editor (Use Asset Database play mode script)";
#endif
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, name);

            field.SetValue(target, value);
        }
    }
}
