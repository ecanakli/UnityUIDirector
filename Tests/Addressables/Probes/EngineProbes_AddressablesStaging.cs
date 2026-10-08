using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Instances created under an inactive parent stay dormant until reparented under an active one.
    [PrebuildSetup(typeof(ProbeContentSetup))]
    [PostBuildCleanup(typeof(ProbeContentSetup))]
    public sealed class EngineProbes_AddressablesStaging : AddressablesProbeFixture
    {
        [UnityTest]
        public IEnumerator P2_InstantiateAsync_UnderInactiveParent_RunsNoAwakeOrOnEnable()
        {
            RequireEnvironment();

            var spawn = new Spawn();
            yield return SpawnView(spawn, Staging.transform);

            GameObject instance = spawn.Instance;
            ProbeView view = spawn.View;
            Assert.IsTrue(view != null, "The instance has no ProbeView");

            bool underStaging = instance.transform.parent == Staging.transform;
            bool activeSelf = instance.activeSelf;
            bool activeInHierarchy = instance.activeInHierarchy;
            int awakeAtOnce = view.AwakeCount;
            int enableAtOnce = view.EnableCount;

            // nothing may run in a later frame either
            yield return null;
            int awakeNextFrame = view.AwakeCount;
            int enableNextFrame = view.EnableCount;

            instance.transform.SetParent(Root.transform, false);
            int awakeAfterReparent = view.AwakeCount;
            int enableAfterReparent = view.EnableCount;
            bool activeAfterReparent = instance.activeInHierarchy;

            ProbeLog.Emit(
                "P2",
                "test", "InstantiateAsync_UnderInactiveParent",
                "completedSync", spawn.SyncDone,
                "completedAfterFrames", spawn.Wait.Frames,
                "instanceExists", instance != null,
                "underStaging", underStaging,
                "activeSelf", activeSelf,
                "activeInHierarchy", activeInHierarchy,
                "awake", awakeAtOnce,
                "onEnable", enableAtOnce,
                "awakeNextFrame", awakeNextFrame,
                "onEnableNextFrame", enableNextFrame,
                "awakeAfterReparent", awakeAfterReparent,
                "onEnableAfterReparent", enableAfterReparent,
                "activeAfterReparent", activeAfterReparent,
                "errorLogs", Logs.Failures.Count);

            Assert.IsTrue(instance != null, "The instance does not exist");
            Assert.IsTrue(underStaging, "The instance is not under the inactive parent");
            Assert.IsFalse(activeInHierarchy, "The instance is active in the hierarchy under an inactive parent");
            Assert.AreEqual(0, awakeAtOnce, "Awake ran under an inactive parent");
            Assert.AreEqual(0, enableAtOnce, "OnEnable ran under an inactive parent");
            Assert.AreEqual(0, awakeNextFrame, "Awake ran a frame later under an inactive parent");
            Assert.AreEqual(0, enableNextFrame, "OnEnable ran a frame later under an inactive parent");
            Assert.AreEqual(1, awakeAfterReparent, "Awake did not run once on reparenting under an active parent");
            Assert.AreEqual(1, enableAfterReparent, "OnEnable did not run once on reparenting under an active parent");
            Assert.AreEqual(0, Logs.Failures.Count, Logs.FailuresText);

            Addressables.ReleaseInstance(spawn.Handle);
        }

        // Companion to the claim for rows that hold a direct prefab reference.
        [UnityTest]
        public IEnumerator P2_ObjectInstantiate_UnderInactiveParent_RunsNoAwakeOrOnEnable()
        {
            RequireEnvironment();

            AsyncOperationHandle<GameObject> lease = Track(Addressables.LoadAssetAsync<GameObject>(ProbeContent.ViewAddress));
            var wait = new WaitResult();
            yield return WaitDone(lease, wait);
            Assert.IsFalse(wait.TimedOut, "LoadAssetAsync did not complete in time");
            Assert.AreEqual(AsyncOperationStatus.Succeeded, lease.Status, "LoadAssetAsync failed");

            GameObject instance = Object.Instantiate(lease.Result, Staging.transform, false);
            ProbeView view = instance.GetComponent<ProbeView>();
            Assert.IsTrue(view != null, "The instance has no ProbeView");

            int awakeAtOnce = view.AwakeCount;
            int enableAtOnce = view.EnableCount;
            bool activeInHierarchy = instance.activeInHierarchy;

            yield return null;
            int awakeNextFrame = view.AwakeCount;
            int enableNextFrame = view.EnableCount;

            instance.transform.SetParent(Root.transform, false);
            int awakeAfterReparent = view.AwakeCount;
            int enableAfterReparent = view.EnableCount;

            ProbeLog.Emit(
                "P2",
                "test", "ObjectInstantiate_UnderInactiveParent",
                "activeInHierarchy", activeInHierarchy,
                "awake", awakeAtOnce,
                "onEnable", enableAtOnce,
                "awakeNextFrame", awakeNextFrame,
                "onEnableNextFrame", enableNextFrame,
                "awakeAfterReparent", awakeAfterReparent,
                "onEnableAfterReparent", enableAfterReparent,
                "errorLogs", Logs.Failures.Count);

            Assert.IsFalse(activeInHierarchy, "The instance is active in the hierarchy under an inactive parent");
            Assert.AreEqual(0, awakeAtOnce, "Awake ran under an inactive parent");
            Assert.AreEqual(0, enableAtOnce, "OnEnable ran under an inactive parent");
            Assert.AreEqual(0, awakeNextFrame, "Awake ran a frame later under an inactive parent");
            Assert.AreEqual(0, enableNextFrame, "OnEnable ran a frame later under an inactive parent");
            Assert.AreEqual(1, awakeAfterReparent, "Awake did not run once on reparenting under an active parent");
            Assert.AreEqual(1, enableAfterReparent, "OnEnable did not run once on reparenting under an active parent");
            Assert.AreEqual(0, Logs.Failures.Count, Logs.FailuresText);

            Addressables.Release(lease);
        }
    }
}
