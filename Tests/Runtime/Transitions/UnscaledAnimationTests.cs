using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.UIDirector.Tests
{
    // The frame loop behind the built-in transitions and the backdrop: start and end values, cancellation and the per-frame clamp.
    [TestFixture]
    public sealed class UnscaledAnimationTests
    {
        private readonly List<CancellationTokenSource> _owners = new List<CancellationTokenSource>();

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _owners.Count; i++)
                _owners[i].Dispose();
            _owners.Clear();
        }

        private CancellationTokenSource CreateOwner()
        {
            var owner = new CancellationTokenSource();
            _owners.Add(owner);
            return owner;
        }

        private static void Record(List<float> values, float t)
        {
            values.Add(t);
        }

        [Test]
        public void RunAsync_NonPositiveOrNaNDuration_CallsStepOnceWithOneAndFinishesAtOnce([Values(0f, -1f, float.NaN)] float duration)
        {
            var values = new List<float>();

            var task = UnscaledAnimation.RunAsync(duration, values, Record, CancellationToken.None);

            Assert.That(values, Is.EqualTo(new[] { 1f }), "step calls");
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded), "task status when RunAsync returns");
        }

        [Test]
        public void RunAsync_TokenCancelledBeforeStart_CallsStepNever()
        {
            var values = new List<float>();

            var task = UnscaledAnimation.RunAsync(0.2f, values, Record, new CancellationToken(true));

            Assert.That(values, Is.Empty, "step calls");
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded), "task status when RunAsync returns");
        }

        [UnityTest]
        public IEnumerator RunAsync_CancelledAfterTheFirstFrame_StopsWithoutThrowingAndWithoutAFinalStep()
        {
            var owner = CreateOwner();
            var values = new List<float>();
            var task = UnscaledAnimation.RunAsync(1f, values, Record, owner.Token);
            yield return null;

            Assert.That(values, Is.Not.Empty, "step calls before cancellation");
            Assert.That(values[0], Is.EqualTo(0f), "first t before cancellation");
            var countAtCancel = values.Count;
            owner.Cancel();
            yield return TransitionConformance.WaitFor(() => !task.IsPending(), "the cancelled animation to return");
            yield return null;
            yield return null;

            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded), "a cancelled animation returns normally");
            Assert.That(values.Count, Is.EqualTo(countAtCancel), "step calls after cancellation; values: " + string.Join(", ", values));
            Assert.That(values, Has.None.EqualTo(1f), "no final step after cancellation");
        }

        [UnityTest]
        public IEnumerator RunAsync_NormalDuration_FirstCallIsZeroLastCallIsOneAndValuesNeverDecrease()
        {
            var values = new List<float>();
            var task = UnscaledAnimation.RunAsync(0.2f, values, Record, CancellationToken.None);

            yield return TransitionConformance.WaitFor(() => !task.IsPending(), "the animation to finish");

            Assert.That(values.Count, Is.GreaterThanOrEqualTo(2), "step calls");
            Assert.That(values[0], Is.EqualTo(0f), "first t");
            Assert.That(values[values.Count - 1], Is.EqualTo(1f), "last t");
            Assert.That(values, Is.Ordered, "t values in call order");
            Assert.That(values, Has.All.InRange(0f, 1f), "every t lies in 0..1");
            Assert.That(values, Has.Exactly(1).EqualTo(1f), "t == 1 is delivered once, as the last call");
        }

        [UnityTest]
        public IEnumerator RunAsync_FramesLongerThanTheClamp_NeedAtLeastDurationTimesThirtyFrames()
        {
            // 0.2 s at 1/30 s per frame; an unclamped loop with 60 ms frames would finish in about 4.
            const float duration = 0.2f;
            const int minimumFrames = 6;
            var recorder = new SlowStepRecorder(60);
            var task = UnscaledAnimation.RunAsync(duration, recorder, SlowStepRecorder.Step, CancellationToken.None);

            yield return TransitionConformance.WaitFor(() => !task.IsPending(), "the slowed animation to finish");

            // The first delta belongs to the frame that started the animation, before any sleep.
            var framedDeltas = recorder.Deltas.GetRange(1, recorder.Deltas.Count - 1);
            Assert.That(framedDeltas, Has.All.GreaterThan(UnscaledAnimation.MaxStepSeconds), "each animated frame must be longer than the clamp; deltas: " + string.Join(", ", framedDeltas));
            Assert.That(framedDeltas.Count, Is.GreaterThanOrEqualTo(minimumFrames), "animated frames");
        }

        [Test]
        public void RunAsync_NullStep_FaultsTheTaskWithArgumentNullException()
        {
            var task = UnscaledAnimation.RunAsync<object>(0.2f, null, null, CancellationToken.None);

            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Faulted), "task status when RunAsync returns");
            try
            {
                task.GetAwaiter().GetResult();
                Assert.Fail("Expected ArgumentNullException from the faulted task");
            }
            catch (ArgumentNullException expectedException)
            {
                Assert.That(expectedException.ParamName, Is.EqualTo("step"));
            }
        }

        // Records the unscaled delta of every step call and sleeps, so the next frame is longer than the clamp.
        private sealed class SlowStepRecorder
        {
            private readonly int _sleepMilliseconds;

            public SlowStepRecorder(int sleepMilliseconds)
            {
                _sleepMilliseconds = sleepMilliseconds;
            }

            public List<float> Deltas { get; } = new List<float>();

            public static void Step(SlowStepRecorder recorder, float t)
            {
                recorder.Deltas.Add(Time.unscaledDeltaTime);
                Thread.Sleep(recorder._sleepMilliseconds);
            }
        }
    }
}
