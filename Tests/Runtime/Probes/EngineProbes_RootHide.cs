using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Hiding every view at once through the root canvas, its CanvasGroup or its activation, with and without a shield.
    public sealed class EngineProbes_RootHide
    {
        sealed class State<TRig>
        {
            public readonly string Name;
            public readonly Action<TRig> Apply;
            public readonly Action<TRig> Restore;

            public State(string name, Action<TRig> apply, Action<TRig> restore)
            {
                Name = name;
                Apply = apply;
                Restore = restore;
            }
        }

        sealed class RaycastRig
        {
            public Canvas Root;
            public CanvasGroup RootGroup;
            public Canvas View;
            public Image ViewBackground;
            public Canvas Inner;
            public Button InnerButton;
            public Canvas Shield;
            public Image ShieldImage;
            public Vector2 ButtonPoint;
            public Vector2 BackgroundPoint;
        }

        const string k_Baseline = "baseline";
        const string k_RootCanvasDisabled = "rootCanvasDisabled";
        const string k_RootGroupBlocksRaycastsFalse = "rootGroupBlocksRaycastsFalse";
        const string k_RootGroupAlphaZero = "rootGroupAlphaZero";
        const string k_RootGroupAlphaZeroInteractableFalse = "rootGroupAlphaZeroInteractableFalse";
        const string k_RootGroupAlphaZeroShieldUp = "rootGroupAlphaZeroShieldUp";
        const string k_RootInactive = "rootInactive";

        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();

        [TearDown]
        public void TearDown()
        {
            m_Cleanup.DestroyAll();
        }

        [UnityTest]
        public IEnumerator P4r_RootHide_Rendering()
        {
            // A Screen Space - Camera root renders into a small RenderTexture that is read back.
            var capture = new ProbeLogCapture();
            var previousActive = RenderTexture.active;
            var failures = new List<string>();
            ProbeRender.RenderRig rig = null;
            try
            {
                rig = ProbeRender.BuildRenderRig(m_Cleanup, true);
                foreach (var step in ProbeUi.Frames(2))
                    yield return step;

                var first = ProbeRender.ReadSample(rig);
                var baselineWorks = ProbeRender.IsBaselineLook(first);
                ProbeLog.Write("P4r", "case", "renderingBaseline",
                    "target", ProbeRender.TargetSize + "x" + ProbeRender.TargetSize,
                    "left", ProbeRender.Describe(first.Left),
                    "right", ProbeRender.Describe(first.Right),
                    "works", baselineWorks);

                if (!baselineWorks)
                    Assert.Inconclusive("The baseline does not show green on the left and red on the right, so rendering to a RenderTexture does not work in this run mode.");

                var states = new List<State<ProbeRender.RenderRig>>
                {
                    new State<ProbeRender.RenderRig>(k_Baseline, r => { }, r => { }),
                    new State<ProbeRender.RenderRig>(k_RootCanvasDisabled, r => r.Root.enabled = false, r => r.Root.enabled = true),
                    new State<ProbeRender.RenderRig>(k_RootGroupAlphaZero, r => r.RootGroup.alpha = 0f, r => r.RootGroup.alpha = 1f),
                    new State<ProbeRender.RenderRig>(k_RootGroupAlphaZeroInteractableFalse,
                        r =>
                        {
                            r.RootGroup.alpha = 0f;
                            r.RootGroup.interactable = false;
                        },
                        r =>
                        {
                            r.RootGroup.alpha = 1f;
                            r.RootGroup.interactable = true;
                        }),
                    new State<ProbeRender.RenderRig>(k_RootInactive, r => r.Root.gameObject.SetActive(false), r => r.Root.gameObject.SetActive(true)),
                };

                foreach (var state in states)
                {
                    state.Apply(rig);
                    var same = ProbeRender.ReadSample(rig);
                    foreach (var step in ProbeUi.Frames(1))
                        yield return step;

                    var after = ProbeRender.ReadSample(rig);
                    LogCanvases(state.Name, rig);
                    state.Restore(rig);
                    foreach (var step in ProbeUi.Frames(1))
                        yield return step;

                    var restored = ProbeRender.ReadSample(rig);
                    ProbeLog.Write("P4r", "case", "rendering",
                        "state", state.Name,
                        "sameFrameLeft", ProbeRender.Describe(same.Left),
                        "sameFrameRight", ProbeRender.Describe(same.Right),
                        "afterFrameLeft", ProbeRender.Describe(after.Left),
                        "afterFrameRight", ProbeRender.Describe(after.Right),
                        "restoredLeft", ProbeRender.Describe(restored.Left),
                        "restoredRight", ProbeRender.Describe(restored.Right));

                    // rootCanvasDisabled and the interactable variant are logged only.
                    if (state.Name == k_Baseline && !ProbeRender.IsBaselineLook(after))
                        failures.Add(state.Name + ": after one frame " + Describe(after) + ", expected green/red");
                    if (state.Name == k_RootInactive && !IsBlack(after))
                        failures.Add(state.Name + ": after one frame " + Describe(after) + ", expected black/black");
                    if (state.Name == k_RootGroupAlphaZero && !IsBlack(after))
                        failures.Add(state.Name + ": after one frame " + Describe(after) + ", expected black/black");
                    if (state.Name == k_RootGroupAlphaZero && !ProbeRender.IsBaselineLook(restored))
                        failures.Add(state.Name + ": restored " + Describe(restored) + ", expected green/red");
                }
            }
            finally
            {
                if (rig != null && rig.Camera != null)
                    rig.Camera.targetTexture = null;
                RenderTexture.active = previousActive;
                ProbeLog.Write("P4r", "case", "renderingLogs", "logCount", capture.Count, "firstLog", capture.First);
                capture.Dispose();
            }

            Assert.That(failures, Is.Empty, "A root hide state did not render as expected: " + string.Join("; ", failures));
        }

        [UnityTest]
        public IEnumerator P4r_RootHide_Raycasting()
        {
            if (!ProbeUi.ScreenIsUsable())
                Assert.Inconclusive("Screen is " + Screen.width + "x" + Screen.height + "; too small for raycast probes.");

            var rig = BuildRaycastRig();
            foreach (var step in ProbeUi.Frames(3))
                yield return step;

            rig.ButtonPoint = ProbeUi.ScreenPointOf((RectTransform)rig.InnerButton.transform);
            rig.BackgroundPoint = new Vector2(Screen.width * 0.1f, Screen.height * 0.1f);

            var buttonDepth = rig.InnerButton.targetGraphic.canvasRenderer.absoluteDepth;
            var backgroundDepth = rig.ViewBackground.canvasRenderer.absoluteDepth;
            if (buttonDepth < 0 || backgroundDepth < 0)
                Assert.Inconclusive("Canvases did not render in this run mode: button depth " + buttonDepth + ", background depth " + backgroundDepth + ".");

            var states = new List<State<RaycastRig>>
            {
                new State<RaycastRig>(k_Baseline, r => { }, r => { }),
                new State<RaycastRig>(k_RootCanvasDisabled, r => r.Root.enabled = false, r => r.Root.enabled = true),
                new State<RaycastRig>(k_RootGroupBlocksRaycastsFalse, r => r.RootGroup.blocksRaycasts = false, r => r.RootGroup.blocksRaycasts = true),
                new State<RaycastRig>(k_RootGroupAlphaZero, r => r.RootGroup.alpha = 0f, r => r.RootGroup.alpha = 1f),
                new State<RaycastRig>(k_RootGroupAlphaZeroShieldUp,
                    r =>
                    {
                        r.RootGroup.alpha = 0f;
                        r.ShieldImage.raycastTarget = true;
                    },
                    r =>
                    {
                        r.RootGroup.alpha = 1f;
                        r.ShieldImage.raycastTarget = false;
                    }),
                new State<RaycastRig>(k_RootInactive, r => r.Root.gameObject.SetActive(false), r => r.Root.gameObject.SetActive(true)),
            };

            var failures = new List<string>();
            foreach (var state in states)
            {
                state.Apply(rig);
                foreach (var step in ProbeUi.Frames(2))
                    yield return step;

                var atButton = ProbeUi.Raycast(rig.ButtonPoint);
                var atBackground = ProbeUi.Raycast(rig.BackgroundPoint);
                var hitButton = ProbeUi.Hits(atButton, rig.InnerButton.gameObject);
                var hitBackground = ProbeUi.Hits(atBackground, rig.ViewBackground.gameObject);
                var underRootAtButton = ProbeUi.HitsUnder(atButton, rig.Root.transform);
                var underRootAtBackground = ProbeUi.HitsUnder(atBackground, rig.Root.transform);
                ProbeLog.Write("P4r", "case", "raycasting",
                    "state", state.Name,
                    "rootActive", rig.Root.gameObject.activeSelf,
                    "rootCanvasEnabled", rig.Root.enabled,
                    "rootGroupAlpha", rig.RootGroup.alpha,
                    "rootGroupBlocksRaycasts", rig.RootGroup.blocksRaycasts,
                    "shieldRaycastTarget", rig.ShieldImage.raycastTarget,
                    "buttonInteractable", rig.InnerButton.IsInteractable(),
                    "hitButton", hitButton,
                    "hitBackground", hitBackground,
                    "underRootAtButton", underRootAtButton,
                    "underRootAtBackground", underRootAtBackground,
                    "firstAtButton", FirstName(atButton),
                    "firstModuleAtButton", FirstModule(atButton),
                    "firstAtBackground", FirstName(atBackground),
                    "firstModuleAtBackground", FirstModule(atBackground),
                    "hitsAtButton", ProbeUi.Describe(atButton),
                    "hitsAtBackground", ProbeUi.Describe(atBackground));

                // rootCanvasDisabled, blocksRaycastsFalse and alpha zero without the shield are logged only.
                if (state.Name == k_Baseline && (!hitButton || !hitBackground))
                    failures.Add(state.Name + ": hitButton=" + hitButton + ", hitBackground=" + hitBackground);
                if (state.Name == k_RootInactive && (underRootAtButton || underRootAtBackground))
                    failures.Add(state.Name + ": hits at button point=" + ProbeUi.Describe(atButton) + ", at background point=" + ProbeUi.Describe(atBackground));
                if (state.Name == k_RootGroupAlphaZeroShieldUp && (!IsFirst(atButton, rig.ShieldImage.gameObject) || !IsFirst(atBackground, rig.ShieldImage.gameObject)))
                    failures.Add(state.Name + ": first at button point=" + FirstName(atButton) + ", at background point=" + FirstName(atBackground) + ", expected the shield");

                state.Restore(rig);
                foreach (var step in ProbeUi.Frames(2))
                    yield return step;
            }

            Assert.That(failures, Is.Empty, "A root hide state did not raycast as expected: " + string.Join("; ", failures));
        }

        [UnityTest]
        public IEnumerator P4r_RootGroupInteractableFalse_ButtonNotInteractable()
        {
            var rig = BuildRaycastRig();
            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            var before = rig.InnerButton.IsInteractable();
            rig.RootGroup.interactable = false;
            var sameFrame = rig.InnerButton.IsInteractable();
            foreach (var step in ProbeUi.Frames(1))
                yield return step;

            var disabled = rig.InnerButton.IsInteractable();
            rig.RootGroup.interactable = true;
            foreach (var step in ProbeUi.Frames(1))
                yield return step;

            var restored = rig.InnerButton.IsInteractable();
            ProbeLog.Write("P4r", "case", "rootGroupInteractable",
                "before", before,
                "sameFrame", sameFrame,
                "afterOneFrame", disabled,
                "restored", restored);

            Assert.IsTrue(before, "The nested button must be interactable before the root group changes.");
            Assert.IsFalse(disabled, "A root CanvasGroup with interactable=false must make the button in a nested override canvas non-interactable.");
            Assert.IsTrue(restored, "The nested button must be interactable again after the root group is restored.");
        }

        // Overlay root with a group, a view canvas with a background, a nested canvas with a button and a shield canvas above them.
        RaycastRig BuildRaycastRig()
        {
            m_Cleanup.Track(ProbeUi.NewEventSystem(false));
            var rig = new RaycastRig();
            rig.Root = ProbeUi.NewRootCanvas("ProbeRoot");
            m_Cleanup.Track(rig.Root.gameObject);
            rig.RootGroup = rig.Root.gameObject.AddComponent<CanvasGroup>();

            rig.View = ProbeUi.NewNestedCanvas("View", rig.Root.transform, 10);
            rig.ViewBackground = ProbeUi.NewImage("ViewBackground", rig.View.transform, Vector2.zero, Vector2.one);

            rig.Inner = ProbeUi.NewNestedCanvas("Inner", rig.View.transform, 20);
            ProbeUi.SetAnchors((RectTransform)rig.Inner.transform, new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.7f));
            rig.InnerButton = ProbeUi.NewButton("InnerButton", rig.Inner.transform, new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.75f));

            rig.Shield = ProbeUi.NewNestedCanvas("Shield", rig.Root.transform, 1000);
            rig.ShieldImage = ProbeUi.NewImage("ShieldImage", rig.Shield.transform, Vector2.zero, Vector2.one);
            rig.ShieldImage.raycastTarget = false;
            return rig;
        }

        static void LogCanvases(string state, ProbeRender.RenderRig rig)
        {
            ProbeLog.Write("P4r", "case", "renderingCanvases",
                "state", state,
                "rootScaleFactor", rig.Root.scaleFactor,
                "viewIsRootCanvas", rig.View.isRootCanvas,
                "viewRootCanvasIsSelf", rig.View.rootCanvas == rig.View,
                "viewScaleFactor", rig.View.scaleFactor,
                "viewRenderMode", rig.View.renderMode,
                "viewPixelRect", FormatRect(rig.View.pixelRect),
                "innerIsRootCanvas", rig.Inner.isRootCanvas,
                "innerRootCanvasIsSelf", rig.Inner.rootCanvas == rig.Inner,
                "innerScaleFactor", rig.Inner.scaleFactor,
                "innerRenderMode", rig.Inner.renderMode,
                "innerPixelRect", FormatRect(rig.Inner.pixelRect));
        }

        static bool IsBlack(ProbeRender.Sample sample)
        {
            return ProbeRender.Classify(sample.Left) == "black" && ProbeRender.Classify(sample.Right) == "black";
        }

        static string Describe(ProbeRender.Sample sample)
        {
            return ProbeRender.Describe(sample.Left) + "/" + ProbeRender.Describe(sample.Right);
        }

        static string FormatRect(Rect rect)
        {
            return rect.x.ToString("0.#", CultureInfo.InvariantCulture) + ","
                   + rect.y.ToString("0.#", CultureInfo.InvariantCulture) + ","
                   + rect.width.ToString("0.#", CultureInfo.InvariantCulture) + "x"
                   + rect.height.ToString("0.#", CultureInfo.InvariantCulture);
        }

        static bool IsFirst(List<RaycastResult> results, GameObject target)
        {
            return results.Count > 0 && results[0].gameObject == target;
        }

        static string FirstName(List<RaycastResult> results)
        {
            if (results.Count == 0)
                return "none";

            return results[0].gameObject != null ? results[0].gameObject.name : "null";
        }

        static string FirstModule(List<RaycastResult> results)
        {
            if (results.Count == 0)
                return "none";

            var module = results[0].module;
            return module != null ? module.GetType().Name + "@" + module.gameObject.name : "null";
        }
    }
}
