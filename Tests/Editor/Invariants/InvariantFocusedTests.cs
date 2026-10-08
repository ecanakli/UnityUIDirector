using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Hand-written sequences next to the randomized run: the checker on calm flows, the checker catching planted faults,
    // and the three package rules the randomized run is expected to trip over, each pinned in a few steps.
    public sealed class InvariantFocusedTests
    {
        private InvariantWorld _world;
        private InvariantChecker _checker;

        [SetUp]
        public void SetUp()
        {
            _world = new InvariantWorld(1, manualStage: true, chaos: false);
            _checker = new InvariantChecker(_world);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        private Navigator Nav
        {
            get { return _world.Navigator; }
        }

        private FakeStage Stage
        {
            get { return _world.Stage; }
        }

        // Snapshot reading

        [Test]
        public void Snapshot_ReadsLayersRecordsHoldersAndKeptInstances_FromDescribe()
        {
            var handle = Open("map");
            _checker.SyncStage();

            var snapshot = Parse();
            Assert.That(snapshot.State, Is.EqualTo("busy"));
            Assert.That(snapshot.ShieldCount, Is.EqualTo(1));
            Assert.That(snapshot.Holders.Count, Is.EqualTo(1));
            Assert.That(snapshot.Holders[0].Kind, Is.EqualTo(ShieldHolderKind.Operation));
            Assert.That(snapshot.Holders[0].RequestId, Is.EqualTo(handle.RequestId));

            var screens = snapshot.Layer("screens");
            Assert.That(screens, Is.Not.Null);
            Assert.That(screens.Running, Is.True);
            Assert.That(screens.Records.Count, Is.EqualTo(1));
            Assert.That(screens.Records[0].RequestId, Is.EqualTo(handle.RequestId));
            Assert.That(screens.Records[0].ViewId, Is.EqualTo("map"));
            Assert.That(screens.Records[0].Phase, Is.EqualTo(ViewPhase.Loading));
            Assert.That(screens.Records[0].Slot, Is.EqualTo(1));
            Assert.That(screens.Records[0].ClosePending, Is.False);

            Load();
            Play();
            Assert.That(Parse().Find(handle.RequestId).Phase, Is.EqualTo(ViewPhase.Open));

            // A kept row is stashed on close and Describe names it.
            handle.Close();
            Settle();

            snapshot = Parse();
            Assert.That(snapshot.State, Is.EqualTo("settled"));
            Assert.That(snapshot.TotalRecords, Is.Zero);
            Assert.That(snapshot.KeptIds, Is.EqualTo(new[] { "map" }));
        }

        [Test]
        public void Snapshot_ReadsBeingReplaced_FromDescribe()
        {
            var first = OpenItem(1);
            Load();
            Play();
            var home = Open("home");
            Load();
            Play();

            var second = OpenItem(2);

            var snapshot = Parse();
            Assert.That(snapshot.Find(home.RequestId).Phase, Is.EqualTo(ViewPhase.Closing), "Setup: the pop-to is hiding the screen above.");
            var old = snapshot.Find(first.RequestId);
            Assert.That(old.BeingReplaced, Is.True, "old request being replaced");
            Assert.That(old.ClosePending, Is.False, "old request close pending");
            Assert.That(snapshot.Find(second.RequestId).BeingReplaced, Is.False, "new request being replaced");
        }

        [Test]
        public void Snapshot_ReadsClosePendingAndBeingReplaced_FromTheSameLine()
        {
            var first = OpenItem(1);
            Load();
            Play();
            Open("home");
            Load();
            Play();
            OpenItem(2);

            // The old request may be closed while it waits to be replaced.
            first.Close();

            var old = Parse().Find(first.RequestId);
            Assert.That(old.ClosePending, Is.True, "old request close pending");
            Assert.That(old.BeingReplaced, Is.True, "old request being replaced");
            Check("old request closed while it waits to be replaced");
        }

        // The checker on flows the package allows

        [Test]
        public void CalmScreenAndPopupFlow_PassesEveryInvariant_AfterEveryStep()
        {
            // A popup with an awaiter over a screen: backdrop under the popup, screen not interactive.
            Open("home");
            Check("home requested");
            Load();
            Check("home loaded");
            Play();
            Check("home shown");

            var showTask = Nav.ShowAsync(ViewKey.Create("confirm"), default(NoArgs), _world.Harness.Owner);
            var awaiter = new InvariantAwaiter(1, "confirm", false, null);
            awaiter.Show = showTask;
            awaiter.RequestId = _world.Harness.Observer.LastOf(ViewEventKind.Requested).RequestId;
            _world.Awaiters.Add(awaiter);
            Check("confirm requested");
            Load();
            Play();
            Check("confirm shown");

            // A covering popup hides what is below it once it is open.
            Open("tip");
            Load();
            Play();
            Check("tip shown");

            ViewOf("tip").Context.Complete(default(NoResult));
            Check("tip complete latched");
            Settle();
            Check("tip closed");

            var hold = Nav.HoldInput("scripted", _world.Harness.Owner);
            _world.Holds.Add(new InvariantHold(1, hold, null, false));
            Check("hold added");
            hold.Remove();
            _world.Holds[0].Removed = true;
            Check("hold removed");

            ViewOf("confirm").Context.Complete(default(NoResult));
            Settle();
            Check("confirm closed");
            Assert.That(showTask.Status, Is.EqualTo(UniTaskStatus.Succeeded));

            // A second screen closes the popups opened before it, then back closes it.
            Open("poll");
            Load();
            Play();
            Check("poll shown");
            Open("shop");
            Load();
            Play();
            Check("shop shown, popups closed by navigation");

            Assert.That(Nav.Back(), Is.True);
            Settle();
            Check("back from shop");

            OpenItem(0);
            Load();
            Play();
            Check("item shown");

            Nav.CloseAll(ViewLayers.Screens);
            Check("close-all started");
            Settle();
            Check("close-all finished");

            Nav.Teardown();
            _checker.CheckAfterTeardown();
            _checker.CheckInstancesFinal();
            AssertNoViolations("after teardown");
        }

        [Test]
        public void ReopenInPlace_KeepsOneInstance_AndPassesEveryInvariant()
        {
            var first = OpenItem(1);
            Load();
            Play();
            Check("item(1) shown");

            var second = OpenItem(2);
            Check("item(2) reopened in place");

            Assert.That(second.RequestId, Is.Not.EqualTo(first.RequestId));
            Assert.That(first.IsAlive, Is.False);
            Assert.That(second.IsAlive, Is.True);
            Assert.That(second.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(Stage.Instances.Count, Is.EqualTo(1), "The instance is not released and made again between the two opens.");
            Assert.That(_world.Harness.Observer.KindsFor(first.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }));
            Assert.That(_world.Harness.Observer.EventsFor(first.RequestId)[2].Reason, Is.EqualTo(CloseReason.Navigated));
            Assert.That(_world.Harness.Observer.KindsFor(second.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }));

            second.Close();
            Settle();
            Check("item closed");
            Stage.AssertAllReleased();
        }

        [Test]
        public void CloseAllFollowedByOpenOfTheSameId_PassesEveryInvariant_AfterEveryStep()
        {
            var home = Open("home");
            Load();
            Play();
            Check("home shown");
            Open("shop");
            Load();
            Play();
            Check("shop shown");
            OpenItem(1);
            Load();
            Play();
            Check("item(1) shown");

            // A view the close-all removes does not count as open for a new open of its id.
            Nav.CloseAll(ViewLayers.Screens);
            Check("close-all started");
            var newHome = Open("home");
            Assert.That(newHome.IsAlive, Is.True, "Setup: the open of home during the close-all is accepted.");
            Assert.That(newHome.RequestId, Is.Not.EqualTo(home.RequestId), "The open of home during the close-all is a new request.");
            Check("home opened while the close-all runs");
            SettleChecked("close-all and the new home");

            Assert.That(home.IsAlive, Is.False, "The first home ended with the close-all.");
            AssertOpenScreens("after the close-all", newHome);
            Assert.That(Nav.IsBusy, Is.False, "The navigator is settled after the close-all and the new open.");
        }

        [Test]
        public void CloseToFollowedByOpenOfTheSameId_PassesEveryInvariant_AfterEveryStep()
        {
            var home = Open("home");
            Load();
            Play();
            Check("home shown");
            var shop = Open("shop");
            Load();
            Play();
            Check("shop shown");

            // A view the pop removes does not count as open for a new open of its id.
            Assert.That(Nav.CloseTo("home"), Is.True);
            Check("close-to started");
            var newShop = Open("shop");
            Assert.That(newShop.IsAlive, Is.True, "Setup: the open of shop while the pop is pending is accepted.");
            Assert.That(newShop.RequestId, Is.Not.EqualTo(shop.RequestId), "The open of shop while the pop is pending is a new request.");
            Check("shop opened while the pop is pending");
            SettleChecked("close-to and the new shop");

            Assert.That(shop.IsAlive, Is.False, "The first shop ended with the pop.");
            AssertOpenScreens("after the close-to", home, newShop);
            Assert.That(Nav.IsBusy, Is.False, "The navigator is settled after the pop and the new open.");
        }

        [Test]
        public void CloseToWithAnOpenInFlightAndUnstartedRequestsAbove_PassesEveryInvariant_AfterEveryStep()
        {
            var home = Open("home");
            Load();
            Play();
            Check("home shown");
            var settings = Open("settings");
            Load();
            Play();
            Check("settings shown above home");

            var item = OpenItem(1);
            var shop = Open("shop");
            var snapshot = Parse();
            Assert.That(snapshot.Find(item.RequestId).Phase, Is.EqualTo(ViewPhase.Loading), "Setup: item(1) is loading.");
            Assert.That(snapshot.Find(shop.RequestId).Phase, Is.EqualTo(ViewPhase.Waiting), "Setup: shop waits behind item(1).");
            Check("item(1) loading and shop waiting");

            Assert.That(Nav.CloseTo("home"), Is.True);
            Check("close-to called behind the running open");
            Assert.That(item.IsAlive, Is.False, "The loading request above the target is withdrawn at the call.");
            Assert.That(shop.IsAlive, Is.False, "The waiting request above the target is withdrawn at the call.");

            SettleChecked("close-to");

            Assert.That(settings.IsAlive, Is.False, "The screen above the target ended with the pop.");
            AssertOpenScreens("after the close-to", home);
            Assert.That(Nav.IsBusy, Is.False, "The navigator is settled after the pop.");
        }

        [Test]
        public void CloseAllWithAShowInFlight_PassesEveryInvariant_AfterEveryStep()
        {
            var home = Open("home");
            Load();
            Play();
            Check("home shown");
            var settings = Open("settings");
            Load();
            Play();
            Check("settings shown above home");
            var item = OpenItem(1);
            Load();
            Assert.That(Parse().Find(item.RequestId).Phase, Is.EqualTo(ViewPhase.Opening), "Setup: the show of item(1) is running.");
            Check("item(1) showing");

            // The open views are latched while the runner is still busy with the show.
            Nav.CloseAll(ViewLayers.Screens);
            Assert.That(Nav.ShieldCount, Is.EqualTo(4), "One hold per latched open view, two for the view still showing.");
            Check("close-all called during the show");

            SettleChecked("close-all");

            Assert.That(home.IsAlive, Is.False, "home ended with the close-all.");
            Assert.That(settings.IsAlive, Is.False, "settings ended with the close-all.");
            Assert.That(item.IsAlive, Is.False, "item(1) ended with the close-all.");
            AssertOpenScreens("after the close-all");
            Assert.That(Nav.ShieldCount, Is.Zero, "No hold is left after the close-all.");
            Assert.That(Nav.IsBusy, Is.False, "The navigator is settled after the close-all.");
        }

        [Test]
        public void ReopenInPlaceWithAScreenAbove_ReplacementWithdrawn_PassesEveryInvariant_AfterEveryStep()
        {
            var first = OpenItem(1);
            Load();
            Play();
            Check("item(1) shown");
            var itemInstance = Stage.LastInstanceOf("item");
            var home = Open("home");
            Load();
            Play();
            Check("home shown above item(1)");

            var second = OpenItem(2);
            Assert.That(Parse().Find(home.RequestId).Phase, Is.EqualTo(ViewPhase.Closing), "Setup: the pop-to is hiding the screen above.");
            Check("item(2) waits behind the pop");
            var trace = _world.Traces[first.RequestId];
            var beginsBefore = trace.VisibleBegins;

            Assert.That(Nav.Back(), Is.True);
            Assert.That(second.IsAlive, Is.False, "Setup: back withdrew item(2) before its open.");
            Check("item(2) withdrawn during the hide");
            Assert.That(Parse().Find(first.RequestId).BeingReplaced, Is.False, "The old request is no longer being replaced once its replacement ended.");
            Assert.That(trace.VisibleBegins - beginsBefore, Is.EqualTo(1), "OnVisible of the old request ran again when it was restored.");
            Assert.That(trace.VisibleBegins - trace.VisibleEnds, Is.EqualTo(1), "The old request has an open visible scope after the restore.");

            Play();
            Check("hide of home finished");
            SettleChecked("after the hide");

            Assert.That(first.IsAlive, Is.True, "The old request stays once its replacement is withdrawn.");
            Assert.That(first.Phase, Is.EqualTo(ViewPhase.Open), "The old request is open again.");
            Assert.That(Stage.LastInstanceOf("item"), Is.SameAs(itemInstance), "No new item instance was acquired.");
            Assert.That(itemInstance.Visual.Hidden, Is.False, "The restored item(1) is applied visible.");
            Assert.That(itemInstance.Visual.Interactive, Is.True, "The restored item(1) is applied interactive.");
            Assert.That(trace.VisibleBegins - trace.VisibleEnds, Is.EqualTo(1), "The old request keeps its visible scope after the hide.");

            // The restored request counts as live again: a new open of the id reopens it in place.
            var third = OpenItem(3);
            Check("item(3) reopened in place");
            SettleChecked("item(3) open");
            Assert.That(third.Phase, Is.EqualTo(ViewPhase.Open), "item(3) is open.");
            Assert.That(first.IsAlive, Is.False, "item(1) handed its instance to item(3).");
            Assert.That(Stage.LastInstanceOf("item"), Is.SameAs(itemInstance), "item(3) took over the instance of item(1).");
        }

        // The checker catches what it is built to catch

        [Test]
        public void Checker_ReportsI3_WhenOnOpenRanTwiceForOneRequest()
        {
            _world.TraceFor(77, "home").OpenCalls = 2;

            _checker.CheckStep();

            AssertViolation("I3");
        }

        [Test]
        public void Checker_ReportsI4_WhenAnAwaiterNeverCompletes()
        {
            var never = new UniTaskCompletionSource<ViewResult<NoResult>>();
            var awaiter = new InvariantAwaiter(1, "home", false, null);
            awaiter.Show = never.Task;
            _world.Awaiters.Add(awaiter);

            Nav.Teardown();
            _checker.CheckAfterTeardown();

            AssertViolation("I4");
        }

        [Test]
        public void Checker_ReportsI10_WhenTheStageWasToldTheWrongVisualState()
        {
            Open("home");
            Load();
            Play();
            Check("home shown");

            // Home is open, uncovered and interactive; plant the opposite on the stage.
            var instance = Stage.LastInstanceOf("home");
            instance.Visual = new ViewVisualState(true, false, instance.Visual.SortingOrder, instance.Visual.CoveredMode);

            _checker.CheckStep();

            AssertViolation("I10");
        }

        [Test]
        public void Checker_ReportsI2_WhenAnInputHoldIsMissingFromTheShield()
        {
            _world.Holds.Add(new InvariantHold(1, default(ViewRegistration), null, false));

            _checker.CheckStep();

            AssertViolation("I2.count");
        }

        // Rules the randomized run is expected to trip over; each asserts the specified rule, not the current behavior.

        // A close cause during Opening is "Shield +1" on top of the hold the open already carries.
        [Test]
        public void Shield_CountsTheOpenAndThePendingClose_WhenCloseIsLatchedDuringOpening()
        {
            var handle = Open("home");
            Load();
            Assert.That(Nav.ShieldCount, Is.EqualTo(1), "The open holds the shield from acceptance.");

            handle.Close();

            Assert.That(Nav.ShieldCount, Is.EqualTo(2), "A close cause during Opening adds one hold to the open's hold.");

            Play();
            Assert.That(Nav.ShieldCount, Is.EqualTo(1), "When the show ends the open's hold goes; the pending close's hold stays while the hide runs.");

            Play();
            Assert.That(Nav.ShieldCount, Is.Zero, "I2: nothing is in flight once the view is finalized.");
            Assert.That(Stage.ShieldBlocking, Is.False);
        }

        // I2: a pop-to in flight is an operation in flight on a shielding layer, so the shield stays up while it hides the top view.
        [Test]
        public void Shield_StaysUp_WhileAPopToHidesTheTopView()
        {
            Open("home");
            Load();
            Play();

            var shop = Open("shop");
            Load();
            Assert.That(Nav.CloseTo("home"), Is.True);

            // The show of shop ends; the pop-to that was queued behind it starts the hide transition.
            Play();
            Assert.That(Parse().Find(shop.RequestId).Phase, Is.EqualTo(ViewPhase.Closing), "Setup: the pop-to is hiding the top view.");

            Assert.That(Nav.ShieldCount, Is.GreaterThanOrEqualTo(1), "I2: the pop-to operation is in flight, so the shield count is at least 1.");
            Assert.That(Stage.ShieldBlocking, Is.True, "Input stays blocked while the layer runs the pop-to.");

            Play();
            Assert.That(Nav.ShieldCount, Is.Zero);
        }

        // I6: a terminate cause during Opening ends the request as either [Requested, NotShown] or [Requested, Shown, Closed].
        [TestCase(true)]
        [TestCase(false)]
        public void Events_WhenTheRequestIsTerminatedDuringOpening_FollowOneOfTheTwoSequencesOfI6(bool destroyInstance)
        {
            var owner = _world.Harness.NewOwner();
            var task = Nav.ShowAsync(ViewKey.Create("home"), default(NoArgs), owner.Token);
            Load();

            var requestId = _world.Harness.Observer.LastOf(ViewEventKind.Requested).RequestId;
            Assert.That(Parse().Find(requestId).Phase, Is.EqualTo(ViewPhase.Opening), "Setup: the show transition is running.");

            if (destroyInstance)
                Stage.RaiseInstanceLost(Stage.LastInstanceOf("home"), true);
            else
                owner.Cancel();

            var kinds = _world.Harness.Observer.KindsFor(requestId);
            var notShown = new[] { ViewEventKind.Requested, ViewEventKind.NotShown };
            var shown = new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed };
            Assert.That(kinds, Is.EqualTo(notShown).Or.EqualTo(shown),
                "I6: per request id the sequence is Requested, NotShown or Requested, Shown, Closed. Got " + Join(kinds) + ".");

            var result = task.ResultNow();
            Assert.That(result.WasShown, Is.EqualTo(kinds.Length == 3), "The awaiter and the published events describe the same end.");
        }

        // Helpers

        private ViewHandle Open(string id)
        {
            return Nav.Open(ViewKey.Create(id), default(NoArgs), _world.Harness.Owner);
        }

        private ViewHandle OpenItem(int argument)
        {
            return Nav.Open(ViewKey.Create<int>("item"), argument, _world.Harness.Owner);
        }

        private InvariantViewBase ViewOf(string id)
        {
            for (var i = _world.Views.Count - 1; i >= 0; i--)
            {
                var view = _world.Views[i];
                if (view.ViewId == id && view.Context != null)
                    return view;
            }

            Assert.Fail("No view of '" + id + "' has been opened.");
            return null;
        }

        private InvariantSnapshot Parse()
        {
            return InvariantSnapshot.Parse(_world.Harness.Describe());
        }

        // Completes the oldest pending load.
        private void Load()
        {
            _checker.SyncStage();
            Assert.That(_checker.PendingAcquires.Count, Is.GreaterThan(0), "No load is pending.");
            Stage.CompleteAcquire(0);
            _checker.AcquireTaken(0);
        }

        // Completes the oldest pending transition.
        private void Play()
        {
            _checker.SyncStage();
            Assert.That(_checker.PendingPlays.Count, Is.GreaterThan(0), "No transition is pending.");
            Stage.CompletePlay(0);
            _checker.PlayTaken(0);
        }

        private void Settle()
        {
            for (var guard = 0; guard < 100; guard++)
            {
                _checker.SyncStage();

                if (_checker.PendingAcquires.Count > 0)
                    Load();
                else if (_checker.PendingPlays.Count > 0)
                    Play();
                else
                    return;
            }

            Assert.Fail("The stage never ran out of pending loads and transitions.");
        }

        // Settle with a Check after every load and every transition.
        private void SettleChecked(string when)
        {
            for (var step = 1; step <= 100; step++)
            {
                _checker.SyncStage();

                if (_checker.PendingAcquires.Count > 0)
                {
                    Load();
                    Check(when + ", load " + step);
                }
                else if (_checker.PendingPlays.Count > 0)
                {
                    Play();
                    Check(when + ", transition " + step);
                }
                else
                {
                    return;
                }
            }

            Assert.Fail("The stage never ran out of pending loads and transitions " + when + ".");
        }

        // Fails unless the screens layer holds exactly these requests, bottom first, each one Open.
        private void AssertOpenScreens(string when, params ViewHandle[] expected)
        {
            var screens = Parse().Layer("screens");
            Assert.That(screens, Is.Not.Null, "The screens layer is described.");

            var actual = new int[screens.Records.Count];
            for (var i = 0; i < actual.Length; i++)
                actual[i] = screens.Records[i].RequestId;

            var ids = new int[expected.Length];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = expected[i].RequestId;

            Assert.That(actual, Is.EqualTo(ids), "Requests on the screens layer " + when + ", bottom first.");
            for (var i = 0; i < screens.Records.Count; i++)
                Assert.That(screens.Records[i].Phase, Is.EqualTo(ViewPhase.Open), "Phase of " + screens.Records[i] + " " + when + ".");
        }

        private void Check(string when)
        {
            _world.CurrentStep++;
            _checker.CheckStep();
            AssertNoViolations(when);
        }

        private void AssertNoViolations(string when)
        {
            if (!_checker.HasViolations)
                return;

            var text = new StringBuilder("Invariants violated ").Append(when).Append(":\n");
            for (var i = 0; i < _checker.Violations.Count; i++)
                text.Append("  ").Append(_checker.Violations[i]).Append('\n');
            text.Append(_checker.DescribeForReport());
            Assert.Fail(text.ToString());
        }

        private void AssertViolation(string id)
        {
            for (var i = 0; i < _checker.Violations.Count; i++)
            {
                if (_checker.Violations[i].Id == id)
                    return;
            }

            Assert.Fail("The checker did not report " + id + ". It reported " + _checker.Violations.Count + " other violation(s).");
        }

        private static string Join(ViewEventKind[] kinds)
        {
            var text = new StringBuilder("[");
            for (var i = 0; i < kinds.Length; i++)
            {
                if (i > 0)
                    text.Append(", ");
                text.Append(kinds[i]);
            }

            return text.Append(']').ToString();
        }
    }
}
