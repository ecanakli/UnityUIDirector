using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Finalize and terminate share one routine: state first, then isolated notifications in order.
    public sealed class LifecycleFinalizeTests
    {
        private const string Target = LifecycleDriver.Target;

        private NavigatorHarness _h;

        [SetUp]
        public void SetUp()
        {
            _h = new NavigatorHarness();
        }

        [TearDown]
        public void TearDown()
        {
            _h.Dispose();
        }

        // A screen view with the visible hook whose reaction the test sets.
        private sealed class HookedVisibleView : FakeView<NoArgs, NoResult>, IViewVisible
        {
            public Action<ViewScope> OnVisibleHook { get; set; }

            public void OnVisible(ViewScope visible)
            {
                RecordVisible(visible);
                if (OnVisibleHook != null)
                    OnVisibleHook(visible);
            }
        }

        [Test]
        public void Finalize_StateIsInPlace_BeforeAnyUserCodeRuns()
        {
            var phaseAtScopeEnd = ViewPhase.Open;
            var requestsAtScopeEnd = -1;
            var openAtScopeEnd = true;
            var hiddenAtScopeEnd = false;
            var interactiveAtScopeEnd = true;
            var handle = default(ViewHandle);
            _h.Row(Target, ViewLayers.Popups);
            _h.Map(Target, () => new FakeView<NoArgs, bool>
            {
                BindOnOpen = scope => scope.OnEnd(() =>
                {
                    var instance = _h.Stage.LastInstanceOf(Target);
                    phaseAtScopeEnd = handle.Phase;
                    requestsAtScopeEnd = _h.RequestCount(ViewLayers.Popups);
                    openAtScopeEnd = _h.Navigator.IsOpen(Target);
                    hiddenAtScopeEnd = instance.Visual.Hidden;
                    interactiveAtScopeEnd = instance.Visual.Interactive;
                })
            });
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            handle = LifecycleDriver.HandleOf(_h, Target);

            LifecycleDriver.ViewOf(_h, Target).Complete(true);

            Assert.That(task.ResultNow().Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(phaseAtScopeEnd, Is.EqualTo(ViewPhase.Ended), "The record is marked Ended first.");
            Assert.That(requestsAtScopeEnd, Is.Zero, "The record left its layer first.");
            Assert.That(openAtScopeEnd, Is.False);
            Assert.That(hiddenAtScopeEnd, Is.True, "The canvas is disabled first.");
            Assert.That(interactiveAtScopeEnd, Is.False, "The raycaster is disabled first.");
        }

        [Test]
        public void Finalize_NotificationsRunInTheDocumentedOrder()
        {
            var log = new List<string>();
            var releasesAtScopeEnd = -1;
            var releasesAtClosed = -1;
            FakeInstance instance = null;
            FakeVisibleView<NoArgs, bool> view = null;

            _h.Row(Target, ViewLayers.Popups);
            _h.Map(Target, () => view = new FakeVisibleView<NoArgs, bool>
            {
                BindOnOpen = scope =>
                {
                    scope.Token.Register(() => log.Add("open token"));
                    scope.OnEnd(() => log.Add("open entry 1"));
                    scope.OnEnd(() =>
                    {
                        log.Add("open entry 2");
                        releasesAtScopeEnd = instance.ReleaseCount;
                    });
                }
            });
            _h.Navigator.AddObserver(
                new LifecycleActionObserver(e =>
                {
                    if (e.Kind == ViewEventKind.Closed)
                    {
                        log.Add("event Closed");
                        releasesAtClosed = instance.ReleaseCount;
                    }
                    else if (e.Kind == ViewEventKind.Settled)
                    {
                        log.Add("event Settled");
                    }
                }),
                _h.Owner);

            var showTask = LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            instance = _h.Stage.LastInstanceOf(Target);
            var handle = LifecycleDriver.HandleOf(_h, Target);
            var waiter = handle.WaitForCloseAsync(CancellationToken.None);

            view.LastVisible.Token.Register(() => log.Add("visible token"));
            view.LastVisible.OnEnd(() => log.Add("visible entry"));
            showTask.GetAwaiter().OnCompleted(() => log.Add("show awaiter"));
            waiter.GetAwaiter().OnCompleted(() => log.Add("close awaiter"));
            log.Clear();

            view.Complete(true);

            // Visible scope, then open scope (token first, entries newest first), release, Closed, ShowAsync, WaitForCloseAsync, Settled.
            Assert.That(
                log,
                Is.EqualTo(new[]
                {
                    "visible token",
                    "visible entry",
                    "open token",
                    "open entry 2",
                    "open entry 1",
                    "event Closed",
                    "show awaiter",
                    "close awaiter",
                    "event Settled"
                }));
            Assert.That(releasesAtScopeEnd, Is.Zero, "Scopes end before the instance is released.");
            Assert.That(releasesAtClosed, Is.EqualTo(1), "The instance is released before Closed is published.");
        }

        [Test]
        public void Finalize_APopupOwnedByAScreensOpenScope_ReportsClosedBeforeTheScreenDoes()
        {
            _h.Row("s").Map<NoArgs, bool>("s");
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, "p");
            var screenTask = LifecycleDriver.Show(_h, "s", _h.Owner);
            var screen = LifecycleDriver.ViewOf(_h, "s");
            var popupTask = LifecycleDriver.Show(_h, "p", screen.Context.Open.Token);
            Assert.That(LifecycleDriver.HandleOf(_h, "p").Phase, Is.EqualTo(ViewPhase.Open));

            screen.Complete(true);

            Assert.That(screenTask.ResultNow().Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(popupTask.ResultNow().Reason, Is.EqualTo(CloseReason.OwnerEnded));

            var closedPopup = -1;
            var closedScreen = -1;
            var events = _h.Observer.Events;
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Kind != ViewEventKind.Closed)
                    continue;

                if (events[i].ViewId == "p")
                    closedPopup = i;
                else if (events[i].ViewId == "s")
                    closedScreen = i;
            }

            Assert.That(closedPopup, Is.GreaterThanOrEqualTo(0));
            Assert.That(closedScreen, Is.GreaterThanOrEqualTo(0));
            Assert.That(closedPopup, Is.LessThan(closedScreen), "A notification caused by a notification runs before the older pending ones.");
        }

        [Test]
        public void Finalize_ARevealedViewBeginsItsVisibleScope_AfterTheReleaseAndBeforeClosedIsPublished()
        {
            var log = new List<string>();
            FakeInstance topInstance = null;
            _h.Row("a").Row("b");
            _h.Map("a", () => new HookedVisibleView
            {
                OnVisibleHook = scope =>
                {
                    if (topInstance != null)
                        log.Add("a visible, b released " + topInstance.ReleaseCount);
                }
            });
            _h.Navigator.AddObserver(
                new LifecycleActionObserver(e =>
                {
                    if (e.Kind == ViewEventKind.Closed && e.ViewId == "b")
                        log.Add("b closed, released " + topInstance.ReleaseCount);
                }),
                _h.Owner);

            _h.Navigator.Open(ViewKey.Create("a"), _h.Owner);
            var ownerOfB = _h.NewOwner();
            _h.Navigator.Open(ViewKey.Create("b"), ownerOfB.Token);
            topInstance = _h.Stage.LastInstanceOf("b");
            Assert.That(_h.Stage.LastInstanceOf("a").Visual.Hidden, Is.True, "Covered by b.");

            // A terminate cause has no reveal-first step, so the reveal happens inside finalize.
            ownerOfB.Cancel();

            Assert.That(log, Is.EqualTo(new[] { "a visible, b released 1", "b closed, released 1" }));
        }

        [Test]
        public void Finalize_AThrowingScopeCleanup_DoesNotStopTheRestOfFinalize()
        {
            var olderCleanups = 0;
            _h.Row(Target, ViewLayers.Popups);
            _h.Map(Target, () => new FakeView<NoArgs, bool>
            {
                BindOnOpen = scope =>
                {
                    scope.OnEnd(() => olderCleanups++);
                    scope.OnEnd(() => { throw new InvalidOperationException("cleanup"); });
                }
            });
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            var instance = _h.Stage.LastInstanceOf(Target);

            LifecycleDriver.ViewOf(_h, Target).Complete(true);

            var result = task.ResultNow();
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(result.Value, Is.True);
            Assert.That(olderCleanups, Is.EqualTo(1), "The older cleanup still ran.");
            Assert.That(instance.ReleaseCount, Is.EqualTo(1), "The instance is still released.");
            Assert.That(_h.Observer.CountOf(ViewEventKind.Closed), Is.EqualTo(1), "Closed is still published.");
            Assert.That(_h.Errors.Count, Is.EqualTo(1));
            Assert.That(_h.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.ScopeCleanup));
        }

        [Test]
        public void Finalize_AThrowingObserver_DoesNotStopTheAwaitersOrTheRelease()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            var instance = _h.Stage.LastInstanceOf(Target);
            _h.Observer.ThrowOnEvent = new InvalidOperationException("observer");

            LifecycleDriver.ViewOf(_h, Target).Complete(true);

            var result = task.ResultNow();
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(instance.ReleaseCount, Is.EqualTo(1));

            var routed = false;
            for (var i = 0; i < _h.Errors.Count; i++)
            {
                if (_h.Errors[i].Context.Source == ViewErrorSource.Observer)
                    routed = true;
            }

            Assert.That(routed, Is.True, "The observer's exception is routed with the Observer source.");
        }

        [Test]
        public void Finalize_StashesAKeepInstanceRow_InsteadOfReleasingIt()
        {
            _h.Row(Target, ViewLayers.Popups, retention: ViewRetention.KeepInstance).Map<NoArgs, bool>(Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            var instance = _h.Stage.LastInstanceOf(Target);

            LifecycleDriver.ViewOf(_h, Target).Complete(true);

            Assert.That(task.ResultNow().Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(instance.StashCount, Is.EqualTo(1));
            Assert.That(instance.ReleaseCount, Is.Zero, "A kept instance is stashed, not released.");
            Assert.That(instance.Stashed, Is.True);
            _h.Stage.AssertNoLiveInstances();

            // The next open of the same id reuses the kept instance.
            LifecycleDriver.Show(_h, Target, _h.Owner);
            Assert.That(_h.Stage.Instances.Count, Is.EqualTo(1));
            Assert.That(_h.Stage.CountOf(StageCallKind.Acquire), Is.EqualTo(1));
            Assert.That(LifecycleDriver.ViewOf(_h, Target).OpenCount, Is.EqualTo(2));
        }
    }
}
