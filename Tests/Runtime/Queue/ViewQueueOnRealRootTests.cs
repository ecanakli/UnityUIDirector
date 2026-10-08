using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // ViewQueue through its public constructor on a real ViewRoot, with the production scheduler.
    [TestFixture]
    public sealed class ViewQueueOnRealRootTests
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 1f;
        private const int RootGoneFrameBudget = 3;

        // Long enough for several queue ticks, to show that nothing is presented.
        private const int QuietWindowFrames = 10;

        private StageWorld _world;
        private CancellationTokenSource _lifetime;

        [SetUp]
        public void SetUp()
        {
            _lifetime = new CancellationTokenSource();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();

            if (_world != null)
            {
                _world.Dispose();
                _world = null;
            }

            // Destroy is deferred; the next test must not see these objects.
            yield return null;
        }

        [UnityTest]
        public IEnumerator Enqueue_TwoItemsInOneFrame_PresentByPriority()
        {
            var world = CreateWorld("low", "high");
            var queue = CreateQueue(world);

            queue.Enqueue(ViewKey.Create("low"), default(NoArgs), world.Owner, new QueueOptions { Priority = 0 });
            queue.Enqueue(ViewKey.Create("high"), default(NoArgs), world.Owner, new QueueOptions { Priority = 10 });
            yield return WaitFor(() => world.Navigator.IsOpen("high") || world.Navigator.IsOpen("low"), "the queue to present an item");

            Assert.That(world.Navigator.IsOpen("high"), Is.True, "the higher priority item is presented");
            Assert.That(world.Navigator.IsOpen("low"), Is.False, "the lower priority item is not presented yet");
            Assert.That(queue.Count, Is.EqualTo(1), "items still waiting");
        }

        [UnityTest]
        public IEnumerator Enqueue_OnAnEmptyLayer_PresentsOnALaterFrame_NotInTheEnqueueCall()
        {
            var world = CreateWorld("popup");
            var queue = CreateQueue(world);
            yield return WaitFor(() => world.Navigator.IsLayerClear(ViewLayers.Popups), "the popup layer to be clear");

            queue.Enqueue(ViewKey.Create("popup"), default(NoArgs), world.Owner);
            var openInTheCall = world.Navigator.IsOpen("popup");
            yield return WaitFor(() => world.Navigator.IsOpen("popup"), "the queue to present the popup");

            Assert.That(openInTheCall, Is.False, "presented inside the Enqueue call");
        }

        [UnityTest]
        public IEnumerator Hold_BlocksPresentationUntilRemoved()
        {
            var world = CreateWorld("popup");
            var queue = CreateQueue(world);
            var hold = queue.Hold("test hold", world.Owner);

            queue.Enqueue(ViewKey.Create("popup"), default(NoArgs), world.Owner);
            yield return WaitFrames(QuietWindowFrames);
            var openWhileHeld = world.Navigator.IsOpen("popup");
            var waitingWhileHeld = queue.Count;

            hold.Remove();
            yield return WaitFor(() => world.Navigator.IsOpen("popup"), "the popup after the hold was removed");

            Assert.That(openWhileHeld, Is.False, "presented while held");
            Assert.That(waitingWhileHeld, Is.EqualTo(1), "items waiting while held");
            Assert.That(queue.IsHeld, Is.False, "held after Remove");
        }

        [UnityTest]
        public IEnumerator Gate_DenyAtPresentation_EndsTheItemDenied()
        {
            var world = CreateWorld("popup");
            var queue = CreateQueue(world);
            queue.AddGate(new DenyAtPresentationGate("denied at presentation"), world.Owner);

            var result = queue.EnqueueAsync(ViewKey.Create("popup"), default(NoArgs), world.Owner);
            yield return WaitFor(() => !result.IsPending(), "the item to end");

            var actual = result.ResultNow();
            Assert.That(actual.WasShown, Is.False, "WasShown");
            Assert.That(actual.Rejected, Is.EqualTo(RejectReason.Denied), "reject reason");
            Assert.That(actual.Detail, Is.EqualTo("denied at presentation"), "detail");
            Assert.That(world.Navigator.IsOpen("popup"), Is.False, "the popup was opened");
        }

        [UnityTest]
        public IEnumerator LifetimeEnds_WaitingItemsEndNotShownCancelled()
        {
            var world = CreateWorld("popup");
            var queue = CreateQueue(world);
            queue.Hold("keeps the item waiting", world.Owner);
            var result = queue.EnqueueAsync(ViewKey.Create("popup"), default(NoArgs), world.Owner);
            yield return null;
            Assume.That(result.IsPending(), Is.True, "control: the item waits behind the hold");

            _lifetime.Cancel();

            var actual = result.ResultNow();
            Assert.That(actual.WasShown, Is.False, "WasShown");
            Assert.That(actual.Rejected, Is.EqualTo(RejectReason.Cancelled), "reject reason");
            Assert.That(actual.Detail, Is.EqualTo("The queue's lifetime ended."), "detail");
            Assert.That(queue.Count, Is.Zero, "items still waiting");
        }

        [UnityTest]
        public IEnumerator DestroyingTheRoot_EndsAWaitingItemOfAQueueWithAnIndependentLifetime()
        {
            var world = CreateWorld("blocker", "popup");
            var blocker = world.Navigator.Open(ViewKey.Create("blocker"), world.Owner);
            yield return WaitFor(() => blocker.Phase == ViewPhase.Open, "the blocking popup to open");
            var queue = CreateQueue(world);
            var events = new RecordingObserver();
            queue.AddObserver(events, world.Owner);
            var waiting = queue.EnqueueAsync(ViewKey.Create("popup"), default(NoArgs), world.Owner);
            yield return null;
            Assume.That(waiting.IsPending(), Is.True, "control: the item waits behind the open popup");

            Object.Destroy(world.Root.gameObject);
            for (var frame = 0; frame < RootGoneFrameBudget && waiting.IsPending(); frame++)
                yield return null;

            Assert.That(waiting.IsPending(), Is.False, "the waiting item ended within " + RootGoneFrameBudget + " frames");
            var actual = waiting.ResultNow();
            Assert.That(actual.WasShown, Is.False, "WasShown");
            Assert.That(actual.Rejected, Is.EqualTo(RejectReason.RootGone), "reject reason");
            Assert.That(actual.Detail, Is.EqualTo("The root was destroyed."), "detail");
            Assert.That(events.KindsForView("popup"), Is.EqualTo(new[] { ViewEventKind.Enqueued, ViewEventKind.NotShown }), "queue events of the item");

            var later = queue.EnqueueAsync(ViewKey.Create("popup"), default(NoArgs), world.Owner);

            Assert.That(later.IsPending(), Is.False, "a later EnqueueAsync completes in the call");
            Assert.That(later.ResultNow().Rejected, Is.EqualTo(RejectReason.RootGone), "later reject reason");
            LogAssert.NoUnexpectedReceived();
        }

        // One popup row per id, all on the popup layer.
        private StageWorld CreateWorld(params string[] popupIds)
        {
            _world = new StageWorld();
            for (var i = 0; i < popupIds.Length; i++)
                _world.Row(popupIds[i], _world.Template<RecordingView>(popupIds[i]), ViewLayers.Popups);

            return _world;
        }

        private ViewQueue CreateQueue(StageWorld world)
        {
            return new ViewQueue(world.Navigator, ViewLayers.Popups, _lifetime.Token);
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

        private static IEnumerator WaitFrames(int frames)
        {
            for (var i = 0; i < frames; i++)
                yield return null;
        }

        // Allows at enqueue and denies right before presentation.
        private sealed class DenyAtPresentationGate : IViewGate
        {
            private readonly string _reason;

            public DenyAtPresentationGate(string reason)
            {
                _reason = reason;
            }

            public GateDecision Evaluate(in GateRequest request)
            {
                return request.AtPresentation ? GateDecision.Deny(_reason) : GateDecision.Allow;
            }
        }
    }
}
