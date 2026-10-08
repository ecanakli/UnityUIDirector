// Built on InputTestFixture, so it compiles only when the Input System is installed.
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Ecanakli.UIDirector.Tests.Probes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests
{
    // The input shield on a real stage: raise and lower, raycast blocking, HoldInput, selection clearing and same-frame pointers.
    public sealed class ShieldTests : InputTestFixture
    {
        private const int MaxFrames = 300;

        // Well above the fade, so fast batch-mode frames do not time out a running open.
        private const float MinWaitSeconds = 5f;

        // Long enough that the open is still in flight for several frames (one frame advances at most 1/30 s).
        private const float SlowFadeSeconds = 0.5f;

        private StageWorld _world;
        private Keyboard _keyboard;
        private Touchscreen _touchscreen;

        public override void Setup()
        {
            base.Setup();

            // Headless runs may report the player as unfocused; focus is not what these tests check.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _touchscreen = InputSystem.AddDevice<Touchscreen>();
        }

        public override void TearDown()
        {
            try
            {
                if (_world != null)
                {
                    _world.Dispose();
                    _world = null;
                }
            }
            finally
            {
                base.TearDown();
            }
        }

        // Destroy is deferred; the next test must not see this world's objects.
        [UnityTearDown]
        public IEnumerator WaitForDestroy()
        {
            yield return null;
        }

        [UnityTest]
        public IEnumerator Open_ShieldingLayer_RaisesShieldInsideOpenCallAndLowersItWhenSettled()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithSlowFade));
            yield return null;
            var shield = _world.Stage.Shield;
            var before = shield.raycastTarget;

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            var inFlight = shield.raycastTarget;
            yield return null;
            var phaseNextFrame = screen.Phase;
            var nextFrame = shield.raycastTarget;
            yield return WaitFor(() => screen.Phase == ViewPhase.Open && !_world.Navigator.IsBusy, "the screen to open");
            yield return null;

            Assume.That(phaseNextFrame, Is.Not.EqualTo(ViewPhase.Open), "the open was not in flight a frame later; the fade is too short");
            Assert.That(before, Is.False, "raycastTarget before the open");
            Assert.That(inFlight, Is.True, "raycastTarget right after Open returned");
            Assert.That(nextFrame, Is.True, "raycastTarget a frame later, with the open still in flight");
            Assert.That(shield.raycastTarget, Is.False, "raycastTarget once the open has settled");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_InFlightAndSettled_ShieldStaysActiveAndEnabled()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithSlowFade));
            yield return null;
            var shield = _world.Stage.Shield;
            var states = new List<bool> { IsDrawn(shield) };

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            states.Add(IsDrawn(shield));
            yield return null;
            states.Add(IsDrawn(shield));
            yield return WaitFor(() => screen.Phase == ViewPhase.Open && !_world.Navigator.IsBusy, "the screen to open");
            yield return null;
            states.Add(IsDrawn(shield));

            Assert.That(shield.GetComponent<CanvasRenderer>(), Is.Not.Null, "CanvasRenderer on the shield");
            Assert.That(states, Has.All.True, "shield object active, graphic and canvas enabled: before, in the Open call, a frame later, settled");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Raycast_AtButtonWhileOpenInFlight_HitsShield()
        {
            _world = new StageWorld();
            _world.Row("base", _world.Template<RecordingView>("Base", WithButtons));
            _world.Row("next", _world.Template<RecordingView>("Next", WithSlowFade));
            yield return OpenSettled("base");
            yield return SettleRender();
            var button = ButtonOf("base", "ButtonA");
            RequireRendered(button);
            var point = ProbeUi.ScreenPointOf((RectTransform)button.transform);
            Assume.That(TopHit(point), Is.SameAs(button.gameObject), "control: the button is the topmost hit while no shield is up");

            _world.Navigator.Open(ViewKey.Create("next"), _world.Owner);
            var hits = ProbeUi.Raycast(point);

            Assert.That(hits, Is.Not.Empty, "nothing under the button");
            Assert.That(hits[0].gameObject, Is.SameAs(_world.Stage.Shield.gameObject), "topmost hit: " + ProbeUi.Describe(hits));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Raycast_AfterOpenSettled_DoesNotHitShield()
        {
            _world = new StageWorld();
            _world.Row("base", _world.Template<RecordingView>("Base", WithButtons));
            _world.Row("next", _world.Template<RecordingView>("Next", WithSlowFade));
            yield return OpenSettled("base");
            yield return SettleRender();
            var button = ButtonOf("base", "ButtonA");
            RequireRendered(button);
            var point = ProbeUi.ScreenPointOf((RectTransform)button.transform);
            var shieldObject = _world.Stage.Shield.gameObject;

            yield return OpenSettled("next");
            var hits = ProbeUi.Raycast(point);

            Assert.That(hits, Has.None.Matches<RaycastResult>(hit => hit.gameObject == shieldObject), "hits: " + ProbeUi.Describe(hits));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HoldInput_UntilOwnerEnds_KeepsButtonUnreachable()
        {
            _world = new StageWorld();
            _world.Row("base", _world.Template<RecordingView>("Base", WithButtons));
            yield return OpenSettled("base");
            yield return SettleRender();
            var button = ButtonOf("base", "ButtonA");
            RequireRendered(button);
            var point = ProbeUi.ScreenPointOf((RectTransform)button.transform);
            var shieldObject = _world.Stage.Shield.gameObject;
            var owner = new CancellationTokenSource();
            try
            {
                _world.Navigator.HoldInput("test hold", owner.Token);
                for (var i = 0; i < 5; i++)
                    yield return null;

                var whileHeld = TopHit(point);
                owner.Cancel();
                yield return WaitFor(() => !_world.Stage.Shield.raycastTarget, "the shield to drop after the owner ended");
                var afterOwnerEnded = TopHit(point);

                Assert.That(whileHeld, Is.SameAs(shieldObject), "topmost hit five frames into the hold");
                Assert.That(afterOwnerEnded, Is.SameAs(button.gameObject), "topmost hit after the owner ended");
            }
            finally
            {
                owner.Dispose();
            }

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SetShield_RaisedWithButtonSelected_ClearsSelection()
        {
            _world = new StageWorld();
            _world.Row("base", _world.Template<RecordingView>("Base", WithButtons));
            yield return OpenSettled("base");
            var eventSystem = _world.EventSystemObject.GetComponent<EventSystem>();
            var button = ButtonOf("base", "ButtonA");
            eventSystem.SetSelectedGameObject(button.gameObject);
            Assume.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject), "control: the button is selected");

            _world.Navigator.HoldInput("test hold", _world.Owner);

            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Submit_AfterShieldRaised_DoesNotReachPreviouslySelectedButton()
        {
            _world = new StageWorld();
            _world.EventSystemObject.AddComponent<InputSystemUIInputModule>();
            _world.Row("base", _world.Template<RecordingView>("Base", WithButtons));
            yield return OpenSettled("base");
            var eventSystem = _world.EventSystemObject.GetComponent<EventSystem>();
            var button = ButtonOf("base", "ButtonA");
            var clicks = 0;
            button.onClick.AddListener(() => clicks++);

            eventSystem.SetSelectedGameObject(button.gameObject);
            yield return null;
            yield return PressEnter();
            Assume.That(clicks, Is.EqualTo(1), "control: Enter with the button selected and no shield clicks it once");

            eventSystem.SetSelectedGameObject(button.gameObject);
            yield return null;
            _world.Navigator.HoldInput("test hold", _world.Owner);
            yield return PressEnter();

            Assert.That(clicks, Is.EqualTo(1), "clicks after Submit with the shield up");
            LogAssert.NoUnexpectedReceived();
        }

        // Both fingers down in one frame, both released in one later frame; A's click raises the shield.
        [UnityTest]
        public IEnumerator HoldInput_RaisedInClickHandler_BlocksSecondPointerReleasedInSameFrame()
        {
            _world = new StageWorld();
            _world.EventSystemObject.AddComponent<InputSystemUIInputModule>();
            _world.Row("base", _world.Template<RecordingView>("Base", WithButtons));
            yield return OpenSettled("base");
            yield return SettleRender();
            var buttonA = ButtonOf("base", "ButtonA");
            var buttonB = ButtonOf("base", "ButtonB");
            RequireRendered(buttonA);
            var pointA = ProbeUi.ScreenPointOf((RectTransform)buttonA.transform);
            var pointB = ProbeUi.ScreenPointOf((RectTransform)buttonB.transform);

            var clicksA = 0;
            var clicksB = 0;
            var upOrder = new List<string>();
            buttonA.onClick.AddListener(() =>
            {
                clicksA++;
                _world.Navigator.HoldInput("raised in click", _world.Owner);
            });
            buttonB.onClick.AddListener(() => clicksB++);
            buttonA.gameObject.AddComponent<ProbePointerRecorder>().Init("A", upOrder);
            buttonB.gameObject.AddComponent<ProbePointerRecorder>().Init("B", upOrder);

            // Pointers are processed in the order the touches began, so A begins first.
            BeginTouch(1, pointA, queueEventOnly: true, screen: _touchscreen);
            BeginTouch(2, pointB, queueEventOnly: true, screen: _touchscreen);
            yield return null;
            yield return null;
            EndTouch(1, pointA, queueEventOnly: true, screen: _touchscreen);
            EndTouch(2, pointB, queueEventOnly: true, screen: _touchscreen);
            for (var i = 0; i < 3; i++)
                yield return null;

            Assume.That(upOrder, Is.EqualTo(new[] { "A", "B" }), "both releases reached the UI, A first");
            Assert.That(clicksA, Is.EqualTo(1), "clicks on A");
            Assert.That(clicksB, Is.Zero, "clicks on B, released in the frame the shield was raised");
            LogAssert.NoUnexpectedReceived();
        }

        // Helpers

        private static void WithSlowFade(GameObject view)
        {
            var fade = view.AddComponent<FadeTransition>();
            StageWorld.SetField(fade, "_duration", SlowFadeSeconds);
            StageWorld.SetField(view.GetComponent<ViewHost>(), "_transition", fade);
        }

        private static void WithButtons(GameObject view)
        {
            ProbeUi.NewButton("ButtonA", view.transform, new Vector2(0.15f, 0.4f), new Vector2(0.35f, 0.6f));
            ProbeUi.NewButton("ButtonB", view.transform, new Vector2(0.65f, 0.4f), new Vector2(0.85f, 0.6f));
        }

        private static bool IsDrawn(ShieldGraphic shield)
        {
            var canvas = shield.GetComponent<Canvas>();
            return shield.gameObject.activeInHierarchy && shield.enabled && canvas != null && canvas.enabled;
        }

        // Batch runs may give no usable screen or render nothing; then pointer and raycast checks cannot run.
        private static void RequireRendered(Button button)
        {
            Assume.That(ProbeUi.ScreenIsUsable(), Is.True, "screen is " + Screen.width + "x" + Screen.height);
            Assume.That(button.targetGraphic.canvasRenderer.absoluteDepth, Is.GreaterThanOrEqualTo(0), "the button image has not rendered");
        }

        private static GameObject TopHit(Vector2 point)
        {
            var hits = ProbeUi.Raycast(point);
            return hits.Count == 0 ? null : hits[0].gameObject;
        }

        // Layout and a rendered frame, so graphics have a depth the raycaster accepts.
        private static IEnumerator SettleRender()
        {
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
        }

        private IEnumerator PressEnter()
        {
            Press(_keyboard.enterKey);
            yield return null;
            Release(_keyboard.enterKey);
            yield return null;
            yield return null;
        }

        private Button ButtonOf(string viewId, string buttonName)
        {
            ViewHandle handle;
            Assert.That(_world.Navigator.TryGetOpen(viewId, out handle), Is.True, viewId + " is open");
            var instance = StageWorld.InstanceOf(handle);
            Assert.That(instance, Is.Not.Null, "instance of " + viewId);
            var child = instance.transform.Find(buttonName);
            Assert.That(child, Is.Not.Null, buttonName + " in " + viewId);
            return child.GetComponent<Button>();
        }

        private IEnumerator OpenSettled(string viewId)
        {
            var handle = _world.Navigator.Open(ViewKey.Create(viewId), _world.Owner);
            return WaitFor(
                () => handle.Phase == ViewPhase.Open && !_world.Navigator.IsBusy && !_world.Stage.Shield.raycastTarget,
                viewId + " to open and the root to settle");
        }

        // Bounded by frames and a real-time floor.
        private static IEnumerator WaitFor(Func<bool> condition, string what)
        {
            var started = Time.realtimeSinceStartup;
            for (var frame = 0; !condition(); frame++)
            {
                if (frame >= MaxFrames && Time.realtimeSinceStartup - started >= MinWaitSeconds)
                    Assert.Fail("Timed out after " + frame + " frames waiting for " + what + ".");

                yield return null;
            }
        }
    }
}
#endif
