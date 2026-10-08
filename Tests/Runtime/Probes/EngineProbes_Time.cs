using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Unscaled time keeps advancing while the time scale is zero.
    public sealed class EngineProbes_Time
    {
        float m_OriginalTimeScale = 1f;

        [SetUp]
        public void SetUp()
        {
            m_OriginalTimeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = m_OriginalTimeScale;
        }

        [UnityTest]
        public IEnumerator P10_UnscaledDeltaTime_AdvancesAtTimeScaleZero()
        {
            var original = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                yield return null;

                var startUnscaled = Time.unscaledTime;
                var startRealtime = Time.realtimeSinceStartup;
                var maxUnscaledDelta = 0f;
                var maxScaledDelta = 0f;
                var firstUnscaledDelta = 0f;

                for (var i = 0; i < 5; i++)
                {
                    yield return null;
                    if (i == 0)
                        firstUnscaledDelta = Time.unscaledDeltaTime;
                    maxUnscaledDelta = Mathf.Max(maxUnscaledDelta, Time.unscaledDeltaTime);
                    maxScaledDelta = Mathf.Max(maxScaledDelta, Time.deltaTime);
                }

                var unscaledAdvance = Time.unscaledTime - startUnscaled;
                var realtimeAdvance = Time.realtimeSinceStartup - startRealtime;
                ProbeLog.Write("P10",
                    "timeScale", Time.timeScale,
                    "firstUnscaledDeltaAfterZero", firstUnscaledDelta,
                    "maxUnscaledDelta", maxUnscaledDelta,
                    "maxScaledDelta", maxScaledDelta,
                    "unscaledTimeAdvance", unscaledAdvance,
                    "realtimeAdvance", realtimeAdvance);

                Assert.Greater(unscaledAdvance, 0f, "Time.unscaledTime must advance at timeScale 0.");
                Assert.Greater(maxUnscaledDelta, 0f, "Time.unscaledDeltaTime must be positive at timeScale 0.");
                Assert.AreEqual(0f, maxScaledDelta, "Time.deltaTime must stay zero at timeScale 0.");
            }
            finally
            {
                Time.timeScale = original;
            }
        }
    }
}
