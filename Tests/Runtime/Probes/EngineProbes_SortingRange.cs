using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Canvas.sortingOrder range on an override-sorting nested canvas.
    public sealed class EngineProbes_SortingRange
    {
        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();

        [TearDown]
        public void TearDown()
        {
            m_Cleanup.DestroyAll();
        }

        [Test]
        public void P17_SortingOrder_Range()
        {
            var root = ProbeUi.NewRootCanvas("ProbeRoot");
            m_Cleanup.Track(root.gameObject);
            var nested = ProbeUi.NewNestedCanvas("Nested", root.transform, 0, true, false);

            nested.sortingOrder = 29999;
            var read29999 = nested.sortingOrder;
            nested.sortingOrder = 32767;
            var read32767 = nested.sortingOrder;

            // Out-of-range values may log; the logs are part of the observation.
            int read32768;
            int read40000;
            int logCount;
            string firstLog;
            using (var capture = new ProbeLogCapture())
            {
                nested.sortingOrder = 32768;
                read32768 = nested.sortingOrder;
                nested.sortingOrder = 40000;
                read40000 = nested.sortingOrder;
                logCount = capture.Count;
                firstLog = capture.First;
            }

            ProbeLog.Write("P17",
                "set29999", read29999,
                "set32767", read32767,
                "set32768", read32768,
                "set40000", read40000,
                "outOfRangeLogCount", logCount,
                "outOfRangeFirstLog", firstLog);

            Assert.AreEqual(29999, read29999, "29999 must be stored as given.");
            Assert.AreEqual(32767, read32767, "32767 must be stored as given.");
        }
    }
}
