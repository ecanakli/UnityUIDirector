using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.UIDirector.Tests
{
    // Idle cost: the queries and an idle frame allocate 0 B once warm.
    [TestFixture]
    public sealed class IdleAllocationTests
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 5f;
        private const int WarmFrames = 5;
        private const int MeasuredFrames = 30;
        private const int MeasuredTransitionFrames = 10;

        // Long enough that the measured frames all fall inside the show transition.
        private const float FadeSeconds = 1f;

        private StageWorld _world;
        private UpdateAllocationCounter _counter;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_counter != null)
            {
                _counter.Dispose();
                _counter = null;
            }

            if (_world == null)
                yield break;

            _world.Dispose();
            _world = null;

            // Destroy is deferred; the next test must not see this world's objects.
            yield return null;
        }

        [UnityTest]
        public IEnumerator Back_NothingToDo_AllocatesNothing()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            yield return OpenSettled("screen");
            var navigator = _world.Navigator;
            Assert.That(navigator.Back(), Is.False, "control: Back() at the root screen has nothing to do");

            TestDelegate back = () => { navigator.Back(); };

            Assert.That(back, Is.Not.AllocatingGCMemory());
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Back_SwallowedByInputHold_AllocatesNothing()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            yield return OpenSettled("screen");
            var navigator = _world.Navigator;
            navigator.HoldInput("test hold", _world.Owner);
            Assert.That(navigator.Back(), Is.True, "control: Back() is swallowed while the shield is up");

            TestDelegate back = () => { navigator.Back(); };

            Assert.That(back, Is.Not.AllocatingGCMemory());
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator IsOpen_OpenScreen_AllocatesNothing()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            yield return OpenSettled("screen");
            var navigator = _world.Navigator;
            Assert.That(navigator.IsOpen("screen"), Is.True, "control: the screen is open");

            TestDelegate isOpen = () => { navigator.IsOpen("screen"); };

            // Warm first: the first run interns the delegate's string literal, which is the test's allocation, not the package's.
            isOpen();
            Assert.That(isOpen, Is.Not.AllocatingGCMemory());
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator IsBusy_Settled_AllocatesNothing()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            yield return OpenSettled("screen");
            var navigator = _world.Navigator;
            Assert.That(navigator.IsBusy, Is.False, "control: the root is settled");

            TestDelegate isBusy = () => { _ = navigator.IsBusy; };

            Assert.That(isBusy, Is.Not.AllocatingGCMemory());
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator IsLayerClear_PopupsOverOpenScreen_AllocatesNothing()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            yield return OpenSettled("screen");
            var navigator = _world.Navigator;
            Assert.That(navigator.IsLayerClear(ViewLayers.Popups), Is.True, "control: the popup layer is clear");

            TestDelegate isLayerClear = () => { navigator.IsLayerClear(ViewLayers.Popups); };

            Assert.That(isLayerClear, Is.Not.AllocatingGCMemory());
            LogAssert.NoUnexpectedReceived();
        }

        // Measures the root clock tick, the BackInput poll and the registered fitter, frame by frame.
        [UnityTest]
        public IEnumerator IdleFrame_ScreenOpenWithBackInputAndFitter_AllocatesNothing()
        {
            _world = new StageWorld();
            _world.Root.gameObject.AddComponent<BackInput>();
            var fitterObject = new GameObject("Fitter", typeof(RectTransform));
            fitterObject.transform.SetParent(_world.Root.transform, false);
            fitterObject.AddComponent<SafeAreaFitter>();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            yield return OpenSettled("screen");

            // The EventSystem is not the package's idle cost.
            _world.EventSystemObject.SetActive(false);
            for (var i = 0; i < WarmFrames; i++)
                yield return null;

            var clockBefore = _world.Core.Clock.Now;
            _counter = new UpdateAllocationCounter();
            for (var i = 0; i < MeasuredFrames; i++)
                yield return null;
            _counter.Dispose();

            Assert.That(_world.Core.Clock.Now, Is.GreaterThan(clockBefore), "control: the root clock ticked during the measured frames");
            Assert.That(_world.Navigator.IsOpen("screen"), Is.True, "control: the screen stayed open through the measured frames");
            Assert.That(_world.Navigator.IsBusy, Is.False, "control: the root stayed settled through the measured frames");
            Assume.That(_counter.Frames, Is.GreaterThan(0), "frames measured; the player loop bracket did not run");
            Assert.That(_counter.Allocations, Is.Zero, "GC.Alloc samples across " + _counter.Frames + " idle frames");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TransitionFrame_FadeRunningWhenWarm_AllocatesNothing()
        {
            _world = new StageWorld();
            _world.Row("faded", _world.Template<RecordingView>("Faded", WithFade));

            // One full show and hide first, so the async machinery and its pools are warm.
            var warm = _world.Navigator.Open(ViewKey.Create("faded"), _world.Owner);
            yield return WaitFor(() => warm.Phase == ViewPhase.Open && !_world.Navigator.IsBusy, "the warm-up open to finish");
            warm.Close();
            yield return WaitFor(() => warm.Phase == ViewPhase.Ended && !_world.Navigator.IsBusy, "the warm-up close to finish");

            // The EventSystem is not the package's cost.
            _world.EventSystemObject.SetActive(false);
            var handle = _world.Navigator.Open(ViewKey.Create("faded"), _world.Owner);
            yield return null;
            yield return null;
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Opening), "control: the show transition is running");

            _counter = new UpdateAllocationCounter();
            for (var i = 0; i < MeasuredTransitionFrames; i++)
                yield return null;
            _counter.Dispose();

            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Opening), "control: the show transition ran through every measured frame");
            Assume.That(_counter.Frames, Is.GreaterThan(0), "frames measured; the player loop bracket did not run");
            Assert.That(_counter.Allocations, Is.Zero, "GC.Alloc samples across " + _counter.Frames + " transition frames");
            LogAssert.NoUnexpectedReceived();
        }

        // Helpers

        private static void WithFade(GameObject view)
        {
            var fade = view.AddComponent<FadeTransition>();
            StageWorld.SetField(fade, "_duration", FadeSeconds);
            StageWorld.SetField(view.GetComponent<ViewHost>(), "_transition", fade);
        }

        // Open, nothing running and the shield down.
        private IEnumerator OpenSettled(string viewId)
        {
            var handle = _world.Navigator.Open(ViewKey.Create(viewId), _world.Owner);
            yield return WaitFor(
                () => handle.Phase == ViewPhase.Open && !_world.Navigator.IsBusy && !_world.Stage.Shield.raycastTarget,
                viewId + " to open and the root to settle");

            for (var i = 0; i < WarmFrames; i++)
                yield return null;
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
