using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Legacy Dropdown as a TMP_Dropdown stand-in; the blocker parent rule differs (Dropdown.cs:755 vs TMP_Dropdown.cs:797), variant C copies TMP's.
    public sealed class EngineProbes_Dropdown
    {
        enum Variant
        {
            LegacyOverrideView,
            LegacyPlainView,
            TmpLikeOverrideView,
        }

        sealed class Rig
        {
            public Variant Variant;
            public GameObject EventSystemObject;
            public Canvas Root;
            public Canvas View;
            public Dropdown Dropdown;
            public Transform List;
            public Transform Blocker;

            public void Dispose()
            {
                if (EventSystemObject != null)
                    Object.DestroyImmediate(EventSystemObject);
                if (Root != null)
                    Object.DestroyImmediate(Root.gameObject);
            }
        }

        sealed class OrderObservation
        {
            public bool ListFound;
            public bool BlockerFound;
            public int ListOrder = int.MinValue;
            public int BlockerOrder = int.MinValue;
            public string BlockerParent = "none";
            public bool BlockerUnderRoot;
        }

        static readonly Variant[] s_Variants =
        {
            Variant.LegacyOverrideView,
            Variant.LegacyPlainView,
            Variant.TmpLikeOverrideView,
        };

        readonly List<Rig> m_Rigs = new List<Rig>();

        [TearDown]
        public void TearDown()
        {
            foreach (var rig in m_Rigs)
                rig.Dispose();
            m_Rigs.Clear();
        }

        [UnityTest]
        public IEnumerator P8_Dropdown_ListCanvasOrder()
        {
            var failures = new List<string>();
            foreach (var variant in s_Variants)
            {
                var rig = BuildRig(variant);
                foreach (var step in Open(rig))
                    yield return step;

                var observation = ObserveOrder(rig);
                rig.Dispose();
                yield return null;

                if (!observation.ListFound || !observation.BlockerFound)
                {
                    failures.Add(variant + ": list canvas found=" + observation.ListFound + ", blocker canvas found=" + observation.BlockerFound);
                    continue;
                }

                if (observation.ListOrder != 30000)
                    failures.Add(variant + ": list order " + observation.ListOrder + " instead of 30000");
                if (observation.BlockerOrder != 29999)
                    failures.Add(variant + ": blocker order " + observation.BlockerOrder + " instead of 29999");

                // The parent claim is asserted where the parent rule matches TMP_Dropdown; the legacy rule with an override view is logged only.
                if (variant != Variant.LegacyOverrideView && !observation.BlockerUnderRoot)
                    failures.Add(variant + ": blocker parent is '" + observation.BlockerParent + "', not the root canvas");
            }

            Assert.That(failures, Is.Empty, "Dropdown canvas order claim failed: " + string.Join("; ", failures));
        }

        [UnityTest]
        public IEnumerator P8_Dropdown_OwnerViewCanvasDisabled_WhileOpen()
        {
            if (!ProbeUi.ScreenIsUsable())
                Assert.Inconclusive("Screen is " + Screen.width + "x" + Screen.height + "; too small for raycast probes.");

            foreach (var variant in s_Variants)
            {
                var rig = BuildRig(variant);
                foreach (var step in Open(rig))
                    yield return step;

                if (rig.List == null || rig.Blocker == null)
                    Assert.Inconclusive(variant + ": Show() did not create the list or the blocker (list=" + (rig.List != null) + ", blocker=" + (rig.Blocker != null) + ").");

                rig.View.enabled = false;
                foreach (var step in ProbeUi.Frames(2))
                    yield return step;

                var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                var corner = new Vector2(Screen.width * 0.1f, Screen.height * 0.1f);
                var centreHits = ProbeUi.Raycast(centre);
                var cornerHits = ProbeUi.Raycast(corner);
                var blockerObject = rig.Blocker != null ? rig.Blocker.gameObject : null;
                var blockerCanvas = blockerObject != null ? blockerObject.GetComponent<Canvas>() : null;

                ProbeLog.Write("P8", "case", "viewCanvasDisabled",
                    "variant", variant,
                    "listExists", rig.List != null,
                    "listActive", rig.List != null && rig.List.gameObject.activeInHierarchy,
                    "blockerExists", blockerObject != null,
                    "blockerActive", blockerObject != null && blockerObject.activeInHierarchy,
                    "blockerCanvasEnabled", blockerCanvas != null && blockerCanvas.enabled,
                    "centreHitsBlocker", ProbeUi.Hits(centreHits, blockerObject),
                    "centreHits", ProbeUi.Describe(centreHits),
                    "cornerHitsBlocker", ProbeUi.Hits(cornerHits, blockerObject),
                    "cornerHits", ProbeUi.Describe(cornerHits));

                rig.Dispose();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator P8_Dropdown_OwnerViewDestroyed_WhileOpen()
        {
            var failures = new List<string>();
            foreach (var variant in s_Variants)
            {
                var rig = BuildRig(variant);
                foreach (var step in Open(rig))
                    yield return step;

                if (rig.Blocker == null)
                    Assert.Inconclusive(variant + ": Show() did not create a blocker, so there is nothing to leak.");

                var blockerParentBefore = rig.Blocker.parent != null ? rig.Blocker.parent.name : "none";
                Object.Destroy(rig.View.gameObject);
                foreach (var step in ProbeUi.Frames(2))
                    yield return step;

                var leftover = ProbeUi.FindDescendant(rig.Root.transform, "Blocker");
                ProbeLog.Write("P8", "case", "viewDestroyed",
                    "variant", variant,
                    "blockerParentBeforeDestroy", blockerParentBefore,
                    "blockerLeftUnderRoot", leftover != null);

                if (leftover != null)
                    failures.Add(variant + ": a blocker is left under '" + leftover.parent.name + "'");

                rig.Dispose();
                yield return null;
            }

            Assert.That(failures, Is.Empty, "A blocker was left under the root canvas after the owning view was destroyed: " + string.Join("; ", failures));
        }

        Rig BuildRig(Variant variant)
        {
            var rig = new Rig { Variant = variant };
            m_Rigs.Add(rig);
            rig.EventSystemObject = ProbeUi.NewEventSystem(false);
            rig.Root = ProbeUi.NewRootCanvas("ProbeRoot");
            rig.View = ProbeUi.NewNestedCanvas("View", rig.Root.transform, 100, variant != Variant.LegacyPlainView);

            var dropdownObject = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            dropdownObject.transform.SetParent(rig.View.transform, false);
            var rect = (RectTransform)dropdownObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.9f);
            rect.anchorMax = new Vector2(0.5f, 0.9f);
            rect.anchoredPosition = Vector2.zero;

            var legacy = dropdownObject.GetComponent<Dropdown>();
            rig.Dropdown = variant == Variant.TmpLikeOverrideView ? SwapToTmpLike(legacy) : legacy;
            return rig;
        }

        // Replaces the Dropdown component with the subclass and carries the references over.
        static Dropdown SwapToTmpLike(Dropdown legacy)
        {
            var go = legacy.gameObject;
            var template = legacy.template;
            var caption = legacy.captionText;
            var itemText = legacy.itemText;
            var graphic = legacy.targetGraphic;
            var options = new List<Dropdown.OptionData>(legacy.options);

            Object.DestroyImmediate(legacy);
            var replacement = go.AddComponent<ProbeTmpLikeDropdown>();
            replacement.template = template;
            replacement.captionText = caption;
            replacement.itemText = itemText;
            replacement.targetGraphic = graphic;
            replacement.options = options;
            return replacement;
        }

        // Dropdown.Start must run before Show, so wait first.
        static IEnumerable<object> Open(Rig rig)
        {
            foreach (var step in ProbeUi.Frames(3))
                yield return step;

            rig.Dropdown.Show();
            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            rig.List = rig.Dropdown.transform.Find("Dropdown List");
            rig.Blocker = ProbeUi.FindDescendant(rig.Root.transform, "Blocker");
        }

        static OrderObservation ObserveOrder(Rig rig)
        {
            var observation = new OrderObservation();
            if (rig.List != null)
            {
                var listCanvas = rig.List.GetComponent<Canvas>();
                observation.ListFound = listCanvas != null;
                if (listCanvas != null)
                    observation.ListOrder = listCanvas.sortingOrder;
            }

            if (rig.Blocker != null)
            {
                var blockerCanvas = rig.Blocker.GetComponent<Canvas>();
                observation.BlockerFound = blockerCanvas != null;
                if (blockerCanvas != null)
                    observation.BlockerOrder = blockerCanvas.sortingOrder;
                if (rig.Blocker.parent != null)
                {
                    observation.BlockerParent = rig.Blocker.parent.name;
                    observation.BlockerUnderRoot = rig.Blocker.parent == rig.Root.transform;
                }
            }

            ProbeLog.Write("P8", "case", "order",
                "variant", rig.Variant,
                "listFound", observation.ListFound,
                "listOrder", observation.ListOrder,
                "blockerFound", observation.BlockerFound,
                "blockerOrder", observation.BlockerOrder,
                "blockerParent", observation.BlockerParent,
                "blockerUnderRootCanvas", observation.BlockerUnderRoot);
            return observation;
        }
    }
}
