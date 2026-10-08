using System;
using System.Collections.Generic;

namespace Ecanakli.UIDirector.Tests
{
    // A main thread the test controls: posted actions wait in a queue until the test flushes it.
    internal sealed class FakeMainThread : IMainThread
    {
        private const int MaxFlushActions = 10000;

        private readonly Queue<Action> _queue = new Queue<Action>();

        // Set false to make the navigator believe a call comes from another thread.
        public bool IsMainThread { get; set; } = true;

        // Every action ever posted, run or not.
        public int PostedCount { get; private set; }

        // Actions posted and not yet run.
        public int PendingCount
        {
            get { return _queue.Count; }
        }

        public void Post(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            PostedCount++;
            _queue.Enqueue(action);
        }

        // Runs one posted action as the main thread; false when the queue is empty.
        public bool RunNext()
        {
            if (_queue.Count == 0)
                return false;

            var action = _queue.Dequeue();
            var was = IsMainThread;
            IsMainThread = true;
            try
            {
                action();
            }
            finally
            {
                IsMainThread = was;
            }

            return true;
        }

        // Runs posted actions, including ones posted while flushing, and returns how many ran.
        public int Flush()
        {
            var ran = 0;
            while (RunNext())
            {
                ran++;
                if (ran >= MaxFlushActions)
                    throw new InvalidOperationException("FakeMainThread: " + MaxFlushActions + " actions ran in one flush; something keeps posting.");
            }

            return ran;
        }
    }
}
