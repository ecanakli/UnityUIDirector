using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Events: the sequences, the payloads and observer handling.
    public sealed class EventContractTests
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
            _harness.MainThread.IsMainThread = true;
            _harness.Dispose();
        }

        // The sequence per request id

        [Test]
        public void AcceptedAndShownAndClosed_PublishRequestedThenShownThenClosed()
        {
            var run = ContractShownRun.Start(_harness);
            run.View.Complete(default(NoResult));

            var id = run.Result.ResultNow().RequestId;

            Assert.That(
                _harness.Observer.KindsFor(id),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }));
        }

        [TestCase(ContractNotShownPath.UnknownKey, RejectReason.UnknownKey)]
        [TestCase(ContractNotShownPath.OwnerAlreadyCancelled, RejectReason.OwnerEnded)]
        public void RejectedAtAcceptance_PublishesOnlyNotShown_WithTheRejectReason(ContractNotShownPath path, RejectReason expected)
        {
            var run = ContractNotShownRun.Start(_harness, path);
            run.Finish();
            var result = run.Result.ResultNow();

            var events = _harness.Observer.EventsFor(result.RequestId);

            Assert.That(events.Length, Is.EqualTo(1), "A rejected call publishes NotShown only: no Requested.\n" + Describe());
            Assert.That(events[0].Kind, Is.EqualTo(ViewEventKind.NotShown));
            Assert.That(events[0].Reason, Is.EqualTo(CloseReason.NotShown));
            Assert.That(events[0].Rejected, Is.EqualTo(expected));
            Assert.That(result.Rejected, Is.EqualTo(expected));
        }

        [Test]
        public void DuplicateOnANonHistoryLayer_PublishesOnlyNotShown_AndTheFirstRequestIsUnaffected()
        {
            var run = ContractShownRun.Start(_harness, id: "p");
            var firstId = _harness.Observer.LastOf(ViewEventKind.Requested).RequestId;

            var second = _harness.Navigator.Show("p", _harness.Owner, new OpenOptions { Source = "dup-tag" }).ResultNow();

            Assert.That(second.Rejected, Is.EqualTo(RejectReason.Duplicate));
            var events = _harness.Observer.EventsFor(second.RequestId);
            Assert.That(events.Length, Is.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(ViewEventKind.NotShown));
            Assert.That(events[0].Rejected, Is.EqualTo(RejectReason.Duplicate));
            Assert.That(events[0].ViewId, Is.EqualTo("p"));
            Assert.That(events[0].LayerId, Is.EqualTo(ViewLayers.Popups.Id));
            Assert.That(events[0].Source, Is.EqualTo("dup-tag"));
            Assert.That(
                _harness.Observer.KindsFor(firstId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }),
                "The first request is still open.");
            Assert.That(run.Result.IsPending(), Is.True);
        }

        [Test]
        public void RejectWhenBusy_PublishesOnlyNotShown_Busy()
        {
            _harness.Row("a", ViewLayers.Popups).Row("b", ViewLayers.Popups);
            _harness.Stage.AcquireMode = StageMode.Manual;
            _harness.Navigator.Show("a", _harness.Owner);

            var busy = _harness.Navigator.Show("b", _harness.Owner, new OpenOptions { RejectWhenBusy = true }).ResultNow();

            Assert.That(busy.Rejected, Is.EqualTo(RejectReason.Busy));
            Assert.That(_harness.Observer.KindsFor(busy.RequestId), Is.EqualTo(new[] { ViewEventKind.NotShown }));
        }

        [Test]
        public void WaitingCapReached_PublishesOnlyNotShown_Busy_WithUIDIRECTOR112()
        {
            for (var i = 0; i <= 5; i++)
                _harness.Row("w" + i, ViewLayers.Popups);

            // One request loads, four wait: the cap of a layer is four waiting requests.
            _harness.Stage.AcquireMode = StageMode.Manual;
            for (var i = 0; i <= 4; i++)
                _harness.Navigator.Show("w" + i, _harness.Owner);

            var refused = _harness.Navigator.Show("w5", _harness.Owner).ResultNow();

            Assert.That(refused.Rejected, Is.EqualTo(RejectReason.Busy));
            Assert.That(_harness.Diagnostics.Count("UIDIRECTOR112"), Is.EqualTo(1));
            Assert.That(_harness.Observer.KindsFor(refused.RequestId), Is.EqualTo(new[] { ViewEventKind.NotShown }));
        }

        [TestCase(ContractNotShownPath.OnOpenThrows, RejectReason.Faulted)]
        [TestCase(ContractNotShownPath.OnOpenDeclines, RejectReason.Declined)]
        [TestCase(ContractNotShownPath.LoadFails, RejectReason.LoadFailed)]
        [TestCase(ContractNotShownPath.OwnerEndsWhileLoading, RejectReason.OwnerEnded)]
        [TestCase(ContractNotShownPath.HandleClosedWhileLoading, RejectReason.Cancelled)]
        [TestCase(ContractNotShownPath.TeardownWhileLoading, RejectReason.RootGone)]
        [TestCase(ContractNotShownPath.TeardownWhileWaiting, RejectReason.RootGone)]
        public void AcceptedThenEndedBeforeBeingShown_PublishesRequestedThenNotShown_WithTheRejectReason(ContractNotShownPath path, RejectReason expected)
        {
            var run = ContractNotShownRun.Start(_harness, path);
            run.Finish();
            var result = run.Result.ResultNow();

            var events = _harness.Observer.EventsFor(result.RequestId);

            Assert.That(
                _harness.Observer.KindsFor(result.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.NotShown }),
                "No Shown event may appear for a request that never reached Open.\n" + Describe());
            Assert.That(events[1].Reason, Is.EqualTo(CloseReason.NotShown));
            Assert.That(events[1].Rejected, Is.EqualTo(expected));
            Assert.That(events[1].ViewId, Is.EqualTo(ContractNotShownRun.DefaultId));
        }

        [Test]
        public void Teardown_PublishesATerminalEventForEveryLiveRequest()
        {
            var shown = ContractShownRun.Start(_harness, id: "shown");
            var loading = ContractNotShownRun.Start(_harness, ContractNotShownPath.TeardownWhileWaiting);

            loading.Finish();

            var shownResult = shown.Result.ResultNow();
            var loadingResult = loading.Companion.ResultNow();
            var waitingResult = loading.Result.ResultNow();

            AssertEveryRequestIsLegal();
            Assert.That(EventOf(shownResult.RequestId, ViewEventKind.Closed).Reason, Is.EqualTo(CloseReason.Destroyed));
            Assert.That(EventOf(loadingResult.RequestId, ViewEventKind.NotShown).Rejected, Is.EqualTo(RejectReason.RootGone));
            Assert.That(EventOf(waitingResult.RequestId, ViewEventKind.NotShown).Rejected, Is.EqualTo(RejectReason.RootGone));
        }

        [Test]
        public void EveryRequestOfAMixedRun_FollowsOneOfTheLegalSequences()
        {
            var runs = 0;

            // Shown paths first: they register the popup row with the backdrop option the Backdrop path needs.
            foreach (ContractClosePath path in Enum.GetValues(typeof(ContractClosePath)))
            {
                if (path == ContractClosePath.Teardown)
                    continue;

                var run = ContractShownRun.Start(_harness);
                run.End(path);
                run.Result.ResultNow();
                runs++;
            }

            foreach (ContractNotShownPath path in Enum.GetValues(typeof(ContractNotShownPath)))
            {
                var teardown = path == ContractNotShownPath.TeardownWhileLoading
                    || path == ContractNotShownPath.TeardownWhileWaiting
                    || path == ContractNotShownPath.ShowAfterTeardown;
                if (teardown)
                    continue;

                var run = ContractNotShownRun.Start(_harness, path);
                run.Finish();
                run.Result.ResultNow();
                runs++;
            }

            AssertEveryRequestIsLegal();

            var terminals = _harness.Observer.CountOf(ViewEventKind.Closed) + _harness.Observer.CountOf(ViewEventKind.NotShown);
            Assert.That(terminals, Is.EqualTo(runs), "Every request ends with exactly one terminal event.\n" + Describe());
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Shown), Is.EqualTo(_harness.Observer.CountOf(ViewEventKind.Closed)), "Every Shown is followed by its Closed.");
        }

        // Payload fields

        [Test]
        public void EventsOfARequest_CarryRequestIdViewIdLayerIdSourceTag_AndZeroPriority()
        {
            var view = new ContractView();
            _harness.Row("p", ViewLayers.Popups).Map("p", () => view);
            var task = _harness.Navigator.Show("p", _harness.Owner, new OpenOptions { Source = "tap" });
            view.Close();

            var result = task.ResultNow();
            var events = _harness.Observer.EventsFor(result.RequestId);

            Assert.That(events.Length, Is.EqualTo(3));
            for (var i = 0; i < events.Length; i++)
            {
                Assert.That(events[i].RequestId, Is.EqualTo(result.RequestId), "Event " + events[i].Kind);
                Assert.That(events[i].ViewId, Is.EqualTo("p"), "Event " + events[i].Kind);
                Assert.That(events[i].LayerId, Is.EqualTo(ViewLayers.Popups.Id), "Event " + events[i].Kind);
                Assert.That(events[i].Source, Is.EqualTo("tap"), "Event " + events[i].Kind);
                Assert.That(events[i].Priority, Is.Zero, "A request that did not come from a queue has priority zero.");
            }
        }

        [Test]
        public void Shown_SecondsWaiting_IsTheClockTimeFromAcceptance()
        {
            _harness.Stage.AcquireMode = StageMode.Manual;
            var run = ContractShownRun.Start(_harness);

            _harness.Advance(0.3f);
            _harness.Stage.CompleteAcquire();

            var shown = _harness.Observer.LastOf(ViewEventKind.Shown);
            Assert.That(shown.SecondsWaiting, Is.EqualTo(0.3f).Within(0.02f));
            Assert.That(run.Result.IsPending(), Is.True);
        }

        [Test]
        public void Shown_SecondsWaiting_IncludesTheShowTransition_BecauseShownIsPublishedWhenItEnds()
        {
            _harness.Stage.AcquireMode = StageMode.Manual;
            _harness.Stage.PlayMode = StageMode.Manual;
            ContractShownRun.Start(_harness);

            _harness.Advance(0.2f);
            _harness.Stage.CompleteAcquire();
            _harness.Advance(0.5f);
            _harness.Stage.CompletePlay();

            var shown = _harness.Observer.LastOf(ViewEventKind.Shown);
            Assert.That(shown.SecondsWaiting, Is.EqualTo(0.7f).Within(0.03f), "Shown carries the seconds from acceptance to Shown.");
        }

        [Test]
        public void Closed_CarriesReasonRejectedNoneAndSecondsOpen_FromShownToClosed()
        {
            var run = ContractShownRun.Start(_harness);

            _harness.Advance(0.4f);
            run.View.Complete(default(NoResult));

            var closed = _harness.Observer.LastOf(ViewEventKind.Closed);
            Assert.That(closed.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(closed.Rejected, Is.EqualTo(RejectReason.None));
            Assert.That(closed.SecondsOpen, Is.EqualTo(0.4f).Within(0.02f));
        }

        [Test]
        public void Closed_SecondsOpen_StartsWhenShownIsPublished_NotWhenTheShowTransitionStarts()
        {
            _harness.Stage.PlayMode = StageMode.Manual;
            var run = ContractShownRun.Start(_harness);

            _harness.Advance(0.5f);
            _harness.Stage.CompletePlay();
            _harness.Advance(0.4f);
            run.View.Complete(default(NoResult));
            _harness.Stage.CompletePlay();

            var closed = _harness.Observer.LastOf(ViewEventKind.Closed);
            Assert.That(closed.SecondsOpen, Is.EqualTo(0.4f).Within(0.02f), "Closed carries the seconds from Shown to Closed.");
        }

        [Test]
        public void NotShown_Declined_CarriesTheDeclineReasonAsDetail_InTheEventAndTheResult()
        {
            var view = new ContractView { RunOnOpen = context => context.Decline("because") };
            _harness.Row("p", ViewLayers.Popups).Map("p", () => view);

            var result = _harness.Navigator.Show("p", _harness.Owner).ResultNow();

            var notShown = _harness.Observer.LastOf(ViewEventKind.NotShown);
            Assert.That(notShown.Rejected, Is.EqualTo(RejectReason.Declined));
            Assert.That(notShown.Detail, Is.EqualTo("because"));
            Assert.That(result.Detail, Is.EqualTo("because"));
        }

        [TestCase(ContractClosePath.ViewComplete, CloseReason.Completed)]
        [TestCase(ContractClosePath.ViewClose, CloseReason.Dismissed)]
        [TestCase(ContractClosePath.HandleClose, CloseReason.Code)]
        [TestCase(ContractClosePath.Back, CloseReason.Back)]
        [TestCase(ContractClosePath.Backdrop, CloseReason.Backdrop)]
        [TestCase(ContractClosePath.CloseAll, CloseReason.Code)]
        [TestCase(ContractClosePath.OwnerCancel, CloseReason.OwnerEnded)]
        [TestCase(ContractClosePath.InstanceDestroyed, CloseReason.Destroyed)]
        [TestCase(ContractClosePath.InstanceDeactivated, CloseReason.Destroyed)]
        [TestCase(ContractClosePath.Teardown, CloseReason.Destroyed)]
        public void ClosedEvent_AndTheShowAsyncResult_AgreeOnTheReason_OnEveryClosePath(ContractClosePath path, CloseReason expected)
        {
            var run = ContractShownRun.Start(_harness);
            run.End(path);
            var result = run.Result.ResultNow();

            var events = _harness.Observer.EventsFor(result.RequestId);

            Assert.That(
                _harness.Observer.KindsFor(result.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }));
            Assert.That(events[2].Reason, Is.EqualTo(expected));
            Assert.That(events[2].Reason, Is.EqualTo(result.Reason));
            Assert.That(events[2].Rejected, Is.EqualTo(RejectReason.None));
        }

        // Settled

        [Test]
        public void Settled_HasNoRequestId_NoViewId_AndNoLayerId()
        {
            ContractShownRun.Start(_harness);

            var settled = _harness.Observer.LastOf(ViewEventKind.Settled);

            Assert.That(settled.RequestId, Is.Zero);
            Assert.That(settled.ViewId, Is.Null);
            Assert.That(settled.LayerId, Is.Null);
        }

        [Test]
        public void Settled_IsPublishedOncePerBusyToSettledChange_AfterTheEventsOfTheWorkThatEndedIt()
        {
            var run = ContractShownRun.Start(_harness);
            run.View.Complete(default(NoResult));

            Assert.That(
                _harness.Observer.AllKinds(),
                Is.EqualTo(new[]
                {
                    ViewEventKind.Requested,
                    ViewEventKind.Shown,
                    ViewEventKind.Settled,
                    ViewEventKind.Closed,
                    ViewEventKind.Settled
                }),
                Describe());
        }

        [Test]
        public void Settled_IsNotPublishedWhileAnOperationRuns_AndWhileAnInputHoldStands()
        {
            _harness.Stage.AcquireMode = StageMode.Manual;
            ContractShownRun.Start(_harness);
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Settled), Is.Zero, "The navigator is busy while the load runs.");

            _harness.Stage.CompleteAcquire();
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Settled), Is.EqualTo(1));

            var hold = _harness.Navigator.HoldInput("test", _harness.Owner);
            Assert.That(_harness.Navigator.IsBusy, Is.True);
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Settled), Is.EqualTo(1), "A hold makes the navigator busy; no event on the way in.");

            hold.Remove();
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Settled), Is.EqualTo(2), "Removing the last hold settles the navigator.");
        }

        // Observer isolation

        [Test]
        public void AnObserverThatThrows_IsRouted_AndTheOtherObserversStillReceiveEveryEvent()
        {
            var failure = new InvalidOperationException("observer failed");
            var thrower = new RecordingObserver { ThrowOnEvent = failure };
            var healthy = new RecordingObserver();
            _harness.Navigator.AddObserver(thrower, _harness.Owner);
            _harness.Navigator.AddObserver(healthy, _harness.Owner);

            var run = ContractShownRun.Start(_harness);
            run.View.Complete(default(NoResult));

            var expected = new[]
            {
                ViewEventKind.Requested,
                ViewEventKind.Shown,
                ViewEventKind.Settled,
                ViewEventKind.Closed,
                ViewEventKind.Settled
            };
            Assert.That(healthy.AllKinds(), Is.EqualTo(expected), "An observer after the failing one must still run.");
            Assert.That(_harness.Observer.AllKinds(), Is.EqualTo(expected), "An observer before the failing one is unaffected.");
            Assert.That(thrower.Count, Is.EqualTo(expected.Length), "The failing observer stays registered.");
            Assert.That(run.Result.ResultNow().Reason, Is.EqualTo(CloseReason.Completed), "The view flow is unaffected.");

            Assert.That(_harness.Errors.Count, Is.EqualTo(expected.Length), "One routed error per event.");
            for (var i = 0; i < _harness.Errors.Count; i++)
            {
                Assert.That(_harness.Errors[i].Exception, Is.SameAs(failure));
                Assert.That(_harness.Errors[i].Context.Source, Is.EqualTo(ViewErrorSource.Observer));
            }
        }

        [Test]
        public void RoutedObserverError_NamesTheViewAndTheRequestOfTheEvent()
        {
            var thrower = new RecordingObserver { ThrowOnEvent = new InvalidOperationException("observer failed") };
            _harness.Navigator.AddObserver(thrower, _harness.Owner);

            ContractShownRun.Start(_harness);

            var requested = _harness.Observer.LastOf(ViewEventKind.Requested);
            Assert.That(_harness.Errors.Count, Is.GreaterThan(0));
            Assert.That(_harness.Errors[0].Context.ViewId, Is.EqualTo(requested.ViewId));
            Assert.That(_harness.Errors[0].Context.RequestId, Is.EqualTo(requested.RequestId));
        }

        // Add and remove during a dispatch

        [Test]
        public void AnObserverAddedDuringADispatch_ReceivesEventsFromTheNextOneOnly()
        {
            var late = new RecordingObserver();
            var added = false;
            var adder = new ContractActionObserver(viewEvent =>
            {
                if (added)
                    return;

                added = true;
                _harness.Navigator.AddObserver(late, _harness.Owner);
            });
            _harness.Navigator.AddObserver(adder, _harness.Owner);

            var run = ContractShownRun.Start(_harness);
            run.View.Close();

            Assert.That(
                late.AllKinds(),
                Is.EqualTo(new[] { ViewEventKind.Shown, ViewEventKind.Settled, ViewEventKind.Closed, ViewEventKind.Settled }),
                "The first event, Requested, was being dispatched when the observer was added.");
        }

        [Test]
        public void AnObserverRemovedDuringADispatch_IsSkippedAtOnce_ForThatEventAndEveryLaterOne()
        {
            var victim = new RecordingObserver();
            var victimRegistration = default(ViewRegistration);
            var remover = new ContractActionObserver(viewEvent =>
            {
                if (viewEvent.Kind == ViewEventKind.Requested)
                    victimRegistration.Remove();
            });
            _harness.Navigator.AddObserver(remover, _harness.Owner);
            victimRegistration = _harness.Navigator.AddObserver(victim, _harness.Owner);

            var run = ContractShownRun.Start(_harness);
            run.View.Close();

            Assert.That(victim.Count, Is.Zero, "A removal takes effect at once, even for the event being dispatched.");
            Assert.That(victimRegistration.IsActive, Is.False);
        }

        [Test]
        public void AnObserverThatRemovesItselfDuringADispatch_ReceivesThatEventAndNoLaterOne()
        {
            var seen = new List<ViewEventKind>();
            var registration = default(ViewRegistration);
            var selfRemover = new ContractActionObserver(viewEvent =>
            {
                seen.Add(viewEvent.Kind);
                registration.Remove();
            });
            registration = _harness.Navigator.AddObserver(selfRemover, _harness.Owner);

            var run = ContractShownRun.Start(_harness);
            run.View.Close();

            Assert.That(seen, Is.EqualTo(new[] { ViewEventKind.Requested }));
            Assert.That(registration.IsActive, Is.False);
            Assert.That(_harness.Observer.CountOf(ViewEventKind.Closed), Is.EqualTo(1), "The other observers keep receiving.");
        }

        [Test]
        public void AnObserverWhoseOwnerEnds_IsRemoved_AndReceivesNothingMore()
        {
            var owner = _harness.NewOwner();
            var observer = new RecordingObserver();
            var registration = _harness.Navigator.AddObserver(observer, owner.Token);
            Assert.That(registration.IsActive, Is.True);

            owner.Cancel();
            ContractShownRun.Start(_harness);

            Assert.That(registration.IsActive, Is.False);
            Assert.That(observer.Count, Is.Zero);
        }

        [Test]
        public void AddObserver_WithAnAlreadyCancelledOwner_NeverReceivesEvents()
        {
            var owner = _harness.NewOwner();
            owner.Cancel();
            var observer = new RecordingObserver();

            var registration = _harness.Navigator.AddObserver(observer, owner.Token);
            ContractShownRun.Start(_harness);

            Assert.That(registration.IsActive, Is.False);
            Assert.That(observer.Count, Is.Zero);
        }

        [Test]
        public void ObserverRegistrationRemove_CalledTwice_NeverThrows_AndTheSecondCallDoesNothing()
        {
            var observer = new RecordingObserver();
            var registration = _harness.Navigator.AddObserver(observer, _harness.Owner);

            Assert.DoesNotThrow(() => registration.Remove());
            Assert.DoesNotThrow(() => registration.Remove());

            ContractShownRun.Start(_harness);
            Assert.That(registration.IsActive, Is.False);
            Assert.That(observer.Count, Is.Zero);
        }

        [Test]
        public void AddObserver_WithANullObserver_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _harness.Navigator.AddObserver(null, _harness.Owner));
        }

        // Events describe state that is already in place

        [Test]
        public void EachEvent_IsPublishedAfterTheStateItDescribesIsInPlace()
        {
            var requestedOpen = false;
            var requestedShield = -1;
            var requestedCount = -1;
            var shownPhase = ViewPhase.Ended;
            var shownShield = -1;
            var closedOpen = true;
            var closedShield = -1;
            var closedCount = -1;
            var closedReleases = -1;

            var spy = new ContractActionObserver(viewEvent =>
            {
                if (viewEvent.ViewId != ContractShownRun.DefaultId)
                    return;

                var navigator = _harness.Navigator;
                ViewHandle handle;
                var found = navigator.TryGetOpen(ContractShownRun.DefaultId, out handle);

                switch (viewEvent.Kind)
                {
                    case ViewEventKind.Requested:
                        requestedOpen = navigator.IsOpen(ContractShownRun.DefaultId);
                        requestedShield = _harness.ShieldCount;
                        requestedCount = _harness.RequestCount(ViewLayers.Popups);
                        break;

                    case ViewEventKind.Shown:
                        shownPhase = found ? handle.Phase : ViewPhase.Ended;
                        shownShield = _harness.ShieldCount;
                        break;

                    case ViewEventKind.Closed:
                        var instance = _harness.Stage.LastInstanceOf(ContractShownRun.DefaultId);
                        closedOpen = navigator.IsOpen(ContractShownRun.DefaultId);
                        closedShield = _harness.ShieldCount;
                        closedCount = _harness.RequestCount(ViewLayers.Popups);
                        closedReleases = instance != null ? instance.ReleaseCount : -1;
                        break;
                }
            });
            _harness.Navigator.AddObserver(spy, _harness.Owner);

            var run = ContractShownRun.Start(_harness);
            run.View.Complete(default(NoResult));

            Assert.That(requestedOpen, Is.True, "Requested: the request is already in its layer.");
            Assert.That(requestedShield, Is.GreaterThanOrEqualTo(1), "Requested: the shield is already up.");
            Assert.That(requestedCount, Is.EqualTo(1));
            Assert.That(shownPhase, Is.EqualTo(ViewPhase.Open), "Shown: the view is open.");
            Assert.That(shownShield, Is.Zero, "Shown: the open's shield hold is already released.");
            Assert.That(closedOpen, Is.False, "Closed: the request already left its layer.");
            Assert.That(closedShield, Is.Zero, "Closed: the close's shield hold is already released.");
            Assert.That(closedCount, Is.Zero);
            Assert.That(closedReleases, Is.EqualTo(1), "Closed is published after the instance was released.");
        }

        [Test]
        public void Closed_KeepInstanceRow_InstanceIsStashedAndNotReleasedWhenPublished()
        {
            const string keptId = "kept";
            var closedStashed = false;
            var closedReleases = -1;
            var closedStashCalls = -1;

            var spy = new ContractActionObserver(viewEvent =>
            {
                if (viewEvent.Kind != ViewEventKind.Closed || viewEvent.ViewId != keptId)
                    return;

                var instance = _harness.Stage.LastInstanceOf(keptId);
                closedStashed = instance != null && instance.Stashed;
                closedReleases = instance != null ? instance.ReleaseCount : -1;
                closedStashCalls = _harness.Stage.CountOf(StageCallKind.Stash);
            });
            _harness.Navigator.AddObserver(spy, _harness.Owner);
            _harness.Row(keptId, ViewLayers.Popups, ViewRetention.KeepInstance);

            var run = ContractShownRun.Start(_harness, id: keptId);
            run.View.Complete(default(NoResult));

            Assert.That(closedStashed, Is.True, "Closed: the instance is already parked for reuse.\n" + _harness.Stage.DescribeCalls());
            Assert.That(closedReleases, Is.Zero, "Closed: a kept instance is not released.");
            Assert.That(closedStashCalls, Is.EqualTo(1), "Closed: the stage was asked to park the instance once.");
        }

        // Events caused from inside an observer callback reach every observer after the event in progress

        [Test]
        public void ObserverEndingTheRequestDuringRequested_LaterObserverSeesRequestedThenNotShown()
        {
            _harness.Row("x", ViewLayers.Popups);
            var fired = false;
            var closer = new ContractActionObserver(viewEvent =>
            {
                if (fired || viewEvent.Kind != ViewEventKind.Requested || viewEvent.ViewId != "x")
                    return;

                fired = true;
                _harness.Navigator.CloseAll(ViewLayers.Popups);
            });
            var later = new RecordingObserver();
            _harness.Navigator.AddObserver(closer, _harness.Owner);
            _harness.Navigator.AddObserver(later, _harness.Owner);

            var probe = ContractAwait.Watch(_harness.Navigator.Show("x", _harness.NewOwner().Token));

            Assert.That(fired, Is.True, "The observer never reacted to Requested.");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "ShowAsync of 'x' resumes once.");
            Assert.That(
                later.KindsFor(probe.Result.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.NotShown }),
                "The later observer sees Requested before the NotShown it caused.\n" + TraceText(later));
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.NotShown), "reason");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Cancelled), "rejected");
        }

        [Test]
        public void ObserverCancellingTheOwnerDuringShown_LaterObserverSeesShownThenClosed()
        {
            _harness.Row("x", ViewLayers.Popups);
            var owner = _harness.NewOwner();
            var fired = false;
            var canceller = new ContractActionObserver(viewEvent =>
            {
                if (fired || viewEvent.Kind != ViewEventKind.Shown || viewEvent.ViewId != "x")
                    return;

                fired = true;
                owner.Cancel();
            });
            var later = new RecordingObserver();
            _harness.Navigator.AddObserver(canceller, _harness.Owner);
            _harness.Navigator.AddObserver(later, _harness.Owner);

            var probe = ContractAwait.Watch(_harness.Navigator.Show("x", owner.Token));

            Assert.That(fired, Is.True, "The observer never reacted to Shown.");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "ShowAsync of 'x' resumes once.");
            var requestId = probe.Result.RequestId;
            Assert.That(
                later.KindsFor(requestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }),
                "The later observer sees Shown before the Closed it caused.\n" + TraceText(later));
            Assert.That(EventIn(later, requestId, ViewEventKind.Closed).Reason, Is.EqualTo(CloseReason.OwnerEnded), "Closed reason");
        }

        [Test]
        public void ObserverOpeningAnUnknownIdDuringRequested_LaterObserverSeesTheCurrentRequestedBeforeTheRefusal()
        {
            _harness.Row("x", ViewLayers.Popups);
            var fired = false;
            var refused = default(UniTask<ViewResult<NoResult>>);
            var opener = new ContractActionObserver(viewEvent =>
            {
                if (fired || viewEvent.Kind != ViewEventKind.Requested || viewEvent.ViewId != "x")
                    return;

                fired = true;
                refused = _harness.Navigator.Show("missing", _harness.Owner);
            });
            var later = new RecordingObserver();
            _harness.Navigator.AddObserver(opener, _harness.Owner);
            _harness.Navigator.AddObserver(later, _harness.Owner);

            _harness.Navigator.Show("x", _harness.Owner);

            Assert.That(fired, Is.True, "The observer never reacted to Requested.");
            var refusal = refused.ResultNow();
            Assert.That(refusal.Rejected, Is.EqualTo(RejectReason.UnknownKey), "The call from the observer is refused.");
            Assert.That(
                IndexOf(later, ViewEventKind.Requested, "x"),
                Is.LessThan(IndexOfRequest(later, ViewEventKind.NotShown, refusal.RequestId)),
                "Requested of the current request comes before the refusal it caused.\n" + TraceText(later));
        }

        [Test]
        public void ObserverOpeningAViewThatShowsAtOnceDuringRequested_LaterObserverSeesItsRequestedBeforeItsShown()
        {
            _harness.Row("x", ViewLayers.Popups).Row("z", ViewLayers.Screens);
            var fired = false;
            var opener = new ContractActionObserver(viewEvent =>
            {
                if (fired || viewEvent.Kind != ViewEventKind.Requested || viewEvent.ViewId != "x")
                    return;

                fired = true;
                _harness.Navigator.OpenView("z", _harness.Owner);
            });
            var later = new RecordingObserver();
            _harness.Navigator.AddObserver(opener, _harness.Owner);
            _harness.Navigator.AddObserver(later, _harness.Owner);

            _harness.Navigator.Show("x", _harness.Owner);

            Assert.That(fired, Is.True, "The observer never reacted to Requested.");
            var requestedX = IndexOf(later, ViewEventKind.Requested, "x");
            var requestedZ = IndexOf(later, ViewEventKind.Requested, "z");
            var shownZ = IndexOf(later, ViewEventKind.Shown, "z");
            Assert.That(requestedX, Is.LessThan(requestedZ), "Requested(x) before Requested(z).\n" + TraceText(later));
            Assert.That(requestedZ, Is.LessThan(shownZ), "Requested(z) before Shown(z).\n" + TraceText(later));
        }

        [Test]
        public void ObserverOpeningAndClosingAViewDuringRequested_LaterObserverSeesRequestedThenShownThenClosed()
        {
            ViewHandle opened;
            ViewPhase phaseBeforeEnd;
            ViewPhase phaseAfterEnd;
            var later = OpenAndEndAViewDuringRequested(_harness.Owner, handle => handle.Close(), out opened, out phaseBeforeEnd, out phaseAfterEnd);

            AssertHeldBackShownComesBeforeClosed(later, opened, phaseBeforeEnd, phaseAfterEnd, CloseReason.Code);
        }

        [Test]
        public void ObserverOpeningAViewAndEndingItsOwnerDuringRequested_LaterObserverSeesRequestedThenShownThenClosed()
        {
            var owner = _harness.NewOwner();
            ViewHandle opened;
            ViewPhase phaseBeforeEnd;
            ViewPhase phaseAfterEnd;
            var later = OpenAndEndAViewDuringRequested(owner.Token, handle => owner.Cancel(), out opened, out phaseBeforeEnd, out phaseAfterEnd);

            AssertHeldBackShownComesBeforeClosed(later, opened, phaseBeforeEnd, phaseAfterEnd, CloseReason.OwnerEnded);
        }

        [Test]
        public void ObserverMakingTwoRefusedCallsDuringRequested_LaterObserverSeesTheRefusalsInCallOrder()
        {
            _harness.Row("x", ViewLayers.Popups);
            var fired = false;
            ContractAwaitProbe<ViewResult<NoResult>> first = null;
            ContractAwaitProbe<ViewResult<NoResult>> second = null;
            var opener = new ContractActionObserver(viewEvent =>
            {
                if (fired || viewEvent.Kind != ViewEventKind.Requested || viewEvent.ViewId != "x")
                    return;

                fired = true;
                first = ContractAwait.Watch(_harness.Navigator.Show("missing-1", _harness.Owner));
                second = ContractAwait.Watch(_harness.Navigator.Show("missing-2", _harness.Owner));
            });
            var later = new RecordingObserver();
            _harness.Navigator.AddObserver(opener, _harness.Owner);
            _harness.Navigator.AddObserver(later, _harness.Owner);

            _harness.Navigator.Show("x", _harness.Owner);

            Assert.That(fired, Is.True, "The observer never reacted to Requested.");
            Assert.That(first.ResumeCount, Is.EqualTo(1), "ShowAsync of 'missing-1' resumes once.");
            Assert.That(second.ResumeCount, Is.EqualTo(1), "ShowAsync of 'missing-2' resumes once.");
            var firstId = first.Result.RequestId;
            var secondId = second.Result.RequestId;
            var requestedX = IndexOf(later, ViewEventKind.Requested, "x");
            var firstRefusal = IndexOfRequest(later, ViewEventKind.NotShown, firstId);
            var secondRefusal = IndexOfRequest(later, ViewEventKind.NotShown, secondId);
            Assert.That(requestedX, Is.LessThan(firstRefusal), "Requested(x) before the first refusal.\n" + TraceText(later));
            Assert.That(firstRefusal, Is.LessThan(secondRefusal), "The refusals arrive in the order of the calls.\n" + TraceText(later));
            Assert.That(EventIn(later, firstId, ViewEventKind.NotShown).Rejected, Is.EqualTo(RejectReason.UnknownKey), "NotShown(missing-1) rejected");
            Assert.That(EventIn(later, secondId, ViewEventKind.NotShown).Rejected, Is.EqualTo(RejectReason.UnknownKey), "NotShown(missing-2) rejected");
        }

        [Test]
        public void ThrowingObserverThatClosedAllDuringRequested_StillDeliversHeldBackEventsToTheLaterObserver()
        {
            _harness.Row("x", ViewLayers.Popups);
            var failure = new InvalidOperationException("observer failed");
            var fired = false;
            var thrower = new ContractActionObserver(viewEvent =>
            {
                if (fired || viewEvent.Kind != ViewEventKind.Requested || viewEvent.ViewId != "x")
                    return;

                fired = true;
                _harness.Navigator.CloseAll(ViewLayers.Popups);
                throw failure;
            });
            var later = new RecordingObserver();
            _harness.Navigator.AddObserver(thrower, _harness.Owner);
            _harness.Navigator.AddObserver(later, _harness.Owner);

            var probe = ContractAwait.Watch(_harness.Navigator.Show("x", _harness.NewOwner().Token));

            Assert.That(fired, Is.True, "The observer never reacted to Requested.");
            Assert.That(probe.ResumeCount, Is.EqualTo(1), "ShowAsync of 'x' resumes once.");
            Assert.That(
                later.KindsFor(probe.Result.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.NotShown }),
                "The later observer still receives the NotShown held back during the failed dispatch.\n" + TraceText(later));
            Assert.That(probe.Result.Reason, Is.EqualTo(CloseReason.NotShown), "reason");
            Assert.That(probe.Result.Rejected, Is.EqualTo(RejectReason.Cancelled), "rejected");
            Assert.That(_harness.Errors.Count, Is.EqualTo(1), "One routed error: the observer's throw.");
            Assert.That(_harness.Errors[0].Exception, Is.SameAs(failure));
            Assert.That(_harness.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Observer));
        }

        [Test]
        public void OpenFromAnAwaiterContinuation_PublishesRequestedWhileWaitingAndShownWhileOpen()
        {
            _harness.Row("b", ViewLayers.Popups);
            var requestedPhase = ViewPhase.Ended;
            var shownPhase = ViewPhase.Ended;
            var spy = new ContractActionObserver(viewEvent =>
            {
                if (viewEvent.ViewId != "b")
                    return;

                ViewHandle handle;
                var phase = _harness.Navigator.TryGetOpen("b", out handle) ? handle.Phase : ViewPhase.Ended;
                if (viewEvent.Kind == ViewEventKind.Requested)
                    requestedPhase = phase;
                else if (viewEvent.Kind == ViewEventKind.Shown)
                    shownPhase = phase;
            });
            _harness.Navigator.AddObserver(spy, _harness.Owner);
            var run = ContractShownRun.Start(_harness, id: "a");
            var probe = ContractAwait.Watch(run.Result, () => _harness.Navigator.OpenView("b", _harness.Owner));

            run.View.Close();

            Assert.That(probe.ResumeCount, Is.EqualTo(1), "The continuation ran.");
            Assert.That(requestedPhase, Is.EqualTo(ViewPhase.Waiting), "Requested: the request is still waiting.\n" + Describe());
            Assert.That(shownPhase, Is.EqualTo(ViewPhase.Open), "Shown: the view is open.\n" + Describe());
        }

        [Test]
        public void TeardownFromAnObserverDuringRequested_CompletesEveryAwaiterExactlyOnce()
        {
            var run = ContractShownRun.Start(_harness, id: "p");
            ViewHandle shownHandle;
            Assert.That(_harness.Navigator.TryGetOpen("p", out shownHandle), Is.True, "The popup 'p' is open.");
            var shownResult = ContractAwait.Watch(run.Result);
            var shownClose = ContractAwait.Watch(shownHandle.WaitForCloseAsync(_harness.Owner));
            _harness.Row("x", ViewLayers.Popups);
            ContractAwaitProbe<ViewClosed> requestedClose = null;
            var fired = false;
            var destroyer = new ContractActionObserver(viewEvent =>
            {
                if (fired || viewEvent.Kind != ViewEventKind.Requested || viewEvent.ViewId != "x")
                    return;

                fired = true;
                ViewHandle handle;
                if (_harness.Navigator.TryGetOpen("x", out handle))
                    requestedClose = ContractAwait.Watch(handle.WaitForCloseAsync(_harness.Owner));
                _harness.Navigator.Teardown();
            });
            _harness.Navigator.AddObserver(destroyer, _harness.Owner);

            var requestedResult = ContractAwait.Watch(_harness.Navigator.Show("x", _harness.NewOwner().Token));

            Assert.That(fired, Is.True, "The observer never reacted to Requested.");
            Assert.That(requestedClose, Is.Not.Null, "The request was not found inside its Requested callback.");
            Assert.That(shownResult.ResumeCount, Is.EqualTo(1), "ShowAsync of the open popup");
            Assert.That(shownClose.ResumeCount, Is.EqualTo(1), "WaitForCloseAsync of the open popup");
            Assert.That(requestedResult.ResumeCount, Is.EqualTo(1), "ShowAsync of the request being dispatched");
            Assert.That(requestedClose.ResumeCount, Is.EqualTo(1), "WaitForCloseAsync of the request being dispatched");
            Assert.That(_harness.Diagnostics.FaultCount, Is.Zero, _harness.Diagnostics.Describe());
        }

        [Test]
        public void ObserverCallingCloseAllDuringRequested_WithAPopupOpen_LaterObserverSeesRequestedThenNotShownThenClosed()
        {
            var run = ContractShownRun.Start(_harness, id: "a");
            var shownResult = ContractAwait.Watch(run.Result);
            _harness.Row("b", ViewLayers.Popups);
            _harness.Stage.PlayMode = StageMode.Manual;
            var fired = false;
            var closer = new ContractActionObserver(viewEvent =>
            {
                if (fired || viewEvent.Kind != ViewEventKind.Requested || viewEvent.ViewId != "b")
                    return;

                fired = true;
                _harness.Navigator.CloseAll(ViewLayers.Popups);
            });
            var later = new RecordingObserver();
            _harness.Navigator.AddObserver(closer, _harness.Owner);
            _harness.Navigator.AddObserver(later, _harness.Owner);

            var requestedResult = ContractAwait.Watch(_harness.Navigator.Show("b", _harness.NewOwner().Token));
            Assert.That(_harness.Stage.PendingPlayCount, Is.GreaterThan(0), "The hide of 'a' is running.\n" + _harness.Stage.DescribeCalls());
            CompleteEveryPlay();

            Assert.That(fired, Is.True, "The observer never reacted to Requested.");
            Assert.That(requestedResult.ResumeCount, Is.EqualTo(1), "ShowAsync of 'b' resumes once.");
            Assert.That(shownResult.ResumeCount, Is.EqualTo(1), "ShowAsync of 'a' resumes once.");
            Assert.That(
                Trace(later),
                Is.EqualTo(new[] { "Requested b", "NotShown b", "Closed a" }),
                "The later observer sees Requested(b), then the events CloseAll caused.");
            Assert.That(EventIn(later, requestedResult.Result.RequestId, ViewEventKind.NotShown).Rejected, Is.EqualTo(RejectReason.Cancelled), "NotShown(b) rejected");
            Assert.That(EventIn(later, shownResult.Result.RequestId, ViewEventKind.Closed).Reason, Is.EqualTo(CloseReason.Code), "Closed(a) reason");
            Assert.That(_harness.ShieldCount, Is.Zero, "shield count after the hide");
        }

        // Helpers

        // Settles every pending play, including plays a completed one starts.
        private void CompleteEveryPlay()
        {
            while (_harness.Stage.PendingPlayCount > 0)
                _harness.Stage.CompletePlay();
        }

        // Popup 'x' and screen 'z'; on Requested(x) an observer opens z with the owner, then ends it with the action.
        private RecordingObserver OpenAndEndAViewDuringRequested(
            CancellationToken owner,
            Action<ViewHandle> end,
            out ViewHandle opened,
            out ViewPhase phaseBeforeEnd,
            out ViewPhase phaseAfterEnd)
        {
            _harness.Row("x", ViewLayers.Popups).Row("z", ViewLayers.Screens);
            var fired = false;
            var handle = default(ViewHandle);
            var before = ViewPhase.Ended;
            var after = ViewPhase.Open;
            var opener = new ContractActionObserver(viewEvent =>
            {
                if (fired || viewEvent.Kind != ViewEventKind.Requested || viewEvent.ViewId != "x")
                    return;

                fired = true;
                handle = _harness.Navigator.OpenView("z", owner);
                before = handle.Phase;
                end(handle);
                after = handle.Phase;
            });
            var later = new RecordingObserver();
            _harness.Navigator.AddObserver(opener, _harness.Owner);
            _harness.Navigator.AddObserver(later, _harness.Owner);

            _harness.Navigator.Show("x", _harness.Owner);

            Assert.That(fired, Is.True, "The observer never reacted to Requested.");
            opened = handle;
            phaseBeforeEnd = before;
            phaseAfterEnd = after;
            return later;
        }

        private static void AssertHeldBackShownComesBeforeClosed(
            RecordingObserver later,
            ViewHandle opened,
            ViewPhase phaseBeforeEnd,
            ViewPhase phaseAfterEnd,
            CloseReason expected)
        {
            Assert.That(phaseBeforeEnd, Is.EqualTo(ViewPhase.Open), "'z' is open inside the callback, so its Shown is held back.\n" + TraceText(later));
            Assert.That(phaseAfterEnd, Is.EqualTo(ViewPhase.Ended), "'z' ends inside the callback, while its Shown is still held back.\n" + TraceText(later));
            Assert.That(
                later.KindsFor(opened.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }),
                "The later observer sees the held-back Shown before Closed, once each.\n" + TraceText(later));
            Assert.That(EventIn(later, opened.RequestId, ViewEventKind.Closed).Reason, Is.EqualTo(expected), "Closed reason");
        }

        // Every event except Settled as "Kind viewId", in the order the observer received them.
        private static string[] Trace(RecordingObserver observer)
        {
            var trace = new List<string>();
            for (var i = 0; i < observer.Events.Count; i++)
            {
                var viewEvent = observer.Events[i];
                if (viewEvent.Kind != ViewEventKind.Settled)
                    trace.Add(viewEvent.Kind + " " + viewEvent.ViewId);
            }

            return trace.ToArray();
        }

        private static string TraceText(RecordingObserver observer)
        {
            return "[" + string.Join(", ", Trace(observer)) + "]";
        }

        private static int IndexOf(RecordingObserver observer, ViewEventKind kind, string viewId)
        {
            for (var i = 0; i < observer.Events.Count; i++)
            {
                if (observer.Events[i].Kind == kind && observer.Events[i].ViewId == viewId)
                    return i;
            }

            Assert.Fail("The observer received no " + kind + " for '" + viewId + "': [" + string.Join(", ", Trace(observer)) + "]");
            return -1;
        }

        private static int IndexOfRequest(RecordingObserver observer, ViewEventKind kind, int requestId)
        {
            for (var i = 0; i < observer.Events.Count; i++)
            {
                if (observer.Events[i].Kind == kind && observer.Events[i].RequestId == requestId)
                    return i;
            }

            Assert.Fail("The observer received no " + kind + " for request " + requestId + ": " + TraceText(observer));
            return -1;
        }

        private static ViewEvent EventIn(RecordingObserver observer, int requestId, ViewEventKind kind)
        {
            var events = observer.EventsFor(requestId);
            for (var i = 0; i < events.Length; i++)
            {
                if (events[i].Kind == kind)
                    return events[i];
            }

            Assert.Fail("Request " + requestId + " has no " + kind + " event: [" + string.Join(", ", Trace(observer)) + "]");
            return default(ViewEvent);
        }

        private ViewEvent EventOf(int requestId, ViewEventKind kind)
        {
            var events = _harness.Observer.EventsFor(requestId);
            for (var i = 0; i < events.Length; i++)
            {
                if (events[i].Kind == kind)
                    return events[i];
            }

            Assert.Fail("Request " + requestId + " published no " + kind + " event.\n" + Describe());
            return default(ViewEvent);
        }

        private void AssertEveryRequestIsLegal()
        {
            var events = _harness.Observer.Events;
            var seen = new List<int>();

            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == ViewEventKind.Settled)
                {
                    Assert.That(events[i].RequestId, Is.Zero, "Settled carries request id zero.");
                    continue;
                }

                Assert.That(events[i].RequestId, Is.Not.Zero, "Only Settled has request id zero.");
                if (!seen.Contains(events[i].RequestId))
                    seen.Add(events[i].RequestId);
            }

            for (var i = 0; i < seen.Count; i++)
            {
                var kinds = _harness.Observer.KindsFor(seen[i]);
                Assert.That(IsLegalSequence(kinds), Is.True, "Request " + seen[i] + " published [" + string.Join(", ", kinds) + "], which I6 does not allow.");
            }
        }

        // Per request id: [Enqueued] then NotShown, or [Enqueued] Requested NotShown, or [Enqueued] Requested Shown Closed.
        private static bool IsLegalSequence(ViewEventKind[] kinds)
        {
            var start = kinds.Length > 0 && kinds[0] == ViewEventKind.Enqueued ? 1 : 0;

            return Matches(kinds, start, ViewEventKind.NotShown)
                || Matches(kinds, start, ViewEventKind.Requested, ViewEventKind.NotShown)
                || Matches(kinds, start, ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed);
        }

        private static bool Matches(ViewEventKind[] kinds, int start, params ViewEventKind[] expected)
        {
            if (kinds.Length - start != expected.Length)
                return false;

            for (var i = 0; i < expected.Length; i++)
            {
                if (kinds[start + i] != expected[i])
                    return false;
            }

            return true;
        }

        private string Describe()
        {
            return "events: [" + string.Join(", ", _harness.Observer.AllKinds()) + "]";
        }
    }
}
