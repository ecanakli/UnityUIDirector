using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Hiding and blocking a covered view with canvas, raycaster, group and activation switches.
    public sealed class EngineProbes_CoveredViews
    {
        sealed class Rig
        {
            public Canvas Root;
            public Canvas View;
            public Canvas Inner;
            public GraphicRaycaster ViewRaycaster;
            public Image ViewBackground;
            public Button InnerButton;
            public CanvasGroup ViewGroup;
            public CanvasGroup InnerGroup;
            public Vector2 InnerPoint;
            public Vector2 BackgroundPoint;
        }

        sealed class Observation
        {
            public bool HitInnerButton;
            public bool HitUnderView;
        }

        sealed class BlockingObservation
        {
            public bool HitInnerButton;
            public bool HitViewBackground;
            public bool HitUnderViewAtInnerPoint;
            public bool HitUnderViewAtBackgroundPoint;
            public string HitsAtInnerPoint;
            public string HitsAtBackgroundPoint;
        }

        sealed class Measure
        {
            public readonly string Name;
            public readonly bool MustUnhit;
            public readonly Action<Rig> Apply;

            public Measure(string name, bool mustUnhit, Action<Rig> apply)
            {
                Name = name;
                MustUnhit = mustUnhit;
                Apply = apply;
            }
        }

        sealed class RenderState
        {
            public readonly string Name;
            public readonly Action<ProbeRender.RenderRig> Apply;
            public readonly Action<ProbeRender.RenderRig> Restore;

            public RenderState(string name, Action<ProbeRender.RenderRig> apply, Action<ProbeRender.RenderRig> restore)
            {
                Name = name;
                Apply = apply;
                Restore = restore;
            }
        }

        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();

        [TearDown]
        public void TearDown()
        {
            m_Cleanup.DestroyAll();
        }

        [UnityTest]
        public IEnumerator P4_DisabledViewCanvas_NestedCanvasState()
        {
            if (!ProbeUi.ScreenIsUsable())
                Assert.Inconclusive("Screen is " + Screen.width + "x" + Screen.height + "; too small for raycast probes.");

            var rig = BuildRig(true, false);
            foreach (var step in ProbeUi.Frames(3))
                yield return step;

            rig.InnerPoint = ProbeUi.ScreenPointOf((RectTransform)rig.InnerButton.transform);
            var baseline = Observe(rig, "baseline");

            rig.View.enabled = false;
            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            Observe(rig, "viewCanvasDisabled_viewRaycasterEnabled");

            rig.ViewRaycaster.enabled = false;
            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            Observe(rig, "viewCanvasDisabled_viewRaycasterDisabled");

            rig.View.enabled = true;
            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            Observe(rig, "viewCanvasEnabled_viewRaycasterDisabled");

            rig.ViewRaycaster.enabled = true;
            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            Observe(rig, "restored");

            // The disabled states are documented by the log; only the control is asserted.
            Assert.IsTrue(baseline.HitInnerButton, "The baseline raycast must hit the inner button, otherwise the disabled states cannot be compared.");
        }

        [UnityTest]
        public IEnumerator P4_Rendering_NestedCanvasUnderDisabledViewCanvas()
        {
            // Observation only. A Screen Space - Camera root renders into a small RenderTexture that is read back.
            var capture = new ProbeLogCapture();
            var previousActive = RenderTexture.active;
            ProbeRender.RenderRig rig = null;
            try
            {
                rig = ProbeRender.BuildRenderRig(m_Cleanup);

                // Same frame as the build: no frame has run yet.
                var firstSame = ProbeRender.ReadSample(rig);
                foreach (var step in ProbeUi.Frames(2))
                    yield return step;

                var firstAfter = ProbeRender.ReadSample(rig);
                var sameFrameWorks = ProbeRender.IsBaselineLook(firstSame);
                var afterFrameWorks = ProbeRender.IsBaselineLook(firstAfter);
                ProbeLog.Write("P4", "case", "renderingBaseline",
                    "target", ProbeRender.TargetSize + "x" + ProbeRender.TargetSize,
                    "sameFrameLeft", ProbeRender.Describe(firstSame.Left),
                    "sameFrameRight", ProbeRender.Describe(firstSame.Right),
                    "afterFrameLeft", ProbeRender.Describe(firstAfter.Left),
                    "afterFrameRight", ProbeRender.Describe(firstAfter.Right),
                    "forceUpdateCanvasesPlusRenderWorksSameFrame", sameFrameWorks,
                    "worksAfterOneFrame", afterFrameWorks);

                if (!sameFrameWorks && !afterFrameWorks)
                    Assert.Inconclusive("The baseline does not show green on the left and red on the right, so rendering to a RenderTexture does not work in this run mode.");

                var states = new List<RenderState>
                {
                    new RenderState("baseline", r => { }, r => { }),
                    new RenderState("viewCanvasDisabled", r => r.View.enabled = false, r => r.View.enabled = true),
                    new RenderState("viewAndInnerCanvasDisabled",
                        r =>
                        {
                            r.View.enabled = false;
                            r.Inner.enabled = false;
                        },
                        r =>
                        {
                            r.View.enabled = true;
                            r.Inner.enabled = true;
                        }),
                    new RenderState("viewInactive", r => r.View.gameObject.SetActive(false), r => r.View.gameObject.SetActive(true)),
                    new RenderState("viewGroupAlphaZero", r => r.ViewGroup.alpha = 0f, r => r.ViewGroup.alpha = 1f),
                };

                foreach (var state in states)
                {
                    state.Apply(rig);
                    var same = ProbeRender.ReadSample(rig);
                    foreach (var step in ProbeUi.Frames(1))
                        yield return step;

                    var after = ProbeRender.ReadSample(rig);
                    state.Restore(rig);
                    foreach (var step in ProbeUi.Frames(1))
                        yield return step;

                    var restored = ProbeRender.ReadSample(rig);
                    ProbeLog.Write("P4", "case", "rendering",
                        "state", state.Name,
                        "sameFrameLeft", ProbeRender.Describe(same.Left),
                        "sameFrameRight", ProbeRender.Describe(same.Right),
                        "afterFrameLeft", ProbeRender.Describe(after.Left),
                        "afterFrameRight", ProbeRender.Describe(after.Right),
                        "restoredLeft", ProbeRender.Describe(restored.Left),
                        "restoredRight", ProbeRender.Describe(restored.Right));
                }
            }
            finally
            {
                if (rig != null && rig.Camera != null)
                    rig.Camera.targetTexture = null;
                RenderTexture.active = previousActive;
                ProbeLog.Write("P4", "case", "renderingLogs", "logCount", capture.Count, "firstLog", capture.First);
                capture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator P4_Blocking_WhatMakesAViewUnhittable()
        {
            if (!ProbeUi.ScreenIsUsable())
                Assert.Inconclusive("Screen is " + Screen.width + "x" + Screen.height + "; too small for raycast probes.");

            var measures = new List<Measure>
            {
                new Measure("a_viewRaycasterDisabled", false, r => r.ViewRaycaster.enabled = false),
                new Measure("b_allRaycastersUnderViewDisabled", true, r =>
                {
                    foreach (var found in r.View.GetComponentsInChildren<GraphicRaycaster>(true))
                        found.enabled = false;
                }),
                new Measure("c_viewGroupBlocksRaycastsFalse", false, r => r.ViewGroup.blocksRaycasts = false),
                new Measure("d_viewGroupInteractableFalse", false, r => r.ViewGroup.interactable = false),
                new Measure("e_allCanvasesUnderViewDisabled", true, r =>
                {
                    foreach (var found in r.View.GetComponentsInChildren<Canvas>(true))
                        found.enabled = false;
                }),
                new Measure("f_viewInactive", true, r => r.View.gameObject.SetActive(false)),
                new Measure("g_groupOnEveryViewCanvasBlocksRaycastsFalse", false, r =>
                {
                    r.ViewGroup.blocksRaycasts = false;
                    r.InnerGroup.blocksRaycasts = false;
                }),
            };

            var failures = new List<string>();
            string inconclusive = null;
            foreach (var innerHasRaycaster in new[] { true, false })
            {
                var variant = innerHasRaycaster ? "innerHasRaycaster" : "innerHasNoRaycaster";
                var rig = BuildRig(innerHasRaycaster, true);
                foreach (var step in ProbeUi.Frames(3))
                    yield return step;

                rig.InnerPoint = ProbeUi.ScreenPointOf((RectTransform)rig.InnerButton.transform);
                rig.BackgroundPoint = new Vector2(Screen.width * 0.1f, Screen.height * 0.1f);

                var baseline = ObserveBlocking(rig, variant, "baseline");
                if (!baseline.HitViewBackground || (innerHasRaycaster && !baseline.HitInnerButton))
                {
                    inconclusive = variant + ": the baseline does not hit what it should (background=" + baseline.HitViewBackground + ", innerButton=" + baseline.HitInnerButton + ").";
                    break;
                }

                foreach (var measure in measures)
                {
                    measure.Apply(rig);
                    foreach (var step in ProbeUi.Frames(2))
                        yield return step;

                    var observation = ObserveBlocking(rig, variant, measure.Name);
                    if (measure.MustUnhit && (observation.HitUnderViewAtInnerPoint || observation.HitUnderViewAtBackgroundPoint))
                        failures.Add(variant + "/" + measure.Name + ": hits at inner point=" + observation.HitsAtInnerPoint + ", at background point=" + observation.HitsAtBackgroundPoint);

                    ResetBlocking(rig);
                    foreach (var step in ProbeUi.Frames(2))
                        yield return step;
                }
            }

            Assert.That(failures, Is.Empty, "A view was still hit after a switch that must make it unhittable: " + string.Join("; ", failures));
            if (inconclusive != null)
                Assert.Inconclusive(inconclusive);
        }

        [UnityTest]
        public IEnumerator M3_CoverAndReveal_CanvasEnabledVersusSetActive()
        {
            const int repetitions = 20;
            var root = ProbeUi.NewRootCanvas("ProbeRoot");
            m_Cleanup.Track(root.gameObject);
            var view = ProbeUi.BuildHeavyView(root.transform, 300, 40);
            var canvas = view.GetComponent<Canvas>();

            // Settle the first build so only hide and show are measured.
            foreach (var step in ProbeUi.Frames(3))
                yield return step;

            var watch = new System.Diagnostics.Stopwatch();
            var canvasHide = new List<double>();
            var canvasShow = new List<double>();
            var activeHide = new List<double>();
            var activeShow = new List<double>();

            for (var i = 0; i < repetitions; i++)
            {
                watch.Restart();
                canvas.enabled = false;
                Canvas.ForceUpdateCanvases();
                watch.Stop();
                canvasHide.Add(watch.Elapsed.TotalMilliseconds);
                yield return null;

                watch.Restart();
                canvas.enabled = true;
                Canvas.ForceUpdateCanvases();
                watch.Stop();
                canvasShow.Add(watch.Elapsed.TotalMilliseconds);
                yield return null;
            }

            for (var i = 0; i < repetitions; i++)
            {
                watch.Restart();
                view.SetActive(false);
                Canvas.ForceUpdateCanvases();
                watch.Stop();
                activeHide.Add(watch.Elapsed.TotalMilliseconds);
                yield return null;

                watch.Restart();
                view.SetActive(true);
                Canvas.ForceUpdateCanvases();
                watch.Stop();
                activeShow.Add(watch.Elapsed.TotalMilliseconds);
                yield return null;
            }

            ProbeLog.Write("M3",
                "images", 300,
                "texts", 40,
                "repetitions", repetitions,
                "canvasEnabledHideMedianMs", ProbeLog.Ms(ProbeStats.Median(canvasHide)),
                "canvasEnabledShowMedianMs", ProbeLog.Ms(ProbeStats.Median(canvasShow)),
                "setActiveHideMedianMs", ProbeLog.Ms(ProbeStats.Median(activeHide)),
                "setActiveShowMedianMs", ProbeLog.Ms(ProbeStats.Median(activeShow)));
        }

        // Root canvas, a view (nested canvas), and inside it a second nested canvas with a button.
        Rig BuildRig(bool innerRaycaster, bool withGroups)
        {
            m_Cleanup.Track(ProbeUi.NewEventSystem(false));
            var rig = new Rig();
            rig.Root = ProbeUi.NewRootCanvas("ProbeRoot");
            m_Cleanup.Track(rig.Root.gameObject);

            rig.View = ProbeUi.NewNestedCanvas("View", rig.Root.transform, 10);
            rig.ViewRaycaster = rig.View.GetComponent<GraphicRaycaster>();
            rig.ViewBackground = ProbeUi.NewImage("ViewBackground", rig.View.transform, Vector2.zero, Vector2.one);

            rig.Inner = ProbeUi.NewNestedCanvas("Inner", rig.View.transform, 20, true, innerRaycaster);
            ProbeUi.SetAnchors((RectTransform)rig.Inner.transform, new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.7f));
            rig.InnerButton = ProbeUi.NewButton("InnerButton", rig.Inner.transform, new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.75f));

            if (withGroups)
            {
                rig.ViewGroup = rig.View.gameObject.AddComponent<CanvasGroup>();
                rig.InnerGroup = rig.Inner.gameObject.AddComponent<CanvasGroup>();
            }

            return rig;
        }

        static Observation Observe(Rig rig, string state)
        {
            var results = ProbeUi.Raycast(rig.InnerPoint);
            var observation = new Observation
            {
                HitInnerButton = ProbeUi.Hits(results, rig.InnerButton.gameObject),
                HitUnderView = ProbeUi.HitsUnder(results, rig.View.transform),
            };

            var renderer = rig.InnerButton.targetGraphic.canvasRenderer;
            ProbeLog.Write("P4", "state", state,
                "viewCanvasEnabled", rig.View.enabled,
                "viewRaycasterEnabled", rig.ViewRaycaster.enabled,
                "innerEnabled", rig.Inner.enabled,
                "innerIsActiveAndEnabled", rig.Inner.isActiveAndEnabled,
                "innerRootCanvasIsRoot", rig.Inner.rootCanvas == rig.Root,
                "innerButtonDepth", renderer.absoluteDepth,
                "innerButtonCull", renderer.cull,
                "hitInnerButton", observation.HitInnerButton,
                "innerHitModule", ProbeUi.ModuleOf(results, rig.InnerButton.gameObject),
                "hitUnderView", observation.HitUnderView,
                "hits", ProbeUi.Describe(results));
            return observation;
        }

        static BlockingObservation ObserveBlocking(Rig rig, string variant, string measure)
        {
            var atInner = ProbeUi.Raycast(rig.InnerPoint);
            var atBackground = ProbeUi.Raycast(rig.BackgroundPoint);
            var observation = new BlockingObservation
            {
                HitInnerButton = ProbeUi.Hits(atInner, rig.InnerButton.gameObject),
                HitViewBackground = ProbeUi.Hits(atBackground, rig.ViewBackground.gameObject),
                HitUnderViewAtInnerPoint = ProbeUi.HitsUnder(atInner, rig.View.transform),
                HitUnderViewAtBackgroundPoint = ProbeUi.HitsUnder(atBackground, rig.View.transform),
                HitsAtInnerPoint = ProbeUi.Describe(atInner),
                HitsAtBackgroundPoint = ProbeUi.Describe(atBackground),
            };

            ProbeLog.Write("P4", "case", "blocking",
                "variant", variant,
                "measure", measure,
                "viewActive", rig.View.gameObject.activeSelf,
                "viewCanvasEnabled", rig.View.enabled,
                "viewRaycasterEnabled", rig.ViewRaycaster.enabled,
                "innerCanvasEnabled", rig.Inner.enabled,
                "hitInnerButton", observation.HitInnerButton,
                "innerHitModule", ProbeUi.ModuleOf(atInner, rig.InnerButton.gameObject),
                "hitViewBackground", observation.HitViewBackground,
                "backgroundHitModule", ProbeUi.ModuleOf(atBackground, rig.ViewBackground.gameObject),
                "hitsAtInnerPoint", observation.HitsAtInnerPoint,
                "hitsAtBackgroundPoint", observation.HitsAtBackgroundPoint);
            return observation;
        }

        // Puts every switch the measures use back to its baseline value.
        static void ResetBlocking(Rig rig)
        {
            rig.View.gameObject.SetActive(true);
            foreach (var raycaster in rig.View.GetComponentsInChildren<GraphicRaycaster>(true))
                raycaster.enabled = true;
            foreach (var canvas in rig.View.GetComponentsInChildren<Canvas>(true))
                canvas.enabled = true;
            ResetGroup(rig.ViewGroup);
            ResetGroup(rig.InnerGroup);
        }

        static void ResetGroup(CanvasGroup group)
        {
            if (group == null)
                return;

            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }
    }
}
