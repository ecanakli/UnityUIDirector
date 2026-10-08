using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // UniTask behaviours the package relies on. No player loop is needed for any of them.
    public sealed class EngineProbes_UniTask
    {
        // A private payload type keeps the pooled auto-reset source pool of this probe separate.
        readonly struct Marker
        {
            public readonly int Value;

            public Marker(int value)
            {
                Value = value;
            }

            public override string ToString()
            {
                return Value.ToString();
            }
        }

        sealed class Outcome
        {
            public bool Completed;
            public string Result = "-";
            public string Error = "-";
            public string ErrorMessage = "-";

            public string Describe()
            {
                return Completed ? "ok:" + Result : "err:" + Error;
            }
        }

        [Test]
        public void P11_CompletionSource_TrySetResult_RunsContinuationSynchronously()
        {
            var source = new UniTaskCompletionSource<int>();
            var events = new List<string>();

            WaitThenRecord(source, events).Forget();
            events.Add("beforeSet");
            var accepted = source.TrySetResult(5);
            events.Add("afterSet");

            ProbeLog.Write("P11", "case", "syncContinuation", "accepted", accepted, "events", events);

            Assert.IsTrue(accepted, "TrySetResult must accept the first result.");
            CollectionAssert.AreEqual(
                new[] { "awaiting", "beforeSet", "resumed:5", "afterSet" },
                events,
                "The continuation must run inside TrySetResult, before it returns.");
        }

        [Test]
        public void P11_CompletionSource_Task_AwaitedTwice()
        {
            // Both awaiters register before completion.
            var pending = new UniTaskCompletionSource<int>();
            var first = new Outcome();
            var second = new Outcome();
            Observe(pending.Task, first);
            Observe(pending.Task, second);
            pending.TrySetResult(7);

            // The source completes first, then both await.
            var completed = new UniTaskCompletionSource<int>();
            completed.TrySetResult(7);
            var third = new Outcome();
            var fourth = new Outcome();
            Observe(completed.Task, third);
            Observe(completed.Task, fourth);

            var awaitBefore = first.Describe() + "|" + second.Describe();
            var awaitAfter = third.Describe() + "|" + fourth.Describe();
            ProbeLog.Write("P11", "case", "taskAwaitedTwice",
                "awaitBeforeCompletion", awaitBefore,
                "awaitAfterCompletion", awaitAfter,
                "secondErrorBefore", second.ErrorMessage,
                "secondErrorAfter", fourth.ErrorMessage,
                "stableAcrossOrders", awaitBefore == awaitAfter);

            Assert.AreEqual(awaitBefore, awaitAfter, "Two awaiters must behave the same whether they await before or after completion.");
        }

        [Test]
        public void P11_AutoResetCompletionSource_SecondAwait_Throws()
        {
            var source = AutoResetUniTaskCompletionSource<Marker>.Create();
            var task = source.Task;
            source.TrySetResult(new Marker(3));

            var first = new Outcome();
            var second = new Outcome();
            Observe(task, first);
            Observe(task, second);

            ProbeLog.Write("P11", "case", "autoResetSecondAwait",
                "first", first.Describe(),
                "second", second.Describe(),
                "secondMessage", second.ErrorMessage);

            Assert.IsTrue(first.Completed, "The first await must complete with the result.");
            Assert.IsFalse(second.Completed, "The second await of a pooled auto-reset source must not complete.");
            StringAssert.Contains("InvalidOperationException", second.Error, "The second await must throw InvalidOperationException.");
        }

        [Test]
        public void P11_RegisterWithoutCaptureExecutionContext_DisposeUnregistersWithoutInvoking()
        {
            var cts = new CancellationTokenSource();
            try
            {
                var disposedCalls = 0;
                var liveCalls = 0;
                var disposedRegistration = cts.Token.RegisterWithoutCaptureExecutionContext(() => disposedCalls++);
                var liveRegistration = cts.Token.RegisterWithoutCaptureExecutionContext(() => liveCalls++);
                var flowSuppressedAfterRegister = ExecutionContext.IsFlowSuppressed();

                disposedRegistration.Dispose();
                cts.Cancel();
                liveRegistration.Dispose();

                ProbeLog.Write("P11", "case", "registerDispose",
                    "disposedRegistrationCalls", disposedCalls,
                    "liveRegistrationCalls", liveCalls,
                    "executionContextFlowSuppressedAfterRegister", flowSuppressedAfterRegister);

                Assert.AreEqual(0, disposedCalls, "A disposed registration must not be invoked by Cancel.");
                Assert.AreEqual(1, liveCalls, "Control: a live registration is invoked once by Cancel.");
                Assert.IsFalse(flowSuppressedAfterRegister, "The registration helper must restore the execution context flow.");
            }
            finally
            {
                cts.Dispose();
            }
        }

        [Test]
        public void P11_MainThreadDetection()
        {
            if (PlayerLoopHelper.MainThreadId == 0)
                Assert.Inconclusive("UniTask has not captured the main thread id in this run.");

            var onThisThread = PlayerLoopHelper.IsMainThread;
            var onPoolThread = Task.Run(() => PlayerLoopHelper.IsMainThread).GetAwaiter().GetResult();

            ProbeLog.Write("P11", "case", "mainThread",
                "mainThreadId", PlayerLoopHelper.MainThreadId,
                "currentThreadId", Thread.CurrentThread.ManagedThreadId,
                "isMainThreadHere", onThisThread,
                "isMainThreadOnPoolThread", onPoolThread);

            Assert.IsTrue(onThisThread, "PlayerLoopHelper.IsMainThread must be true on the test thread.");
            Assert.IsFalse(onPoolThread, "PlayerLoopHelper.IsMainThread must be false on a thread-pool thread.");
        }

        static async UniTaskVoid WaitThenRecord(UniTaskCompletionSource<int> source, List<string> events)
        {
            events.Add("awaiting");
            var value = await source.Task;
            events.Add("resumed:" + value);
        }

        // Follows the exact await protocol: IsCompleted, then UnsafeOnCompleted, then GetResult.
        static void Observe<T>(UniTask<T> task, Outcome outcome)
        {
            try
            {
                var awaiter = task.GetAwaiter();
                Action resume = () =>
                {
                    try
                    {
                        var value = awaiter.GetResult();
                        outcome.Result = value == null ? "null" : value.ToString();
                        outcome.Completed = true;
                    }
                    catch (Exception exception)
                    {
                        outcome.Error = "GetResult:" + exception.GetType().Name;
                        outcome.ErrorMessage = exception.Message;
                    }
                };

                if (awaiter.IsCompleted)
                    resume();
                else
                    awaiter.UnsafeOnCompleted(resume);
            }
            catch (Exception exception)
            {
                outcome.Error = "Register:" + exception.GetType().Name;
                outcome.ErrorMessage = exception.Message;
            }
        }
    }
}
