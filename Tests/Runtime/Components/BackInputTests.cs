using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // BackInput.HandleBack on real roots: one back press without an input device, in every input configuration.
    // With the Input System active, Update also polls the live keyboard, so a real Escape press during a local run adds a press.
    public sealed class BackInputTests
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 1f;

        private static readonly Regex NoRootWarning = new Regex("found no ViewRoot");

        private readonly List<Object> _created = new List<Object>();
        private StageWorld _world;
        private StageWorld _otherWorld;

        [TearDown]
        public void TearDown()
        {
            if (_otherWorld != null)
            {
                _otherWorld.Dispose();
                _otherWorld = null;
            }

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

        // Destroy is deferred; the next test must not see this test's objects.
        [UnityTearDown]
        public IEnumerator WaitForDestroy()
        {
            yield return null;
        }

        [UnityTest]
        public IEnumerator HandleBack_PushedScreen_ClosesItWithBack()
        {
            _world = new StageWorld();
            var backInput = _world.Root.gameObject.AddComponent<BackInput>();
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);
            _world.Row("first", _world.Template<RecordingView>("First"));
            _world.Row("second", _world.Template<RecordingView>("Second"));
            _world.Navigator.Open(ViewKey.Create("first"), _world.Owner);
            yield return WaitSettled(_world, "first");
            var second = _world.Navigator.ShowAsync(ViewKey.Create("second"), _world.Owner);
            yield return WaitSettled(_world, "second");

            backInput.HandleBack();
            yield return WaitFor(() => !second.IsPending(), "the second screen to close");

            Assert.That(second.ResultNow().Reason, Is.EqualTo(CloseReason.Back), "second screen close reason");
            Assert.That(unhandled, Is.Zero, "Unhandled raised");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HandleBack_RootScreen_RaisesUnhandled()
        {
            _world = new StageWorld();
            var backInput = _world.Root.gameObject.AddComponent<BackInput>();
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled(_world, "screen");

            backInput.HandleBack();

            Assert.That(unhandled, Is.EqualTo(1), "Unhandled raised");
            Assert.That(screen.Phase, Is.EqualTo(ViewPhase.Open), "root screen phase");
            LogAssert.NoUnexpectedReceived();
        }

        // On the root screen an unswallowed press would raise Unhandled, so the hold is what keeps it silent.
        [UnityTest]
        public IEnumerator HandleBack_ShieldUp_RaisesNothing()
        {
            _world = new StageWorld();
            var backInput = _world.Root.gameObject.AddComponent<BackInput>();
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled(_world, "screen");
            var hold = _world.Navigator.HoldInput("test", _world.Owner);

            backInput.HandleBack();

            Assert.That(unhandled, Is.Zero, "Unhandled raised for a swallowed press");
            Assert.That(screen.Phase, Is.EqualTo(ViewPhase.Open), "root screen phase");
            hold.Remove();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HandleBack_RootOnAParent_ClosesPushedScreenWithBack()
        {
            _world = new StageWorld();
            _world.Row("first", _world.Template<RecordingView>("First"));
            _world.Row("second", _world.Template<RecordingView>("Second"));
            _world.Navigator.Open(ViewKey.Create("first"), _world.Owner);
            yield return WaitSettled(_world, "first");
            var second = _world.Navigator.ShowAsync(ViewKey.Create("second"), _world.Owner);
            yield return WaitSettled(_world, "second");
            var backInput = CreateBackInput("BackInput", _world.Root.transform);
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);

            backInput.HandleBack();
            Assert.That(unhandled, Is.Zero, "Unhandled raised: the parent root was not found or did not receive Back");
            yield return WaitFor(() => !second.IsPending(), "the parent root's second screen to close");

            Assert.That(second.ResultNow().Reason, Is.EqualTo(CloseReason.Back), "second screen close reason");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void HandleBack_NoRootAnywhere_WarnsAndRaisesUnhandled()
        {
            var backInput = CreateBackInput("LooseBackInput");
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);

            LogAssert.Expect(LogType.Warning, NoRootWarning);
            backInput.HandleBack();

            Assert.That(unhandled, Is.EqualTo(1), "Unhandled raised");
            LogAssert.NoUnexpectedReceived();
        }

        // The parent root has nothing open, so picking it would raise Unhandled and leave the pushed screen open.
        [UnityTest]
        public IEnumerator HandleBack_ExplicitRootField_WinsOverTheParentRoot()
        {
            _world = new StageWorld();
            _otherWorld = new StageWorld(eventSystem: false);
            var explicitWorld = _otherWorld;
            explicitWorld.Row("first", explicitWorld.Template<RecordingView>("First"));
            explicitWorld.Row("second", explicitWorld.Template<RecordingView>("Second"));
            explicitWorld.Navigator.Open(ViewKey.Create("first"), explicitWorld.Owner);
            yield return WaitSettled(explicitWorld, "first");
            var second = explicitWorld.Navigator.ShowAsync(ViewKey.Create("second"), explicitWorld.Owner);
            yield return WaitSettled(explicitWorld, "second");
            var backInput = CreateBackInput("BackInput", _world.Root.transform);
            StageWorld.SetField(backInput, "_root", explicitWorld.Root);
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);

            backInput.HandleBack();
            Assert.That(unhandled, Is.Zero, "Unhandled raised: the wrong root received Back");
            yield return WaitFor(() => !second.IsPending(), "the explicit root's second screen to close");

            Assert.That(second.ResultNow().Reason, Is.EqualTo(CloseReason.Back), "second screen close reason");
            LogAssert.NoUnexpectedReceived();
        }

        // The own root is nested under a parent root that has nothing open, so picking the parent would raise Unhandled.
        [UnityTest]
        public IEnumerator HandleBack_RootOnTheSameObjectUnderAParentRoot_ClosesOwnRootScreen()
        {
            _world = new StageWorld();
            _otherWorld = new StageWorld(eventSystem: false);
            var ownWorld = _otherWorld;
            ownWorld.Root.transform.SetParent(_world.Root.transform, false);
            ownWorld.Row("first", ownWorld.Template<RecordingView>("First"));
            ownWorld.Row("second", ownWorld.Template<RecordingView>("Second"));
            ownWorld.Navigator.Open(ViewKey.Create("first"), ownWorld.Owner);
            yield return WaitSettled(ownWorld, "first");
            var second = ownWorld.Navigator.ShowAsync(ViewKey.Create("second"), ownWorld.Owner);
            yield return WaitSettled(ownWorld, "second");
            var backInput = ownWorld.Root.gameObject.AddComponent<BackInput>();
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);

            backInput.HandleBack();
            Assert.That(unhandled, Is.Zero, "Unhandled raised: the wrong root received Back");
            yield return WaitFor(() => !second.IsPending(), "the own root's second screen to close");

            Assert.That(second.ResultNow().Reason, Is.EqualTo(CloseReason.Back), "second screen close reason");
            LogAssert.NoUnexpectedReceived();
        }

        // The first press resolves the root; after it is destroyed nothing else is in reach, so only a fresh lookup warns.
        [UnityTest]
        public IEnumerator HandleBack_RootDestroyed_ReResolvesAndRaisesUnhandledWithAWarning()
        {
            _world = new StageWorld();
            yield return null;
            var backInput = CreateBackInput("BackInput");
            StageWorld.SetField(backInput, "_root", _world.Root);
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);
            backInput.HandleBack();
            Assume.That(unhandled, Is.EqualTo(1), "the first press did not reach the root's Back(), so this test cannot tell");
            Object.Destroy(_world.Root.gameObject);
            yield return null;

            LogAssert.Expect(LogType.Warning, NoRootWarning);
            backInput.HandleBack();

            Assert.That(unhandled, Is.EqualTo(2), "Unhandled raised once per press");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HandleBack_UnhandledListenerThrows_LogsTheExceptionAndDoesNotThrow()
        {
            _world = new StageWorld();
            var backInput = _world.Root.gameObject.AddComponent<BackInput>();
            backInput.Unhandled.AddListener(() => throw new InvalidOperationException("Unhandled listener failure"));
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled(_world, "screen");

            LogAssert.Expect(LogType.Exception, new Regex("Unhandled listener failure"));

            Assert.That(() => backInput.HandleBack(), Throws.Nothing);
            LogAssert.NoUnexpectedReceived();
        }

        // Reading the navigator here would initialise the root with defaults, before the bootstrap's Initialize.
        [Test]
        public void HandleBack_RootNotInitialized_LeavesRootUninitialized()
        {
            var root = CreateUninitializedRoot();
            var backInput = root.gameObject.AddComponent<BackInput>();

            backInput.HandleBack();

            Assert.That(root.IsInitialized, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // Back() was never called, so the press is neither handled nor unhandled.
        [Test]
        public void HandleBack_RootNotInitialized_DoesNotRaiseUnhandled()
        {
            var root = CreateUninitializedRoot();
            var backInput = root.gameObject.AddComponent<BackInput>();
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);

            backInput.HandleBack();

            Assert.That(unhandled, Is.Zero, "Unhandled raised");
            LogAssert.NoUnexpectedReceived();
        }

        // The press before Initialize must not leave the cached root ignored; the initialised root has nothing open.
        [Test]
        public void HandleBack_RootInitializedAfterAnEarlierPress_RaisesUnhandled()
        {
            CreateEventSystem();
            var root = CreateUninitializedRoot();
            var backInput = root.gameObject.AddComponent<BackInput>();
            var unhandled = 0;
            backInput.Unhandled.AddListener(() => unhandled++);
            backInput.HandleBack();
            Assume.That(unhandled, Is.Zero, "the press before Initialize raised Unhandled, so this test cannot tell");
            root.Initialize(new ViewRootSetup { Inject = _ => { } });

            backInput.HandleBack();

            Assert.That(unhandled, Is.EqualTo(1), "Unhandled raised after Initialize");
            LogAssert.NoUnexpectedReceived();
        }

        // Helpers

        // Keeps Initialize free of the missing EventSystem warning.
        private void CreateEventSystem()
        {
            var eventSystemObject = new GameObject("TestEventSystem");
            _created.Add(eventSystemObject);
            eventSystemObject.AddComponent<EventSystem>();
        }

        // Initialize On Awake is off and nothing initialises it.
        private ViewRoot CreateUninitializedRoot()
        {
            var rootObject = new GameObject("UninitializedRoot", typeof(RectTransform));
            rootObject.SetActive(false);
            _created.Add(rootObject);
            rootObject.AddComponent<Canvas>();
            var root = rootObject.AddComponent<ViewRoot>();
            StageWorld.SetField(root, "_initializeOnAwake", false);
            rootObject.SetActive(true);
            return root;
        }

        private BackInput CreateBackInput(string name, Transform parent = null)
        {
            var backInputObject = new GameObject(name);
            _created.Add(backInputObject);
            if (parent != null)
                backInputObject.transform.SetParent(parent, false);

            return backInputObject.AddComponent<BackInput>();
        }

        // Open, nothing running and the shield down, so Back() is not swallowed by the shield.
        private static IEnumerator WaitSettled(StageWorld world, string viewId)
        {
            return WaitFor(
                () =>
                {
                    ViewHandle handle;
                    return world.Navigator.TryGetOpen(viewId, out handle)
                           && handle.Phase == ViewPhase.Open
                           && !world.Navigator.IsBusy
                           && !world.Stage.Shield.raycastTarget;
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
