// BackInput polls the keyboard only when the Input System handles input; the fixture needs the package.
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // BackInput on a real root, driven by a virtual keyboard's Escape key.
    public sealed class BackInputKeyTests : InputTestFixture
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 1f;

        private readonly List<BackInput> _backInputs = new List<BackInput>();
        private readonly List<Object> _created = new List<Object>();
        private StageWorld _world;
        private Keyboard _keyboard;

        public override void Setup()
        {
            base.Setup();

            // Headless runs may report the player as unfocused; focus is not what these tests check.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        public override void TearDown()
        {
            try
            {
                // Each BackInput owns an enabled InputAction; it is disposed before the fixture restores the input state.
                for (var i = _backInputs.Count - 1; i >= 0; i--)
                {
                    if (_backInputs[i] != null)
                        Object.DestroyImmediate(_backInputs[i]);
                }

                _backInputs.Clear();

                if (_world != null)
                {
                    _world.Dispose();
                    _world = null;
                }

                for (var i = _created.Count - 1; i >= 0; i--)
                {
                    if (_created[i] != null)
                        Object.Destroy(_created[i]);
                }

                _created.Clear();
            }
            finally
            {
                base.TearDown();
            }
        }

        // Destroy is deferred; the next test must not see this test's objects.
        [UnityTearDown]
        public IEnumerator WaitForDestroy()
        {
            yield return null;
        }

        [UnityTest]
        public IEnumerator Update_EscapePressedOnPushedScreen_ClosesItWithBack()
        {
            _world = new StageWorld();
            AddBackInput(_world.Root.gameObject);
            _world.Row("first", _world.Template<RecordingView>("First"));
            _world.Row("second", _world.Template<RecordingView>("Second"));
            _world.Navigator.Open(ViewKey.Create("first"), _world.Owner);
            yield return WaitSettled("first");
            var second = _world.Navigator.ShowAsync(ViewKey.Create("second"), _world.Owner);
            yield return WaitSettled("second");

            yield return PressEscape();
            yield return WaitFor(() => !second.IsPending(), "the second screen to close");

            Assert.That(second.ResultNow().Reason, Is.EqualTo(CloseReason.Back));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Update_EscapePressedOnRootScreen_RaisesUnhandledOnce()
        {
            _world = new StageWorld();
            var backInput = AddBackInput(_world.Root.gameObject);
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");

            yield return PressEscape();

            Assert.That(unhandled, Is.EqualTo(1), "Unhandled raised");
            Assert.That(screen.Phase, Is.EqualTo(ViewPhase.Open), "root screen phase");
            LogAssert.NoUnexpectedReceived();
        }

        // Down and up are queued without a frame in between, so both arrive in one input update on a keyboard added just now.
        [UnityTest]
        public IEnumerator Update_EscapeDownAndUpInOneUpdateOnNewKeyboard_ClosesPushedScreenWithBack()
        {
            _world = new StageWorld();
            AddBackInput(_world.Root.gameObject);
            _world.Row("first", _world.Template<RecordingView>("First"));
            _world.Row("second", _world.Template<RecordingView>("Second"));
            _world.Navigator.Open(ViewKey.Create("first"), _world.Owner);
            yield return WaitSettled("first");
            var second = _world.Navigator.ShowAsync(ViewKey.Create("second"), _world.Owner);
            yield return WaitSettled("second");

            var newKeyboard = InputSystem.AddDevice<Keyboard>();
            Press(newKeyboard.escapeKey, queueEventOnly: true);
            Release(newKeyboard.escapeKey, queueEventOnly: true);
            yield return WaitFor(() => !second.IsPending(), "the second screen to close");

            Assert.That(second.ResultNow().Reason, Is.EqualTo(CloseReason.Back));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Update_EscapeHeldOverSeveralFrames_RaisesUnhandledOnce()
        {
            _world = new StageWorld();
            var backInput = AddBackInput(_world.Root.gameObject);
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");

            Press(_keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            yield return null;
            yield return null;
            Release(_keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            yield return null;

            Assert.That(unhandled, Is.EqualTo(1), "Unhandled raised");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Update_EscapePressedWhileBackInputDisabled_RaisesNothing()
        {
            _world = new StageWorld();
            var backInput = AddBackInput(_world.Root.gameObject);
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");

            backInput.enabled = false;
            yield return PressEscape();

            Assert.That(unhandled, Is.Zero, "Unhandled raised");
            backInput.enabled = true;
            yield return PressEscape();
            Assume.That(unhandled, Is.EqualTo(1), "a press on the enabled component did not arrive, so the zero above proves nothing");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Update_EscapePressedAfterBackInputReEnabled_RaisesUnhandledOnce()
        {
            _world = new StageWorld();
            var backInput = AddBackInput(_world.Root.gameObject);
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");
            backInput.enabled = false;
            yield return null;

            backInput.enabled = true;
            yield return PressEscape();

            Assert.That(unhandled, Is.EqualTo(1), "Unhandled raised");
            LogAssert.NoUnexpectedReceived();
        }

        // A press that initialised the root with defaults would make the bootstrap's Initialize throw.
        // The second press reaches Back() on the now empty root, so it shows the key path works here.
        [UnityTest]
        public IEnumerator Update_EscapePressedBeforeRootInitialized_LetsBootstrapInitialize()
        {
            var root = CreateUninitializedRoot();
            var backInput = AddBackInput(root.gameObject);
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);
            yield return null;

            yield return PressEscape();

            Assert.That(() => root.Initialize(new ViewRootSetup { Inject = _ => { } }), Throws.Nothing, "the bootstrap's Initialize");
            Assert.That(root.IsInitialized, Is.True, "IsInitialized");
            yield return PressEscape();
            Assume.That(unhandled, Is.EqualTo(1), "the second press did not reach BackInput, so the first one proves nothing");
            LogAssert.NoUnexpectedReceived();
        }

        // Helpers

        private BackInput AddBackInput(GameObject target)
        {
            var backInput = target.AddComponent<BackInput>();
            _backInputs.Add(backInput);
            return backInput;
        }

        // Initialize On Awake is off and nothing initialises it; the EventSystem keeps a later Initialize free of warnings.
        private ViewRoot CreateUninitializedRoot()
        {
            var eventSystemObject = new GameObject("TestEventSystem");
            _created.Add(eventSystemObject);
            eventSystemObject.AddComponent<EventSystem>();

            var rootObject = new GameObject("UninitializedRoot", typeof(RectTransform));
            rootObject.SetActive(false);
            _created.Add(rootObject);
            rootObject.AddComponent<Canvas>();
            var root = rootObject.AddComponent<ViewRoot>();
            StageWorld.SetField(root, "_initializeOnAwake", false);
            rootObject.SetActive(true);
            return root;
        }

        // The key event is queued so it is processed in the next frame's input update, before BackInput polls.
        private IEnumerator PressEscape()
        {
            Press(_keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            Release(_keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            yield return null;
        }

        // Open, nothing running and the shield down, so Back() is not swallowed by the shield.
        private IEnumerator WaitSettled(string viewId)
        {
            return WaitFor(
                () =>
                {
                    ViewHandle handle;
                    return _world.Navigator.TryGetOpen(viewId, out handle)
                           && handle.Phase == ViewPhase.Open
                           && !_world.Navigator.IsBusy
                           && !_world.Stage.Shield.raycastTarget;
                },
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
