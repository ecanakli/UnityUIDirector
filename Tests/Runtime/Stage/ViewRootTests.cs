using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Ecanakli.UIDirector.Tests.Probes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // ViewRoot: lazy initialisation, root destruction, ViewHost close, a lost UI camera, a missing EventSystem and the low-memory wiring.
    [TestFixture]
    public sealed class ViewRootTests
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 1f;

        private readonly List<Object> _created = new List<Object>();
        private StageWorld _world;
        private StageWorld _secondWorld;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_world != null)
            {
                _world.Dispose();
                _world = null;
            }

            if (_secondWorld != null)
            {
                _secondWorld.Dispose();
                _secondWorld = null;
            }

            for (var i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.Destroy(_created[i]);
            }

            _created.Clear();

            // Destroy is deferred; the next test must not see these objects.
            yield return null;
        }

        [UnityTest]
        public IEnumerator Navigator_FirstAccessWithInitializeOnAwakeOff_InitializesRoot()
        {
            var root = CreateLazyRoot();
            yield return null;
            Assert.That(root.IsInitialized, Is.False, "initialised before the first access");

            var navigator = root.Navigator;

            Assert.That(navigator, Is.Not.Null, "navigator");
            Assert.That(root.IsInitialized, Is.True, "initialised after the first access");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Initialize_AfterFirstNavigatorAccess_ThrowsInvalidOperationException()
        {
            var root = CreateLazyRoot();
            yield return null;
            Assume.That(root.Navigator, Is.Not.Null);

            Assert.That(() => root.Initialize(new ViewRootSetup()), Throws.TypeOf<InvalidOperationException>());
        }

        [UnityTest]
        public IEnumerator Initialize_AfterAViewWasOpened_ThrowsInvalidOperationException()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");

            Assert.That(() => _world.Root.Initialize(new ViewRootSetup()), Throws.TypeOf<InvalidOperationException>());
        }

        [UnityTest]
        public IEnumerator OnDestroy_RootWithOpenViews_CompletesAwaitersAsDestroyed()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Row("popup", _world.Template<RecordingView>("Popup"), ViewLayers.Popups);
            var screen = _world.Navigator.ShowAsync(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => OpenHandleOf("screen").Phase == ViewPhase.Open, "the screen to open");
            var popup = _world.Navigator.ShowAsync(ViewKey.Create("popup"), _world.Owner);
            yield return WaitFor(() => OpenHandleOf("popup").Phase == ViewPhase.Open, "the popup to open");

            Object.Destroy(_world.Root.gameObject);
            yield return WaitFor(() => !screen.IsPending() && !popup.IsPending(), "both awaiters to complete");

            // Two more frames: a loss report would be forwarded on the next frame, and must not be.
            yield return null;
            yield return null;
            Assert.That(screen.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed), "screen close reason");
            Assert.That(popup.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed), "popup close reason");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Close_ViewHostOfOpenView_ClosesAsDismissed()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            var result = _world.Navigator.ShowAsync(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => OpenHandleOf("screen").Phase == ViewPhase.Open, "the screen to open");

            StageWorld.InstanceOf(OpenHandleOf("screen")).GetComponent<ViewHost>().Close();
            yield return WaitFor(() => !result.IsPending(), "the awaiter to complete");

            Assert.That(result.ResultNow().Reason, Is.EqualTo(CloseReason.Dismissed));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_CameraRootLostItsCamera_LogsUIDIRECTOR114()
        {
            _world = new StageWorld(cameraMode: true);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            Object.Destroy(_world.Camera.gameObject);
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR114"));
            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Initialize_SceneWithoutEventSystem_LogsUIDIRECTOR116()
        {
            Assume.That(Object.FindAnyObjectByType<EventSystem>(), Is.Null, "an EventSystem from elsewhere is in the scene");

            LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR116"));
            _world = new StageWorld(eventSystem: false);
            yield return null;

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_CameraMissing_WarnsOnceForTwoOpens()
        {
            _world = new StageWorld(cameraMode: true);
            _world.Row("first", _world.Template<RecordingView>("First"));
            _world.Row("second", _world.Template<RecordingView>("Second"));
            Object.Destroy(_world.Camera.gameObject);
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR114"));
            var first = _world.Navigator.Open(ViewKey.Create("first"), _world.Owner);
            yield return WaitFor(() => first.Phase == ViewPhase.Open, "the first view to open");
            var second = _world.Navigator.Open(ViewKey.Create("second"), _world.Owner);
            yield return WaitFor(() => second.Phase == ViewPhase.Open, "the second view to open");

            // Exactly one UIDIRECTOR114: a second one would be unexpected here.
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_CameraReassignedThenLostAgain_WarnsAgain()
        {
            _world = new StageWorld(cameraMode: true);
            _world.Row("first", _world.Template<RecordingView>("First"));
            _world.Row("second", _world.Template<RecordingView>("Second"));
            _world.Row("third", _world.Template<RecordingView>("Third"));
            Object.Destroy(_world.Camera.gameObject);
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR114"));
            var first = _world.Navigator.Open(ViewKey.Create("first"), _world.Owner);
            yield return WaitFor(() => first.Phase == ViewPhase.Open, "the first view to open");

            var replacement = Track(new GameObject("ReplacementUiCamera")).AddComponent<Camera>();
            _world.RootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _world.RootCanvas.worldCamera = replacement;
            var second = _world.Navigator.Open(ViewKey.Create("second"), _world.Owner);
            yield return WaitFor(() => second.Phase == ViewPhase.Open, "the second view to open with a camera present");

            Object.Destroy(replacement.gameObject);
            yield return null;
            LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR114"));
            var third = _world.Navigator.Open(ViewKey.Create("third"), _world.Owner);
            yield return WaitFor(() => third.Phase == ViewPhase.Open, "the third view to open");

            // Exactly two UIDIRECTOR114: one per missing-camera span.
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_CameraPresent_LogsNoWarning()
        {
            _world = new StageWorld(cameraMode: true);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Initialize_RootNeverActive_DoesNotSubscribeToLowMemory()
        {
            var root = CreateInactiveRoot();

            root.Initialize(new ViewRootSetup());

            Assert.That(root.IsInitialized, Is.True, "initialised while inactive");
            Assert.That(root.IsLowMemorySubscribed, Is.False, "a root whose Awake never ran must not subscribe to Application.lowMemory");
            Assert.That(EngineLowMemory.CountSubscribers(root), Is.EqualTo(0), "Application.lowMemory subscriptions of a root whose Awake never ran");
        }

        [UnityTest]
        public IEnumerator Awake_RootInitializedWhileInactive_SubscribesToLowMemory()
        {
            var root = CreateInactiveRoot();
            root.Initialize(new ViewRootSetup());
            Assume.That(root.IsLowMemorySubscribed, Is.False, "control: not subscribed before activation");

            root.gameObject.SetActive(true);
            yield return null;

            Assert.That(root.IsLowMemorySubscribed, Is.True, "Awake must subscribe a root that was initialised while inactive");
            Assert.That(EngineLowMemory.CountSubscribers(root), Is.EqualTo(1), "Application.lowMemory subscriptions of the root after activation");
        }

        [UnityTest]
        public IEnumerator Initialize_RootActivatedEarlierNowInactive_SubscribesToLowMemory()
        {
            var root = CreateLazyRoot();
            yield return null;
            root.gameObject.SetActive(false);

            root.Initialize(new ViewRootSetup());

            Assert.That(root.IsLowMemorySubscribed, Is.True, "a root whose Awake already ran must subscribe at Initialize, also while inactive");
            Assert.That(EngineLowMemory.CountSubscribers(root), Is.EqualTo(1), "Application.lowMemory subscriptions of the root after Initialize");
        }

        [UnityTest]
        public IEnumerator Awake_InitializeOnAwake_SubscribesToLowMemoryOnce()
        {
            var root = CreateInactiveRoot();

            root.gameObject.SetActive(true);
            yield return null;

            Assume.That(root.IsInitialized, Is.True, "control: initialised on awake");
            Assert.That(EngineLowMemory.CountSubscribers(root), Is.EqualTo(1), "Application.lowMemory subscriptions of the root after Initialize ran inside Awake");
        }

        [UnityTest]
        public IEnumerator OnDestroy_RootInitializedWhileInactiveThenActivated_CancelsRootToken()
        {
            var root = CreateInactiveRoot();
            root.Initialize(new ViewRootSetup());
            root.gameObject.SetActive(true);
            yield return null;
            var token = root.Navigator.RootToken;

            Object.Destroy(root.gameObject);
            yield return null;

            Assert.That(token.IsCancellationRequested, Is.True, "RootToken cancelled after the root was destroyed");
        }

        [UnityTest]
        public IEnumerator OnDestroy_RootInitializedWhileInactiveThenActivated_UnsubscribesFromLowMemory()
        {
            var root = CreateInactiveRoot();
            root.Initialize(new ViewRootSetup());
            root.gameObject.SetActive(true);
            yield return null;
            Assume.That(root.IsLowMemorySubscribed, Is.True, "control: subscribed after activation");
            Assume.That(EngineLowMemory.CountSubscribers(root), Is.EqualTo(1), "control: one engine subscription after activation");

            Object.Destroy(root.gameObject);
            yield return null;

            Assert.That(root.IsLowMemorySubscribed, Is.False, "subscribed to Application.lowMemory after the root was destroyed");
            Assert.That(EngineLowMemory.CountSubscribers(root), Is.EqualTo(0), "Application.lowMemory subscriptions of the root after it was destroyed");
        }

        [UnityTest]
        public IEnumerator OnDestroy_InitializedRoot_UnsubscribesFromLowMemory()
        {
            _world = new StageWorld();
            var root = _world.Root;
            Assume.That(root.IsLowMemorySubscribed, Is.True, "control: subscribed while alive");
            Assume.That(EngineLowMemory.CountSubscribers(root), Is.EqualTo(1), "control: one engine subscription while alive");

            Object.Destroy(root.gameObject);
            yield return null;

            Assert.That(root.IsLowMemorySubscribed, Is.False, "subscribed to Application.lowMemory after the root was destroyed");
            Assert.That(EngineLowMemory.CountSubscribers(root), Is.EqualTo(0), "Application.lowMemory subscriptions of the root after it was destroyed");
        }

        [UnityTest]
        public IEnumerator LowMemory_AfterInitialize_TrimsKeptInstances()
        {
            _world = new StageWorld();
            var parked = new List<GameObject>();
            yield return ParkKeptInstance(_world, "kept", parked);
            yield return null;
            Assume.That((bool)parked[0], Is.True, "control: the instance is kept before the low-memory event");

            EngineLowMemory.Raise();
            // Destroy takes effect at the end of the frame.
            yield return null;

            Assert.That((bool)parked[0], Is.False, "kept instance alive after Application.lowMemory");
        }

        [UnityTest]
        public IEnumerator LowMemory_TwoRoots_BothTrim()
        {
            _world = new StageWorld();
            // The first world's EventSystem serves both roots; a second one would log.
            _secondWorld = new StageWorld(eventSystem: false);
            var parked = new List<GameObject>();
            yield return ParkKeptInstance(_world, "kept", parked);
            yield return ParkKeptInstance(_secondWorld, "kept", parked);
            yield return null;
            Assume.That((bool)parked[0] && (bool)parked[1], Is.True, "control: both instances are kept before the low-memory event");

            EngineLowMemory.Raise();
            yield return null;

            Assert.That((bool)parked[0], Is.False, "first root's kept instance alive after Application.lowMemory");
            Assert.That((bool)parked[1], Is.False, "second root's kept instance alive after Application.lowMemory");
        }

        // StageWorld always calls Initialize, so the lazy path needs a root built here: initialise on awake off, never initialised.
        private ViewRoot CreateLazyRoot()
        {
            var eventSystem = Track(new GameObject("TestEventSystem"));
            eventSystem.AddComponent<EventSystem>();

            var rootObject = Track(new GameObject("LazyRoot", typeof(RectTransform)));
            rootObject.SetActive(false);
            rootObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var root = rootObject.AddComponent<ViewRoot>();
            StageWorld.SetField(root, "_initializeOnAwake", false);
            rootObject.SetActive(true);
            return root;
        }

        // A root that is inactive from creation, so Awake has not run; initialise on awake keeps its default.
        private ViewRoot CreateInactiveRoot()
        {
            var eventSystem = Track(new GameObject("TestEventSystem"));
            eventSystem.AddComponent<EventSystem>();

            var rootObject = Track(new GameObject("InactiveRoot", typeof(RectTransform)));
            rootObject.SetActive(false);
            rootObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            return rootObject.AddComponent<ViewRoot>();
        }

        // Opens and closes a KeepInstance view on the world and adds its parked instance to parked.
        private static IEnumerator ParkKeptInstance(StageWorld world, string viewId, List<GameObject> parked)
        {
            world.Row(viewId, world.Template<RecordingView>("Kept"), retention: ViewRetention.KeepInstance);
            var handle = world.Navigator.Open(ViewKey.Create(viewId), world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the kept view to open");
            parked.Add(StageWorld.InstanceOf(handle));
            handle.Close();
            yield return WaitFor(() => !handle.IsAlive, "the kept view to close");
        }

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        private ViewHandle OpenHandleOf(string viewId)
        {
            ViewHandle handle;
            return _world.Navigator.TryGetOpen(viewId, out handle) ? handle : default(ViewHandle);
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
