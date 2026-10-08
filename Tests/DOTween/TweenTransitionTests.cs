using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // TweenTransition against the ViewTransition contract, eases, poses, cancellation and recycling: on a bench layer and through a real root.
    [TestFixture]
    [Timeout(60000)]
    public sealed class TweenTransitionTests
    {
        public enum PartSet
        {
            FadeOnly,
            ScaleOnly,
            OffsetOnly,
            All
        }

        // Each way a transition's sequence can end.
        public enum SequenceEnd
        {
            Completed,
            TokenCancelled,
            Snapped,
            Replaced,
            KilledFromOutside
        }

        public enum OutsideKill
        {
            KillAll,
            KillTheSequence
        }

        // Terminate causes from the request state table, beside owner-ended during Opening.
        public enum TerminateCause
        {
            InstanceDestroyed,
            InstanceDeactivated,
            RootDestroyed,
            OwnerEndedWhileClosing
        }

        // Causes that end a request while its show tween runs, with the close reason each one reports.
        public enum ShowEndCause
        {
            OwnerEnded,
            InstanceDeactivated
        }

        private const float PlaySeconds = 0.2f;
        private const float LongPlaySeconds = 2f;
        private const float ReplacedPlaySeconds = 0.5f;
        private const float HiddenScale = 0.5f;
        private const int CancelFrames = 2;
        private const int RecyclingCycles = 5;
        private const float MidPlaySeconds = 0.3f;
        private const float EaseCheckSeconds = 1f;
        private const float ReuseWatchSeconds = 0.1f;
        private const float AuthoredAlpha = 0.7f;
        private const float PoseTolerance = 0.01f;

        private static readonly Vector2 HiddenOffset = new Vector2(0.5f, -1f);
        private static readonly Vector2 BenchSize = new Vector2(400f, 300f);
        private static readonly Vector2 FixedViewSize = new Vector2(100f, 80f);
        private static readonly Vector2 AuthoredPosition = new Vector2(30f, 20f);
        private static readonly Vector3 AuthoredScale = new Vector3(0.8f, 0.9f, 1f);
        private static readonly string[] PoseParts = { "alpha", "scale", "x", "y" };

        // Serialized field names are private; candidates are tried in order, so a rename needs one edit here.
        private static readonly string[] FadeOnNames = { "_fade", "_useFade", "_fadeEnabled" };
        private static readonly string[] ScaleOnNames = { "_scale", "_useScale", "_scaleEnabled" };
        private static readonly string[] HiddenScaleNames = { "_hiddenScale", "_scaleFrom" };
        private static readonly string[] OffsetOnNames = { "_offset", "_useOffset", "_offsetEnabled", "_move", "_slide" };
        private static readonly string[] HiddenOffsetNames = { "_hiddenOffset", "_offset", "_offsetFraction" };
        private static readonly string[] DurationNames = { "_duration" };
        private static readonly string[] ShowDurationNames = { "_showDuration" };
        private static readonly string[] HideDurationNames = { "_hideDuration" };
        private static readonly string[] ShowEaseNames = { "_showEase" };
        private static readonly string[] HideEaseNames = { "_hideEase" };

        private readonly List<Object> _created = new List<Object>();
        private readonly List<CancellationTokenSource> _sources = new List<CancellationTokenSource>();
        private float _timeScale;
        private bool _recyclable;
        private LogBehaviour _logBehaviour;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Initialised before any test changes a setting, so the settings asset cannot overwrite it later.
            DOTween.Init();
        }

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            _recyclable = DOTween.defaultRecyclable;
            _logBehaviour = DOTween.logBehaviour;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _timeScale;

            for (var i = 0; i < _sources.Count; i++)
            {
                _sources[i].Cancel();
                _sources[i].Dispose();
            }

            _sources.Clear();

            DOTween.KillAll();
            DOTween.ClearCachedTweens();
            DOTween.defaultRecyclable = _recyclable;
            DOTween.logBehaviour = _logBehaviour;

            // Newest first, so a root is destroyed before the templates its views came from.
            for (var i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.Destroy(_created[i]);
            }

            _created.Clear();
            yield return null;
        }

        // Contract

        [UnityTest]
        public IEnumerator Conformance_TweenTransition_MeetsTheTransitionContract([Values] PartSet parts)
        {
            var transition = CreateBenchTransition(parts, PlaySeconds);

            yield return TransitionConformance.Run(transition, PoseOf(transition));

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_AtZeroTimeScale_ReachesTheEndPoseOnUnscaledTime([Values] bool show)
        {
            var transition = CreateBenchTransition(PartSet.All, PlaySeconds);
            var pose = PoseOf(transition);
            var expected = SnapPose(transition, pose, show);
            transition.Snap(!show);
            Time.timeScale = 0f;

            var run = PlayRun.Start(transition, show, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => run.IsDone, "the play to finish at timeScale 0", 2f);

            Assert.That(run.Error, Is.Null, "the play threw");
            Assert.That(run.WasCanceled, Is.False, "the play ended as cancelled");
            TransitionConformance.AssertPose(pose(), expected, "end pose at timeScale 0");
            LogAssert.NoUnexpectedReceived();
        }

        // Cancellation and kill

        [UnityTest]
        public IEnumerator PlayAsync_TokenCancelled_KillsItsTweenAtOnceAndReturns()
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            var source = CreateSource();
            transition.Snap(false);
            var before = DOTween.TotalActiveTweens();
            var run = PlayRun.Start(transition, true, source.Token);
            yield return null;
            var whilePlaying = DOTween.TotalActiveTweens();

            source.Cancel();
            var rightAfterCancel = DOTween.TotalActiveTweens();
            yield return WaitAtMostFrames(CancelFrames, () => run.IsDone);

            Assert.That(whilePlaying, Is.GreaterThan(before), "live tweens while the play runs (precondition)");
            Assert.That(whilePlaying, Is.EqualTo(before + 1), "live tweens while the play runs: one sequence per play");
            Assert.That(rightAfterCancel, Is.EqualTo(before), "live tweens right after the cancel, before any frame");
            Assert.That(run.IsDone, Is.True, "the play still ran " + CancelFrames + " frames after the cancel");
            Assert.That(run.Error, Is.Null, "the cancelled play threw something other than OperationCanceledException");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_TokenCancelledMidPlay_StopsBetweenTheEndPoses([Values] bool show)
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            SetEases(transition, Ease.Linear, Ease.Linear);
            var pose = PoseOf(transition);
            var shown = SnapPose(transition, pose, true);
            var hidden = SnapPose(transition, pose, false);
            transition.Snap(!show);
            var source = CreateSource();
            var run = PlayRun.Start(transition, show, source.Token);
            yield return WaitRealSeconds(MidPlaySeconds);
            var runningBeforeCancel = !run.IsDone;

            source.Cancel();
            var rightAfterCancel = pose();
            yield return null;
            yield return null;
            var twoFramesLater = pose();

            Assert.That(runningBeforeCancel, Is.True, "the play was running when the token was cancelled (precondition)");
            Assert.That(run.IsDone, Is.True, "the play still ran two frames after the cancel");
            Assert.That(run.Error, Is.Null, "the cancelled play threw something other than OperationCanceledException");
            TransitionConformance.AssertPose(twoFramesLater, rightAfterCancel, "pose two frames after the cancel");
            AssertStrictlyBetween(rightAfterCancel, show ? hidden : shown, show ? shown : hidden, "pose right after the cancel (Kill(false) must not jump to the end pose)");
            LogAssert.NoUnexpectedReceived();
        }

        // The documented contract: a cancelled token ends the play inside the cancel; only kills the transition makes itself are deferred.
        [UnityTest]
        public IEnumerator PlayAsync_TokenCancelled_CompletesTheTaskInsideTheCancel()
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            transition.Snap(false);
            var source = CreateSource();
            var run = PlayRun.Start(transition, true, source.Token);
            yield return null;
            var runningBeforeCancel = !run.IsDone;

            source.Cancel();
            var doneInsideCancel = run.IsDone;

            Assert.That(runningBeforeCancel, Is.True, "the play was running when the token was cancelled (precondition)");
            Assert.That(doneInsideCancel, Is.True, "the play's task must complete inside the cancel, with no frame in between");
            Assert.That(run.Error, Is.Null, "the cancelled play threw something other than OperationCanceledException");
            Assert.That(run.WasCanceled, Is.False, "the cancelled play must return early, not throw OperationCanceledException");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_SequenceKilledFromOutside_EndsTheAwaitAndKeepsLaterCallsClean([Values] OutsideKill kill)
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            var pose = PoseOf(transition);
            var shown = SnapPose(transition, pose, true);
            var hidden = SnapPose(transition, pose, false);
            var before = DOTween.TotalActiveTweens();
            var run = PlayRun.Start(transition, true, CancellationToken.None);
            yield return null;
            var sequence = OnlyPlayingSequence("the play");

            if (kill == OutsideKill.KillAll)
                DOTween.KillAll();
            else
                sequence.Kill();

            var liveRightAfterKill = DOTween.TotalActiveTweens();
            yield return WaitAtMostFrames(CancelFrames, () => run.IsDone);
            var awaitEnded = run.IsDone;
            var killError = run.Error;

            // The stale-reference check: recycling on and every DOTween warning logged.
            EnableRecyclingWithWarnings();
            SetDuration(transition, PlaySeconds);
            transition.Snap(false);
            var afterSnapHidden = pose();
            transition.Snap(true);
            var afterSnapShown = pose();
            var full = PlayRun.Start(transition, true, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => full.IsDone, "the full show play to finish");
            yield return null;

            Assert.That(awaitEnded, Is.True, "the play's await still ran " + CancelFrames + " frames after its sequence was killed from outside");
            Assert.That(killError, Is.Null, "the play killed from outside threw");
            Assert.That(liveRightAfterKill, Is.EqualTo(before), "live tweens right after the outside kill");
            TransitionConformance.AssertPose(afterSnapHidden, hidden, "Snap(false) after the outside kill");
            TransitionConformance.AssertPose(afterSnapShown, shown, "Snap(true) after the outside kill");
            Assert.That(full.Error, Is.Null, "the full play threw");
            Assert.That(full.WasCanceled, Is.False, "the full play ended as cancelled");
            TransitionConformance.AssertPose(pose(), shown, "pose after the full play");
            Assert.That(DOTween.TotalActiveTweens(), Is.EqualTo(before), "live tweens after the full play");
            LogAssert.NoUnexpectedReceived();
        }

        // The documented contract: a kill from outside ends the play inside that kill; only kills the transition makes itself are deferred.
        [UnityTest]
        public IEnumerator PlayAsync_SequenceKilledFromOutside_CompletesTheTaskInsideTheKill([Values] OutsideKill kill)
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            transition.Snap(false);
            var run = PlayRun.Start(transition, true, CancellationToken.None);
            yield return null;
            var sequence = OnlyPlayingSequence("the play");
            var runningBeforeKill = !run.IsDone;

            if (kill == OutsideKill.KillAll)
                DOTween.KillAll();
            else
                sequence.Kill();

            var doneInsideKill = run.IsDone;

            Assert.That(runningBeforeKill, Is.True, "the play was running when its sequence was killed (precondition)");
            Assert.That(doneInsideKill, Is.True, "the play's task must complete inside the outside kill, with no frame in between");
            Assert.That(run.Error, Is.Null, "the play killed from outside threw");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_WhilePlaying_KeepsExactlyOneLiveTween([Values] PartSet parts)
        {
            var transition = CreateBenchTransition(parts, LongPlaySeconds);
            transition.Snap(false);
            var before = DOTween.TotalActiveTweens();
            var source = CreateSource();

            var run = PlayRun.Start(transition, true, source.Token);
            var rightAfterStart = DOTween.TotalActiveTweens();
            yield return null;
            var afterAFrame = DOTween.TotalActiveTweens();
            source.Cancel();
            yield return WaitAtMostFrames(CancelFrames, () => run.IsDone);

            Assert.That(rightAfterStart, Is.EqualTo(before + 1), "live tweens right after PlayAsync returned: one sequence per play");
            Assert.That(afterAFrame, Is.EqualTo(before + 1), "live tweens a frame into the play: one sequence per play");
            Assert.That(run.IsDone, Is.True, "the play still ran " + CancelFrames + " frames after the cancel");
            Assert.That(DOTween.TotalActiveTweens(), Is.EqualTo(before), "live tweens after the cancel");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_TokenAlreadyCancelled_LeavesNoLiveTween()
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            var source = CreateSource();
            source.Cancel();
            var before = DOTween.TotalActiveTweens();

            var run = PlayRun.Start(transition, true, source.Token);
            var rightAfterStart = DOTween.TotalActiveTweens();
            yield return WaitAtMostFrames(CancelFrames, () => run.IsDone);

            Assert.That(rightAfterStart, Is.EqualTo(before), "live tweens right after a play with a cancelled token");
            Assert.That(run.IsDone, Is.True, "the play still ran " + CancelFrames + " frames after it started");
            Assert.That(run.Error, Is.Null, "the play threw something other than OperationCanceledException");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Snap_DuringPlay_KillsTheTweenAndWritesTheEndPose([Values] bool shown)
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            var pose = PoseOf(transition);
            var expected = SnapPose(transition, pose, shown);
            transition.Snap(false);
            var before = DOTween.TotalActiveTweens();
            var run = PlayRun.Start(transition, true, CancellationToken.None);
            yield return null;
            yield return null;
            var liveWhilePlaying = DOTween.TotalActiveTweens();
            var runningBeforeSnap = !run.IsDone;

            transition.Snap(shown);
            var poseRightAfterSnap = pose();
            var liveRightAfterSnap = DOTween.TotalActiveTweens();
            yield return WaitAtMostFrames(CancelFrames, () => run.IsDone);
            yield return null;
            yield return null;

            Assert.That(runningBeforeSnap, Is.True, "the play was running when Snap was called (precondition)");
            Assert.That(liveWhilePlaying, Is.EqualTo(before + 1), "live tweens while the play runs: one sequence per play");
            TransitionConformance.AssertPose(poseRightAfterSnap, expected, "pose right after Snap");
            Assert.That(liveRightAfterSnap, Is.EqualTo(before), "live tweens right after Snap");
            Assert.That(run.IsDone, Is.True, "the play's await still ran " + CancelFrames + " frames after Snap killed its tween");
            Assert.That(run.Error, Is.Null, "the play threw");
            TransitionConformance.AssertPose(pose(), expected, "pose a few frames after Snap");
            LogAssert.NoUnexpectedReceived();
        }

        // A navigator ends the request right after Snap; the play resuming inside Snap would run ahead of that end.
        [UnityTest]
        public IEnumerator Snap_DuringPlay_LeavesThePlayPendingUntilALaterFrame([Values] bool shown)
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            transition.Snap(false);
            var run = PlayRun.Start(transition, true, CancellationToken.None);
            yield return null;
            var runningBeforeSnap = !run.IsDone;

            transition.Snap(shown);
            var doneInsideSnap = run.IsDone;
            yield return WaitAtMostFrames(CancelFrames, () => run.IsDone);

            Assert.That(runningBeforeSnap, Is.True, "the play was running when Snap was called (precondition)");
            Assert.That(doneInsideSnap, Is.False, "the play's task must not complete inside Snap");
            Assert.That(run.IsDone, Is.True, "the play's task still ran " + CancelFrames + " frames after Snap killed its tween");
            Assert.That(run.Error, Is.Null, "the play threw");
            Assert.That(run.WasCanceled, Is.False, "the play ended as cancelled");
            LogAssert.NoUnexpectedReceived();
        }

        // OnDestroy kills the play itself, so like Snap it resumes the play on a later frame.
        [UnityTest]
        public IEnumerator Destroy_DuringPlay_LeavesThePlayPendingUntilALaterFrame()
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            transition.Snap(false);
            var view = transition.gameObject;
            var run = PlayRun.Start(transition, true, CancellationToken.None);
            yield return null;
            var runningBeforeDestroy = !run.IsDone;

            // DestroyImmediate runs OnDestroy inside this call, so the task is read right after OnDestroy returned.
            Object.DestroyImmediate(view);
            var doneInsideDestroy = run.IsDone;
            yield return WaitAtMostFrames(CancelFrames, () => run.IsDone);

            Assert.That(runningBeforeDestroy, Is.True, "the play was running when its view was destroyed (precondition)");
            Assert.That(doneInsideDestroy, Is.False, "the play's task must not complete inside OnDestroy");
            Assert.That(run.IsDone, Is.True, "the play's task still ran " + CancelFrames + " frames after its view was destroyed");
            Assert.That(run.Error, Is.Null, "the play threw");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_SecondPlayWhileTheFirstRuns_KillsTheFirst()
        {
            var transition = CreateBenchTransition(PartSet.All, ReplacedPlaySeconds);
            var pose = PoseOf(transition);
            var shown = SnapPose(transition, pose, true);
            var hidden = SnapPose(transition, pose, false);
            var before = DOTween.TotalActiveTweens();
            var first = PlayRun.Start(transition, true, CancellationToken.None);
            yield return null;
            var liveWithFirst = DOTween.TotalActiveTweens();

            var second = PlayRun.Start(transition, false, CancellationToken.None);
            var poseAtSecondStart = pose();
            var liveWithSecond = DOTween.TotalActiveTweens();
            yield return WaitAtMostFrames(CancelFrames, () => first.IsDone);
            var firstDoneInTime = first.IsDone;
            yield return TransitionConformance.WaitFor(() => second.IsDone, "the second play to finish");
            yield return null;
            yield return null;

            Assert.That(liveWithFirst, Is.GreaterThan(before), "live tweens while the first play runs (precondition)");
            Assert.That(liveWithFirst, Is.EqualTo(before + 1), "live tweens while the first play runs: one sequence per play");
            Assert.That(liveWithSecond, Is.EqualTo(liveWithFirst), "live tweens once the second play started (the first one's tween must be gone)");
            TransitionConformance.AssertPose(poseAtSecondStart, shown, "pose right after the replacing hide started (it starts from the shown pose)");
            Assert.That(firstDoneInTime, Is.True, "the first play's await still ran " + CancelFrames + " frames after the second play started");
            Assert.That(first.Error, Is.Null, "the first play threw");
            Assert.That(second.Error, Is.Null, "the second play threw");
            Assert.That(second.WasCanceled, Is.False, "the second play ended as cancelled");
            TransitionConformance.AssertPose(pose(), hidden, "pose after the second play");
            Assert.That(DOTween.TotalActiveTweens(), Is.EqualTo(before), "live tweens after both plays");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_SecondPlayWhileTheFirstRuns_LeavesTheFirstPendingUntilALaterFrame()
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            transition.Snap(false);
            var first = PlayRun.Start(transition, true, CancellationToken.None);
            yield return null;
            var firstRunning = !first.IsDone;

            // The second play gets a token, so the tear-down ends it.
            var second = PlayRun.Start(transition, false, CreateSource().Token);
            var firstDoneInsideSecondStart = first.IsDone;
            yield return WaitAtMostFrames(CancelFrames, () => first.IsDone);

            Assert.That(firstRunning, Is.True, "the first play was running when the second started (precondition)");
            Assert.That(firstDoneInsideSecondStart, Is.False, "the first play's task must not complete inside the second PlayAsync");
            Assert.That(first.IsDone, Is.True, "the first play's task still ran " + CancelFrames + " frames after the second play started");
            Assert.That(first.Error, Is.Null, "the first play threw");
            Assert.That(first.WasCanceled, Is.False, "the first play ended as cancelled");
            Assert.That(second.Error, Is.Null, "the second play threw");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_EveryPartOff_CompletesAtOnceWithoutATween()
        {
            var transition = CreateBenchTransition(false, false, false, LongPlaySeconds);
            var before = DOTween.TotalActiveTweens();

            var run = PlayRun.Start(transition, true, CancellationToken.None);
            var doneAtOnce = run.IsDone;
            var liveRightAfterStart = DOTween.TotalActiveTweens();
            yield return null;

            Assert.That(doneAtOnce, Is.True, "the play had not completed when PlayAsync returned");
            Assert.That(run.Error, Is.Null, "the play threw");
            Assert.That(run.WasCanceled, Is.False, "the play ended as cancelled");
            Assert.That(liveRightAfterStart, Is.EqualTo(before), "live tweens right after the play");
            Assert.That(() =>
            {
                transition.Snap(false);
                transition.Snap(true);
            }, Throws.Nothing, "Snap with every part off");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_FromItsOwnEndPose_WritesTheOppositeEndPoseThenMovesBetweenThem([Values] bool show)
        {
            var transition = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            SetEases(transition, Ease.Linear, Ease.Linear);
            var pose = PoseOf(transition);
            var shown = SnapPose(transition, pose, true);
            var hidden = SnapPose(transition, pose, false);
            var start = show ? hidden : shown;
            var end = show ? shown : hidden;
            transition.Snap(show);

            var run = PlayRun.Start(transition, show, CancellationToken.None);
            var poseAtStart = pose();
            yield return WaitRealSeconds(MidPlaySeconds);
            var poseMidPlay = pose();
            var runningMidPlay = !run.IsDone;

            TransitionConformance.AssertPose(poseAtStart, start, "pose right after PlayAsync returned (the opposite end pose)");
            Assert.That(runningMidPlay, Is.True, "the play was still running " + MidPlaySeconds + " s into a " + LongPlaySeconds + " s play");
            AssertStrictlyBetween(poseMidPlay, start, end, "pose " + MidPlaySeconds + " s into the play");
            LogAssert.NoUnexpectedReceived();
        }

        // A DOTween Ease per direction.
        [UnityTest]
        public IEnumerator PlayAsync_EasePerDirection_EachPlayUsesTheEaseOfItsDirection([Values] bool swapped)
        {
            var transition = CreateBenchTransition(true, false, false, EaseCheckSeconds);
            var slowStart = Ease.InQuint;
            var fastStart = Ease.OutQuint;
            SetEases(transition, swapped ? fastStart : slowStart, swapped ? slowStart : fastStart);
            var pose = PoseOf(transition);

            transition.Snap(false);
            var show = PlayRun.Start(transition, true, CancellationToken.None);
            yield return WaitRealSeconds(MidPlaySeconds);
            var showProgress = pose().x;
            var showRunning = !show.IsDone;

            transition.Snap(true);
            var hide = PlayRun.Start(transition, false, CancellationToken.None);
            yield return WaitRealSeconds(MidPlaySeconds);
            var hideProgress = 1f - pose().x;
            var hideRunning = !hide.IsDone;
            transition.Snap(false);

            Assert.That(showRunning, Is.True, "the show play was still running when sampled (precondition)");
            Assert.That(hideRunning, Is.True, "the hide play was still running when sampled (precondition)");
            if (swapped)
            {
                Assert.That(showProgress, Is.GreaterThan(0.6f), "show progress " + MidPlaySeconds + " s into a 1 s OutQuint show");
                Assert.That(hideProgress, Is.LessThan(0.2f), "hide progress " + MidPlaySeconds + " s into a 1 s InQuint hide");
            }
            else
            {
                Assert.That(showProgress, Is.LessThan(0.2f), "show progress " + MidPlaySeconds + " s into a 1 s InQuint show");
                Assert.That(hideProgress, Is.GreaterThan(0.6f), "hide progress " + MidPlaySeconds + " s into a 1 s OutQuint hide");
            }

            LogAssert.NoUnexpectedReceived();
        }

        // Poses

        // Shown is alpha 1, scale 1 and the authored position; hidden is alpha 0, the hidden scale and that position moved by a fraction of the layer.
        [UnityTest]
        public IEnumerator SnapAndPlay_ViewSmallerThanItsLayer_WriteTheDesignedEndPosesAndLeaveOffPartsAuthored([Values] PartSet parts)
        {
            var transition = CreateFixedBenchTransition(parts, PlaySeconds);
            var fade = parts == PartSet.FadeOnly || parts == PartSet.All;
            var scale = parts == PartSet.ScaleOnly || parts == PartSet.All;
            var offset = parts == PartSet.OffsetOnly || parts == PartSet.All;

            transition.Snap(true);
            AssertFixedViewPose(transition, fade, scale, offset, true, "Snap(true)");
            transition.Snap(false);
            AssertFixedViewPose(transition, fade, scale, offset, false, "Snap(false)");

            var show = PlayRun.Start(transition, true, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => show.IsDone, "the show play to finish");
            Assert.That(show.Error, Is.Null, "the show play threw");
            AssertFixedViewPose(transition, fade, scale, offset, true, "after a full show play");

            var hide = PlayRun.Start(transition, false, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => hide.IsDone, "the hide play to finish");
            Assert.That(hide.Error, Is.Null, "the hide play threw");
            AssertFixedViewPose(transition, fade, scale, offset, false, "after a full hide play");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Snap_OffsetOnViewSmallerThanItsLayer_OffsetsByTheLayerRectNotTheViewRect()
        {
            var transition = CreateBenchTransition(false, false, true, PlaySeconds, PlaceInsideTheLayer);
            var rect = (RectTransform)transition.transform;
            var layer = (RectTransform)rect.parent;

            transition.Snap(true);
            var shown = rect.anchoredPosition;
            transition.Snap(false);
            var hidden = rect.anchoredPosition;
            var resized = BenchSize * 2f;
            layer.sizeDelta = resized;
            transition.Snap(false);
            var hiddenAfterResize = rect.anchoredPosition;

            Assert.That(shown.x, Is.EqualTo(AuthoredPosition.x).Within(PoseTolerance), "shown x (the authored position)");
            Assert.That(shown.y, Is.EqualTo(AuthoredPosition.y).Within(PoseTolerance), "shown y (the authored position)");
            Assert.That(hidden.x - shown.x, Is.EqualTo(HiddenOffset.x * BenchSize.x).Within(PoseTolerance), "horizontal offset; the view is " + FixedViewSize.x + " wide, the layer " + BenchSize.x);
            Assert.That(hidden.y - shown.y, Is.EqualTo(HiddenOffset.y * BenchSize.y).Within(PoseTolerance), "vertical offset; the view is " + FixedViewSize.y + " high, the layer " + BenchSize.y);
            Assert.That(hiddenAfterResize.x - shown.x, Is.EqualTo(HiddenOffset.x * resized.x).Within(PoseTolerance), "horizontal offset of a Snap(false) after the layer was resized");
            Assert.That(hiddenAfterResize.y - shown.y, Is.EqualTo(HiddenOffset.y * resized.y).Within(PoseTolerance), "vertical offset of a Snap(false) after the layer was resized");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_ViewSmallerThanItsLayerResizedBeforeThePlay_OffsetsByTheLayerRectAtPlayTime()
        {
            var transition = CreateBenchTransition(false, false, true, PlaySeconds, PlaceInsideTheLayer);
            var rect = (RectTransform)transition.transform;
            var layer = (RectTransform)rect.parent;
            transition.Snap(false);
            transition.Snap(true);
            var shown = rect.anchoredPosition;
            var resized = BenchSize * 2f;
            layer.sizeDelta = resized;

            var run = PlayRun.Start(transition, false, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => run.IsDone, "the hide play to finish");
            var hidden = rect.anchoredPosition;

            Assert.That(run.Error, Is.Null, "the play threw");
            Assert.That(hidden.x - shown.x, Is.EqualTo(HiddenOffset.x * resized.x).Within(PoseTolerance), "horizontal offset after the resize");
            Assert.That(hidden.y - shown.y, Is.EqualTo(HiddenOffset.y * resized.y).Within(PoseTolerance), "vertical offset after the resize");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_FadeOnWithACanvasGroupAlreadyThere_AnimatesThatGroup()
        {
            CanvasGroup existing = null;
            var transition = CreateBenchTransition(true, false, false, PlaySeconds, view =>
            {
                Stretch((RectTransform)view.transform);
                existing = view.AddComponent<CanvasGroup>();
                existing.alpha = 1f;
            });

            transition.Snap(false);
            var alphaAfterHiddenSnap = existing.alpha;
            var run = PlayRun.Start(transition, true, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => run.IsDone, "the show play to finish");
            var alphaAfterShow = existing.alpha;
            transition.Snap(false);

            Assert.That(run.Error, Is.Null, "the play threw");
            Assert.That(transition.GetComponents<CanvasGroup>().Length, Is.EqualTo(1), "CanvasGroups on the view");
            Assert.That(alphaAfterHiddenSnap, Is.EqualTo(0f).Within(PoseTolerance), "the existing group's alpha after Snap(false)");
            Assert.That(alphaAfterShow, Is.EqualTo(1f).Within(PoseTolerance), "the existing group's alpha after the show play");
            Assert.That(existing.alpha, Is.EqualTo(0f).Within(PoseTolerance), "the existing group's alpha after the last Snap(false)");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Snap_OffsetOn_OffsetsByAFractionOfTheParentRectNotTheScreen()
        {
            Assume.That(Mathf.Abs(Screen.width - BenchSize.x), Is.GreaterThan(1f), "the screen must be wider or narrower than the bench layer");
            var transition = CreateBenchTransition(false, false, true, PlaySeconds);
            var rect = (RectTransform)transition.transform;

            transition.Snap(true);
            var shown = rect.anchoredPosition;
            transition.Snap(false);
            var hidden = rect.anchoredPosition;

            Assert.That(hidden.x - shown.x, Is.EqualTo(HiddenOffset.x * BenchSize.x).Within(0.01f), "horizontal offset; screen width is " + Screen.width);
            Assert.That(hidden.y - shown.y, Is.EqualTo(HiddenOffset.y * BenchSize.y).Within(0.01f), "vertical offset; screen height is " + Screen.height);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_ParentResizedBeforeThePlay_OffsetsByTheParentRectAtPlayTime()
        {
            var transition = CreateBenchTransition(false, false, true, PlaySeconds);
            var rect = (RectTransform)transition.transform;
            var layer = (RectTransform)rect.parent;
            transition.Snap(false);
            transition.Snap(true);
            var shown = rect.anchoredPosition;
            var resized = BenchSize * 2f;
            layer.sizeDelta = resized;

            var run = PlayRun.Start(transition, false, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => run.IsDone, "the hide play to finish");
            var hidden = rect.anchoredPosition;

            Assert.That(run.Error, Is.Null, "the play threw");
            Assert.That(hidden.x - shown.x, Is.EqualTo(HiddenOffset.x * resized.x).Within(0.01f), "horizontal offset after the resize");
            Assert.That(hidden.y - shown.y, Is.EqualTo(HiddenOffset.y * resized.y).Within(0.01f), "vertical offset after the resize");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayAsync_FadeOff_AddsNoCanvasGroup()
        {
            var transition = CreateBenchTransition(false, true, true, PlaySeconds);

            transition.Snap(false);
            var run = PlayRun.Start(transition, true, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => run.IsDone, "the show play to finish");
            transition.Snap(false);

            Assert.That(transition.GetComponent<CanvasGroup>(), Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        // Through a real root

        [UnityTest]
        public IEnumerator Open_ViewWithTweenTransitionAtZeroTimeScale_OpensAndClosesWithoutTheWatchdog()
        {
            // The watchdog is pushed past the wait deadline, so only the transition itself can finish the steps.
            var root = CreateRoot(10f);
            var owner = CreateSource();
            var key = AddRow(root, "tween-view", CreateTemplate("TweenView", PlaySeconds), ViewRetention.ReleaseOnClose, owner.Token);
            var before = DOTween.TotalActiveTweens();
            Time.timeScale = 0f;

            var handle = root.Navigator.Open(key, owner.Token);
            var closed = CloseWatch.Start(handle);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open at timeScale 0");
            var poseAtOpen = PoseOf(InstanceOf(handle).GetComponent<TweenTransition>())();
            handle.Close();
            yield return TransitionConformance.WaitFor(() => closed.IsDone, "the view to close at timeScale 0");

            TransitionConformance.AssertPose(poseAtOpen, new Vector4(1f, 1f, 0f, 0f), "pose (alpha, scale, x, y) when the view reached Open");
            Assert.That(closed.Result.Reason, Is.EqualTo(CloseReason.Code), "close reason");
            Assert.That(DOTween.TotalActiveTweens(), Is.EqualTo(before), "live tweens after the close");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OwnerCancelled_DuringTweenShow_LeavesNoLiveTween(
            [Values(ViewRetention.ReleaseOnClose, ViewRetention.KeepInstance)] ViewRetention retention)
        {
            var root = CreateRoot(10f);
            var catalogOwner = CreateSource();
            var key = AddRow(root, "terminate-view", CreateTemplate("TerminateView", LongPlaySeconds), retention, catalogOwner.Token);
            var owner = CreateSource();
            var before = DOTween.TotalActiveTweens();

            var handle = root.Navigator.Open(key, owner.Token);
            var closed = CloseWatch.Start(handle);
            yield return TransitionConformance.WaitFor(
                () => handle.Phase == ViewPhase.Opening && DOTween.TotalActiveTweens() > before,
                "the show tween to be under way");
            owner.Cancel();
            var endedAtOnce = closed.IsDone;
            var liveRightAfterCancel = DOTween.TotalActiveTweens();
            yield return TransitionConformance.WaitFor(() => closed.IsDone, "the request to end");
            // Read in the frame the end was first seen, before an end-of-frame destroy could stop the tween instead.
            var liveWhenEnded = DOTween.TotalActiveTweens();
            yield return null;
            yield return null;

            Assert.That(closed.Result.Reason, Is.EqualTo(CloseReason.OwnerEnded), "close reason");
            if (endedAtOnce)
                Assert.That(liveRightAfterCancel, Is.EqualTo(before), "live tweens right after the owner was cancelled (the request ended synchronously)");
            Assert.That(liveWhenEnded, Is.EqualTo(before), "live tweens in the frame the request ended");
            Assert.That(DOTween.TotalActiveTweens(), Is.EqualTo(before), "live tweens after the request ended");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Terminate_DuringTweenTransition_LeavesNoLiveTweenWhenTheRequestEnds([Values] TerminateCause cause)
        {
            var root = CreateRoot(10f);
            var catalogOwner = CreateSource();
            var duringHide = cause == TerminateCause.OwnerEndedWhileClosing;
            var template = CreateTemplate("TerminateView", duringHide ? PlaySeconds : LongPlaySeconds);
            var key = AddRow(root, "terminate-view", template, ViewRetention.ReleaseOnClose, catalogOwner.Token);
            var owner = CreateSource();
            var before = DOTween.TotalActiveTweens();

            var handle = root.Navigator.Open(key, owner.Token);
            var closed = CloseWatch.Start(handle);
            if (duringHide)
            {
                yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");
                // A long hide, so the owner ends while the hide tween runs.
                SetDuration(InstanceOf(handle).GetComponent<TweenTransition>(), LongPlaySeconds);
                handle.Close();
                yield return TransitionConformance.WaitFor(
                    () => handle.Phase == ViewPhase.Closing && DOTween.TotalActiveTweens() > before,
                    "the hide tween to be under way");
            }
            else
            {
                yield return TransitionConformance.WaitFor(
                    () => handle.Phase == ViewPhase.Opening && DOTween.TotalActiveTweens() > before,
                    "the show tween to be under way");
            }

            var instance = InstanceOf(handle);
            switch (cause)
            {
                case TerminateCause.InstanceDestroyed:
                    // A view lost outside the package ends its request and logs UIDIRECTOR107.
                    LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR107"));
                    Object.Destroy(instance);
                    break;
                case TerminateCause.InstanceDeactivated:
                    // DOTween keeps updating tweens on inactive objects, so only a kill stops this one.
                    LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR107"));
                    instance.SetActive(false);
                    break;
                case TerminateCause.RootDestroyed:
                    Object.Destroy(root.gameObject);
                    break;
                default:
                    owner.Cancel();
                    break;
            }

            var endedAtOnce = closed.IsDone;
            var liveRightAfterCause = DOTween.TotalActiveTweens();
            yield return TransitionConformance.WaitFor(() => closed.IsDone, "the request to end");
            var liveWhenEnded = DOTween.TotalActiveTweens();
            yield return null;
            yield return null;

            // Line 649: a terminate returns the latched outcome when there is one; the hide was started by handle.Close().
            var expectedReason = duringHide ? CloseReason.Code : CloseReason.Destroyed;
            Assert.That(closed.Result.Reason, Is.EqualTo(expectedReason), "close reason");
            if (endedAtOnce)
                Assert.That(liveRightAfterCause, Is.EqualTo(before), "live tweens right after the cause (the request ended synchronously)");
            Assert.That(liveWhenEnded, Is.EqualTo(before), "live tweens in the frame the request ended");
            Assert.That(DOTween.TotalActiveTweens(), Is.EqualTo(before), "live tweens two frames after the request ended");
            LogAssert.NoUnexpectedReceived();
        }

        // The request is ended, not completed: its owed Shown comes right before Closed, with no settle of an open view in between.
        [UnityTest]
        public IEnumerator TerminateDuringTweenShow_PublishesNoSettledBeforeClosed([Values] ShowEndCause cause)
        {
            var root = CreateRoot(10f);
            var catalogOwner = CreateSource();
            var key = AddRow(root, "terminate-view", CreateTemplate("TerminateView", LongPlaySeconds), ViewRetention.ReleaseOnClose, catalogOwner.Token);
            var observer = new RecordingObserver();
            root.Navigator.AddObserver(observer, CreateSource().Token);
            var owner = CreateSource();
            var before = DOTween.TotalActiveTweens();

            var handle = root.Navigator.Open(key, owner.Token);
            var requestId = handle.RequestId;
            var closed = CloseWatch.Start(handle);
            yield return TransitionConformance.WaitFor(
                () => handle.Phase == ViewPhase.Opening && DOTween.TotalActiveTweens() > before,
                "the show tween to be under way");

            if (cause == ShowEndCause.OwnerEnded)
            {
                owner.Cancel();
            }
            else
            {
                // A view lost outside the package ends its request and logs UIDIRECTOR107.
                LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR107"));
                InstanceOf(handle).SetActive(false);
            }

            yield return TransitionConformance.WaitFor(() => closed.IsDone, "the request to end");
            yield return null;
            yield return null;

            var shownIndex = -1;
            var shownCount = 0;
            var closedIndex = -1;
            var closedCount = 0;
            var events = observer.Events;
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].RequestId != requestId)
                    continue;

                if (events[i].Kind == ViewEventKind.Shown)
                {
                    shownCount++;
                    shownIndex = i;
                }
                else if (events[i].Kind == ViewEventKind.Closed)
                {
                    closedCount++;
                    closedIndex = i;
                }
            }

            var settledBetween = 0;
            for (var i = shownIndex + 1; shownIndex >= 0 && i < closedIndex; i++)
            {
                if (events[i].Kind == ViewEventKind.Settled)
                    settledBetween++;
            }

            var expectedReason = cause == ShowEndCause.OwnerEnded ? CloseReason.OwnerEnded : CloseReason.Destroyed;
            Assert.That(requestId, Is.Not.EqualTo(0), "the request id of the open (precondition)");
            Assert.That(shownCount, Is.EqualTo(1), "Shown events for the request; kinds were " + string.Join(", ", observer.KindsFor(requestId)));
            Assert.That(closedCount, Is.EqualTo(1), "Closed events for the request; kinds were " + string.Join(", ", observer.KindsFor(requestId)));
            Assert.That(closedIndex, Is.GreaterThan(shownIndex), "the Closed event must follow the Shown event");
            Assert.That(events[closedIndex].Reason, Is.EqualTo(expectedReason), "reason on the Closed event");
            Assert.That(closed.Result.Reason, Is.EqualTo(expectedReason), "close reason of the request");
            Assert.That(settledBetween, Is.EqualTo(0), "Settled events between the request's Shown and Closed; every kind was " + string.Join(", ", observer.AllKinds()));
            LogAssert.NoUnexpectedReceived();
        }

        // Recycling (the reference is dropped inside OnKill)

        [UnityTest]
        public IEnumerator Recycling_CallsAfterACompletedPlay_LeaveAReusedSequenceAlone()
        {
            EnableRecyclingWithWarnings();
            var transition = CreateBenchTransition(PartSet.All, PlaySeconds);
            var pose = PoseOf(transition);
            var hidden = SnapPose(transition, pose, false);
            var source = CreateSource();
            var show = PlayRun.Start(transition, true, source.Token);
            yield return TransitionConformance.WaitFor(() => show.IsDone, "the show play to finish");
            // One more frame, so DOTween has despawned the completed sequence into its pool.
            yield return null;
            var externalKills = 0;
            var externalValue = 0f;
            // Built from the pools, so it likely reuses the sequence and tweens the completed play owned.
            var external = DOTween.Sequence()
                .Append(DOTween.To(() => externalValue, value => externalValue = value, 1f, 30f))
                .SetUpdate(true)
                .OnKill(() => externalKills++);

            source.Cancel();
            transition.Snap(false);
            transition.Snap(true);
            var aliveAfterStaleCalls = external.IsActive();
            var hide = PlayRun.Start(transition, false, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => hide.IsDone, "the hide play to finish");

            Assert.That(show.Error, Is.Null, "the show play threw");
            Assert.That(aliveAfterStaleCalls, Is.True, "the external sequence after the cancel and the snaps");
            Assert.That(external.IsActive(), Is.True, "the external sequence after another play");
            Assert.That(externalKills, Is.EqualTo(0), "OnKill calls on the external sequence");
            Assert.That(hide.Error, Is.Null, "the hide play threw");
            TransitionConformance.AssertPose(pose(), hidden, "pose after the hide play");
            LogAssert.NoUnexpectedReceived();
        }

        // Only a sequence another owner took from the pool exposes a stale reference, so B must be handed A's ended sequence.
        [UnityTest]
        public IEnumerator Recycling_StaleCallsAfterTheSequenceEnded_LeaveTheTransitionThatReusedItAlone([Values] SequenceEnd end)
        {
            EnableRecyclingWithWarnings();
            var transitionA = CreateBenchTransition(PartSet.All, end == SequenceEnd.Completed ? PlaySeconds : LongPlaySeconds);
            var transitionB = CreateBenchTransition(PartSet.All, LongPlaySeconds);
            SetEases(transitionA, Ease.Linear, Ease.Linear);
            SetEases(transitionB, Ease.Linear, Ease.Linear);
            var poseA = PoseOf(transitionA);
            var poseB = PoseOf(transitionB);
            var sourceA = CreateSource();
            var staleSources = new List<CancellationTokenSource> { sourceA };

            transitionA.Snap(false);
            var first = PlayRun.Start(transitionA, true, sourceA.Token);
            yield return null;
            var captured = OnlyPlayingSequence("transition A's play");

            switch (end)
            {
                case SequenceEnd.Completed:
                    yield return TransitionConformance.WaitFor(() => first.IsDone, "transition A's play to complete");
                    break;
                case SequenceEnd.TokenCancelled:
                    sourceA.Cancel();
                    yield return WaitAtMostFrames(CancelFrames, () => first.IsDone);
                    break;
                case SequenceEnd.Snapped:
                    transitionA.Snap(true);
                    yield return WaitAtMostFrames(CancelFrames, () => first.IsDone);
                    break;
                case SequenceEnd.KilledFromOutside:
                    DOTween.KillAll();
                    yield return WaitAtMostFrames(CancelFrames, () => first.IsDone);
                    break;
                default:
                {
                    var replacingSource = CreateSource();
                    staleSources.Add(replacingSource);
                    var replacing = PlayRun.Start(transitionA, false, replacingSource.Token);
                    yield return null;
                    var replacingSequence = OnlyPlayingSequence("transition A's replacing play");
                    Assert.That(ReferenceEquals(replacingSequence, captured), Is.True,
                        "the replacing play reuses the replaced sequence (precondition: without reuse this case checks nothing)");

                    // The replaced play's token must no longer reach the sequence its replacement now owns.
                    var alphaBeforeStaleCancel = poseA().x;
                    sourceA.Cancel();
                    yield return WaitRealSeconds(ReuseWatchSeconds);
                    Assert.That(replacing.IsDone, Is.False, "the replacing play ended after the replaced play's token was cancelled");
                    Assert.That(replacingSequence.IsActive(), Is.True, "the reused sequence after the replaced play's token was cancelled");
                    Assert.That(poseA().x, Is.LessThan(alphaBeforeStaleCancel), "transition A's alpha kept moving toward hidden");

                    // Ends the replacing play, so the sequence goes back to the pool for B.
                    transitionA.Snap(true);
                    yield return WaitAtMostFrames(CancelFrames, () => replacing.IsDone);
                    Assert.That(replacing.IsDone, Is.True, "the replacing play's await still ran " + CancelFrames + " frames after Snap");
                    Assert.That(replacing.Error, Is.Null, "the replacing play threw");
                    break;
                }
            }

            Assert.That(first.IsDone, Is.True, "transition A's play had not ended (" + end + ")");
            Assert.That(first.Error, Is.Null, "transition A's play threw");
            // One more frame, so DOTween has despawned the ended sequence into its pool.
            yield return null;

            transitionB.Snap(false);
            var playB = PlayRun.Start(transitionB, true, CancellationToken.None);
            yield return null;
            var sequenceB = OnlyPlayingSequence("transition B's play");
            Assert.That(ReferenceEquals(sequenceB, captured), Is.True,
                "B's sequence reuses A's ended sequence (precondition: without reuse this test checks nothing)");
            var alphaBBefore = poseB().x;

            for (var i = 0; i < staleSources.Count; i++)
                staleSources[i].Cancel();
            transitionA.Snap(false);
            transitionA.Snap(true);
            var playAgain = PlayRun.Start(transitionA, false, CancellationToken.None);
            yield return WaitRealSeconds(ReuseWatchSeconds);
            var alphaBAfter = poseB().x;

            Assert.That(sequenceB.IsActive(), Is.True, "B's sequence after the stale calls on A");
            Assert.That(playB.IsDone, Is.False, "B's await ended after the stale calls on A (its sequence was killed)");
            Assert.That(alphaBAfter, Is.GreaterThan(alphaBBefore), "B's alpha kept moving toward shown");
            Assert.That(alphaBAfter, Is.LessThan(1f), "B's alpha must still be short of shown");
            Assert.That(playAgain.Error, Is.Null, "transition A's later play threw");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Recycling_RepeatedPlaysCancelsAndSnaps_KeepPosesWithoutWarnings()
        {
            EnableRecyclingWithWarnings();
            var transition = CreateBenchTransition(PartSet.All, PlaySeconds);
            var pose = PoseOf(transition);
            var shown = SnapPose(transition, pose, true);
            var hidden = SnapPose(transition, pose, false);
            var before = DOTween.TotalActiveTweens();
            var problems = new List<string>();

            for (var cycle = 0; cycle < RecyclingCycles; cycle++)
                yield return RunRecyclingCycle(transition, pose, shown, hidden, cycle, problems);
            yield return null;

            Assert.That(problems, Is.Empty, "problems across " + RecyclingCycles + " cycles");
            Assert.That(DOTween.TotalActiveTweens(), Is.EqualTo(before), "live tweens after the cycles");
            LogAssert.NoUnexpectedReceived();
        }

        // Helpers

        // One cycle: a full show, a hide cancelled then snapped, a show cut by Snap, a hide replaced by a show.
        private IEnumerator RunRecyclingCycle(
            TweenTransition transition,
            Func<Vector4> pose,
            Vector4 shown,
            Vector4 hidden,
            int cycle,
            List<string> problems)
        {
            var label = "cycle " + cycle + ": ";

            transition.Snap(false);
            var full = PlayRun.Start(transition, true, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => full.IsDone, label + "the full show");
            Check(problems, full, label + "full show");
            CheckPose(problems, pose(), shown, label + "after a full show");

            var source = CreateSource();
            var cancelled = PlayRun.Start(transition, false, source.Token);
            yield return null;
            source.Cancel();
            yield return WaitAtMostFrames(CancelFrames, () => cancelled.IsDone);
            if (!cancelled.IsDone)
                problems.Add(label + "the cancelled hide still ran " + CancelFrames + " frames after the cancel");
            Check(problems, cancelled, label + "cancelled hide");
            transition.Snap(false);
            CheckPose(problems, pose(), hidden, label + "Snap(false) after a cancelled hide");

            var cut = PlayRun.Start(transition, true, CancellationToken.None);
            yield return null;
            transition.Snap(true);
            CheckPose(problems, pose(), shown, label + "Snap(true) during a show");
            yield return WaitAtMostFrames(CancelFrames, () => cut.IsDone);
            if (!cut.IsDone)
                problems.Add(label + "the show cut by Snap still ran " + CancelFrames + " frames later");
            Check(problems, cut, label + "show cut by Snap");

            var replaced = PlayRun.Start(transition, false, CancellationToken.None);
            yield return null;
            var replacing = PlayRun.Start(transition, true, CancellationToken.None);
            yield return TransitionConformance.WaitFor(() => replaced.IsDone && replacing.IsDone, label + "the replaced hide and the replacing show");
            Check(problems, replaced, label + "replaced hide");
            Check(problems, replacing, label + "replacing show");
            CheckPose(problems, pose(), shown, label + "after a show that replaced a hide");
        }

        private static void Check(List<string> problems, PlayRun run, string what)
        {
            if (run.Error != null)
                problems.Add(what + " threw " + run.Error);
        }

        private static void CheckPose(List<string> problems, Vector4 actual, Vector4 expected, string what)
        {
            if (Vector4.Distance(actual, expected) > 0.001f)
                problems.Add(what + ": expected " + expected.ToString("F4") + ", was " + actual.ToString("F4"));
        }

        // Recycling on, and DOTween logs every warning, so a touched recycled tween shows up in the log.
        private static void EnableRecyclingWithWarnings()
        {
            DOTween.defaultRecyclable = true;
            DOTween.logBehaviour = LogBehaviour.Verbose;
        }

        // Ends as soon as the condition holds or after the given number of frames; the caller asserts.
        private static IEnumerator WaitAtMostFrames(int frames, Func<bool> condition)
        {
            for (var frame = 0; frame < frames && !condition(); frame++)
                yield return null;
        }

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        private CancellationTokenSource CreateSource()
        {
            var source = new CancellationTokenSource();
            _sources.Add(source);
            return source;
        }

        private TweenTransition CreateBenchTransition(PartSet parts, float duration)
        {
            return CreateBenchTransition(
                parts == PartSet.FadeOnly || parts == PartSet.All,
                parts == PartSet.ScaleOnly || parts == PartSet.All,
                parts == PartSet.OffsetOnly || parts == PartSet.All,
                duration);
        }

        // A view stretched over a fixed 400 x 300 layer under an overlay canvas, so offsets do not depend on the screen.
        private TweenTransition CreateBenchTransition(bool fade, bool scale, bool offset, float duration)
        {
            return CreateBenchTransition(fade, scale, offset, duration, view => Stretch((RectTransform)view.transform));
        }

        // A 100 x 80 view at (30, 20) inside the 400 x 300 layer; a part that is off has a non-default authored value to keep.
        private TweenTransition CreateFixedBenchTransition(PartSet parts, float duration)
        {
            var fade = parts == PartSet.FadeOnly || parts == PartSet.All;
            var scale = parts == PartSet.ScaleOnly || parts == PartSet.All;
            var offset = parts == PartSet.OffsetOnly || parts == PartSet.All;
            return CreateBenchTransition(fade, scale, offset, duration, view =>
            {
                PlaceInsideTheLayer(view);
                if (!scale)
                    view.transform.localScale = AuthoredScale;

                if (!fade)
                    view.AddComponent<CanvasGroup>().alpha = AuthoredAlpha;
            });
        }

        // prepareView runs before the TweenTransition is added, so the view is authored when the component first sees it.
        private TweenTransition CreateBenchTransition(bool fade, bool scale, bool offset, float duration, Action<GameObject> prepareView)
        {
            var canvasObject = Track(new GameObject("BenchCanvas", typeof(RectTransform)));
            canvasObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var layer = new GameObject("BenchLayer", typeof(RectTransform));
            var layerRect = (RectTransform)layer.transform;
            layerRect.SetParent(canvasObject.transform, false);
            layerRect.anchorMin = new Vector2(0.5f, 0.5f);
            layerRect.anchorMax = new Vector2(0.5f, 0.5f);
            layerRect.sizeDelta = BenchSize;

            var view = new GameObject("BenchView", typeof(RectTransform));
            var viewRect = (RectTransform)view.transform;
            viewRect.SetParent(layerRect, false);
            prepareView(view);

            var transition = view.AddComponent<TweenTransition>();
            Configure(transition, fade, scale, offset, duration);
            return transition;
        }

        private static void PlaceInsideTheLayer(GameObject view)
        {
            var rect = (RectTransform)view.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = FixedViewSize;
            rect.anchoredPosition = AuthoredPosition;
        }

        // Reads each component directly, so a y-scale bug or a written off part shows up.
        private static void AssertFixedViewPose(TweenTransition transition, bool fade, bool scale, bool offset, bool shown, string what)
        {
            var rect = (RectTransform)transition.transform;
            var group = transition.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null, what + ": the view has no CanvasGroup");

            var alpha = fade ? (shown ? 1f : 0f) : AuthoredAlpha;
            Vector2 expectedScale = scale ? (shown ? Vector2.one : new Vector2(HiddenScale, HiddenScale)) : (Vector2)AuthoredScale;
            var expectedPosition = offset && !shown ? AuthoredPosition + Vector2.Scale(HiddenOffset, BenchSize) : AuthoredPosition;

            Assert.That(group.alpha, Is.EqualTo(alpha).Within(PoseTolerance), what + ": alpha");
            Assert.That(rect.localScale.x, Is.EqualTo(expectedScale.x).Within(PoseTolerance), what + ": scale x");
            Assert.That(rect.localScale.y, Is.EqualTo(expectedScale.y).Within(PoseTolerance), what + ": scale y");
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(expectedPosition.x).Within(PoseTolerance), what + ": anchored x");
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(expectedPosition.y).Within(PoseTolerance), what + ": anchored y");
        }

        // Every component that differs between the end poses must lie strictly between them.
        private static void AssertStrictlyBetween(Vector4 actual, Vector4 from, Vector4 to, string what)
        {
            for (var i = 0; i < 4; i++)
            {
                var low = Mathf.Min(from[i], to[i]);
                var high = Mathf.Max(from[i], to[i]);
                if (high - low <= PoseTolerance)
                    continue;

                Assert.That(actual[i], Is.GreaterThan(low).And.LessThan(high),
                    what + ": " + PoseParts[i] + " must lie strictly between " + from[i].ToString("F4") + " and " + to[i].ToString("F4") + "; pose was " + actual.ToString("F4"));
            }
        }

        // The single playing tween, which must be a sequence; nested tweens are not listed on their own.
        private static Tween OnlyPlayingSequence(string what)
        {
            var playing = DOTween.PlayingTweens();
            var count = playing == null ? 0 : playing.Count;
            Assert.That(count, Is.EqualTo(1), "playing tweens during " + what + " (precondition)");
            Assert.That(playing[0], Is.InstanceOf<Sequence>(), "the playing tween during " + what + " (precondition)");
            return playing[0];
        }

        // Real time also advances at timeScale 0, like the unscaled update the transition uses.
        private static IEnumerator WaitRealSeconds(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        private static void SetEases(TweenTransition transition, Ease show, Ease hide)
        {
            SetField(transition, show, ShowEaseNames);
            SetField(transition, hide, HideEaseNames);
        }

        private static void SetDuration(TweenTransition transition, float duration)
        {
            if (TrySetField(transition, duration, DurationNames))
                return;

            SetField(transition, duration, ShowDurationNames);
            SetField(transition, duration, HideDurationNames);
        }

        private static void Configure(TweenTransition transition, bool fade, bool scale, bool offset, float duration)
        {
            SetField(transition, fade, FadeOnNames);
            SetField(transition, scale, ScaleOnNames);
            if (!TrySetField(transition, HiddenScale, HiddenScaleNames))
                SetField(transition, new Vector3(HiddenScale, HiddenScale, 1f), HiddenScaleNames);
            SetField(transition, offset, OffsetOnNames);
            SetField(transition, HiddenOffset, HiddenOffsetNames);
            SetDuration(transition, duration);
        }

        // (alpha or -1 without a CanvasGroup, uniform scale, anchored x, anchored y) of the transition's own object.
        private static Func<Vector4> PoseOf(TweenTransition transition)
        {
            var view = transition.gameObject;
            var rect = (RectTransform)transition.transform;
            return () =>
            {
                var group = view.GetComponent<CanvasGroup>();
                var alpha = group != null ? group.alpha : -1f;
                return new Vector4(alpha, rect.localScale.x, rect.anchoredPosition.x, rect.anchoredPosition.y);
            };
        }

        private static Vector4 SnapPose(ViewTransition transition, Func<Vector4> pose, bool shown)
        {
            transition.Snap(shown);
            return pose();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Built inactive so the Inspector fields are set before Awake, like the PlayMode kit's StageWorld.
        private ViewRoot CreateRoot(float transitionTimeoutSeconds)
        {
            CreateEventSystem();

            var rootObject = Track(new GameObject("TestRoot", typeof(RectTransform)));
            rootObject.SetActive(false);
            rootObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var root = rootObject.AddComponent<ViewRoot>();
            SetField(root, false, "_initializeOnAwake");
            SetField(root, transitionTimeoutSeconds, "_transitionTimeoutSeconds");

            rootObject.SetActive(true);
            root.Initialize(new ViewRootSetup());
            return root;
        }

        // UnityEngine.UI is not referenced by this assembly, so the EventSystem is added by name.
        private void CreateEventSystem()
        {
            var eventSystemType = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");
            if (eventSystemType == null)
                return;

            var eventSystem = Track(new GameObject("TestEventSystem"));
            eventSystem.AddComponent(eventSystemType);
        }

        // An inactive view template (it serves as the prefab) with every part of its TweenTransition on.
        private GameObject CreateTemplate(string name, float duration)
        {
            var template = Track(new GameObject(name, typeof(RectTransform)));
            template.SetActive(false);
            Stretch((RectTransform)template.transform);

            // ViewHost's RequireComponent adds the Canvas and the GraphicRaycaster.
            var host = template.AddComponent<ViewHost>();
            template.AddComponent<TweenTestView>();
            var transition = template.AddComponent<TweenTransition>();
            Configure(transition, true, true, true, duration);
            SetField(host, transition, "_transition");
            return template;
        }

        private ViewKey<NoArgs, NoResult> AddRow(ViewRoot root, string id, GameObject template, ViewRetention retention, CancellationToken owner)
        {
            var row = new ViewEntry(
                id,
                template,
                null,
                ViewLayers.Screens.Id,
                retention,
                BackPolicy.LayerDefault,
                false,
                CoverPolicy.LayerDefault,
                CoveredMode.DisableCanvas,
                false);

            var catalog = Track(ViewCatalog.Create(new[] { row }));
            root.Navigator.AddCatalog(catalog, owner);
            return ViewKey.Create(id);
        }

        private static GameObject InstanceOf(ViewHandle handle)
        {
            GameObject instance;
            Assert.That(handle.TryGetInstance(out instance), Is.True, "the handle has no live instance");
            return instance;
        }

        private static bool TrySetField(object target, object value, string[] names)
        {
            for (var i = 0; i < names.Length; i++)
            {
                var field = target.GetType().GetField(names[i], BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null || !field.FieldType.IsInstanceOfType(value))
                    continue;

                field.SetValue(target, value);
                return true;
            }

            return false;
        }

        // Sets the first field among the names whose type accepts the value.
        private static void SetField(object target, object value, params string[] names)
        {
            if (!TrySetField(target, value, names))
                throw new MissingFieldException(target.GetType().Name, string.Join(" | ", names) + " (" + value.GetType().Name + ")");
        }

        // Records the outcome of one request without blocking the test.
        private sealed class CloseWatch
        {
            public bool IsDone { get; private set; }
            public ViewClosed Result { get; private set; }

            public static CloseWatch Start(ViewHandle handle)
            {
                var watch = new CloseWatch();

                // Fire-and-forget: the test polls IsDone frame by frame.
                watch.RunAsync(handle).Forget();
                return watch;
            }

            private async UniTaskVoid RunAsync(ViewHandle handle)
            {
                Result = await handle.WaitForCloseAsync(CancellationToken.None);
                IsDone = true;
            }
        }
    }
}
