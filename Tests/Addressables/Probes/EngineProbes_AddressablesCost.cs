using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Editor numbers for opening a heavy prefab through Addressables. Logs only, asserts nothing about time.
    [PrebuildSetup(typeof(ProbeContentSetup))]
    [PostBuildCleanup(typeof(ProbeContentSetup))]
    public sealed class EngineProbes_AddressablesCost : AddressablesProbeFixture
    {
        [UnityTest]
        public IEnumerator M1_FirstOpen_AddressablesInstantiateCost()
        {
            RequireEnvironment();

#if UNITY_EDITOR
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            float previousDelay = settings.SimulatedLoadDelay;

            // the simulated delay would hide the real cost, so it is zero for the measurement only
            settings.SimulatedLoadDelay = 0f;
            var first = new CostSample { Label = "first" };
            var second = new CostSample { Label = "afterRelease" };
            try
            {
                yield return Measure(first);
                yield return Measure(second);
            }
            finally
            {
                settings.SimulatedLoadDelay = previousDelay;
            }

            first.Emit(Application.isBatchMode);
            second.Emit(Application.isBatchMode);

            if (first.Images != ProbeContent.HeavyImageCount)
                Assert.Inconclusive("The heavy prefab holds " + first.Images + " Images instead of " + ProbeContent.HeavyImageCount);
            Assert.AreEqual(0, Logs.Failures.Count, Logs.FailuresText);
#else
            yield break;
#endif
        }

#if UNITY_EDITOR
        // One cold or warm open: the call itself, the wait for completion, then activation.
        IEnumerator Measure(CostSample sample)
        {
            int startFrame = Time.frameCount;
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var handle = Track(Addressables.InstantiateAsync(ProbeContent.HeavyAddress, Staging.transform, false, false));
            sample.CallMilliseconds = watch.Elapsed.TotalMilliseconds;
            sample.CompletedSync = handle.IsDone;

            var wait = new WaitResult();
            yield return WaitDone(handle, wait, 30f);
            sample.TotalMilliseconds = watch.Elapsed.TotalMilliseconds;
            sample.Frames = Time.frameCount - startFrame;

            Assert.IsFalse(wait.TimedOut, "InstantiateAsync of the heavy prefab did not complete in time");
            Assert.AreEqual(AsyncOperationStatus.Succeeded, handle.Status, "InstantiateAsync of the heavy prefab failed");

            GameObject instance = handle.Result;
            foreach (Component component in instance.GetComponentsInChildren<Component>(true))
            {
                if (component != null && component.GetType().Name == "Image")
                    sample.Images++;
            }

            // parenting under the active root runs Awake and OnEnable of every Image
            watch.Restart();
            instance.transform.SetParent(Root.transform, false);
            sample.ActivateMilliseconds = watch.Elapsed.TotalMilliseconds;

            Addressables.ReleaseInstance(handle);
            var destroyWait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => instance == null, destroyWait, 10f);
            sample.DestroyedAfterFrames = destroyWait.Frames;
        }

        sealed class CostSample
        {
            public string Label;
            public double CallMilliseconds;
            public double TotalMilliseconds;
            public double ActivateMilliseconds;
            public int Frames;
            public int DestroyedAfterFrames;
            public int Images;
            public bool CompletedSync;

            public void Emit(bool batchMode)
            {
                ProbeLog.Emit(
                    "M1",
                    "test", "FirstOpen_AddressablesInstantiateCost",
                    "open", Label,
                    "callMs", CallMilliseconds,
                    "toCompletionMs", TotalMilliseconds,
                    "frames", Frames,
                    "completedSyncInCall", CompletedSync,
                    "activateMs", ActivateMilliseconds,
                    "destroyedAfterFrames", DestroyedAfterFrames,
                    "images", Images,
                    "simulatedLoadDelaySeconds", 0f,
                    "batchMode", batchMode,
                    "note", "editor-numbers-asset-database-not-device");
            }
        }
#endif
    }
}
