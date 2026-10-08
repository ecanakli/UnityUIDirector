using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests
{
    // The ViewTransition contract (snap, play, cancel, timeScale 0) as one reusable check: yield Run from a [UnityTest] with any ViewTransition.
    // readPose returns up to four numbers describing what the transition writes; give the transition a duration of at least 0.2 s.
    internal static class TransitionConformance
    {
        public const float DeadlineSeconds = 5f;

        private const float Tolerance = 0.001f;

        // A conforming play returns within this many frames of its token being cancelled; one that ignores the token runs its whole duration.
        private const int CancelFrames = 3;

        public static IEnumerator Run(ViewTransition transition, Func<Vector4> readPose)
        {
            Assert.That((bool)transition, Is.True, "transition");
            Assert.That(readPose, Is.Not.Null, "readPose");

            var poses = SnapEndPoses(transition, readPose);
            yield return PlayReaches(transition, readPose, true, poses.Shown, "show");
            yield return PlayReaches(transition, readPose, false, poses.Hidden, "hide");
            yield return CancelStopsPlay(transition, readPose, poses);
            yield return CompletesAtZeroTimeScale(transition, readPose, poses);
        }

        // Yields a frame at a time; fails after a real-time deadline, which also advances at timeScale 0.
        public static IEnumerator WaitFor(Func<bool> condition, string what, float seconds = DeadlineSeconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail("Timed out after " + seconds + " s waiting for " + what + ".");

                yield return null;
            }
        }

        public static void AssertPose(Vector4 actual, Vector4 expected, string what)
        {
            Assert.That(Vector4.Distance(actual, expected), Is.LessThanOrEqualTo(Tolerance), what + ": expected " + expected.ToString("F4") + ", was " + actual.ToString("F4"));
        }

        // Snap is synchronous, idempotent and never throws; the two end poses must differ or no other check means anything.
        private static EndPoses SnapEndPoses(ViewTransition transition, Func<Vector4> readPose)
        {
            SnapChecked(transition, true);
            var shown = readPose();
            SnapChecked(transition, true);
            AssertPose(readPose(), shown, "Snap(true) twice");

            SnapChecked(transition, false);
            var hidden = readPose();
            SnapChecked(transition, false);
            AssertPose(readPose(), hidden, "Snap(false) twice");

            SnapChecked(transition, true);
            AssertPose(readPose(), shown, "Snap(true) after Snap(false)");

            Assert.That(Vector4.Distance(shown, hidden), Is.GreaterThan(Tolerance), "Shown and hidden poses are equal (" + shown.ToString("F4") + ").");
            return new EndPoses(shown, hidden);
        }

        // A play starts from the opposite end pose and ends on the pose Snap writes for the same direction.
        private static IEnumerator PlayReaches(ViewTransition transition, Func<Vector4> readPose, bool show, Vector4 expected, string label)
        {
            SnapChecked(transition, !show);
            var run = PlayRun.Start(transition, show, CancellationToken.None);

            yield return WaitFor(() => run.IsDone, "the " + label + " play to finish");

            Assert.That(run.Error, Is.Null, "the " + label + " play threw");
            Assert.That(run.WasCanceled, Is.False, "the " + label + " play ended as cancelled with no cancelled token");
            AssertPose(readPose(), expected, "end pose of the " + label + " play");
        }

        private static IEnumerator CancelStopsPlay(ViewTransition transition, Func<Vector4> readPose, EndPoses poses)
        {
            SnapChecked(transition, false);
            var source = new CancellationTokenSource();
            try
            {
                var run = PlayRun.Start(transition, true, source.Token);
                yield return null;
                Assert.That(run.IsDone, Is.False, "the show play finished within one frame; use a longer duration so cancellation can be checked");

                source.Cancel();
                for (var frame = 0; frame < CancelFrames && !run.IsDone; frame++)
                    yield return null;

                Assert.That(run.IsDone, Is.True, "the play was still running " + CancelFrames + " frames after its token was cancelled");
                Assert.That(run.Error, Is.Null, "the cancelled play threw something other than OperationCanceledException");

                var stopped = readPose();
                yield return null;
                yield return null;
                AssertPose(readPose(), stopped, "pose after the cancelled play returned");

                SnapChecked(transition, true);
                AssertPose(readPose(), poses.Shown, "Snap(true) after a cancelled play");
                SnapChecked(transition, false);
                AssertPose(readPose(), poses.Hidden, "Snap(false) after a cancelled play");
            }
            finally
            {
                source.Dispose();
            }
        }

        // The caller's teardown restores the time scale too, because a failed assertion skips this finally.
        private static IEnumerator CompletesAtZeroTimeScale(ViewTransition transition, Func<Vector4> readPose, EndPoses poses)
        {
            var previous = Time.timeScale;
            Time.timeScale = 0f;
            try
            {
                yield return PlayReaches(transition, readPose, true, poses.Shown, "show at timeScale 0");
                yield return PlayReaches(transition, readPose, false, poses.Hidden, "hide at timeScale 0");
            }
            finally
            {
                Time.timeScale = previous;
            }
        }

        private static void SnapChecked(ViewTransition transition, bool shown)
        {
            try
            {
                transition.Snap(shown);
            }
            catch (Exception exception)
            {
                Assert.Fail("Snap(" + shown + ") threw " + exception);
            }
        }

        private readonly struct EndPoses
        {
            public readonly Vector4 Shown;
            public readonly Vector4 Hidden;

            public EndPoses(Vector4 shown, Vector4 hidden)
            {
                Shown = shown;
                Hidden = hidden;
            }
        }
    }

    // Observes one PlayAsync without blocking: done, cancelled or faulted.
    internal sealed class PlayRun
    {
        public bool IsDone { get; private set; }
        public bool WasCanceled { get; private set; }
        public Exception Error { get; private set; }

        public static PlayRun Start(ViewTransition transition, bool show, CancellationToken ct)
        {
            var run = new PlayRun();
            UniTask task;
            try
            {
                task = transition.PlayAsync(show, ct);
            }
            catch (OperationCanceledException)
            {
                run.Finish(true, null);
                return run;
            }
            catch (Exception exception)
            {
                run.Finish(false, exception);
                return run;
            }

            // Fire-and-forget: the test polls IsDone frame by frame instead of awaiting.
            run.ObserveAsync(task).Forget();
            return run;
        }

        private async UniTaskVoid ObserveAsync(UniTask task)
        {
            try
            {
                await task;
                Finish(false, null);
            }
            catch (OperationCanceledException)
            {
                Finish(true, null);
            }
            catch (Exception exception)
            {
                Finish(false, exception);
            }
        }

        private void Finish(bool canceled, Exception error)
        {
            WasCanceled = canceled;
            Error = error;
            IsDone = true;
        }
    }

    // A custom transition on scaled time: at timeScale 0 it never finishes, so only the watchdog ends it.
    // Progress is its pose (0 hidden, 1 shown); every call is recorded with the view canvas state at that moment.
    [AddComponentMenu("")]
    internal sealed class ScaledTimeTransition : ViewTransition
    {
        public const string SnapShown = "Snap(shown)";
        public const string SnapHidden = "Snap(hidden)";
        public const string PlayShow = "Play(show)";
        public const string PlayHide = "Play(hide)";

        public float Duration = 0.2f;

        [NonSerialized] public float Progress;
        [NonSerialized] public bool PlayStoppedByToken;
        [NonSerialized] public readonly List<string> Calls = new List<string>();
        [NonSerialized] public readonly List<bool> CanvasEnabledAtCall = new List<bool>();

        public override async UniTask PlayAsync(bool show, CancellationToken ct)
        {
            Record(show ? PlayShow : PlayHide);
            if (ct.IsCancellationRequested)
                return;

            Progress = show ? 0f : 1f;
            var elapsed = 0f;
            while (elapsed < Duration)
            {
                await UniTask.NextFrame(PlayerLoopTiming.Update);
                if (ct.IsCancellationRequested)
                {
                    PlayStoppedByToken = true;
                    return;
                }

                // Scaled time on purpose: this is the contract violation the watchdog exists for.
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / Duration);
                Progress = show ? t : 1f - t;
            }
        }

        public override void Snap(bool shown)
        {
            if (this == null)
                return;

            Record(shown ? SnapShown : SnapHidden);
            Progress = shown ? 1f : 0f;
        }

        private void Record(string call)
        {
            Calls.Add(call);
            var canvas = this == null ? null : GetComponent<Canvas>();
            CanvasEnabledAtCall.Add(canvas != null && canvas.enabled);
        }
    }
}
