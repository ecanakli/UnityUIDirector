// Built on InputTestFixture, so it compiles only when the Input System is installed.
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Selection and Submit through the Input System UI module; the legacy module half is out of scope (Input System only host).
    public sealed class EngineProbes_Selection : InputTestFixture
    {
        sealed class Rig
        {
            public EventSystem EventSystem;
            public Button Button;
            public Vector2 Point;
            public int Clicks;
            public Action OnClick;
        }

        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();
        Keyboard m_Keyboard;
        Touchscreen m_Touchscreen;
        int m_TouchBase;

        public override void Setup()
        {
            base.Setup();

            // Focus is not what is probed; headless runs may report the player as unfocused.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            m_Keyboard = InputSystem.AddDevice<Keyboard>();
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
        public IEnumerator P13_Submit_WithSelection_ClicksSelectedButton()
        {
            var rig = BuildRig();
            foreach (var step in Warmup(rig))
                yield return step;

            rig.EventSystem.SetSelectedGameObject(rig.Button.gameObject);
            yield return null;
            var selectedBefore = rig.EventSystem.currentSelectedGameObject;

            foreach (var step in PressEnter())
                yield return step;

            ProbeLog.Write("P13", "case", "submitWithSelection",
                "selectedBefore", selectedBefore,
                "clicks", rig.Clicks,
                "focused", rig.EventSystem.isFocused);

            Assert.IsTrue(selectedBefore == rig.Button.gameObject, "Precondition: the button must be selected before Submit.");
            Assert.AreEqual(1, rig.Clicks, "Enter with the button selected must click it once.");
        }

        [UnityTest]
        public IEnumerator P13_Submit_WithoutSelection_ClicksNothing()
        {
            var rig = BuildRig();
            foreach (var step in Warmup(rig))
                yield return step;

            rig.EventSystem.SetSelectedGameObject(null);
            yield return null;
            var selectedBefore = rig.EventSystem.currentSelectedGameObject;

            foreach (var step in PressEnter())
                yield return step;

            var clicksWithoutSelection = rig.Clicks;

            // Control: the same key press must click once the button is selected.
            rig.EventSystem.SetSelectedGameObject(rig.Button.gameObject);
            yield return null;
            foreach (var step in PressEnter())
                yield return step;

            var clicksWithSelection = rig.Clicks - clicksWithoutSelection;
            ProbeLog.Write("P13", "case", "submitWithoutSelection",
                "selectedBefore", selectedBefore,
                "clicksWithoutSelection", clicksWithoutSelection,
                "clicksWithSelectionControl", clicksWithSelection);

            if (clicksWithSelection != 1)
                Assert.Inconclusive("Control failed: Enter with a selection clicked " + clicksWithSelection + " times, so the key path does not work on this host.");

            Assert.IsTrue(selectedBefore == null, "Precondition: nothing is selected.");
            Assert.AreEqual(0, clicksWithoutSelection, "Enter must not click anything when nothing is selected.");
        }

        [UnityTest]
        public IEnumerator P13_ClearingSelectionInsideClickHandler_IsLegal()
        {
            var rig = BuildRig();
            var alreadySelecting = new List<bool>();
            rig.OnClick = () =>
            {
                alreadySelecting.Add(EventSystem.current.alreadySelecting);
                EventSystem.current.SetSelectedGameObject(null);
            };

            foreach (var step in Warmup(rig))
                yield return step;

            // Path 1: Submit from the keyboard.
            rig.EventSystem.SetSelectedGameObject(rig.Button.gameObject);
            yield return null;
            foreach (var step in PressEnter())
                yield return step;

            var submitClicks = rig.Clicks;
            var submitSelectionCleared = rig.EventSystem.currentSelectedGameObject == null;
            ProbeLog.Write("P13", "case", "clearInHandlerSubmit",
                "clicks", submitClicks,
                "alreadySelectingInHandler", alreadySelecting,
                "selectionNullAfter", submitSelectionCleared);

            // Path 2: a touch click; the press selects the button, the click handler clears it.
            var touchClicks = -1;
            var touchSelectionCleared = false;
            if (ProbeUi.ScreenIsUsable())
            {
                var touchId = m_TouchBase + 1;
                BeginTouch(touchId, rig.Point, queueEventOnly: true, screen: m_Touchscreen);
                foreach (var step in ProbeUi.Frames(2))
                    yield return step;

                EndTouch(touchId, rig.Point, queueEventOnly: true, screen: m_Touchscreen);
                foreach (var step in ProbeUi.Frames(2))
                    yield return step;

                touchClicks = rig.Clicks - submitClicks;
                touchSelectionCleared = rig.EventSystem.currentSelectedGameObject == null;
            }

            ProbeLog.Write("P13", "case", "clearInHandlerTouch",
                "clicks", touchClicks,
                "alreadySelectingInHandler", alreadySelecting,
                "selectionNullAfter", touchSelectionCleared);

            Assert.AreEqual(1, submitClicks, "The Submit click handler must have run once.");
            Assert.IsTrue(submitSelectionCleared, "Selection must be null after the handler cleared it (Submit path).");
            if (touchClicks >= 0)
            {
                Assert.AreEqual(1, touchClicks, "The touch click handler must have run once.");
                Assert.IsTrue(touchSelectionCleared, "Selection must be null after the handler cleared it (touch path).");
            }
        }

        Rig BuildRig()
        {
            var rig = new Rig();
            var eventSystemObject = m_Cleanup.Track(ProbeUi.NewEventSystem(true));
            rig.EventSystem = eventSystemObject.GetComponent<EventSystem>();

            var root = ProbeUi.NewRootCanvas("ProbeRoot");
            m_Cleanup.Track(root.gameObject);

            rig.Button = ProbeUi.NewButton("ButtonA", root.transform, new Vector2(0.3f, 0.4f), new Vector2(0.7f, 0.6f));
            rig.Button.onClick.AddListener(() =>
            {
                rig.Clicks++;
                rig.OnClick?.Invoke();
            });
            return rig;
        }

        IEnumerable<object> Warmup(Rig rig)
        {
            foreach (var step in ProbeUi.Frames(3))
                yield return step;

            rig.Point = ProbeUi.ScreenPointOf((RectTransform)rig.Button.transform);
        }

        // Key events are queued and processed on the next frame in a [UnityTest].
        IEnumerable<object> PressEnter()
        {
            Press(m_Keyboard.enterKey);
            yield return null;
            Release(m_Keyboard.enterKey);
            yield return null;
            yield return null;
        }
    }
}
#endif
