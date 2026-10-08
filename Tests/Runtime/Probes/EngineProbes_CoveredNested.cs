using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Cover and reveal cost through ViewHost on a view with nested canvases, each of which must be disabled too.
    public sealed class EngineProbes_CoveredNested
    {
        const int k_Repetitions = 20;
        const int k_NestedCanvases = 3;

        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();

        [TearDown]
        public void TearDown()
        {
            m_Cleanup.DestroyAll();
        }

        [UnityTest]
        public IEnumerator M3_NestedView_CoverAndRevealThroughViewHost()
        {
            var root = ProbeUi.NewRootCanvas("ProbeRoot");
            m_Cleanup.Track(root.gameObject);

            // The same 300 images and 40 texts as the flat view in EngineProbes_CoveredViews, split over the view canvas and three nested ones.
            var view = ProbeUi.BuildHeavyView(root.transform, 150, 16);
            for (var i = 0; i < k_NestedCanvases; i++)
                ProbeUi.BuildHeavyView(view.transform, 50, 8);

            var host = view.AddComponent<ViewHost>();
            var shown = new ViewVisualState(false, true, 10, CoveredMode.DisableCanvas);
            var hiddenCanvas = new ViewVisualState(true, false, 10, CoveredMode.DisableCanvas);
            var hiddenInactive = new ViewVisualState(true, false, 10, CoveredMode.Deactivate);
            var layer = root.sortingLayerID;
            host.Apply(in shown, layer);

            // Settle the first build so only hide and show are measured.
            foreach (var step in ProbeUi.Frames(3))
                yield return step;

            var watch = new System.Diagnostics.Stopwatch();
            var canvasHide = new List<double>();
            var canvasShow = new List<double>();
            var activeHide = new List<double>();
            var activeShow = new List<double>();
            var nestedOffWhileHidden = true;

            for (var i = 0; i < k_Repetitions; i++)
            {
                watch.Restart();
                host.Apply(in hiddenCanvas, layer);
                Canvas.ForceUpdateCanvases();
                watch.Stop();
                canvasHide.Add(watch.Elapsed.TotalMilliseconds);
                nestedOffWhileHidden &= NoCanvasEnabled(view);
                yield return null;

                watch.Restart();
                host.Apply(in shown, layer);
                Canvas.ForceUpdateCanvases();
                watch.Stop();
                canvasShow.Add(watch.Elapsed.TotalMilliseconds);
                yield return null;
            }

            for (var i = 0; i < k_Repetitions; i++)
            {
                watch.Restart();
                host.Apply(in hiddenInactive, layer);
                Canvas.ForceUpdateCanvases();
                watch.Stop();
                activeHide.Add(watch.Elapsed.TotalMilliseconds);
                yield return null;

                watch.Restart();
                host.Apply(in shown, layer);
                Canvas.ForceUpdateCanvases();
                watch.Stop();
                activeShow.Add(watch.Elapsed.TotalMilliseconds);
                yield return null;
            }

            Assert.That(nestedOffWhileHidden, Is.True, "every canvas under the view is off while it is covered");
            Assert.That(view.activeSelf, Is.True, "the view is active again after the last reveal");

            ProbeLog.Write("M3-nested",
                "images", 300,
                "texts", 40,
                "canvases", 1 + k_NestedCanvases,
                "repetitions", k_Repetitions,
                "disableCanvasHideMedianMs", ProbeLog.Ms(ProbeStats.Median(canvasHide)),
                "disableCanvasShowMedianMs", ProbeLog.Ms(ProbeStats.Median(canvasShow)),
                "deactivateHideMedianMs", ProbeLog.Ms(ProbeStats.Median(activeHide)),
                "deactivateShowMedianMs", ProbeLog.Ms(ProbeStats.Median(activeShow)));
        }

        static bool NoCanvasEnabled(GameObject view)
        {
            var canvases = view.GetComponentsInChildren<Canvas>(true);
            for (var i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].enabled)
                    return false;
            }

            return true;
        }
    }
}
