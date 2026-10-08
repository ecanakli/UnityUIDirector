using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Randomized invariant tests: seeded sequences of 200 steps against the logic core on a manual stage.
    public sealed class RandomInvariantTests
    {
        private const int StepsPerSeed = 200;
        private const int SlowSeedCount = 2000;

        // Invariant ids the owner has accepted as known findings; they are not reported. Empty by default.
        private static readonly string[] KnownFindings = new string[0];

        // A small fixed set; each seed is its own test, so a failure names the seed to replay.
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        [TestCase(42)]
        [TestCase(99)]
        [TestCase(123)]
        [TestCase(256)]
        [TestCase(1000)]
        [TestCase(1234)]
        [TestCase(2024)]
        [TestCase(4096)]
        [TestCase(31337)]
        [TestCase(65535)]
        [TestCase(77777)]
        [TestCase(99991)]
        public void FixedSeed_AllInvariantsHold_AfterEveryStep_AndAtTheEnd(int seed)
        {
            var result = InvariantRun.Execute(seed, StepsPerSeed, NewOptions());

            TestContext.WriteLine(Summary(result));

            if (!result.Passed)
                Assert.Fail(result.Describe());
        }

        // The full run: 2,000 seeds. Failures are grouped by invariant id, each with its shortest failing seed.
        [Test]
        [Category("Slow")]
        public void TwoThousandSeeds_AllInvariantsHold_AfterEveryStep_AndAtTheEnd()
        {
            var clock = Stopwatch.StartNew();
            var groups = new Dictionary<string, FailureGroup>();
            var failedSeeds = 0;
            var steps = 0;
            var requested = 0;
            var shown = 0;
            var closed = 0;
            var notShown = 0;
            var enqueued = 0;
            var queuePresented = 0;
            var queueNotShown = 0;
            var queueDenied = 0;
            var queueCancelled = 0;
            var queueOwnerEnded = 0;
            var queueRootGone = 0;
            var queueRootGoneOwnLifetime = 0;
            var queueCancelledRootAlive = 0;

            for (var seed = 0; seed < SlowSeedCount; seed++)
            {
                var result = InvariantRun.Execute(seed, StepsPerSeed, NewOptions());

                steps += result.StepsRun;
                requested += result.Requested;
                shown += result.Shown;
                closed += result.Closed;
                notShown += result.NotShown;
                enqueued += result.Enqueued;
                queuePresented += result.QueuePresented;
                queueNotShown += result.QueueNotShown;
                queueDenied += result.QueueDenied;
                queueCancelled += result.QueueCancelled;
                queueOwnerEnded += result.QueueOwnerEnded;
                queueRootGone += result.QueueRootGone;
                queueRootGoneOwnLifetime += result.QueueRootGoneOwnLifetime;
                queueCancelledRootAlive += result.QueueCancelledRootAlive;

                if (result.Passed)
                    continue;

                failedSeeds++;
                for (var i = 0; i < result.Violations.Count; i++)
                    AddToGroup(groups, result, result.Violations[i]);
            }

            clock.Stop();

            TestContext.WriteLine(
                SlowSeedCount + " seeds, " + steps + " steps in " + clock.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + " s: " + requested + " requests, " + shown + " shown, " + closed + " closed, " + notShown + " not shown; queue: "
                + enqueued + " enqueued, " + queuePresented + " presented, " + queueNotShown + " ended in the queue ("
                + queueDenied + " denied, " + queueCancelled + " cancelled, " + queueRootGone + " root gone, " + queueOwnerEnded + " owner ended); "
                + queueRootGoneOwnLifetime + " root gone on an own lifetime, " + queueCancelledRootAlive + " cancelled with the root alive.");

            // Both end paths must have run, or their expectations were never checked.
            Assert.That(queueRootGoneOwnLifetime, Is.GreaterThan(0), "No waiting item ended RootGone on an own lifetime.");
            Assert.That(queueCancelledRootAlive, Is.GreaterThan(0), "No waiting item ended Cancelled while the root was alive.");

            if (failedSeeds > 0)
                Assert.Fail(Report(groups, failedSeeds));
        }

        private static InvariantOptions NewOptions()
        {
            var options = new InvariantOptions();
            for (var i = 0; i < KnownFindings.Length; i++)
                options.Disabled.Add(KnownFindings[i]);
            return options;
        }

        private static string Summary(InvariantRunResult result)
        {
            return "seed " + result.Seed + ": " + result.StepsRun + " steps, " + result.Requested + " requests, " + result.Shown
                + " shown, " + result.Closed + " closed, " + result.NotShown + " not shown.";
        }

        private sealed class FailureGroup
        {
            public string Id;
            public int Seeds;
            public int TotalSeen;
            public InvariantRunResult Shortest;
            public InvariantViolation ShortestViolation;
        }

        private static void AddToGroup(Dictionary<string, FailureGroup> groups, InvariantRunResult result, InvariantViolation violation)
        {
            FailureGroup group;
            if (!groups.TryGetValue(violation.Id, out group))
            {
                group = new FailureGroup { Id = violation.Id, Shortest = result, ShortestViolation = violation };
                groups.Add(violation.Id, group);
            }

            group.Seeds++;
            group.TotalSeen += violation.Count;

            // The shortest list of steps to the violation is the easiest to read.
            if (violation.Step < group.ShortestViolation.Step)
            {
                group.Shortest = result;
                group.ShortestViolation = violation;
            }
        }

        private static string Report(Dictionary<string, FailureGroup> groups, int failedSeeds)
        {
            var ordered = new List<FailureGroup>(groups.Values);
            ordered.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            var text = new StringBuilder();
            text.Append(failedSeeds).Append(" of ").Append(SlowSeedCount).Append(" seeds violated an invariant; ")
                .Append(ordered.Count).Append(" distinct invariant id(s).\n");

            for (var i = 0; i < ordered.Count; i++)
            {
                var group = ordered[i];
                text.Append("  ").Append(group.Id).Append(": ").Append(group.Seeds).Append(" seed(s); shortest: seed ")
                    .Append(group.Shortest.Seed).Append(", ").Append(group.ShortestViolation).Append('\n');
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                var group = ordered[i];
                text.Append("\n== ").Append(group.Id).Append(" ==\n").Append(group.Shortest.Describe(group.ShortestViolation.Step));
            }

            return text.ToString();
        }
    }
}
