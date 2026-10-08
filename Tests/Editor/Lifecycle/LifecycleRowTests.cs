using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // The request lifecycle, one test (or one small group) per phase and cause; terminate causes are in LifecycleTerminateTests.
    public sealed class LifecycleRowTests
    {
        public enum CloseCause
        {
            Complete,
            ViewClose,
            HandleClose,
            Back,
            Backdrop,
            CloseAll
        }

        private const string Target = LifecycleDriver.Target;
        private const string Blocker = LifecycleDriver.Blocker;

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

        // Withdrawn while waiting or loading: not shown, the shield hold is gone and the load result is released.
        // strictNoLoad is off for CloseAll: its no-load rule has its own test in the serialization group.
        private void AssertWithdrawn(
            UniTask<ViewResult<bool>> task,
            ViewPhase phase,
            RejectReason reason,
            int expectedShield,
            bool strictNoLoad = true)
        {
            var result = task.ResultNow();
            LifecycleDriver.AssertNotShown(result, reason);
            Assert.That(
                _h.Observer.KindsFor(result.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.NotShown }));
            Assert.That(_h.ShieldCount, Is.EqualTo(expectedShield), _h.Describe());
            Assert.That(_h.Navigator.IsOpen(Target), Is.False);

            if (phase == ViewPhase.Loading)
            {
                var calls = _h.Stage.CallsOf(StageCallKind.Acquire);
                StageCall acquire = null;
                for (var i = 0; i < calls.Length; i++)
                {
                    if (calls[i].ViewId == Target)
                        acquire = calls[i];
                }

                Assert.That(acquire, Is.Not.Null);
                Assert.That(acquire.Token.IsCancellationRequested, Is.True, "The load step is cancelled.");

                // The load answers late: the stage releases that instance itself.
                _h.Stage.CompleteAcquire();
                var late = _h.Stage.LastInstanceOf(Target);
                Assert.That(late, Is.Not.Null);
                Assert.That(late.ReleasedLate, Is.True);
                Assert.That(late.ReleaseCount, Is.EqualTo(1));
                return;
            }

            // A waiting request never reaches the provider, even when the runner moves on.
            _h.Stage.CompleteAcquire();
            var targetAcquires = 0;
            var all = _h.Stage.CallsOf(StageCallKind.Acquire);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].ViewId == Target)
                    targetAcquires++;
            }

            if (strictNoLoad)
                Assert.That(targetAcquires, Is.Zero, "A withdrawn waiting request is never loaded.");
        }

        // (call) accepted -> Waiting

        [Test]
        public void Accepted_RecordJoinsItsLayer_TakesAShieldHold_AndPublishesRequested()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Blocker, Target);

            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Waiting, _h.Owner, Blocker);

            var handle = LifecycleDriver.HandleOf(_h, Target);
            Assert.That(task.IsPending(), Is.True);
            Assert.That(_h.RequestCount(ViewLayers.Popups), Is.EqualTo(2));
            Assert.That(_h.ShieldCount, Is.EqualTo(2), "One hold per request on a shielding layer.");
            Assert.That(_h.Stage.ShieldBlocking, Is.True);

            Assert.That(_h.Observer.KindsFor(handle.RequestId), Is.EqualTo(new[] { ViewEventKind.Requested }));
            var requested = _h.Observer.EventsFor(handle.RequestId)[0];
            Assert.That(requested.ViewId, Is.EqualTo(Target));
            Assert.That(requested.LayerId, Is.EqualTo(ViewLayers.Popups.Id));
        }

        [Test]
        public void Accepted_RequestedEventCarriesTheSourceTag()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var options = new OpenOptions { Source = "settings-button" };

            var handle = _h.Navigator.Open(LifecycleDriver.KeyOf(Target), _h.Owner, options);

            var requested = _h.Observer.EventsFor(handle.RequestId)[0];
            Assert.That(requested.Kind, Is.EqualTo(ViewEventKind.Requested));
            Assert.That(requested.Source, Is.EqualTo("settings-button"));
        }

        [Test]
        public void Accepted_RequestedIsPublishedBeforeTheLoadStarts()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var acquiresAtRequested = -1;
            var observer = new LifecycleActionObserver(e =>
            {
                if (e.Kind == ViewEventKind.Requested)
                    acquiresAtRequested = _h.Stage.CountOf(StageCallKind.Acquire);
            });
            _h.Navigator.AddObserver(observer, _h.Owner);

            _h.Navigator.Open(LifecycleDriver.KeyOf(Target), _h.Owner);

            Assert.That(acquiresAtRequested, Is.Zero, "The state Requested describes is in place before any load.");
        }

        [Test]
        public void Accepted_OnALayerThatDoesNotShield_TakesNoShieldHold()
        {
            var quiet = new ViewLayer("quiet", 0, ViewLayerRules.None);
            using (var h = new NavigatorHarness(layers: new[] { quiet }))
            {
                LifecycleDriver.AddRows(h, quiet, Target);

                LifecycleDriver.Reach(h, Target, ViewPhase.Loading, h.Owner);

                Assert.That(h.ShieldCount, Is.Zero);
                Assert.That(h.Stage.ShieldBlocking, Is.False);
            }
        }

        // Waiting -> Loading

        [Test]
        public void Waiting_BecomesLoading_OnlyWhenTheLayerRunnerReachesIt()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Blocker, Target);
            LifecycleDriver.Reach(_h, Target, ViewPhase.Waiting, _h.Owner, Blocker);

            var acquiresWhileWaiting = _h.Stage.CallsOf(StageCallKind.Acquire);
            Assert.That(acquiresWhileWaiting.Length, Is.EqualTo(1));
            Assert.That(acquiresWhileWaiting[0].ViewId, Is.EqualTo(Blocker));

            // The blocker finishes loading and opens; the runner then takes the next operation.
            _h.Stage.CompleteAcquire();

            Assert.That(LifecycleDriver.HandleOf(_h, Blocker).Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(LifecycleDriver.HandleOf(_h, Target).Phase, Is.EqualTo(ViewPhase.Loading));
            Assert.That(_h.Stage.PendingAcquireCount, Is.EqualTo(1));
            var acquires = _h.Stage.CallsOf(StageCallKind.Acquire);
            Assert.That(acquires.Length, Is.EqualTo(2));
            Assert.That(acquires[1].ViewId, Is.EqualTo(Target));
        }

        [Test]
        public void LoadIndicator_ShowsAfterTheDelay_AndHidesWhenTheLoadEnds()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            LifecycleDriver.Reach(_h, Target, ViewPhase.Loading, _h.Owner);

            _h.Advance(0.3f);
            Assert.That(_h.Stage.LoadIndicatorVisible, Is.False, "Not before the 0.4 s delay.");

            _h.Advance(0.2f);
            Assert.That(_h.Stage.LoadIndicatorVisible, Is.True, "A shielding request has loaded for more than 0.4 s.");

            _h.Stage.CompleteAcquire();
            Assert.That(_h.Stage.LoadIndicatorVisible, Is.False, "Nothing is loading any more.");
        }

        [Test]
        public void LoadIndicator_NeverShows_ForALoadThatFinishesAtOnce()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);

            LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            _h.Advance(1f);

            Assert.That(_h.Stage.CountOf(StageCallKind.SetLoadIndicator), Is.Zero);
        }

        [Test]
        public void LoadIndicator_IsNotShown_ForALayerThatDoesNotShield()
        {
            var quiet = new ViewLayer("quiet", 0, ViewLayerRules.None);
            using (var h = new NavigatorHarness(layers: new[] { quiet }))
            {
                LifecycleDriver.AddRows(h, quiet, Target);
                LifecycleDriver.Reach(h, Target, ViewPhase.Loading, h.Owner);

                h.Advance(1f);

                Assert.That(h.Stage.LoadIndicatorVisible, Is.False);
            }
        }

        // Waiting, Loading -> Ended (not shown): owner ended, handle.Close(), CloseAll, navigation, Back during Loading

        [Test]
        public void OwnerEnded_WhileWaitingOrLoading_EndsNotShownAsOwnerEnded(
            [Values(ViewPhase.Waiting, ViewPhase.Loading)] ViewPhase phase)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Blocker, Target);
            var owner = _h.NewOwner();
            var task = LifecycleDriver.Reach(_h, Target, phase, owner.Token, Blocker);

            owner.Cancel();

            AssertWithdrawn(task, phase, RejectReason.OwnerEnded, phase == ViewPhase.Waiting ? 1 : 0);
        }

        [Test]
        public void HandleClose_WhileWaitingOrLoading_EndsNotShownAsCancelled(
            [Values(ViewPhase.Waiting, ViewPhase.Loading)] ViewPhase phase)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Blocker, Target);
            var task = LifecycleDriver.Reach(_h, Target, phase, _h.Owner, Blocker);

            LifecycleDriver.HandleOf(_h, Target).Close();

            AssertWithdrawn(task, phase, RejectReason.Cancelled, phase == ViewPhase.Waiting ? 1 : 0);
        }

        [Test]
        public void CloseAll_WhileWaitingOrLoading_WithdrawsTheRequestAsCancelled(
            [Values(ViewPhase.Waiting, ViewPhase.Loading)] ViewPhase phase)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Blocker, Target);
            var task = LifecycleDriver.Reach(_h, Target, phase, _h.Owner, Blocker);

            _h.Navigator.CloseAll(ViewLayers.Popups);

            AssertWithdrawn(task, phase, RejectReason.Cancelled, 0, false);
            Assert.That(_h.RequestCount(ViewLayers.Popups), Is.Zero);
        }

        [Test]
        public void Navigation_WhileAPopupWaitsOrLoads_WithdrawsItAsCancelled(
            [Values(ViewPhase.Waiting, ViewPhase.Loading)] ViewPhase phase)
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Blocker, Target);
            _h.Row("screen");
            var task = LifecycleDriver.Reach(_h, Target, phase, _h.Owner, Blocker);

            // The screen loads on its own layer; when its open reaches OnOpen the older popups are closed.
            _h.Navigator.Open(ViewKey.Create("screen"), _h.Owner);
            _h.Stage.CompleteAcquire(_h.Stage.PendingAcquireCount - 1);

            Assert.That(LifecycleDriver.HandleOf(_h, "screen").Phase, Is.EqualTo(ViewPhase.Open));
            AssertWithdrawn(task, phase, RejectReason.Cancelled, 0);
        }

        [Test]
        public void Back_WhileLoading_WithdrawsTheRequestAsCancelled()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Loading, _h.Owner);

            var handled = _h.Navigator.Back();

            Assert.That(handled, Is.True);
            AssertWithdrawn(task, ViewPhase.Loading, RejectReason.Cancelled, 0);
        }

        // Loading -> Ended: load failed, load timed out

        [Test]
        public void LoadFails_EndsNotShownAsLoadFailed_LogsUIDIRECTOR104_AndRoutesTheError()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Stage.FailNextAcquire(new InvalidOperationException("no such asset"));

            var result = LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            LifecycleDriver.AssertNotShown(result, RejectReason.LoadFailed);
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.LoadFailed), Is.EqualTo(1));
            Assert.That(_h.Observer.KindsFor(result.RequestId), Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.NotShown }));
            Assert.That(_h.Errors.Count, Is.EqualTo(1));
            Assert.That(_h.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.Provider));
            Assert.That(_h.Errors[0].Context.ViewId, Is.EqualTo(Target));
            LifecycleDriver.AssertClean(_h, ViewLayers.Popups);
            Assert.That(_h.Stage.Instances, Is.Empty);
        }

        [Test]
        public void LoadFailsLater_WhileLoading_EndsNotShownAsLoadFailed()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Loading, _h.Owner);

            _h.Stage.FailAcquire();

            LifecycleDriver.AssertNotShown(task.ResultNow(), RejectReason.LoadFailed);
            LifecycleDriver.AssertClean(_h, ViewLayers.Popups);
        }

        [Test]
        public void LoadTimesOut_AfterFifteenSeconds_EndsNotShownAsLoadTimedOut_AndLogsUIDIRECTOR105()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Loading, _h.Owner);

            _h.Advance(14f);
            Assert.That(task.IsPending(), Is.True, "Still within the 15 s load timeout.");
            Assert.That(LifecycleDriver.HandleOf(_h, Target).Phase, Is.EqualTo(ViewPhase.Loading));

            _h.Advance(1.5f);

            LifecycleDriver.AssertNotShown(task.ResultNow(), RejectReason.LoadTimedOut);
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.LoadTimedOut), Is.EqualTo(1));
            LifecycleDriver.AssertClean(_h, ViewLayers.Popups);

            // The step was cancelled; the provider's late answer is released.
            Assert.That(_h.Stage.CallsOf(StageCallKind.Acquire)[0].Token.IsCancellationRequested, Is.True);
            _h.Stage.CompleteAcquire();
            Assert.That(_h.Stage.LastInstanceOf(Target).ReleasedLate, Is.True);
            _h.Stage.AssertAllReleased();
        }

        [Test]
        public void LoadTimeout_FollowsTheConfiguredValue()
        {
            using (var h = new NavigatorHarness(loadTimeoutSeconds: 1f))
            {
                LifecycleDriver.AddRows(h, ViewLayers.Popups, Target);
                var task = LifecycleDriver.Reach(h, Target, ViewPhase.Loading, h.Owner);

                h.Advance(0.5f);
                Assert.That(task.IsPending(), Is.True);

                h.Advance(0.8f);
                LifecycleDriver.AssertNotShown(task.ResultNow(), RejectReason.LoadTimedOut);
            }
        }

        // Loading -> Ended: no ViewHost, or no IView for the key

        [Test]
        public void InstanceWithoutAViewHost_EndsNotShownAsTypeMismatch_AndReleasesTheInstance()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Stage.NextAcquireReturnsNoHost();

            var result = LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            LifecycleDriver.AssertNotShown(result, RejectReason.TypeMismatch);
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.MissingViewHost), Is.EqualTo(1));
            Assert.That(_h.Stage.Instances.Count, Is.EqualTo(1));
            _h.Stage.AssertAllReleased();
            Assert.That(_h.Stage.CountOf(StageCallKind.Play), Is.Zero, "No transition for a request that never opened.");
            LifecycleDriver.AssertClean(_h, ViewLayers.Popups);
        }

        [Test]
        public void InstanceWithoutAViewForTheKey_EndsNotShownAsTypeMismatch_AndReleasesTheInstance()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Stage.NextAcquireReturnsNoView();

            var result = LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            LifecycleDriver.AssertNotShown(result, RejectReason.TypeMismatch);
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.ViewTypeMismatch), Is.EqualTo(1));
            _h.Stage.AssertAllReleased();
            Assert.That(_h.Observer.KindsFor(result.RequestId), Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.NotShown }));
        }

        [Test]
        public void ViewOfOtherArgumentTypes_EndsNotShownAsTypeMismatch_AndNeverCallsOnOpen()
        {
            _h.Row(Target, ViewLayers.Popups).Map<string, int>(Target);

            var result = LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            LifecycleDriver.AssertNotShown(result, RejectReason.TypeMismatch);
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.ViewTypeMismatch), Is.EqualTo(1));
            var wrongView = (FakeView<string, int>)_h.Stage.LastInstanceOf(Target).View;
            Assert.That(wrongView.OpenCount, Is.Zero);
            _h.Stage.AssertAllReleased();
        }

        // Loading -> Ended: OnOpen threw, or called Decline

        [Test]
        public void OnOpenThrows_EndsNotShownAsFaulted_RoutesTheError_AndEndsTheOpenScopeBeforeTheRelease()
        {
            var boom = new InvalidOperationException("boom");
            var scopeEnds = 0;
            var releasesWhenScopeEnded = -1;
            FakeVisibleView<NoArgs, bool> view = null;
            _h.Row(Target, ViewLayers.Popups);
            _h.Map(Target, () => view = new FakeVisibleView<NoArgs, bool>
            {
                ThrowOnOpen = boom,
                BindOnOpen = scope => scope.OnEnd(() =>
                {
                    scopeEnds++;
                    releasesWhenScopeEnded = _h.Stage.LastInstanceOf(Target).ReleaseCount;
                })
            });

            var result = LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            LifecycleDriver.AssertNotShown(result, RejectReason.Faulted);
            Assert.That(_h.Errors.Count, Is.EqualTo(1));
            Assert.That(_h.Errors[0].Exception, Is.SameAs(boom));
            Assert.That(_h.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.ViewHook));
            Assert.That(_h.Errors[0].Context.ViewId, Is.EqualTo(Target));
            Assert.That(_h.Errors[0].Context.RequestId, Is.EqualTo(result.RequestId));

            Assert.That(scopeEnds, Is.EqualTo(1), "The open scope ended exactly once.");
            Assert.That(releasesWhenScopeEnded, Is.Zero, "Scopes end before the instance is released.");
            _h.Stage.AssertAllReleased();
            Assert.That(_h.Stage.CountOf(StageCallKind.Play), Is.Zero, "No transition.");
            Assert.That(view.VisibleCount, Is.Zero, "OnVisible never ran.");
            Assert.That(_h.Observer.KindsFor(result.RequestId), Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.NotShown }));
            LifecycleDriver.AssertClean(_h, ViewLayers.Popups);
        }

        [Test]
        public void OnOpenThrows_LogsUIDIRECTOR117()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Map(Target, () => new FakeView<NoArgs, bool> { ThrowOnOpen = new InvalidOperationException("boom") });

            LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            Assert.That(_h.Diagnostics.Count(DiagnosticIds.OnOpenThrew), Is.EqualTo(1));
        }

        [Test]
        public void OnOpenDeclines_EndsNotShownAsDeclined_WithTheReason_AndNoTransition()
        {
            var scopeEnds = 0;
            _h.Row(Target, ViewLayers.Popups);
            _h.Map(Target, () => new FakeView<NoArgs, bool>
            {
                BindOnOpen = scope => scope.OnEnd(() => scopeEnds++),
                RunOnOpen = context => context.Decline("not today")
            });

            var result = LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            LifecycleDriver.AssertNotShown(result, RejectReason.Declined);
            Assert.That(result.Detail, Is.EqualTo("not today"));
            Assert.That(scopeEnds, Is.EqualTo(1));
            Assert.That(_h.Errors, Is.Empty, "A decline is not an error.");
            Assert.That(_h.Stage.CountOf(StageCallKind.Play), Is.Zero);
            Assert.That(_h.Observer.CountOf(ViewEventKind.Shown), Is.Zero);
            _h.Stage.AssertAllReleased();
            LifecycleDriver.AssertClean(_h, ViewLayers.Popups);
        }

        [Test]
        public void OnOpenThrowsAfterAComplete_StillEndsNotShownAsFaulted()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Map(Target, () => new FakeView<NoArgs, bool>
            {
                RunOnOpen = context => context.Complete(true),
                ThrowOnOpen = new InvalidOperationException("boom")
            });

            var result = LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            LifecycleDriver.AssertNotShown(result, RejectReason.Faulted);
            Assert.That(_h.Observer.CountOf(ViewEventKind.Shown), Is.Zero);
            _h.Stage.AssertAllReleased();
        }

        [Test]
        public void Decline_AfterOnOpenReturned_IsIgnored()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);

            LifecycleDriver.ViewOf(_h, Target).Decline("too late");

            Assert.That(task.IsPending(), Is.True);
            Assert.That(LifecycleDriver.HandleOf(_h, Target).Phase, Is.EqualTo(ViewPhase.Open));
        }

        [Test]
        public void CompleteInsideOnOpen_IsLatched_TheViewOpensAndThenCloses()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Map(Target, () => new FakeVisibleView<NoArgs, bool> { RunOnOpen = context => context.Complete(true) });

            var result = LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            Assert.That(result.WasShown, Is.True, "The view opened before it closed.");
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(result.HasValue, Is.True);
            Assert.That(result.Value, Is.True);
            Assert.That(
                _h.Observer.KindsFor(result.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }));

            var instance = _h.Stage.LastInstanceOf(Target);
            var show = LifecycleDriver.IndexOfPlay(_h.Stage, instance, true);
            var hide = LifecycleDriver.IndexOfPlay(_h.Stage, instance, false);
            Assert.That(show, Is.GreaterThanOrEqualTo(0));
            Assert.That(hide, Is.GreaterThan(show), "The show transition plays, then the hide transition.");
        }

        [Test]
        public void CloseInsideOnOpen_IsLatchedAsDismissed_TheViewOpensAndThenCloses()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Map(Target, () => new FakeView<NoArgs, bool> { RunOnOpen = context => context.Close() });

            var result = LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            Assert.That(result.WasShown, Is.True);
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Dismissed));
            Assert.That(
                _h.Observer.KindsFor(result.RequestId),
                Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown, ViewEventKind.Closed }));
        }

        [Test]
        public void CompleteInsideOnOpen_LogsUIDIRECTOR118_PointingToDecline()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Map(Target, () => new FakeView<NoArgs, bool> { RunOnOpen = context => context.Complete(true) });

            LifecycleDriver.Show(_h, Target, _h.Owner).ResultNow();

            Assert.That(_h.Diagnostics.Count(DiagnosticIds.CompleteInsideOnOpen), Is.EqualTo(1));
        }

        // Loading -> Opening

        [Test]
        public void OnOpenReturned_SnapsHidden_EnablesTheCanvas_BeginsTheVisibleScope_AndPlaysTheShowTransition()
        {
            var injectedAtOpen = false;
            var snapsAtOpen = -1;
            var shownAtOpen = true;
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Map(Target, () => new FakeVisibleView<NoArgs, bool>
            {
                RunOnOpen = context =>
                {
                    var current = _h.Stage.LastInstanceOf(Target);
                    injectedAtOpen = current.Injected;
                    snapsAtOpen = _h.Stage.CountOf(StageCallKind.Snap);
                    shownAtOpen = current.HasVisual && !current.Visual.Hidden;
                }
            });
            _h.Stage.PlayMode = StageMode.Manual;

            LifecycleDriver.Show(_h, Target, _h.Owner);

            var instance = _h.Stage.LastInstanceOf(Target);
            var view = _h.ViewOf<FakeVisibleView<NoArgs, bool>>(Target);
            Assert.That(LifecycleDriver.HandleOf(_h, Target).Phase, Is.EqualTo(ViewPhase.Opening));

            // OnOpen ran on an injected instance whose canvas was not yet enabled, before the snap.
            Assert.That(injectedAtOpen, Is.True, "Injection ran before OnOpen.");
            Assert.That(snapsAtOpen, Is.Zero, "OnOpen ran before the transition was snapped.");
            Assert.That(shownAtOpen, Is.False, "OnOpen ran with the canvas disabled.");
            Assert.That(view.OpenCount, Is.EqualTo(1));

            Assert.That(
                _h.Stage.Kinds(StageCallKind.Acquire, StageCallKind.Inject, StageCallKind.Snap, StageCallKind.Play),
                Is.EqualTo(new[] { StageCallKind.Acquire, StageCallKind.Inject, StageCallKind.Snap, StageCallKind.Play }));
            Assert.That(_h.Stage.CallsOf(StageCallKind.Snap)[0].On, Is.False, "Snapped to hidden.");

            Assert.That(instance.HasVisual, Is.True);
            Assert.That(instance.Visual.Hidden, Is.False, "The canvas is enabled.");
            Assert.That(instance.Visual.Interactive, Is.False, "Not interactive until the view is open.");

            Assert.That(view.Calls, Is.EqualTo(new[] { "OnOpen", "OnVisible" }));
            Assert.That(_h.Stage.PendingPlayCount, Is.EqualTo(1));
            Assert.That(_h.Stage.CallsOf(StageCallKind.Play)[0].On, Is.True, "The show transition.");
        }

        [Test]
        public void TheCanvasIsEnabled_AfterTheSnapToHidden()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Stage.PlayMode = StageMode.Manual;

            LifecycleDriver.Show(_h, Target, _h.Owner);

            var instance = _h.Stage.LastInstanceOf(Target);
            var snap = LifecycleDriver.IndexOfCall(_h.Stage, StageCallKind.Snap, instance);
            var enabled = -1;
            for (var i = 0; i < _h.Stage.Calls.Count; i++)
            {
                var call = _h.Stage.Calls[i];
                if (call.Kind == StageCallKind.Apply && ReferenceEquals(call.Instance, instance) && !call.Visual.Hidden)
                {
                    enabled = i;
                    break;
                }
            }

            Assert.That(snap, Is.GreaterThanOrEqualTo(0));
            Assert.That(enabled, Is.GreaterThan(snap), "Snap to hidden first, then enable the canvas.");
        }

        // Opening -> Open

        [Test]
        public void OpeningEnds_WhenTheTransitionFinishes_ShownIsPublished_ShieldDrops_AndTheViewBecomesInteractive()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Stage.PlayMode = StageMode.Manual;
            LifecycleDriver.Show(_h, Target, _h.Owner);
            var instance = _h.Stage.LastInstanceOf(Target);
            var handle = LifecycleDriver.HandleOf(_h, Target);

            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Opening));
            Assert.That(_h.ShieldCount, Is.EqualTo(1));
            Assert.That(_h.Stage.ShieldBlocking, Is.True);
            Assert.That(_h.Observer.CountOf(ViewEventKind.Shown), Is.Zero);

            _h.Stage.CompletePlay();

            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_h.Observer.KindsFor(handle.RequestId), Is.EqualTo(new[] { ViewEventKind.Requested, ViewEventKind.Shown }));
            Assert.That(_h.ShieldCount, Is.Zero);
            Assert.That(_h.Stage.ShieldBlocking, Is.False);
            Assert.That(instance.Visual.Hidden, Is.False);
            Assert.That(instance.Visual.Interactive, Is.True, "The raycaster is on once the view is open.");
        }

        [Test]
        public void CoveredView_IsHiddenOnlyOnceTheCoveringViewIsOpen()
        {
            _h.Row("a").Row("b");
            _h.Navigator.Open(ViewKey.Create("a"), _h.Owner);
            var below = _h.Stage.LastInstanceOf("a");
            Assert.That(below.Visual.Hidden, Is.False);

            _h.Stage.PlayMode = StageMode.Manual;
            _h.Navigator.Open(ViewKey.Create("b"), _h.Owner);
            Assert.That(below.Visual.Hidden, Is.False, "The covering view is still opening.");

            _h.Stage.CompletePlay();

            Assert.That(below.Visual.Hidden, Is.True, "The covering view is open, so what it covers is hidden.");
        }

        [Test]
        public void ShowTransitionTimeout_SnapsToShown_LogsUIDIRECTOR106_AndOpensAsIfFinished()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            _h.Stage.PlayMode = StageMode.Manual;
            LifecycleDriver.Show(_h, Target, _h.Owner);
            var instance = _h.Stage.LastInstanceOf(Target);
            var handle = LifecycleDriver.HandleOf(_h, Target);

            _h.Advance(2.5f);
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Opening), "Still within the 3 s transition timeout.");

            _h.Advance(1f);

            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open));
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.TransitionTimedOut), Is.EqualTo(1));
            Assert.That(_h.Observer.CountOf(ViewEventKind.Shown), Is.EqualTo(1));
            Assert.That(_h.Stage.CallsOf(StageCallKind.Play)[0].Token.IsCancellationRequested, Is.True, "The step was cancelled.");

            var lastSnap = _h.Stage.CallsOf(StageCallKind.Snap);
            Assert.That(lastSnap[lastSnap.Length - 1].On, Is.True, "Snapped to the shown state.");
            Assert.That(ReferenceEquals(lastSnap[lastSnap.Length - 1].Instance, instance), Is.True);
            Assert.That(_h.ShieldCount, Is.Zero);
            Assert.That(instance.Visual.Interactive, Is.True);
        }

        // Opening, Open -> close pending: the first cause is latched

        [Test]
        public void CloseCause_IsLatchedAsTheOutcome_AndCloseReportsIt([Values] CloseCause cause)
        {
            _h.Row(Target, ViewLayers.Popups, closeOnBackdrop: true).Map<NoArgs, bool>(Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Open, _h.Owner);
            var expected = CloseReason.NotShown;

            switch (cause)
            {
                case CloseCause.Complete:
                    LifecycleDriver.ViewOf(_h, Target).Complete(true);
                    expected = CloseReason.Completed;
                    break;
                case CloseCause.ViewClose:
                    LifecycleDriver.ViewOf(_h, Target).Close();
                    expected = CloseReason.Dismissed;
                    break;
                case CloseCause.HandleClose:
                    LifecycleDriver.HandleOf(_h, Target).Close();
                    expected = CloseReason.Code;
                    break;
                case CloseCause.Back:
                    Assert.That(_h.Navigator.Back(), Is.True);
                    expected = CloseReason.Back;
                    break;
                case CloseCause.Backdrop:
                    _h.Stage.RaiseBackdropTapped(ViewLayers.Popups);
                    expected = CloseReason.Backdrop;
                    break;
                case CloseCause.CloseAll:
                    _h.Navigator.CloseAll(ViewLayers.Popups);
                    expected = CloseReason.Code;
                    break;
            }

            var result = task.ResultNow();
            Assert.That(result.WasShown, Is.True);
            Assert.That(result.Reason, Is.EqualTo(expected));
            Assert.That(result.HasValue, Is.EqualTo(cause == CloseCause.Complete));
            Assert.That(_h.Observer.LastOf(ViewEventKind.Closed).Reason, Is.EqualTo(expected));
            _h.Stage.AssertAllReleased();
            LifecycleDriver.AssertClean(_h, ViewLayers.Popups);
        }

        [Test]
        public void FirstCloseCause_Wins_LaterCausesAndValuesAreIgnored()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Opening, _h.Owner);
            var view = LifecycleDriver.ViewOf(_h, Target);

            // The first cause is latched while the show transition runs; the others change nothing.
            view.Complete(true);
            view.Complete(false);
            view.Close();
            LifecycleDriver.HandleOf(_h, Target).Close();
            _h.Stage.CompletePlay();
            _h.Stage.CompletePlay();

            var result = task.ResultNow();
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(result.HasValue, Is.True);
            Assert.That(result.Value, Is.True);
            Assert.That(_h.Observer.CountOf(ViewEventKind.Closed), Is.EqualTo(1));
        }

        // Open -> Closing: what it covered is revealed first, the hide transition runs under a watchdog

        [Test]
        public void Closing_RevealsWhatTheViewCovered_BeforeTheHideTransitionPlays()
        {
            _h.Row("a").Row("b");
            _h.Map("a", () => new FakeVisibleView<NoArgs, NoResult>());
            _h.Navigator.Open(ViewKey.Create("a"), _h.Owner);
            _h.Navigator.Open(ViewKey.Create("b"), _h.Owner);
            var below = _h.Stage.LastInstanceOf("a");
            var belowView = _h.ViewOf<FakeVisibleView<NoArgs, NoResult>>("a");
            var top = _h.Stage.LastInstanceOf("b");
            Assert.That(below.Visual.Hidden, Is.True, "Covered by the open screen above it.");
            Assert.That(belowView.VisibleCount, Is.EqualTo(1));

            _h.Stage.PlayMode = StageMode.Manual;
            _h.Stage.ClearCalls();
            _h.ViewOf<FakeView<NoArgs, NoResult>>("b").Close();

            Assert.That(LifecycleDriver.HandleOf(_h, "b").Phase, Is.EqualTo(ViewPhase.Closing));
            Assert.That(below.Visual.Hidden, Is.False, "Revealed before the hide transition ends.");
            Assert.That(belowView.VisibleCount, Is.EqualTo(2), "Its visible scope began again.");

            var revealed = -1;
            for (var i = 0; i < _h.Stage.Calls.Count; i++)
            {
                var call = _h.Stage.Calls[i];
                if (call.Kind == StageCallKind.Apply && ReferenceEquals(call.Instance, below) && !call.Visual.Hidden)
                {
                    revealed = i;
                    break;
                }
            }

            var hide = LifecycleDriver.IndexOfPlay(_h.Stage, top, false);
            Assert.That(revealed, Is.GreaterThanOrEqualTo(0));
            Assert.That(hide, Is.GreaterThan(revealed), "Reveal first, then hide.");
        }

        [Test]
        public void HideTransitionTimeout_SnapsToHidden_LogsUIDIRECTOR106_AndEndsTheRequest()
        {
            LifecycleDriver.AddRows(_h, ViewLayers.Popups, Target);
            var task = LifecycleDriver.Reach(_h, Target, ViewPhase.Closing, _h.Owner);
            var instance = _h.Stage.LastInstanceOf(Target);

            _h.Advance(2.5f);
            Assert.That(LifecycleDriver.HandleOf(_h, Target).Phase, Is.EqualTo(ViewPhase.Closing), "Still within the 3 s transition timeout.");

            _h.Advance(1f);

            var result = task.ResultNow();
            Assert.That(result.WasShown, Is.True);
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Completed));
            Assert.That(_h.Diagnostics.Count(DiagnosticIds.TransitionTimedOut), Is.EqualTo(1));

            var hide = LifecycleDriver.IndexOfPlay(_h.Stage, instance, false);
            Assert.That(_h.Stage.Calls[hide].Token.IsCancellationRequested, Is.True, "The hide step was cancelled.");

            var snaps = _h.Stage.CallsOf(StageCallKind.Snap);
            Assert.That(snaps[snaps.Length - 1].On, Is.False, "Snapped to the hidden state.");
            _h.Stage.AssertAllReleased();
            LifecycleDriver.AssertClean(_h, ViewLayers.Popups);
        }
    }
}
