using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // ViewQueue on the package's own navigator: the layer check at construction, the row layer check at presentation and the hold timer on the navigator clock.
    [TestFixture]
    public sealed class ViewQueueNavigatorTests
    {
        private const string QueueHoldTooLong = "UIDIRECTOR111";
        private const string QueueLayerMismatch = "UIDIRECTOR126";

        private readonly List<CancellationTokenSource> _lifetimes = new List<CancellationTokenSource>();
        private readonly List<ViewCatalog> _catalogs = new List<ViewCatalog>();
        private NavigatorHarness _harness;
        private ManualTicker _ticker;

        [SetUp]
        public void SetUp()
        {
            _ticker = new ManualTicker();
        }

        [TearDown]
        public void TearDown()
        {
            if (_harness != null)
                _harness.MainThread.IsMainThread = true;

            for (var i = 0; i < _lifetimes.Count; i++)
            {
                _lifetimes[i].Cancel();
                _lifetimes[i].Dispose();
            }

            _lifetimes.Clear();

            if (_harness != null)
            {
                _harness.MainThread.Flush();
                _harness.Dispose();
                _harness = null;
            }

            for (var i = 0; i < _catalogs.Count; i++)
                UnityEngine.Object.DestroyImmediate(_catalogs[i]);

            _catalogs.Clear();
        }

        // Layer check

        [TestCase("popup")]
        [TestCase("hud")]
        public void Constructor_WithALayerTheNavigatorDoesNotHave_ThrowsArgumentException(string layerId)
        {
            var harness = CreateHarness();
            var layer = new ViewLayer(layerId, 1, ViewLayerRules.None);
            var lifetime = NewLifetime().Token;

            var exception = Assert.Throws<ArgumentException>(() => { CreateQueue(harness, layer, lifetime, _ticker); });

            Assert.That(exception.ParamName, Is.EqualTo("layer"), "parameter name");
            Assert.That(exception.Message, Does.Contain("'" + layerId + "'"), "the message names the layer id");
        }

        [Test]
        public void Constructor_WithALayerTheNavigatorDoesNotHave_RegistersNothing()
        {
            var harness = CreateHarness();
            var unknown = new ViewLayer("hud", 3, ViewLayerRules.None);
            var lifetime = NewLifetime().Token;
            var observersBefore = harness.Navigator.ObserverCount;

            Assert.Throws<ArgumentException>(() => { CreateQueue(harness, unknown, lifetime, _ticker); });

            Assert.That(harness.Navigator.ObserverCount, Is.EqualTo(observersBefore), "no navigator observer was added");
            Assert.That(harness.Navigator.CopyQueues(new List<ViewQueue>()), Is.Zero, "no queue was listed on the navigator");
        }

        [Test]
        public void Constructor_WithAnExtraLayerTheNavigatorHas_CreatesAnActiveQueue()
        {
            var hud = new ViewLayer("hud", 3, ViewLayerRules.None);
            var harness = CreateHarness(new[] { ViewLayers.Screens, ViewLayers.Popups, ViewLayers.System, hud });
            harness.Row("badge", hud);
            var queue = CreateQueue(harness, hud, NewLifetime().Token, _ticker);
            queue.Enqueue(ViewKey.Create("badge"), default(NoArgs), CancellationToken.None);

            _ticker.Tick();

            Assert.That(CaptureQueue(queue).Inert, Is.False, "inert");
            Assert.That(harness.Navigator.IsOpen("badge"), Is.True, "the queue presented the item on the extra layer");
        }

        [Test]
        public void Constructor_WithAnotherLayerInstanceOfAKnownId_CreatesAnActiveQueue()
        {
            var harness = CreateHarness();
            var popupsCopy = new ViewLayer("popups", ViewLayers.Popups.Order, ViewLayers.Popups.Rules);

            var queue = CreateQueue(harness, popupsCopy, NewLifetime().Token, _ticker);

            Assert.That(CaptureQueue(queue).Inert, Is.False, "a layer is looked up by id, not by instance");
        }

        [Test]
        public void Constructor_AfterTheRootEnded_WithAKnownLayer_GivesAnInertQueueAndDoesNotThrow()
        {
            var harness = CreateHarness();
            var lifetime = NewLifetime().Token;
            harness.Navigator.Teardown();

            ViewQueue queue = null;
            Assert.DoesNotThrow(() => { queue = CreateQueue(harness, ViewLayers.Popups, lifetime, _ticker); }, "a known layer is still found after teardown");

            Assert.That(CaptureQueue(queue).Inert, Is.True, "a queue built after the root ended is inert");
        }

        [Test]
        public void Constructor_AfterTheRootEnded_WithALayerTheNavigatorDoesNotHave_ThrowsArgumentException()
        {
            var harness = CreateHarness();
            var unknown = new ViewLayer("popup", 1, ViewLayerRules.None);
            var lifetime = NewLifetime().Token;
            harness.Navigator.Teardown();

            var exception = Assert.Throws<ArgumentException>(() => { CreateQueue(harness, unknown, lifetime, _ticker); });

            Assert.That(exception.ParamName, Is.EqualTo("layer"), "parameter name");
        }

        // Row layer check at presentation

        [Test]
        public void AQueuedKeyWhoseRowIsOnAnotherLayer_IsTurnedAwayAsNotShownWrongLayer_WithADetailNamingBothLayers()
        {
            var harness = CreateHarness();
            harness.Row("shop", ViewLayers.Screens);
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, _ticker);
            var probe = Watch(queue.EnqueueAsync(ViewKey.Create("shop"), default(NoArgs), CancellationToken.None));

            _ticker.TickUntilIdle();

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "the awaiter completes once");
            Assert.That(probe.Result.WasShown, Is.False, "was shown");
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.NotShown), "close reason");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.WrongLayer), "reject reason");
            Assert.That(probe.Result.Detail, Is.EqualTo(WrongLayerDetail("shop", ViewLayers.Screens, ViewLayers.Popups)), "detail");
        }

        [Test]
        public void AQueuedKeyWhoseRowIsOnAnotherLayer_LogsUIDIRECTOR126OnceAsAnError()
        {
            var harness = CreateHarness();
            harness.Row("shop", ViewLayers.Screens);
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, _ticker);
            queue.Enqueue(ViewKey.Create("shop"), default(NoArgs), CancellationToken.None);

            _ticker.TickUntilIdle();

            Assert.That(harness.Diagnostics.Count(QueueLayerMismatch), Is.EqualTo(1), "one diagnostic. " + harness.Diagnostics.Describe());
            Assert.That(harness.Diagnostics.Entries, Has.Some.Matches<DiagnosticEntry>(entry => entry.Id == QueueLayerMismatch && entry.IsError),
                "logged at error severity. " + harness.Diagnostics.Describe());
        }

        [Test]
        public void AQueuedKeyWhoseRowIsOnAnotherLayer_TakesNoShieldAndPlacesNothing()
        {
            var harness = CreateHarness();
            harness.Row("home", ViewLayers.Screens).Row("shop", ViewLayers.Screens);
            harness.Navigator.Open(ViewKey.Create("home"), harness.Owner);
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, _ticker);
            var probe = Watch(queue.EnqueueAsync(ViewKey.Create("shop"), default(NoArgs), CancellationToken.None));
            var screensBefore = harness.RequestCount(ViewLayers.Screens);
            var shieldsBefore = harness.ShieldCount;
            var shieldRaisesBefore = harness.Stage.ShieldRaiseCount;
            var acquiresBefore = harness.Stage.CountOf(StageCallKind.Acquire);
            var placesBefore = harness.Stage.CountOf(StageCallKind.Place);

            _ticker.TickUntilIdle();

            Assume.That(probe.Result.Rejected, Is.EqualTo(RejectReason.WrongLayer), "control: the item was presented and turned away");
            Assert.That(harness.RequestCount(ViewLayers.Screens), Is.EqualTo(screensBefore), "screens request count");
            Assert.That(harness.ShieldCount, Is.EqualTo(shieldsBefore), "shield holders");
            Assert.That(harness.Stage.ShieldRaiseCount, Is.EqualTo(shieldRaisesBefore), "shield raises");
            Assert.That(harness.Stage.CountOf(StageCallKind.Acquire), Is.EqualTo(acquiresBefore), "acquires. " + harness.Stage.DescribeCalls());
            Assert.That(harness.Stage.CountOf(StageCallKind.Place), Is.EqualTo(placesBefore), "placements. " + harness.Stage.DescribeCalls());
        }

        [Test]
        public void AQueuedKeyWhoseRowIsOnAnotherLayer_PublishesOneNotShownCarryingTheQueuesLayer()
        {
            var harness = CreateHarness();
            harness.Row("shop", ViewLayers.Screens);
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, _ticker);
            queue.Enqueue(ViewKey.Create("shop"), default(NoArgs), CancellationToken.None);

            _ticker.TickUntilIdle();

            Assert.That(harness.Observer.KindsForView("shop"), Is.EqualTo(new[] { ViewEventKind.NotShown }), "a single NotShown and no Requested");
            var notShown = harness.Observer.LastOf(ViewEventKind.NotShown);
            Assert.That(notShown.LayerId, Is.EqualTo(ViewLayers.Popups.Id), "the event carries the queue's layer");
            Assert.That(notShown.Rejected, Is.EqualTo(RejectReason.WrongLayer), "reject reason");
        }

        [Test]
        public void AQueuedKeyWhoseRowIsOnAnotherLayer_EndsAndTheNextItemIsPresented()
        {
            var harness = CreateHarness();
            harness.Row("shop", ViewLayers.Screens).Row("offer", ViewLayers.Popups);
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, _ticker);
            var first = Watch(queue.EnqueueAsync(ViewKey.Create("shop"), default(NoArgs), CancellationToken.None));
            queue.Enqueue(ViewKey.Create("offer"), default(NoArgs), CancellationToken.None);

            _ticker.TickUntilIdle();

            Assume.That(first.Result.Rejected, Is.EqualTo(RejectReason.WrongLayer), "control: the first item was turned away");
            Assert.That(harness.Observer.KindsForView("offer"), Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }), "the next item is presented and shown");
            Assert.That(queue.Count, Is.Zero, "no item waits");
        }

        [Test]
        public void AQueuedKeyWhoseRowIsOnTheQueuesLayer_IsPresented()
        {
            var harness = CreateHarness();
            harness.Row("offer", ViewLayers.Popups);
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, _ticker);
            var probe = Watch(queue.EnqueueAsync(ViewKey.Create("offer"), default(NoArgs), CancellationToken.None));

            _ticker.TickUntilIdle();

            Assert.That(harness.Observer.KindsForView("offer"), Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }), "presented and shown");
            Assert.That(probe.IsPending, Is.True, "the awaiter waits for the view");
            Assert.That(harness.Diagnostics.Count(QueueLayerMismatch), Is.Zero, "no layer mismatch. " + harness.Diagnostics.Describe());
        }

        [Test]
        public void AQueuedKeyWhoseRowMovedToAnotherLayerAfterEnqueue_IsTurnedAwayAtPresentation()
        {
            var harness = CreateHarness();
            var popupsCatalog = AddRemovableRow(harness, "shop", ViewLayers.Popups);
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, _ticker);
            var probe = Watch(queue.EnqueueAsync(ViewKey.Create("shop"), default(NoArgs), CancellationToken.None));
            popupsCatalog.Remove();
            harness.Row("shop", ViewLayers.Screens);

            _ticker.TickUntilIdle();

            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.WrongLayer), "reject reason");
            Assert.That(probe.Result.Detail, Is.EqualTo(WrongLayerDetail("shop", ViewLayers.Screens, ViewLayers.Popups)), "detail");
            Assert.That(harness.Navigator.IsOpen("shop"), Is.False, "nothing was opened on the row's new layer");
        }

        // Hold timer on the navigator clock

        [Test]
        public void Hold_OnThePackageNavigator_SchedulesItsWarningOnTheNavigatorClock()
        {
            var harness = CreateHarness();
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, null);
            var timersBefore = harness.Clock.TimerCount;

            queue.Hold("tutorial", CancellationToken.None);

            Assert.That(harness.Clock.TimerCount, Is.EqualTo(timersBefore + 1));
        }

        [Test]
        public void AHoldOlderThanSixtyClockSeconds_LogsUIDIRECTOR111Once()
        {
            var harness = CreateHarness();
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, null);
            queue.Hold("tutorial", CancellationToken.None);

            TickClock(harness.Clock, 600);
            var afterSixtySeconds = harness.Diagnostics.Count(QueueHoldTooLong);
            TickClock(harness.Clock, 600);

            Assert.That(afterSixtySeconds, Is.EqualTo(1), "one warning after 60 clock seconds. " + harness.Diagnostics.Describe());
            Assert.That(harness.Diagnostics.Count(QueueHoldTooLong), Is.EqualTo(1), "no second warning for the same hold. " + harness.Diagnostics.Describe());
        }

        [Test]
        public void Hold_Removed_RemovesItsTimerFromTheNavigatorClock()
        {
            var harness = CreateHarness();
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, null);
            var timersBefore = harness.Clock.TimerCount;
            var hold = queue.Hold("tutorial", CancellationToken.None);

            hold.Remove();
            var timersAfterRemove = harness.Clock.TimerCount;
            TickClock(harness.Clock, 1200);

            Assert.That(timersAfterRemove, Is.EqualTo(timersBefore), "the removed hold left no clock timer");
            Assert.That(harness.Diagnostics.Count(QueueHoldTooLong), Is.Zero, "a removed hold never warns. " + harness.Diagnostics.Describe());
        }

        [Test]
        public void Hold_WhoseOwnerEnds_RemovesItsTimerFromTheNavigatorClock()
        {
            var harness = CreateHarness();
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, null);
            var owner = harness.NewOwner();
            var timersBefore = harness.Clock.TimerCount;
            queue.Hold("tutorial", owner.Token);

            owner.Cancel();
            var timersAfterOwnerEnd = harness.Clock.TimerCount;
            TickClock(harness.Clock, 1200);

            Assert.That(timersAfterOwnerEnd, Is.EqualTo(timersBefore), "the ended hold left no clock timer");
            Assert.That(harness.Diagnostics.Count(QueueHoldTooLong), Is.Zero, "a hold whose owner ended never warns. " + harness.Diagnostics.Describe());
        }

        [Test]
        public void TwoHolds_RemovingOne_KeepsTheOthersTimerAndTheOtherWarnsOnce()
        {
            var harness = CreateHarness();
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, null);
            var timersBefore = harness.Clock.TimerCount;
            var first = queue.Hold("tutorial", CancellationToken.None);
            queue.Hold("reward", CancellationToken.None);
            Assume.That(harness.Clock.TimerCount, Is.EqualTo(timersBefore + 2), "control: each hold scheduled one clock timer");

            first.Remove();
            var timersAfterRemove = harness.Clock.TimerCount;
            TickClock(harness.Clock, 600);

            Assert.That(timersAfterRemove, Is.EqualTo(timersBefore + 1), "only the removed hold's timer left the clock");
            Assert.That(harness.Diagnostics.Count(QueueHoldTooLong), Is.EqualTo(1), "the remaining hold warns once. " + harness.Diagnostics.Describe());
        }

        [Test]
        public void Hold_WhenTheQueueLifetimeEnds_RemovesItsTimerFromTheNavigatorClock()
        {
            var harness = CreateHarness();
            var lifetime = NewLifetime();
            var queue = CreateQueue(harness, ViewLayers.Popups, lifetime.Token, null);
            var timersBefore = harness.Clock.TimerCount;
            queue.Hold("tutorial", CancellationToken.None);

            lifetime.Cancel();

            Assert.That(harness.Clock.TimerCount, Is.EqualTo(timersBefore), "the queue's end left no clock timer");
        }

        [Test]
        public void Teardown_WithAHeldQueue_LeavesNoClockTimerFaultOrWarning()
        {
            var harness = CreateHarness();
            var queue = CreateQueue(harness, ViewLayers.Popups, NewLifetime().Token, null);
            var hold = queue.Hold("tutorial", CancellationToken.None);

            harness.Navigator.Teardown();
            var timersAfterTeardown = harness.Clock.TimerCount;
            TickClock(harness.Clock, 1200);

            Assert.That(timersAfterTeardown, Is.Zero, "clock timers after teardown");
            Assert.That(harness.Diagnostics.FaultCount, Is.Zero, "the queue ended on a cleared clock without a fault. " + harness.Diagnostics.Describe());
            Assert.That(harness.Diagnostics.Count(QueueHoldTooLong), Is.Zero, "no warning for a hold the root end removed. " + harness.Diagnostics.Describe());
            Assert.That(hold.IsActive, Is.False, "the hold registration ended with the root");
            Assert.That(CaptureQueue(queue).Inert, Is.True, "the queue ended with the root while its lifetime is alive");
        }

        // Helpers

        private NavigatorHarness CreateHarness(IReadOnlyList<ViewLayer> layers = null)
        {
            _harness = new NavigatorHarness(layers: layers);
            return _harness;
        }

        private CancellationTokenSource NewLifetime()
        {
            var lifetime = new CancellationTokenSource();
            _lifetimes.Add(lifetime);
            return lifetime;
        }

        // A null scheduler gives the queue its default scheduler on the navigator clock.
        private static ViewQueue CreateQueue(NavigatorHarness harness, ViewLayer layer, CancellationToken lifetime, IQueueScheduler scheduler)
        {
            return new ViewQueue(
                harness.Navigator,
                layer,
                lifetime,
                "flow",
                scheduler,
                harness.MainThread,
                null,
                null);
        }

        private static QueueState CaptureQueue(ViewQueue queue)
        {
            var state = new QueueState();
            queue.CaptureState(state);
            return state;
        }

        // Adds one row in its own catalog, so the test can remove it while the harness lives.
        private ViewRegistration AddRemovableRow(NavigatorHarness harness, string id, ViewLayer layer)
        {
            var row = new ViewEntry(id, null, id, layer.Id, ViewRetention.ReleaseOnClose, BackPolicy.LayerDefault, false,
                CoverPolicy.LayerDefault, CoveredMode.DisableCanvas, false);
            var catalog = ViewCatalog.Create(new[] { row });
            _catalogs.Add(catalog);
            harness.Map<NoArgs, NoResult>(id);
            return harness.Navigator.AddCatalog(catalog, CancellationToken.None);
        }

        private static string WrongLayerDetail(string viewId, ViewLayer rowLayer, ViewLayer queueLayer)
        {
            return "The row '" + viewId + "' is on layer '" + rowLayer.Id + "', not on the queue's layer '" + queueLayer.Id + "'.";
        }

        private static ResultProbe<TResult> Watch<TResult>(UniTask<ViewResult<TResult>> task)
        {
            var probe = new ResultProbe<TResult>();
            AwaitInto(task, probe).Forget();
            return probe;
        }

        private static async UniTaskVoid AwaitInto<TResult>(UniTask<ViewResult<TResult>> task, ResultProbe<TResult> probe)
        {
            var result = await task;
            probe.Result = result;
            probe.ResumeCount++;
        }

        // One tick per root Update at the clock's largest step.
        private static void TickClock(NavigatorClock clock, int ticks)
        {
            for (var i = 0; i < ticks; i++)
                clock.Tick(NavigatorClock.MaxStepSeconds);
        }
    }
}
