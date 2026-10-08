using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // The provider owns the InstantiateAsync handle (tracking off) and is the only releaser.
    [PrebuildSetup(typeof(ProbeContentSetup))]
    [PostBuildCleanup(typeof(ProbeContentSetup))]
    public sealed class EngineProbes_AddressablesHandles : AddressablesProbeFixture
    {
        [UnityTest]
        public IEnumerator P3_TrackHandleCanBeTurnedOffPerCall()
        {
            RequireEnvironment();

            // source shape: public InstantiateAsync overloads that take trackHandle
            int overloads = 0;
            int withTrackHandle = 0;
            foreach (MethodInfo method in typeof(Addressables).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "InstantiateAsync")
                    continue;

                overloads++;
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    if (parameter.Name == "trackHandle" && parameter.ParameterType == typeof(bool))
                    {
                        withTrackHandle++;
                        break;
                    }
                }
            }

            // tracking off: the GameObject overload finds nothing; the instance is never activated here
            var untracked = new Spawn();
            yield return SpawnView(untracked, Staging.transform, null, false);

            bool gameObjectReleased = Addressables.ReleaseInstance(untracked.Instance);
            bool handleValidAfterGameObjectRelease = untracked.Handle.IsValid();
            bool instanceAliveAfterGameObjectRelease = untracked.Instance != null;

            CallOutcome handleRelease = ProbeSupport.Call(() => Addressables.ReleaseInstance(untracked.Handle));
            bool handleValidAfterHandleRelease = untracked.Handle.IsValid();

            var untrackedWait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => untracked.Instance == null, untrackedWait, 5f);

            // tracking on, for contrast; a frame lets the deferred tracking callback run
            var tracked = new Spawn();
            yield return SpawnView(tracked, Staging.transform, null, true);
            yield return null;

            bool trackedGameObjectReleased = Addressables.ReleaseInstance(tracked.Instance);
            bool trackedHandleValidAfter = tracked.Handle.IsValid();

            var trackedWait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => tracked.Instance == null, trackedWait, 5f);

            ProbeLog.Emit(
                "P3",
                "test", "TrackHandleCanBeTurnedOffPerCall",
                "instantiateOverloads", overloads,
                "overloadsWithTrackHandle", withTrackHandle,
                "untrackedGameObjectReleaseReturned", gameObjectReleased,
                "untrackedInstanceAliveAfterIt", instanceAliveAfterGameObjectRelease,
                "untrackedHandleValidAfterIt", handleValidAfterGameObjectRelease,
                "untrackedHandleReleaseThrew", handleRelease.Threw,
                "untrackedHandleReleaseReturned", handleRelease.Value,
                "untrackedHandleValidAfterHandleRelease", handleValidAfterHandleRelease,
                "untrackedDestroyedAfterFrames", untrackedWait.Frames,
                "untrackedDestroyTimedOut", untrackedWait.TimedOut,
                "neverActivatedOnDestroyCount", untracked.View.DestroyCount,
                "trackedGameObjectReleaseReturned", trackedGameObjectReleased,
                "trackedHandleValidAfter", trackedHandleValidAfter,
                "trackedDestroyedAfterFrames", trackedWait.Frames,
                "errorLogs", Logs.Failures.Count,
                "warnings", Logs.Warnings.Count);

            Assert.GreaterOrEqual(withTrackHandle, 1, "No public InstantiateAsync overload takes trackHandle");
            Assert.IsFalse(gameObjectReleased, "ReleaseInstance(GameObject) released an instance created with trackHandle false");
            Assert.IsFalse(handleRelease.Threw, "ReleaseInstance(handle) threw: " + handleRelease.Error);
            Assert.IsFalse(handleValidAfterHandleRelease, "The handle is still valid after ReleaseInstance(handle)");
            Assert.IsFalse(untrackedWait.TimedOut, "ReleaseInstance(handle) did not destroy the instance");
            Assert.IsTrue(trackedGameObjectReleased, "Contrast failed: a tracked instance was not releasable by GameObject");
            Assert.AreEqual(0, Logs.Failures.Count, Logs.FailuresText);
        }

        [UnityTest]
        public IEnumerator P3_ReleaseInstance_DestroysInstance_AndInvalidatesHandle()
        {
            RequireEnvironment();

            var spawn = new Spawn();
            yield return SpawnView(spawn, Staging.transform);
            Activate(spawn);

            ProbeView view = spawn.View;
            int enableCount = view.EnableCount;
            int referenceCountBefore = spawn.Handle.ReferenceCount;

            CallOutcome first = ProbeSupport.Call(() => Addressables.ReleaseInstance(spawn.Handle));
            bool validRightAfter = spawn.Handle.IsValid();
            bool destroyedRightAfter = spawn.Instance == null;

            var wait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => spawn.Instance == null, wait, 5f);

            // a second release of the same handle shows what the provider has to guard against
            CallOutcome second = ProbeSupport.Call(() => Addressables.ReleaseInstance(spawn.Handle));

            ProbeLog.Emit(
                "P3",
                "test", "ReleaseInstance_DestroysInstance_AndInvalidatesHandle",
                "onEnableCount", enableCount,
                "referenceCountBefore", referenceCountBefore,
                "firstReleaseThrew", first.Threw,
                "firstReleaseReturned", first.Value,
                "handleValidRightAfter", validRightAfter,
                "destroyedRightAfter", destroyedRightAfter,
                "destroyedAfterFrames", wait.Frames,
                "destroyTimedOut", wait.TimedOut,
                "onDestroyCount", view.DestroyCount,
                "secondReleaseThrew", second.Threw,
                "secondReleaseError", second.Error ?? "none",
                "errorLogs", Logs.Failures.Count,
                "warnings", Logs.Warnings.Count);

            Assert.IsFalse(first.Threw, "ReleaseInstance(handle) threw: " + first.Error);
            Assert.IsFalse(validRightAfter, "The handle is still valid right after ReleaseInstance(handle)");
            Assert.IsFalse(wait.TimedOut, "The instance was not destroyed after ReleaseInstance(handle)");
            Assert.AreEqual(0, Logs.Failures.Count, Logs.FailuresText);
        }

        [UnityTest]
        public IEnumerator P3_ReleaseFromInsideOwnOnDestroy_IsSafe()
        {
            RequireEnvironment();

            var spawn = new Spawn();
            yield return SpawnView(spawn, Staging.transform);
            Activate(spawn);

            ProbeView view = spawn.View;
            if (view.EnableCount != 1)
                Assert.Inconclusive("The instance was not activated, so OnDestroy cannot be observed");

            int releaseCalls = 0;
            bool releaseReturned = false;
            view.OnDestroyAction = () =>
            {
                releaseCalls++;
                releaseReturned = Addressables.ReleaseInstance(spawn.Handle);
            };

            Object.Destroy(spawn.Instance);

            var wait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => view.DestroyCount > 0, wait, 5f);
            yield return null;

            bool handleValid = spawn.Handle.IsValid();
            string onDestroyError = view.OnDestroyException == null
                ? "none"
                : view.OnDestroyException.GetType().Name + ": " + view.OnDestroyException.Message;

            ProbeLog.Emit(
                "P3",
                "test", "ReleaseFromInsideOwnOnDestroy",
                "onDestroyAfterFrames", wait.Frames,
                "onDestroyTimedOut", wait.TimedOut,
                "onDestroyCount", view.DestroyCount,
                "releaseCalls", releaseCalls,
                "releaseReturned", releaseReturned,
                "onDestroyException", onDestroyError,
                "handleValidAfter", handleValid,
                "instanceDestroyed", spawn.Instance == null,
                "errorLogs", Logs.Failures.Count,
                "warnings", Logs.Warnings.Count);

            if (wait.TimedOut || view.DestroyCount == 0)
                Assert.Inconclusive("OnDestroy did not run, so the release from inside it was not exercised");

            Assert.IsNull(view.OnDestroyException, "Releasing from inside OnDestroy threw: " + onDestroyError);
            Assert.AreEqual(1, releaseCalls, "The release inside OnDestroy ran an unexpected number of times");
            Assert.IsFalse(handleValid, "The handle is still valid after the release inside OnDestroy");
            Assert.IsTrue(spawn.Instance == null, "The instance still exists");
            Assert.AreEqual(0, Logs.Failures.Count, Logs.FailuresText);
        }

        [UnityTest]
        public IEnumerator P3_HandDestroyedInstance_ThenRelease_LeavesNothing()
        {
            RequireEnvironment();

            OpSnapshot before = OpSnapshot.Take();

            var spawn = new Spawn();
            yield return SpawnView(spawn, Staging.transform);
            Activate(spawn);

            Object.Destroy(spawn.Instance);
            var wait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => spawn.Instance == null, wait, 5f);

            bool handleValidAfterDestroy = spawn.Handle.IsValid();
            int referenceCountAfterDestroy = spawn.Handle.ReferenceCount;
            int warningsBeforeRelease = Logs.Warnings.Count;

            CallOutcome release = ProbeSupport.Call(() => Addressables.ReleaseInstance(spawn.Handle));
            bool handleValidAfterRelease = spawn.Handle.IsValid();
            int warningsFromRelease = Logs.Warnings.Count - warningsBeforeRelease;

            yield return null;
            yield return null;
            OpSnapshot after = OpSnapshot.Take();
            string diff = before.Diff(after);

            ProbeLog.Emit(
                "P3",
                "test", "HandDestroyedInstance_ThenRelease",
                "destroyedAfterFrames", wait.Frames,
                "handleValidAfterHandDestroy", handleValidAfterDestroy,
                "referenceCountAfterHandDestroy", referenceCountAfterDestroy,
                "releaseThrew", release.Threw,
                "releaseError", release.Error ?? "none",
                "releaseReturned", release.Value,
                "handleValidAfterRelease", handleValidAfterRelease,
                "warningsFromRelease", warningsFromRelease,
                "countersBefore", before.ToString(),
                "countersAfter", after.ToString(),
                "counterDiff", diff.Length == 0 ? "none" : diff,
                "errorLogs", Logs.Failures.Count);

            Assert.IsFalse(wait.TimedOut, "The hand-destroyed instance was not destroyed");
            Assert.IsFalse(release.Threw, "Releasing after a hand destroy threw: " + release.Error);
            Assert.IsFalse(handleValidAfterRelease, "The handle is still valid after the release");
            Assert.AreEqual(0, diff.Length, "Operations remain after the release: " + diff);
            Assert.AreEqual(0, Logs.Failures.Count, Logs.FailuresText);
        }

        [UnityTest]
        public IEnumerator P3_ReleaseAttachedToLateCompletion()
        {
            RequireEnvironment();

            int callbackCount = 0;
            int callbackFrames = -1;
            int referenceCountInsideCallback = -1;
            GameObject callbackInstance = null;
            Exception callbackException = null;

            int startFrame = Time.frameCount;
            AsyncOperationHandle<GameObject> handle = Track(
                Addressables.InstantiateAsync(ProbeContent.ViewAddress, Staging.transform, false, false));
            bool completedSync = handle.IsDone;

            handle.Completed += completed =>
            {
                callbackCount++;
                callbackFrames = Time.frameCount - startFrame;
                callbackInstance = completed.Result;
                referenceCountInsideCallback = completed.ReferenceCount;
                try
                {
                    Addressables.ReleaseInstance(completed);
                }
                catch (Exception exception)
                {
                    callbackException = exception;
                }
            };

            var callbackWait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => callbackCount > 0, callbackWait, 10f);

            var destroyWait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => callbackInstance == null, destroyWait, 5f);

            bool handleValid = handle.IsValid();

            ProbeLog.Emit(
                "P3",
                "test", "ReleaseAttachedToLateCompletion",
                "completedSyncAtCall", completedSync,
                "simulatedLoadDelaySeconds", SimulatedLoadDelay(),
                "callbackCount", callbackCount,
                "callbackAfterFrames", callbackFrames,
                "referenceCountInsideCallback", referenceCountInsideCallback,
                "callbackException", callbackException == null ? "none" : callbackException.GetType().Name + ": " + callbackException.Message,
                "instanceDestroyedAfterFrames", destroyWait.Frames,
                "destroyTimedOut", destroyWait.TimedOut,
                "handleValidAfter", handleValid,
                "errorLogs", Logs.Failures.Count,
                "warnings", Logs.Warnings.Count);

            Assert.IsFalse(callbackWait.TimedOut, "The completion callback never ran");
            Assert.AreEqual(1, callbackCount, "The completion callback ran an unexpected number of times");
            Assert.IsNull(callbackException, "Releasing inside the completion callback threw");
            Assert.IsFalse(destroyWait.TimedOut, "The instance was not destroyed after the release in the callback");
            Assert.IsFalse(handleValid, "The handle is still valid after the release in the callback");
            Assert.AreEqual(0, Logs.Failures.Count, Logs.FailuresText);

            if (completedSync)
                Assert.Inconclusive("The operation completed synchronously, so this mode cannot show a real late completion");
        }

        [UnityTest]
        public IEnumerator P3_AssetReferenceCount_ReturnsToBaseline()
        {
            RequireEnvironment();

            // let deferred callbacks from the setup run before the baseline
            yield return null;
            yield return null;
            OpSnapshot before = OpSnapshot.Take();
            if (!before.AnyAvailable)
            {
                Assert.Inconclusive(
                    "4.0.1 exposes no public observable: ResourceManager.OperationCacheCount, ResourceManager.InstanceOperationCount "
                    + "and AddressablesImpl.TrackedHandleCount are internal and were not found by reflection; "
                    + "the diagnostic callback registration API no longer exists");
            }

            AsyncOperationHandle<GameObject> lease = Track(Addressables.LoadAssetAsync<GameObject>(ProbeContent.ViewAddress));
            var leaseWait = new WaitResult();
            yield return WaitDone(lease, leaseWait);
            Assert.IsFalse(leaseWait.TimedOut, "LoadAssetAsync did not complete in time");
            int leaseCountAfterLoad = lease.ReferenceCount;

            var spawn = new Spawn();
            yield return SpawnView(spawn, Staging.transform);
            Activate(spawn);
            OpSnapshot peak = OpSnapshot.Take();
            int leaseCountWithInstance = lease.ReferenceCount;

            Addressables.ReleaseInstance(spawn.Handle);
            var destroyWait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => spawn.Instance == null, destroyWait, 5f);
            int leaseCountAfterInstanceRelease = lease.ReferenceCount;

            Addressables.Release(lease);
            bool leaseValidAfterRelease = lease.IsValid();

            yield return null;
            yield return null;
            OpSnapshot after = OpSnapshot.Take();
            string diff = before.Diff(after);
            bool moved = before.Moved(peak);

            ProbeLog.Emit(
                "P3",
                "test", "AssetReferenceCount_ReturnsToBaseline",
                "observable", "ResourceManager.OperationCacheCount+InstanceOperationCount+AddressablesImpl.TrackedHandleCount(internal,reflection)",
                "before", before.ToString(),
                "peak", peak.ToString(),
                "after", after.ToString(),
                "counterDiff", diff.Length == 0 ? "none" : diff,
                "leaseRefsAfterLoad", leaseCountAfterLoad,
                "leaseRefsWithInstance", leaseCountWithInstance,
                "leaseRefsAfterInstanceRelease", leaseCountAfterInstanceRelease,
                "leaseValidAfterRelease", leaseValidAfterRelease,
                "errorLogs", Logs.Failures.Count);

            if (!moved)
                Assert.Inconclusive("The counters did not move while content was loaded, so they do not observe this load");

            Assert.AreEqual(0, diff.Length, "Counters did not return to the baseline: " + diff);
            Assert.IsFalse(leaseValidAfterRelease, "The lease handle is still valid after Release");
            Assert.AreEqual(0, Logs.Failures.Count, Logs.FailuresText);
        }

        [UnityTest]
        public IEnumerator P3_Preload_LoadAssetAsync_ThenInstantiateAndRelease()
        {
            RequireEnvironment();

            var instanceFirst = new PreloadOrder { Name = "instanceFirst" };
            yield return RunPreloadOrder(true, instanceFirst);

            var leaseFirst = new PreloadOrder { Name = "leaseFirst" };
            yield return RunPreloadOrder(false, leaseFirst);

            instanceFirst.Emit();
            leaseFirst.Emit();

            Assert.IsEmpty(instanceFirst.Problems, "Order instanceFirst: " + string.Join("; ", instanceFirst.Problems));
            Assert.IsEmpty(leaseFirst.Problems, "Order leaseFirst: " + string.Join("; ", leaseFirst.Problems));
        }

        // Both handles are independent reference counts; the order of release must not matter.
        IEnumerator RunPreloadOrder(bool instanceFirst, PreloadOrder result)
        {
            OpSnapshot before = OpSnapshot.Take();
            int failuresBefore = Logs.Failures.Count;

            AsyncOperationHandle<GameObject> lease = Track(Addressables.LoadAssetAsync<GameObject>(ProbeContent.ViewAddress));
            var leaseWait = new WaitResult();
            yield return WaitDone(lease, leaseWait);
            if (leaseWait.TimedOut || lease.Status != AsyncOperationStatus.Succeeded)
            {
                result.Problems.Add("the preload lease did not load");
                yield break;
            }

            result.LeaseRefsAfterLoad = lease.ReferenceCount;

            var spawn = new Spawn();
            yield return SpawnView(spawn, Staging.transform);
            Activate(spawn);
            result.InstanceCompletedSync = spawn.SyncDone;
            result.InstanceCompletedAfterFrames = spawn.Wait.Frames;
            result.LeaseRefsWithInstance = lease.ReferenceCount;

            var destroyWait = new WaitResult();

            if (instanceFirst)
            {
                CallOutcome releaseInstance = ProbeSupport.Call(() => Addressables.ReleaseInstance(spawn.Handle));
                if (releaseInstance.Threw)
                    result.Problems.Add("ReleaseInstance threw: " + releaseInstance.Error);

                yield return ProbeSupport.WaitUntil(() => spawn.Instance == null, destroyWait, 5f);
                if (destroyWait.TimedOut)
                    result.Problems.Add("the instance was not destroyed");

                result.FirstReleasedOtherStillValid = lease.IsValid();
                result.FirstReleasedOtherStillUsable = lease.IsValid() && lease.Result != null;
                result.OtherRefsAfterFirstRelease = lease.ReferenceCount;
                if (!result.FirstReleasedOtherStillValid || !result.FirstReleasedOtherStillUsable)
                    result.Problems.Add("the lease was invalidated by releasing the instance");

                CallOutcome releaseLease = ProbeSupport.Run(() => Addressables.Release(lease));
                if (releaseLease.Threw)
                    result.Problems.Add("Release(lease) threw: " + releaseLease.Error);
                result.BothInvalidAtEnd = !lease.IsValid() && !spawn.Handle.IsValid();
            }
            else
            {
                CallOutcome releaseLease = ProbeSupport.Run(() => Addressables.Release(lease));
                if (releaseLease.Threw)
                    result.Problems.Add("Release(lease) threw: " + releaseLease.Error);

                result.FirstReleasedOtherStillValid = spawn.Handle.IsValid();
                result.FirstReleasedOtherStillUsable = spawn.Handle.IsValid() && spawn.Instance != null;
                result.OtherRefsAfterFirstRelease = spawn.Handle.ReferenceCount;
                if (!result.FirstReleasedOtherStillValid || !result.FirstReleasedOtherStillUsable)
                    result.Problems.Add("the instance was invalidated by releasing the lease");

                CallOutcome releaseInstance = ProbeSupport.Call(() => Addressables.ReleaseInstance(spawn.Handle));
                if (releaseInstance.Threw)
                    result.Problems.Add("ReleaseInstance threw: " + releaseInstance.Error);

                yield return ProbeSupport.WaitUntil(() => spawn.Instance == null, destroyWait, 5f);
                if (destroyWait.TimedOut)
                    result.Problems.Add("the instance was not destroyed");
                result.BothInvalidAtEnd = !lease.IsValid() && !spawn.Handle.IsValid();
            }

            if (!result.BothInvalidAtEnd)
                result.Problems.Add("a handle is still valid after both releases");

            yield return null;
            yield return null;
            OpSnapshot after = OpSnapshot.Take();
            result.CounterDiff = before.Diff(after);
            result.Counters = before.ToString() + " -> " + after.ToString();
            if (result.CounterDiff.Length > 0)
                result.Problems.Add("operations remain: " + result.CounterDiff);

            result.ErrorLogs = Logs.Failures.Count - failuresBefore;
            if (result.ErrorLogs > 0)
                result.Problems.Add("error logs: " + Logs.FailuresText);
        }

        sealed class PreloadOrder
        {
            public string Name;
            public int LeaseRefsAfterLoad;
            public int LeaseRefsWithInstance;
            public bool InstanceCompletedSync;
            public int InstanceCompletedAfterFrames;
            public bool FirstReleasedOtherStillValid;
            public bool FirstReleasedOtherStillUsable;
            public int OtherRefsAfterFirstRelease;
            public bool BothInvalidAtEnd;
            public string CounterDiff = string.Empty;
            public string Counters = "n/a";
            public int ErrorLogs;
            public readonly List<string> Problems = new List<string>();

            public void Emit()
            {
                ProbeLog.Emit(
                    "P3",
                    "test", "Preload_LoadAssetAsync_ThenInstantiateAndRelease",
                    "order", Name,
                    "leaseRefsAfterLoad", LeaseRefsAfterLoad,
                    "leaseRefsWithInstance", LeaseRefsWithInstance,
                    "instanceCompletedSync", InstanceCompletedSync,
                    "instanceCompletedAfterFrames", InstanceCompletedAfterFrames,
                    "otherHandleValidAfterFirstRelease", FirstReleasedOtherStillValid,
                    "otherHandleUsableAfterFirstRelease", FirstReleasedOtherStillUsable,
                    "otherRefsAfterFirstRelease", OtherRefsAfterFirstRelease,
                    "bothInvalidAtEnd", BothInvalidAtEnd,
                    "counters", Counters,
                    "counterDiff", CounterDiff.Length == 0 ? "none" : CounterDiff,
                    "errorLogs", ErrorLogs,
                    "problems", Problems.Count == 0 ? "none" : string.Join("; ", Problems));
            }
        }
    }
}
