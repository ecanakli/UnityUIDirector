using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // First-open cost of a view with 300 images and 40 texts, in editor numbers. Logs only.
    public sealed class EngineProbes_InstantiateCost
    {
        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();

        [TearDown]
        public void TearDown()
        {
            m_Cleanup.DestroyAll();
        }

        [UnityTest]
        public IEnumerator M1_FirstOpen_InstantiateCost()
        {
            const int syncRuns = 5;
            const int asyncRuns = 3;

            var root = ProbeUi.NewRootCanvas("ProbeRoot");
            m_Cleanup.Track(root.gameObject);
            var layer = ProbeUi.NewObject("ActiveLayer", root.transform);
            ProbeUi.Stretch((RectTransform)layer.transform);

            var holder = m_Cleanup.Track(new GameObject("PrefabHolder"));
            holder.SetActive(false);
            var template = ProbeUi.BuildHeavyView(holder.transform, 300, 40);
            var staging = m_Cleanup.Track(new GameObject("Staging"));
            staging.SetActive(false);

            var watch = new System.Diagnostics.Stopwatch();
            var syncInstantiate = new List<double>();
            var syncActivate = new List<double>();
            for (var i = 0; i < syncRuns; i++)
            {
                watch.Restart();
                var clone = Object.Instantiate(template, staging.transform);
                watch.Stop();
                syncInstantiate.Add(watch.Elapsed.TotalMilliseconds);

                watch.Restart();
                clone.transform.SetParent(layer.transform, false);
                Canvas.ForceUpdateCanvases();
                watch.Stop();
                syncActivate.Add(watch.Elapsed.TotalMilliseconds);

                yield return null;
                Object.Destroy(clone);
                yield return null;
            }

            var asyncElapsed = new List<double>();
            var asyncFrames = new List<double>();
            var asyncActivate = new List<double>();
            var asyncTimedOut = false;
            for (var i = 0; i < asyncRuns && !asyncTimedOut; i++)
            {
                var startFrame = Time.frameCount;
                watch.Restart();
                var operation = Object.InstantiateAsync(template, staging.transform);
                while (!operation.isDone && Time.frameCount - startFrame < 600)
                    yield return null;
                watch.Stop();

                if (!operation.isDone)
                {
                    asyncTimedOut = true;
                    break;
                }

                asyncElapsed.Add(watch.Elapsed.TotalMilliseconds);
                asyncFrames.Add(Time.frameCount - startFrame);

                var clone = operation.Result[0];
                watch.Restart();
                clone.transform.SetParent(layer.transform, false);
                Canvas.ForceUpdateCanvases();
                watch.Stop();
                asyncActivate.Add(watch.Elapsed.TotalMilliseconds);

                yield return null;
                Object.Destroy(clone);
                yield return null;
            }

            ProbeLog.Write("M1",
                "images", 300,
                "texts", 40,
                "syncInstantiateFirstMs", ProbeLog.Ms(syncInstantiate[0]),
                "syncInstantiateMedianMs", ProbeLog.Ms(ProbeStats.Median(syncInstantiate)),
                "syncReparentAndRebuildFirstMs", ProbeLog.Ms(syncActivate[0]),
                "syncReparentAndRebuildMedianMs", ProbeLog.Ms(ProbeStats.Median(syncActivate)),
                "asyncCompleted", asyncElapsed.Count,
                "asyncTimedOut", asyncTimedOut,
                "asyncElapsedFirstMs", asyncElapsed.Count > 0 ? ProbeLog.Ms(asyncElapsed[0]) : "n/a",
                "asyncElapsedMedianMs", asyncElapsed.Count > 0 ? ProbeLog.Ms(ProbeStats.Median(asyncElapsed)) : "n/a",
                "asyncFramesFirst", asyncFrames.Count > 0 ? asyncFrames[0] : -1.0,
                "asyncReparentAndRebuildFirstMs", asyncActivate.Count > 0 ? ProbeLog.Ms(asyncActivate[0]) : "n/a",
                "asyncReparentAndRebuildMedianMs", asyncActivate.Count > 0 ? ProbeLog.Ms(ProbeStats.Median(asyncActivate)) : "n/a");
        }
    }
}
