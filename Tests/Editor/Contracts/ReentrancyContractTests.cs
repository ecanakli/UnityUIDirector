using System;
using System.Threading;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Re-entrancy: navigator calls made from inside the package's own callbacks.
    public sealed class ReentrancyContractTests
    {
        public enum Site
        {
            OnOpen,
            OnVisible,
            ScopeCleanup,
            Observer,
            AwaiterContinuation
        }

        public enum Call
        {
            Open,
            Close,
            Back
        }

        private enum Ending
        {
            Open,
            Dismissed,
            Code,
            Back,
            WithdrawnAsCancelled
        }

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

        // The matrix: three calls from five callbacks

        [TestCase(Site.OnOpen, Call.Open)]
        [TestCase(Site.OnOpen, Call.Close)]
        [TestCase(Site.OnOpen, Call.Back)]
        [TestCase(Site.OnVisible, Call.Open)]
        [TestCase(Site.OnVisible, Call.Close)]
        [TestCase(Site.OnVisible, Call.Back)]
        [TestCase(Site.ScopeCleanup, Call.Open)]
        [TestCase(Site.ScopeCleanup, Call.Close)]
        [TestCase(Site.ScopeCleanup, Call.Back)]
        [TestCase(Site.Observer, Call.Open)]
        [TestCase(Site.Observer, Call.Close)]
        [TestCase(Site.Observer, Call.Back)]
        [TestCase(Site.AwaiterContinuation, Call.Open)]
        [TestCase(Site.AwaiterContinuation, Call.Close)]
        [TestCase(Site.AwaiterContinuation, Call.Back)]
        public void ANavigatorCallFromACallback_IsLegal_NeverThrows_AndTheOutcomeFollowsTheDesign(Site site, Call call)
        {
            var navigator = _harness.Navigator;
            var victim = new ContractView();
            var trigger = new ContractView();
            var extra = new ContractView();
            _harness.Row("victim", ViewLayers.Popups).Row("trigger", ViewLayers.Popups).Row("extra", ViewLayers.Popups);
            _harness.Map("victim", () => victim).Map("trigger", () => trigger).Map("extra", () => extra);

            var victimProbe = ContractAwait.Watch(navigator.Show("victim", _harness.NewOwner().Token));
            Assert.That(victimProbe.IsPending, Is.True, "The victim popup is open before the trigger runs.");

            var fired = false;
            bool? backResult = null;
            Action action = () =>
            {
                if (fired)
                    return;

                fired = true;
                switch (call)
                {
                    case Call.Open:
                        navigator.OpenView("extra", _harness.Owner);
                        break;

                    case Call.Close:
                    {
                        ViewHandle handle;
                        if (navigator.TryGetOpen("victim", out handle))
                            handle.Close();
                        break;
                    }

                    case Call.Back:
                        backResult = navigator.Back();
                        break;
                }
            };

            var triggerOwner = _harness.NewOwner();
            ContractAwaitProbe<ViewResult<NoResult>> triggerProbe = null;

            switch (site)
            {
                case Site.OnOpen:
                    trigger.RunOnOpen = context => action();
                    triggerProbe = ContractAwait.Watch(navigator.Show("trigger", triggerOwner.Token));
                    break;

                case Site.OnVisible:
                    trigger.OnVisibleHook = scope => action();
                    triggerProbe = ContractAwait.Watch(navigator.Show("trigger", triggerOwner.Token));
                    break;

                case Site.ScopeCleanup:
                    trigger.BindOnOpen = scope => scope.OnEnd(action);
                    triggerProbe = ContractAwait.Watch(navigator.Show("trigger", triggerOwner.Token));
                    trigger.Close();
                    break;

                case Site.Observer:
                    navigator.AddObserver(
                        new ContractActionObserver(viewEvent =>
                        {
                            if (viewEvent.Kind == ViewEventKind.Shown && viewEvent.ViewId == "trigger")
                                action();
                        }),
                        _harness.Owner);
                    triggerProbe = ContractAwait.Watch(navigator.Show("trigger", triggerOwner.Token));
                    break;

                case Site.AwaiterContinuation:
                    triggerProbe = ContractAwait.Watch(navigator.Show("trigger", triggerOwner.Token), action);
                    trigger.Close();
                    break;
            }

            Assert.That(fired, Is.True, "The callback never ran.");
            var expected = Expect(site, call);

            AssertEnding("trigger", expected.Trigger, "trigger", triggerProbe);
            AssertEnding("victim", expected.Victim, "victim", victimProbe);

            Assert.That(navigator.IsOpen("extra"), Is.EqualTo(expected.ExtraOpen), "extra open");
            if (expected.ExtraOpen)
            {
                ViewHandle extraHandle;
                Assert.That(navigator.TryGetOpen("extra", out extraHandle), Is.True);
                Assert.That(extraHandle.Phase, Is.EqualTo(ViewPhase.Open));
            }
            else
            {
                Assert.That(_harness.Stage.LastInstanceOf("extra"), Is.Null, "Nothing was loaded for the view that was not opened.");
            }

            if (call == Call.Back)
                Assert.That(backResult, Is.EqualTo(true), "Back handled the request in every case.");

            Assert.That(_harness.Errors, Is.Empty, "A legal call from a callback routes no error.");
            Assert.That(_harness.Diagnostics.FaultCount, Is.Zero);
            Assert.That(_harness.ShieldCount, Is.Zero, "The navigator ends up settled.");
            Assert.That(navigator.IsBusy, Is.False);

            _harness.Dispose();
            Assert.That(triggerProbe.ResumeCount, Is.EqualTo(1), "Every awaiter completes exactly once.");
            Assert.That(victimProbe.ResumeCount, Is.EqualTo(1), "Every awaiter completes exactly once.");
            _harness.Stage.AssertAllReleased();
        }

        // Close and Complete inside OnOpen

        [Test]
        public void CloseInsideOnOpen_IsLatched_TheViewOpensAndThenCloses()
        {
            var view = new ContractView { RunOnOpen = context => context.Close() };
            _harness.Row("p", ViewLayers.Popups).Map("p", () => view);

            var result = _harness.Navigator.Show("p", _harness.Owner).ResultNow();

            Assert.That(result.WasShown, Is.True, "A close requested inside OnOpen is latched: the view opens, and then closes.");
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Dismissed));
            Assert.That(
                _harness.Observer.KindsFor(result.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }));
            Assert.That(view.VisibleCount, Is.EqualTo(1), "The view became visible once before it closed.");
        }

        [Test]
        public void CompleteInsideOnOpen_IsLatched_TheViewOpensAndThenClosesWithTheValue()
        {
            _harness.Row("c", ViewLayers.Popups);
            _harness.Map("c", () => new FakeView<NoArgs, bool> { RunOnOpen = context => context.Complete(true) });

            var result = _harness.Navigator.ShowAsync(ViewKey.Create<NoArgs, bool>("c"), default(NoArgs), _harness.Owner).ResultNow();

            Assert.That(result.WasShown, Is.True);
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(result.HasValue, Is.True);
            Assert.That(result.Value, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompleteOrCloseInsideOnOpen_ReportsUIDIRECTOR118_PointingToDecline(bool complete)
        {
            _harness.Row("c", ViewLayers.Popups);
            _harness.Map("c", () => new FakeView<NoArgs, bool>
            {
                RunOnOpen = context =>
                {
                    if (complete)
                        context.Complete(true);
                    else
                        context.Close();
                }
            });

            _harness.Navigator.ShowAsync(ViewKey.Create<NoArgs, bool>("c"), default(NoArgs), _harness.Owner);

            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR118"), Is.EqualTo(1), "Complete or Close inside OnOpen reports UIDIRECTOR118 once, pointing to Decline.\n" + _harness.Diagnostics.Describe());
        }

        // Open from OnOpen

        [Test]
        public void OpenFromOnOpen_IsAcceptedAtOnce_ButPresentsAfterTheOpeningView()
        {
            var extraOpened = false;
            var trigger = new ContractView { RunOnOpen = context => extraOpened = context.Navigator.OpenView("extra", _harness.Owner).IsAlive };
            _harness.Row("trigger", ViewLayers.Popups).Row("extra", ViewLayers.Popups);
            _harness.Map("trigger", () => trigger);

            _harness.Navigator.Show("trigger", _harness.Owner);

            Assert.That(extraOpened, Is.True, "The call from OnOpen returned a live handle: the request was accepted.");

            var requestedTrigger = IndexOf(ViewEventKind.Requested, "trigger");
            var requestedExtra = IndexOf(ViewEventKind.Requested, "extra");
            var shownTrigger = IndexOf(ViewEventKind.Shown, "trigger");
            var shownExtra = IndexOf(ViewEventKind.Shown, "extra");
            Assert.That(requestedTrigger, Is.LessThan(requestedExtra));
            Assert.That(requestedExtra, Is.LessThan(shownTrigger), "Accepted inside OnOpen, before the opening view is shown.");
            Assert.That(shownTrigger, Is.LessThan(shownExtra), "One operation per layer, never interrupted; the new open waits its turn.");
        }

        // State is consistent before user code runs

        [Test]
        public void AScopeCleanup_SeesTheEndingRequestAlreadyRemoved()
        {
            var handle = default(ViewHandle);
            var snapshotTaken = false;
            var isOpen = true;
            var found = true;
            var requests = -1;
            var phase = ViewPhase.Open;
            var alive = true;
            var run = ContractShownRun.Start(_harness, view => view.BindOnOpen = scope => scope.OnEnd(() =>
            {
                snapshotTaken = true;
                ViewHandle current;
                isOpen = _harness.Navigator.IsOpen(ContractShownRun.DefaultId);
                found = _harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out current);
                requests = _harness.RequestCount(ViewLayers.Popups);
                phase = handle.Phase;
                alive = handle.IsAlive;
            }));
            Assert.That(_harness.Navigator.TryGetOpen(ContractShownRun.DefaultId, out handle), Is.True);

            run.View.Close();

            Assert.That(snapshotTaken, Is.True);
            Assert.That(isOpen, Is.False, "Ending first removes the record, before any user code runs.");
            Assert.That(found, Is.False);
            Assert.That(requests, Is.Zero);
            Assert.That(phase, Is.EqualTo(ViewPhase.Ended));
            Assert.That(alive, Is.False);
        }

        [Test]
        public void ReopeningTheSameId_FromItsOwnScopeCleanup_IsAcceptedAsANewRequest()
        {
            var reopened = false;
            var run = ContractShownRun.Start(_harness, view => view.BindOnOpen = scope => scope.OnEnd(() =>
            {
                if (reopened)
                    return;

                reopened = true;
                _harness.Navigator.OpenView(ContractShownRun.DefaultId, _harness.Owner);
            }));

            run.View.Close();

            Assert.That(reopened, Is.True);
            Assert.That(_harness.Navigator.IsOpen(ContractShownRun.DefaultId), Is.True, "The id was free by the time the cleanup ran, so it is not a duplicate.");
            Assert.That(_harness.Stage.Instances.Count, Is.EqualTo(2));
            Assert.That(_harness.Stage.Instances[0].ReleaseCount, Is.EqualTo(1), "The first instance is released.");
            Assert.That(_harness.Stage.Instances[1].IsLive, Is.True, "The new request has its own instance.");
            Assert.That(run.Result.ResultNow().Reason, Is.EqualTo(CloseReason.Dismissed), "The first request ended with its own cause.");
            Assert.That(_harness.Errors, Is.Empty);
        }

        // Helpers

        private sealed class Expectation
        {
            public Ending Trigger;
            public Ending Victim;
            public bool ExtraOpen;
        }

        // The trigger itself closes by its own hand at the cleanup and continuation sites; the others leave it open.
        private static Expectation Expect(Site site, Call call)
        {
            var triggerCloses = site == Site.ScopeCleanup || site == Site.AwaiterContinuation;
            var expectation = new Expectation
            {
                Trigger = triggerCloses ? Ending.Dismissed : Ending.Open,
                Victim = Ending.Open
            };

            switch (call)
            {
                case Call.Open:
                    expectation.ExtraOpen = true;
                    break;

                case Call.Close:
                    expectation.Victim = Ending.Code;
                    break;

                case Call.Back:
                    // Back rules: a request in Loading is withdrawn; a shield above zero swallows; otherwise the top view closes.
                    switch (site)
                    {
                        case Site.OnOpen:
                            expectation.Trigger = Ending.WithdrawnAsCancelled;
                            break;

                        case Site.OnVisible:
                            break;

                        case Site.Observer:
                            expectation.Trigger = Ending.Back;
                            break;

                        default:
                            expectation.Victim = Ending.Back;
                            break;
                    }

                    break;
            }

            return expectation;
        }

        private void AssertEnding(string what, Ending ending, string viewId, ContractAwaitProbe<ViewResult<NoResult>> probe)
        {
            if (ending == Ending.Open)
            {
                Assert.That(probe.IsPending, Is.True, what + " should still be open.");
                Assert.That(_harness.Navigator.IsOpen(viewId), Is.True, what + " should still be open.");
                return;
            }

            Assert.That(probe.ResumeCount, Is.EqualTo(1), what + " should have ended.");
            Assert.That(_harness.Navigator.IsOpen(viewId), Is.False, what + " should have ended.");

            if (ending == Ending.WithdrawnAsCancelled)
            {
                Assert.That(probe.Result.WasShown, Is.False, what);
                Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.NotShown), what);
                Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Cancelled), what);
                return;
            }

            var reason = ending == Ending.Dismissed ? CloseReason.Dismissed : (ending == Ending.Code ? CloseReason.Code : CloseReason.Back);
            Assert.That(probe.Result.WasShown, Is.True, what);
            Assert.That(probe.Result.Reason, Is.EqualTo(reason), what);
        }

        private int IndexOf(ViewEventKind kind, string viewId)
        {
            var events = _harness.Observer.Events;
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == kind && events[i].ViewId == viewId)
                    return i;
            }

            Assert.Fail("No " + kind + " event for '" + viewId + "'.");
            return -1;
        }
    }
}
