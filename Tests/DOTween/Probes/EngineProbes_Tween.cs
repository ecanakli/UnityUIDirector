using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Core DOTween.dll behaviour only: generic DOTween.To, DOTween.Sequence, SetUpdate, Kill, OnKill.
    public sealed class EngineProbes_Tween
    {
        float m_Value;
        float m_OriginalTimeScale = 1f;

        [SetUp]
        public void SetUp()
        {
            m_OriginalTimeScale = Time.timeScale;
            m_Value = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            DOTween.KillAll();
            Time.timeScale = m_OriginalTimeScale;
        }

        [UnityTest]
        public IEnumerator P14_UnscaledSequence_CompletesAtTimeScaleZero()
        {
            var completed = false;
            var updates = 0;
            var controlValue = 0f;
            Sequence sequence = null;
            Tween control = null;
            try
            {
                Time.timeScale = 0f;

                var step = DOTween.To(() => m_Value, value => m_Value = value, 1f, 0.1f);
                sequence = DOTween.Sequence()
                    .Append(step)
                    .SetUpdate(true)
                    .OnUpdate(() => updates++)
                    .OnComplete(() => completed = true);

                // Control: a tween that follows the time scale must not move at timeScale 0.
                control = DOTween.To(() => controlValue, value => controlValue = value, 1f, 0.1f).SetUpdate(false);

                var start = Time.realtimeSinceStartup;
                while (!completed && Time.realtimeSinceStartup - start < 3f)
                    yield return null;

                var elapsed = Time.realtimeSinceStartup - start;
                ProbeLog.Write("P14", "case", "unscaledSequence",
                    "completed", completed,
                    "value", m_Value,
                    "updates", updates,
                    "realSecondsToComplete", elapsed,
                    "timeScaleDuringRun", Time.timeScale,
                    "controlValue", controlValue);

                if (controlValue != 0f)
                    Assert.Inconclusive("The scaled control tween moved (" + controlValue + "), so timeScale 0 was not in effect.");

                Assert.IsTrue(completed, "A SetUpdate(true) sequence must complete while Time.timeScale is 0.");
                Assert.AreEqual(1f, m_Value, 0.0001f, "The tween must reach its end value.");
            }
            finally
            {
                Time.timeScale = m_OriginalTimeScale;
                if (sequence != null && sequence.IsActive())
                    sequence.Kill();
                if (control != null && control.IsActive())
                    control.Kill();
            }
        }

        [UnityTest]
        public IEnumerator P14_Kill_InvokesOnKill_AndTweenIsInactive()
        {
            var killCount = 0;
            var completeCount = 0;
            Tween tween = DOTween.To(() => m_Value, value => m_Value = value, 1f, 5f)
                .OnKill(() => killCount++)
                .OnComplete(() => completeCount++);

            foreach (var step in Frames(3))
                yield return step;

            var activeBeforeKill = tween.IsActive();
            var valueBeforeKill = m_Value;

            tween.Kill();
            var killCountRightAfter = killCount;
            var activeRightAfter = tween.IsActive();

            foreach (var step in Frames(3))
                yield return step;

            ProbeLog.Write("P14", "case", "kill",
                "activeBeforeKill", activeBeforeKill,
                "valueBeforeKill", valueBeforeKill,
                "onKillCountRightAfterKill", killCountRightAfter,
                "activeRightAfterKill", activeRightAfter,
                "onKillCountAfterFrames", killCount,
                "valueAfterFrames", m_Value,
                "onCompleteCount", completeCount);

            Assert.IsTrue(activeBeforeKill, "Precondition: the tween is active before Kill.");
            Assert.AreEqual(1, killCount, "OnKill must be invoked exactly once.");
            Assert.IsFalse(tween.IsActive(), "A killed tween must be inactive.");
            Assert.AreEqual(0, completeCount, "Kill without complete must not invoke OnComplete.");
        }

        [UnityTest]
        public IEnumerator P14_KillOnCancellation()
        {
            var updates = 0;
            var completes = 0;
            var kills = 0;
            var cts = new CancellationTokenSource();
            Tween tween = DOTween.To(() => m_Value, value => m_Value = value, 1f, 5f)
                .OnUpdate(() => updates++)
                .OnComplete(() => completes++)
                .OnKill(() => kills++);
            var registration = cts.Token.RegisterWithoutCaptureExecutionContext(() => tween.Kill());
            try
            {
                foreach (var step in Frames(3))
                    yield return step;

                var updatesBeforeCancel = updates;
                var valueBeforeCancel = m_Value;

                cts.Cancel();
                var activeRightAfter = tween.IsActive();
                var killsRightAfter = kills;

                foreach (var step in Frames(5))
                    yield return step;

                var updatesAfterFrames = updates;
                ProbeLog.Write("P14", "case", "killOnCancellation",
                    "updatesBeforeCancel", updatesBeforeCancel,
                    "valueBeforeCancel", valueBeforeCancel,
                    "activeRightAfterCancel", activeRightAfter,
                    "killsRightAfterCancel", killsRightAfter,
                    "updatesAfterFrames", updatesAfterFrames,
                    "valueAfterFrames", m_Value,
                    "completes", completes,
                    "kills", kills);

                Assert.Greater(updatesBeforeCancel, 0, "Precondition: the tween ran before the cancel.");
                Assert.IsFalse(activeRightAfter, "Cancelling the token must kill the tween.");
                Assert.AreEqual(1, kills, "OnKill must be invoked exactly once.");
                Assert.AreEqual(updatesBeforeCancel, updatesAfterFrames, "No update may run after the cancel.");
                Assert.AreEqual(valueBeforeCancel, m_Value, 0.0001f, "The target value must not change after the cancel.");
                Assert.AreEqual(0, completes, "OnComplete must not run after the cancel.");
            }
            finally
            {
                registration.Dispose();
                cts.Dispose();
                if (tween.IsActive())
                    tween.Kill();
            }
        }

        static IEnumerable Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }
    }
}
