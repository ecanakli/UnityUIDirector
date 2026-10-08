// Built on InputTestFixture, so it compiles only when the Input System is installed.
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Touch input via InputTestFixture; the legacy StandaloneInputModule half is out of scope (Input System only host).
    public sealed class EngineProbes_InputShield : InputTestFixture
    {
        enum ShieldMode
        {
            AlwaysActive,
            ActivatedOnDemand,
        }

        struct Config
        {
            public int IdA;
            public int IdB;
            public bool BeginAFirst;
            public bool EndAFirst;
        }

        sealed class Rig
        {
            public ShieldMode Mode;
            public GameObject EventSystemObject;
            public GameObject RootObject;
            public GameObject ShieldObject;
            public EventSystem EventSystem;
            public Button ButtonA;
            public Button ButtonB;
            public ProbeEmptyGraphic ShieldGraphic;
            public Vector2 PointA;
            public Vector2 PointB;

            public void RaiseShield()
            {
                if (Mode == ShieldMode.AlwaysActive)
                    ShieldGraphic.raycastTarget = true;
                else
                    ShieldObject.SetActive(true);
            }

            public void Dispose()
            {
                if (EventSystemObject != null)
                    Object.DestroyImmediate(EventSystemObject);
                if (RootObject != null)
                    Object.DestroyImmediate(RootObject);
            }
        }

        sealed class Scenario
        {
            public ShieldMode Mode = ShieldMode.AlwaysActive;
            public bool SingleTouch;
            public int IdA = 1;
            public int IdB = 2;
            public bool BeginAFirst = true;
            public bool EndAFirst = true;
            public string Raiser = "";

            public int ClicksA;
            public int ClicksB;
            public int RaiseCount;
            public readonly List<string> ClickOrder = new List<string>();
            public readonly List<string> UpOrder = new List<string>();
            public int ControlDepth = -2;
            public int ShieldAbsoluteDepth = -2;
            public int ShieldDepth = -2;
            public int ShieldDepthAfter = -2;
            public bool Focused;
            public bool Skipped;

            public string FirstUp => UpOrder.Count > 0 ? UpOrder[0] : "";
        }

        static readonly Config[] s_Configs =
        {
            new Config { IdA = 1, IdB = 2, BeginAFirst = true, EndAFirst = true },
            new Config { IdA = 1, IdB = 2, BeginAFirst = true, EndAFirst = false },
            new Config { IdA = 1, IdB = 2, BeginAFirst = false, EndAFirst = false },
            new Config { IdA = 2, IdB = 1, BeginAFirst = true, EndAFirst = true },
            new Config { IdA = 2, IdB = 1, BeginAFirst = false, EndAFirst = false },
            new Config { IdA = 2, IdB = 1, BeginAFirst = false, EndAFirst = true },
        };

        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();
        Touchscreen m_Touchscreen;
        int m_TouchBase;

        public override void Setup()
        {
            base.Setup();

            // Focus is not what is probed; headless runs may report the player as unfocused.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            m_Touchscreen = InputSystem.AddDevice<Touchscreen>();
            m_TouchBase = 100;
        }

        public override void TearDown()
        {
            try
            {
                m_Cleanup.DestroyAll();
            }
            finally
            {
                base.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator P1_AlwaysActiveEmptyGraphic_HasDepthAfterFirstRender()
        {
            var scenario = new Scenario();
            var rig = BuildRig(ShieldMode.AlwaysActive);
            try
            {
                foreach (var step in Prepare(rig, scenario, 2))
                    yield return step;

                ProbeLog.Write("P1", "case", "depth",
                    "shieldAbsoluteDepth", scenario.ShieldAbsoluteDepth,
                    "shieldDepth", scenario.ShieldDepth,
                    "controlImageDepth", scenario.ControlDepth,
                    "shieldCull", rig.ShieldGraphic.canvasRenderer.cull,
                    "shieldRaycastTarget", rig.ShieldGraphic.raycastTarget,
                    "screen", Screen.width + "x" + Screen.height);

                RequireUsable(scenario);
                Assert.AreNotEqual(-1, scenario.ShieldAbsoluteDepth, "Shield canvasRenderer.absoluteDepth is -1 although the control image rendered (depth " + scenario.ControlDepth + ").");
                Assert.AreNotEqual(-1, scenario.ShieldDepth, "Shield graphic depth is -1 although the control image rendered.");
            }
            finally
            {
                rig.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator P1_SingleTouch_ClicksButton()
        {
            var scenario = new Scenario { SingleTouch = true };
            foreach (var step in RunScenario(scenario))
                yield return step;

            LogScenario("single", scenario);
            RequireUsable(scenario);
            Assert.AreEqual(1, scenario.ClicksA, "One touch down and up on A must click A once.");
            Assert.AreEqual(0, scenario.ClicksB, "B was not touched.");
        }

        [UnityTest]
        public IEnumerator P1_ShieldRaisedInClickHandler_BlocksSecondPointerReleasedInSameFrame()
        {
            var scenario = new Scenario { Raiser = "A" };
            foreach (var step in RunScenario(scenario))
                yield return step;

            LogScenario("blocking", scenario);
            RequireUsable(scenario);
            if (scenario.UpOrder.Count < 2)
                Assert.Inconclusive("Both pointer releases were not observed (upOrder=" + string.Join(",", scenario.UpOrder) + "); the touch input did not reach the UI module.");
            if (scenario.FirstUp != "A")
                Assert.Inconclusive("A was not processed first (upOrder=" + string.Join(",", scenario.UpOrder) + "); P1_ReleaseOrder covers the other order.");

            Assert.AreEqual(1, scenario.RaiseCount, "A's click handler must have raised the shield once.");
            Assert.AreEqual(1, scenario.ClicksA, "A must be clicked.");
            Assert.AreEqual(0, scenario.ClicksB, "B was released in the same frame after the shield was raised and must not be clicked.");
        }

        [UnityTest]
        public IEnumerator P1_ShieldActivatedOnDemand_DoesNotBlockInSameFrame()
        {
            var scenario = new Scenario { Mode = ShieldMode.ActivatedOnDemand, Raiser = "A" };
            foreach (var step in RunScenario(scenario))
                yield return step;

            LogScenario("onDemandControl", scenario);
            RequireUsable(scenario);
            if (scenario.UpOrder.Count < 2)
                Assert.Inconclusive("Both pointer releases were not observed (upOrder=" + string.Join(",", scenario.UpOrder) + "); the touch input did not reach the UI module.");
            if (scenario.FirstUp != "A")
                Assert.Inconclusive("A was not processed first (upOrder=" + string.Join(",", scenario.UpOrder) + "); the control needs A first.");

            Assert.AreEqual(1, scenario.RaiseCount, "A's click handler must have activated the shield once.");
            Assert.AreEqual(1, scenario.ClicksA, "A must be clicked.");
            Assert.AreEqual(1, scenario.ClicksB, "A graphic activated in the same frame has depth -1 and must be skipped, so B is still clicked.");
        }

        [UnityTest]
        public IEnumerator P1_ReleaseOrder()
        {
            var failures = new List<string>();
            var unstable = new List<string>();
            var lowerIdFirst = new List<bool>();
            var earlierBeginFirst = new List<bool>();
            Scenario skipped = null;
            string incomplete = null;
            var literalOppositeFirst = "n/a";

            foreach (var config in s_Configs)
            {
                var label = Describe(config);
                var neutral = NewScenario(config, "");
                foreach (var step in RunScenario(neutral))
                    yield return step;

                LogScenario("order-neutral " + label, neutral);
                if (neutral.Skipped)
                {
                    skipped = neutral;
                    break;
                }

                if (neutral.UpOrder.Count < 2)
                {
                    incomplete = label + " upOrder=" + string.Join(",", neutral.UpOrder);
                    break;
                }

                var first = neutral.FirstUp;
                lowerIdFirst.Add(first == (config.IdA < config.IdB ? "A" : "B"));
                earlierBeginFirst.Add(first == (config.BeginAFirst ? "A" : "B"));
                if (config.IdA == 2 && config.IdB == 1 && !config.BeginAFirst && !config.EndAFirst)
                    literalOppositeFirst = first;

                // The first processed pointer raises the shield; the second must then be blocked.
                var blocking = NewScenario(config, first);
                foreach (var step in RunScenario(blocking))
                    yield return step;

                LogScenario("order-blocking " + label, blocking);
                if (blocking.FirstUp != first)
                {
                    unstable.Add(label + " neutralFirst=" + first + " blockingFirst=" + blocking.FirstUp);
                    continue;
                }

                var raiserClicks = first == "A" ? blocking.ClicksA : blocking.ClicksB;
                var otherClicks = first == "A" ? blocking.ClicksB : blocking.ClicksA;
                if (raiserClicks != 1 || otherClicks != 0)
                    failures.Add(label + " first=" + first + " raiserClicks=" + raiserClicks + " otherClicks=" + otherClicks);
            }

            ProbeLog.Write("P1", "case", "orderSummary",
                "lowerIdAlwaysFirst", !lowerIdFirst.Contains(false),
                "earlierBeginAlwaysFirst", !earlierBeginFirst.Contains(false),
                "oppositeOrderFirst", literalOppositeFirst,
                "failures", failures.Count,
                "unstable", unstable.Count);

            if (skipped != null)
                RequireUsable(skipped);
            if (incomplete != null)
                Assert.Inconclusive("Both pointer releases were not observed: " + incomplete);

            Assert.That(failures, Is.Empty, "The second pointer was not blocked: " + string.Join("; ", failures));
            if (unstable.Count > 0)
                Assert.Inconclusive("Processing order changed between the neutral and the blocking run: " + string.Join("; ", unstable));
        }

        static string Describe(Config config)
        {
            return "ids=" + config.IdA + "/" + config.IdB
                   + "_begin=" + (config.BeginAFirst ? "AB" : "BA")
                   + "_end=" + (config.EndAFirst ? "AB" : "BA");
        }

        static Scenario NewScenario(Config config, string raiser)
        {
            return new Scenario
            {
                IdA = config.IdA,
                IdB = config.IdB,
                BeginAFirst = config.BeginAFirst,
                EndAFirst = config.EndAFirst,
                Raiser = raiser,
            };
        }

        static void RequireUsable(Scenario scenario)
        {
            if (!ProbeUi.ScreenIsUsable())
                Assert.Inconclusive("Screen is " + Screen.width + "x" + Screen.height + "; too small for pointer probes.");
            if (scenario.ControlDepth < 0)
                Assert.Inconclusive("Canvases did not render in this run mode: the control image has depth " + scenario.ControlDepth + ".");
        }

        static void LogScenario(string label, Scenario scenario)
        {
            ProbeLog.Write("P1", "case", label,
                "mode", scenario.Mode,
                "ids", scenario.IdA + "/" + scenario.IdB,
                "beginFirst", scenario.BeginAFirst ? "A" : "B",
                "endFirst", scenario.EndAFirst ? "A" : "B",
                "raiser", scenario.Raiser == "" ? "none" : scenario.Raiser,
                "clicksA", scenario.ClicksA,
                "clicksB", scenario.ClicksB,
                "clickOrder", scenario.ClickOrder,
                "upOrder", scenario.UpOrder,
                "raiseCount", scenario.RaiseCount,
                "shieldDepthBefore", scenario.ShieldAbsoluteDepth,
                "shieldDepthAfter", scenario.ShieldDepthAfter,
                "controlDepth", scenario.ControlDepth,
                "focused", scenario.Focused,
                "skipped", scenario.Skipped);
        }

        static void OnClick(Scenario scenario, Rig rig, string label)
        {
            scenario.ClickOrder.Add(label);
            if (label == "A")
                scenario.ClicksA++;
            else
                scenario.ClicksB++;

            if (scenario.Raiser == label)
            {
                scenario.RaiseCount++;
                rig.RaiseShield();
            }
        }

        Rig BuildRig(ShieldMode mode)
        {
            var rig = new Rig { Mode = mode };
            rig.EventSystemObject = m_Cleanup.Track(ProbeUi.NewEventSystem(true));
            rig.EventSystem = rig.EventSystemObject.GetComponent<EventSystem>();

            var root = ProbeUi.NewRootCanvas("ProbeRoot");
            rig.RootObject = m_Cleanup.Track(root.gameObject);

            rig.ButtonA = ProbeUi.NewButton("ButtonA", root.transform, new Vector2(0.15f, 0.4f), new Vector2(0.35f, 0.6f));
            rig.ButtonB = ProbeUi.NewButton("ButtonB", root.transform, new Vector2(0.65f, 0.4f), new Vector2(0.85f, 0.6f));

            // The shield: nested canvas, high order, full-screen graphic with an empty mesh.
            var shieldCanvas = ProbeUi.NewNestedCanvas("Shield", root.transform, 1000, true, true, mode == ShieldMode.AlwaysActive);
            rig.ShieldObject = shieldCanvas.gameObject;
            var graphicObject = ProbeUi.NewObject("ShieldGraphic", shieldCanvas.transform);
            ProbeUi.Stretch((RectTransform)graphicObject.transform);
            rig.ShieldGraphic = graphicObject.AddComponent<ProbeEmptyGraphic>();
            rig.ShieldGraphic.raycastTarget = mode == ShieldMode.ActivatedOnDemand;
            return rig;
        }

        IEnumerable<object> Prepare(Rig rig, Scenario scenario, int frames)
        {
            foreach (var step in ProbeUi.Frames(frames))
                yield return step;

            rig.PointA = ProbeUi.ScreenPointOf((RectTransform)rig.ButtonA.transform);
            rig.PointB = ProbeUi.ScreenPointOf((RectTransform)rig.ButtonB.transform);
            scenario.ControlDepth = rig.ButtonA.targetGraphic.canvasRenderer.absoluteDepth;
            scenario.ShieldAbsoluteDepth = rig.ShieldGraphic.canvasRenderer.absoluteDepth;
            scenario.ShieldDepth = rig.ShieldGraphic.depth;
        }

        IEnumerable<object> RunScenario(Scenario scenario)
        {
            var rig = BuildRig(scenario.Mode);
            try
            {
                rig.ButtonA.onClick.AddListener(() => OnClick(scenario, rig, "A"));
                rig.ButtonB.onClick.AddListener(() => OnClick(scenario, rig, "B"));
                rig.ButtonA.gameObject.AddComponent<ProbePointerRecorder>().Init("A", scenario.UpOrder);
                rig.ButtonB.gameObject.AddComponent<ProbePointerRecorder>().Init("B", scenario.UpOrder);

                foreach (var step in Prepare(rig, scenario, 3))
                    yield return step;

                scenario.Focused = rig.EventSystem.isFocused;
                if (!ProbeUi.ScreenIsUsable() || scenario.ControlDepth < 0)
                {
                    scenario.Skipped = true;
                    yield break;
                }

                var touchA = m_TouchBase + scenario.IdA;
                var touchB = m_TouchBase + scenario.IdB;
                m_TouchBase += 10;

                if (scenario.SingleTouch)
                {
                    QueueBegin(touchA, rig.PointA);
                    foreach (var step in ProbeUi.Frames(2))
                        yield return step;

                    QueueEnd(touchA, rig.PointA);
                    foreach (var step in ProbeUi.Frames(2))
                        yield return step;
                }
                else
                {
                    // Both fingers go down in one frame, then both are released in one later frame.
                    if (scenario.BeginAFirst)
                    {
                        QueueBegin(touchA, rig.PointA);
                        QueueBegin(touchB, rig.PointB);
                    }
                    else
                    {
                        QueueBegin(touchB, rig.PointB);
                        QueueBegin(touchA, rig.PointA);
                    }

                    foreach (var step in ProbeUi.Frames(2))
                        yield return step;

                    if (scenario.EndAFirst)
                    {
                        QueueEnd(touchA, rig.PointA);
                        QueueEnd(touchB, rig.PointB);
                    }
                    else
                    {
                        QueueEnd(touchB, rig.PointB);
                        QueueEnd(touchA, rig.PointA);
                    }

                    foreach (var step in ProbeUi.Frames(1))
                        yield return step;
                }

                // Let pointer states settle, then read the shield depth once more.
                foreach (var step in ProbeUi.Frames(2))
                    yield return step;

                scenario.ShieldDepthAfter = rig.ShieldGraphic.canvasRenderer.absoluteDepth;
            }
            finally
            {
                rig.Dispose();
            }
        }

        void QueueBegin(int touchId, Vector2 position)
        {
            BeginTouch(touchId, position, queueEventOnly: true, screen: m_Touchscreen);
        }

        void QueueEnd(int touchId, Vector2 position)
        {
            EndTouch(touchId, position, queueEventOnly: true, screen: m_Touchscreen);
        }
    }
}
#endif
