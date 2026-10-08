using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // What happens in what order when one view closes.
    public sealed class CloseOrderContractTests
    {
        private NavigatorHarness _harness;

        [SetUp]
        public void SetUp()
        {
            _harness = new NavigatorHarness();
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        [Test]
        public void Close_RunsScopesEnd_ThenRelease_ThenClosed_ThenShowAwaiters_ThenWaitAwaiters_ThenSettled()
        {
            var timeline = new List<string>();
            var releasedAt = new Dictionary<string, int>();

            var run = ContractShownRun.Start(_harness, view => view.BindOnOpen = scope => scope.OnEnd(() =>
            {
                timeline.Add("open scope end");
                releasedAt["open scope end"] = Releases();
            }));
            run.View.LastVisible.OnEnd(() =>
            {
                timeline.Add("visible scope end");
                releasedAt["visible scope end"] = Releases();
            });

            _harness.Navigator.AddObserver(
                new ContractActionObserver(viewEvent =>
                {
                    if (viewEvent.Kind != ViewEventKind.Closed && viewEvent.Kind != ViewEventKind.Settled)
                        return;

                    var name = "event " + viewEvent.Kind;
                    timeline.Add(name);
                    releasedAt[name] = Releases();
                }),
                _harness.Owner);

            ViewHandle handle;
            Assert.That(_harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle), Is.True);

            ContractAwait.Watch(run.Result, () =>
            {
                timeline.Add("show awaiter");
                releasedAt["show awaiter"] = Releases();
            });
            ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None), () => timeline.Add("wait awaiter"));

            run.View.Complete(default(NoResult));

            Assert.That(
                timeline,
                Is.EqualTo(new[]
                {
                    "visible scope end",
                    "open scope end",
                    "event Closed",
                    "show awaiter",
                    "wait awaiter",
                    "event Settled"
                }),
                "Close order: scopes end, instance released, Closed, ShowAsync awaiters, WaitForCloseAsync awaiters, Settled.");
            Assert.That(releasedAt["visible scope end"], Is.Zero, "The instance is released after the scopes ended.");
            Assert.That(releasedAt["open scope end"], Is.Zero, "The instance is released after the scopes ended.");
            Assert.That(releasedAt["event Closed"], Is.EqualTo(1), "The instance is released before Closed is published.");
            Assert.That(releasedAt["show awaiter"], Is.EqualTo(1));
        }

        [Test]
        public void ShowAsyncAwaiters_ResumeBeforeWaitForCloseAwaiters_WhateverTheRegistrationOrder()
        {
            _harness.Row("home");
            var order = new List<string>();
            var handle = _harness.Navigator.OpenView("home", _harness.Owner);

            ContractAwait.Watch(handle.WaitForCloseAsync(CancellationToken.None), () => order.Add("wait"));
            ContractAwait.Watch(_harness.Navigator.Show("home", _harness.Owner), () => order.Add("show 1"));
            ContractAwait.Watch(_harness.Navigator.Show("home", _harness.Owner), () => order.Add("show 2"));

            handle.Close();

            Assert.That(order, Is.EqualTo(new[] { "show 1", "show 2", "wait" }));
        }

        [Test]
        public void RevealedView_GetsOnVisible_BeforeClosedIsPublished_WhenTheCoveringViewCloses()
        {
            var timeline = new List<string>();
            var view = OpenCoveredScreen(timeline, out var coverRun);

            coverRun.View.Complete(default(NoResult));

            var reveal = timeline.IndexOf("a visible");
            var closed = timeline.IndexOf("event Closed b");
            Assert.That(reveal, Is.GreaterThanOrEqualTo(0), "The covered screen must get OnVisible again.");
            Assert.That(closed, Is.GreaterThanOrEqualTo(0));
            Assert.That(reveal, Is.LessThan(closed), "Revealed views get OnVisible before Closed is published.");
            Assert.That(view.VisibleCount, Is.EqualTo(2), "Covered, then revealed: two visible spans.");
        }

        [Test]
        public void WhenAViewIsTerminated_ItsScopesEndAndItIsReleased_BeforeTheRevealedViewGetsOnVisible_AndBeforeClosed()
        {
            var timeline = new List<string>();
            var releasedAtReveal = -1;
            var view = OpenCoveredScreen(
                timeline,
                out var coverRun,
                () => releasedAtReveal = _harness.Stage.LastInstanceOf("b").ReleaseCount);

            coverRun.Owner.Cancel();

            Assert.That(
                timeline,
                Is.EqualTo(new[]
                {
                    "b visible scope end",
                    "b open scope end",
                    "a visible",
                    "event Closed b",
                    "b show awaiter"
                }),
                "Terminate order: scopes end, release, revealed OnVisible, Closed, awaiters.");
            Assert.That(releasedAtReveal, Is.EqualTo(1), "The instance of the closing view is released before the revealed view gets OnVisible.");
            Assert.That(view.VisibleCount, Is.EqualTo(2));
        }

        [Test]
        public void PopupOwnedByAScreensOpenScope_ReportsClosedBeforeTheScreen_AndItsAwaiterResumesFirst()
        {
            var timeline = new List<string>();
            _harness.Row("screen").Row("popup", ViewLayers.Popups);
            var screen = new ContractView();
            var popup = new ContractView();
            _harness.Map("screen", () => screen).Map("popup", () => popup);

            var screenTask = _harness.Navigator.Show("screen", _harness.Owner);
            ContractAwait.Watch(screenTask, () => timeline.Add("screen awaiter"));

            // The popup's owner is the screen's open scope token.
            var popupTask = _harness.Navigator.Show("popup", screen.Context.Open.Token);
            ContractAwait.Watch(popupTask, () => timeline.Add("popup awaiter"));

            _harness.Navigator.AddObserver(
                new ContractActionObserver(viewEvent =>
                {
                    if (viewEvent.Kind == ViewEventKind.Closed)
                        timeline.Add("closed " + viewEvent.ViewId);
                }),
                _harness.Owner);

            screen.Close();

            Assert.That(
                timeline,
                Is.EqualTo(new[] { "closed popup", "popup awaiter", "closed screen", "screen awaiter" }),
                "Notifications caused by a notification run right after it, before older pending ones.");
            Assert.That(popupTask.ResultNow().Reason, Is.EqualTo(CloseReason.OwnerEnded));
            Assert.That(screenTask.ResultNow().Reason, Is.EqualTo(CloseReason.Dismissed));
        }

        [Test]
        public void AContinuationThatOpensAView_RunsBeforeSettled_AndThatViewPresentsBeforeAnythingElse()
        {
            _harness.Row("first", ViewLayers.Popups).Row("follow-up", ViewLayers.Popups);
            var first = new ContractView();
            _harness.Map("first", () => first);

            var task = _harness.Navigator.Show("first", _harness.Owner);
            var followUp = default(ViewHandle);
            var settledAtContinuation = -1;
            ContractAwait.Watch(task, () =>
            {
                settledAtContinuation = _harness.Observer.CountOf(ViewEventKind.Settled);
                followUp = _harness.Navigator.OpenView("follow-up", _harness.Owner);
            });

            first.Complete(default(NoResult));

            Assert.That(settledAtContinuation, Is.EqualTo(1), "Settled is published after the awaiters resume, not before; only the one from the first open exists.");
            Assert.That(followUp.IsAlive, Is.True, "The follow-up is open.");
            Assert.That(
                _harness.Observer.AllKinds(),
                Is.EqualTo(new[]
                {
                    ViewEventKind.Requested,
                    ViewEventKind.Shown,
                    ViewEventKind.Settled,
                    ViewEventKind.Closed,
                    ViewEventKind.Requested,
                    ViewEventKind.Shown,
                    ViewEventKind.Settled
                }),
                "The follow-up opened by the continuation keeps the navigator busy: no Settled between Closed and its own Shown.");
        }

        [Test]
        public void AnItemThatWaitsForSettled_PresentsAfterTheFollowUpOfTheContinuation_NotBeforeIt()
        {
            _harness.Row("first", ViewLayers.Popups).Row("follow-up", ViewLayers.Popups).Row("queued", ViewLayers.Popups);
            var first = new ContractView();
            var followUp = new ContractView();
            _harness.Map("first", () => first).Map("follow-up", () => followUp);

            // A stand-in for the queue: it presents its item once the navigator settles with the layer clear.
            var queuedOpened = false;
            var queuedAtFollowUpOpen = false;
            _harness.Navigator.AddObserver(
                new ContractActionObserver(viewEvent =>
                {
                    if (viewEvent.Kind != ViewEventKind.Settled || queuedOpened)
                        return;

                    if (!_harness.Navigator.IsLayerClear(ViewLayers.Popups))
                        return;

                    queuedOpened = true;
                    queuedAtFollowUpOpen = _harness.Navigator.IsOpen("follow-up");
                    _harness.Navigator.OpenView("queued", _harness.Owner);
                }),
                _harness.Owner);

            var task = _harness.Navigator.Show("first", _harness.Owner);
            ContractAwait.Watch(task, () => _harness.Navigator.OpenView("follow-up", _harness.Owner));

            // The first open settled with the layer busy: the stand-in found nothing to present.
            Assert.That(queuedOpened, Is.False);

            first.Complete(default(NoResult));

            Assert.That(_harness.Navigator.IsOpen("follow-up"), Is.True);
            Assert.That(queuedOpened, Is.False, "The queued item waits while the follow-up is open.");
            Assert.That(_harness.Navigator.IsOpen("queued"), Is.False);

            followUp.Close();

            Assert.That(queuedOpened, Is.True, "Once the follow-up closed and the navigator settled, the queued item presents.");
            Assert.That(queuedAtFollowUpOpen, Is.False, "It presented after the follow-up was gone.");
            Assert.That(_harness.Navigator.IsOpen("queued"), Is.True);
        }

        [Test]
        public void WithoutAFollowUp_SettledIsPublishedAfterTheContinuation_AndTheWaitingItemPresentsThen()
        {
            _harness.Row("first", ViewLayers.Popups).Row("queued", ViewLayers.Popups);
            var first = new ContractView();
            _harness.Map("first", () => first);

            var timeline = new List<string>();
            var queuedOpened = false;
            _harness.Navigator.AddObserver(
                new ContractActionObserver(viewEvent =>
                {
                    if (viewEvent.Kind != ViewEventKind.Settled || queuedOpened || !_harness.Navigator.IsLayerClear(ViewLayers.Popups))
                        return;

                    queuedOpened = true;
                    timeline.Add("queued presented");
                    _harness.Navigator.OpenView("queued", _harness.Owner);
                }),
                _harness.Owner);

            var task = _harness.Navigator.Show("first", _harness.Owner);
            ContractAwait.Watch(task, () => timeline.Add("continuation"));

            first.Complete(default(NoResult));

            Assert.That(timeline, Is.EqualTo(new[] { "continuation", "queued presented" }), "The opener's continuation runs before the next item presents.");
            Assert.That(_harness.Navigator.IsOpen("queued"), Is.True);
        }

        // Helpers

        private int Releases()
        {
            var instance = _harness.Stage.LastInstanceOf(ContractShownRun.DefaultId);
            return instance == null ? -1 : instance.ReleaseCount;
        }

        // The screen on top and what ends it.
        private sealed class CoverRun
        {
            public ContractView View;
            public CancellationTokenSource Owner;
        }

        // Screen a is open and covered by screen b; both are tracked on the timeline.
        private ContractView OpenCoveredScreen(List<string> timeline, out CoverRun cover, Action onRevealed = null)
        {
            _harness.Row("a").Row("b");
            var a = new ContractView
            {
                OnVisibleHook = scope =>
                {
                    // The first span begins while b does not exist yet.
                    if (_harness.Stage.LastInstanceOf("b") == null)
                        return;

                    timeline.Add("a visible");
                    if (onRevealed != null)
                        onRevealed();
                }
            };
            var b = new ContractView
            {
                BindOnOpen = scope => scope.OnEnd(() => timeline.Add("b open scope end"))
            };
            _harness.Map("a", () => a).Map("b", () => b);

            _harness.Navigator.AddObserver(
                new ContractActionObserver(viewEvent =>
                {
                    if (viewEvent.Kind == ViewEventKind.Closed)
                        timeline.Add("event Closed " + viewEvent.ViewId);
                }),
                _harness.Owner);

            _harness.Navigator.OpenView("a", _harness.Owner);

            var owner = _harness.NewOwner();
            var task = _harness.Navigator.Show("b", owner.Token);
            ContractAwait.Watch(task, () => timeline.Add("b show awaiter"));
            b.LastVisible.OnEnd(() => timeline.Add("b visible scope end"));

            cover = new CoverRun { View = b, Owner = owner };
            Assert.That(a.VisibleCount, Is.EqualTo(1), "Screen a is covered by b: its visible scope ended.");
            Assert.That(a.LastVisible.IsEnded, Is.True);
            return a;
        }
    }
}
