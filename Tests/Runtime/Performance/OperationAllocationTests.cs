using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // Operation cost: an observer dispatch and a timed-wait frame allocate 0 B once warm; an open and close stays under a byte budget.
    [TestFixture]
    public sealed class OperationAllocationTests
    {
        private const int ObserverCount = 4;
        private const int WarmFrames = 5;
        private const int MeasuredFrames = 30;
        private const string PlainId = "plain";
        private const string RemoteId = "remote";
        private const string RemoteKey = "remote-key";

        // The clock advances at most 0.1 s per frame, so neither timer can fire within the frames these tests run.
        private const float LongTimerSeconds = 60f;

        private const int WarmCycles = 5;

        // Lead-in frames before the measured rounds, so no reading can still belong to the counted cycle.
        private const int SkippedReadings = 2;

        // Each round is one idle frame, one open-and-close frame and one probe frame, so editor drift hits all three alike.
        private const int FloorRounds = 20;
        private const int IdleFrame = 0;
        private const int CycleFrame = 1;
        private const int ProbeFrame = 2;
        private const int FrameKinds = 3;

        // Warm-up, the counted cycle and one cycle per round.
        private const int TotalCycles = WarmCycles + 1 + FloorRounds;

        // A known allocation that proves the byte counter reads the frame it is sampled for.
        private const int ProbeBytes = 32 * 1024;
        private const long ProbeToleranceBytes = 8192;

        // Provisional ceiling; tighten it to the measured value plus headroom after the first recorded run.
        private const long OpenCloseBudgetBytes = 8192;

        private const string FrameBytesCounter = "GC Allocated In Frame";

        private readonly List<ViewRegistration> _registrations = new List<ViewRegistration>();

        private StageWorld _world;
        private UpdateAllocationCounter _counter;
        private ProfilerRecorder _frameBytes;
        private bool _frameBytesStarted;

        // Written by RecordFrameFloors, indexed by frame kind.
        private readonly long[] _floorBytes = new long[FrameKinds];
        private int _unfinishedCycles;
        private byte[] _probeSink;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_counter != null)
            {
                _counter.Dispose();
                _counter = null;
            }

            if (_frameBytesStarted)
            {
                _frameBytes.Dispose();
                _frameBytesStarted = false;
            }

            for (var i = 0; i < _registrations.Count; i++)
                _registrations[i].Remove();
            _registrations.Clear();

            // The fixture instance is shared by its tests.
            Array.Clear(_floorBytes, 0, FrameKinds);
            _unfinishedCycles = 0;
            _probeSink = null;

            if (_world == null)
                yield break;

            _world.Dispose();
            _world = null;

            // Destroy is deferred; the next test must not see this world's objects.
            yield return null;
        }

        // The observer list the navigator publishes through, built bare so exactly four observers listen.
        [Test]
        public void Publish_OneEventToFourObserversWhenWarm_AllocatesNothing()
        {
            var observerList = new ObserverList(new ErrorRouter(null, new Diagnostics()));
            var counters = AddCountingObservers(observerList, ObserverCount);
            var viewEvent = ClosedEvent();
            observerList.Publish(in viewEvent);
            Assert.That(counters, Has.All.Matches<CountingObserver>(counter => counter.Count == 1), "control: the warm-up dispatch reached all four observers");

            TestDelegate publish = () => { observerList.Publish(in viewEvent); };

            Assert.That(publish, Is.Not.AllocatingGCMemory());
            Assert.That(counters, Has.All.Matches<CountingObserver>(counter => counter.Count == 2), "the measured dispatch reached all four observers");
            LogAssert.NoUnexpectedReceived();
        }

        // A timed wait: a load the provider has not finished, under the load timeout and the indicator delay, while the root clock ticks.
        [UnityTest]
        public IEnumerator TimedWaitFrame_LoadPendingUnderTimeoutAndIndicatorDelay_AllocatesNothing()
        {
            var provider = new PendingViewProvider();
            _world = new StageWorld(WithLongTimers, new ViewRootSetup { AssetProvider = provider });
            _world.Row(RemoteId, null, assetKey: RemoteKey);
            var handle = _world.Navigator.Open(ViewKey.Create(RemoteId), _world.Owner);
            for (var i = 0; i < WarmFrames; i++)
                yield return null;

            Assert.That(provider.AcquireCount, Is.EqualTo(1), "control: the provider was asked once");
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Loading), "control: the request waits on the provider");
            Assert.That(_world.Core.Clock.TimerCount, Is.EqualTo(2), "control: the load timeout and the indicator delay are pending");

            // The EventSystem is not the package's cost.
            _world.EventSystemObject.SetActive(false);
            var clockBefore = _world.Core.Clock.Now;

            _counter = new UpdateAllocationCounter();
            for (var i = 0; i < MeasuredFrames; i++)
                yield return null;
            _counter.Dispose();

            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Loading), "control: the request still waited after the measured frames");
            Assert.That(_world.Core.Clock.TimerCount, Is.EqualTo(2), "control: no timer fired during the measured frames");
            Assert.That(_world.Core.Clock.Now, Is.GreaterThan(clockBefore), "control: the root clock ticked during the measured frames");
            Assume.That(_counter.Frames, Is.GreaterThan(0), "frames measured; the player loop bracket did not run");
            Assert.That(_counter.Allocations, Is.Zero, "GC.Alloc samples across " + _counter.Frames + " timed-wait frames");
            LogAssert.NoUnexpectedReceived();
        }

        // Bytes are the lowest open-and-close frame minus the lowest idle frame; a known probe allocation proves the counter reads the sampled frame.
        [UnityTest]
        public IEnumerator OpenAndClose_PlainScreenWhenWarm_StaysUnderByteBudget()
        {
            _world = new StageWorld();
            _world.Row(PlainId, _world.Template<RecordingView>("Plain"));
            var observer = new CountingObserver();
            _registrations.Add(_world.Navigator.AddObserver(observer, _world.Owner));
            var key = ViewKey.Create(PlainId);
            _frameBytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, FrameBytesCounter);
            _frameBytesStarted = true;
            Assume.That(_frameBytes.Valid, Is.True, "control: the '" + FrameBytesCounter + "' counter is available in this editor");

            // Pools, lists and JIT reach their steady state.
            yield return WarmUp(key);

            // The EventSystem is not the package's cost.
            _world.EventSystemObject.SetActive(false);
            var allocationsPerCycle = CountAllocationsOfOneCycle(key);
            yield return null;
            yield return RecordFrameFloors(key);
            var idleFloor = _floorBytes[IdleFrame];
            var cycleFloor = _floorBytes[CycleFrame];
            var probeFloor = _floorBytes[ProbeFrame];
            var probeSeen = probeFloor - idleFloor;
            var bytesPerCycle = Math.Max(0L, cycleFloor - idleFloor);

            Assert.That(_unfinishedCycles, Is.Zero, "control: every open and close finished inside its frame");
            Assert.That(observer.Closed, Is.EqualTo(TotalCycles), "control: every cycle showed and closed the view");
            Assume.That(probeSeen, Is.GreaterThanOrEqualTo(ProbeBytes), "control: the '" + FrameBytesCounter + "' counter saw the known " + _probeSink.Length + " B allocation in its frame; probe floor " + probeFloor + " B, idle floor " + idleFloor + " B");
            Assume.That(probeSeen, Is.LessThan(ProbeBytes + ProbeToleranceBytes), "control: the '" + FrameBytesCounter + "' counter read the known " + _probeSink.Length + " B allocation without unrelated bytes; probe floor " + probeFloor + " B, idle floor " + idleFloor + " B");
            Assert.That(allocationsPerCycle, Is.GreaterThan(0), "control: the GC.Alloc recorder saw the counted open and close");
            Assert.That(cycleFloor, Is.GreaterThan(idleFloor), "control: the counter saw the open and close; cycle floor " + cycleFloor + " B, idle floor " + idleFloor + " B");
            // Checked before the measurement lines, which are this test's own output.
            LogAssert.NoUnexpectedReceived();
            Debug.Log("[MEASURE M4] bytes_per_open_close=" + bytesPerCycle);
            Debug.Log("[MEASURE M4] allocations_per_open_close=" + allocationsPerCycle);
            Assert.That(bytesPerCycle, Is.LessThan(OpenCloseBudgetBytes), "bytes per warm open and close; cycle floor " + cycleFloor + " B, idle floor " + idleFloor + " B, " + allocationsPerCycle + " allocations");
        }

        // Helpers

        private static CountingObserver[] AddCountingObservers(ObserverList observerList, int count)
        {
            var counters = new CountingObserver[count];
            for (var i = 0; i < count; i++)
            {
                counters[i] = new CountingObserver();
                observerList.Add(counters[i]);
            }

            return counters;
        }

        // Shaped as the navigator publishes a closed view.
        private static ViewEvent ClosedEvent()
        {
            return new ViewEvent(ViewEventKind.Closed, 1, PlainId, ViewLayers.Screens.Id, null, 0, CloseReason.Code, RejectReason.None, null, 0f, 1f);
        }

        private static void WithLongTimers(ViewRoot root)
        {
            StageWorld.SetField(root, "_loadTimeoutSeconds", LongTimerSeconds);
            StageWorld.SetField(root, "_indicatorDelaySeconds", LongTimerSeconds);
        }

        // One cycle per frame, so each instance is destroyed before the next opens.
        private IEnumerator WarmUp(ViewKey<NoArgs, NoResult> key)
        {
            for (var i = 0; i < WarmCycles; i++)
            {
                if (!OpenAndClose(key))
                    _unfinishedCycles++;

                yield return null;
            }
        }

        // Opens and closes the plain screen; true when the request ended inside this call.
        private bool OpenAndClose(ViewKey<NoArgs, NoResult> key)
        {
            var handle = _world.Navigator.Open(key, _world.Owner);
            handle.Close();
            return !handle.IsAlive;
        }

        // GC.Alloc samples of one synchronous open and close, bracketed on this thread as Is.Not.AllocatingGCMemory does.
        private int CountAllocationsOfOneCycle(ViewKey<NoArgs, NoResult> key)
        {
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                if (!OpenAndClose(key))
                    _unfinishedCycles++;
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }

            return recorder.sampleBlockCount;
        }

        // The lowest per-frame GC byte count of each frame kind; the floor drops editor repaints and other spikes.
        private IEnumerator RecordFrameFloors(ViewKey<NoArgs, NoResult> key)
        {
            for (var kind = 0; kind < FrameKinds; kind++)
                _floorBytes[kind] = long.MaxValue;

            for (var i = 0; i < SkippedReadings; i++)
                yield return null;

            for (var round = 0; round < FloorRounds; round++)
            {
                for (var kind = 0; kind < FrameKinds; kind++)
                {
                    if (kind == CycleFrame)
                    {
                        if (!OpenAndClose(key))
                            _unfinishedCycles++;
                    }
                    else if (kind == ProbeFrame)
                    {
                        _probeSink = new byte[ProbeBytes];
                    }

                    // LastValue is the frame that just ended.
                    yield return null;
                    _floorBytes[kind] = Math.Min(_floorBytes[kind], _frameBytes.LastValue);
                }
            }
        }

        // Counts events; allocates nothing per event.
        private sealed class CountingObserver : IViewObserver
        {
            public int Count;
            public int Closed;

            public void OnViewEvent(in ViewEvent viewEvent)
            {
                Count++;
                if (viewEvent.Kind == ViewEventKind.Closed)
                    Closed++;
            }
        }

        // A provider whose loads never finish; each ends when its token is cancelled.
        private sealed class PendingViewProvider : IViewProvider
        {
            public int AcquireCount;

            public UniTask<GameObject> AcquireAsync(string assetKey, Transform inactiveParent, CancellationToken ct)
            {
                AcquireCount++;
                return UniTask.Never<GameObject>(ct);
            }

            public void Release(GameObject instance)
            {
                if (instance != null)
                    Object.Destroy(instance);
            }

            public UniTask<IDisposable> PreloadAsync(string assetKey, CancellationToken ct)
            {
                return UniTask.Never<IDisposable>(ct);
            }
        }
    }
}
