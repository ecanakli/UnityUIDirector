using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    // One queue hold; age is in seconds on the queue's scheduler clock.
    internal struct QueueHoldSnapshot
    {
        public string Reason;
        public double AgeSeconds;
        public string CallSite;
    }

    // One queued item as tools see it; age is in seconds since it was enqueued.
    internal struct QueueItemSnapshot
    {
        public int RequestId;
        public string ViewId;
        public int Priority;
        public string Source;
        public double AgeSeconds;
    }

    // A queue read-out for tools; the caller keeps one and every capture clears and refills it.
    internal sealed class QueueState
    {
        private readonly List<QueueHoldSnapshot> _holds = new List<QueueHoldSnapshot>();
        private readonly List<QueueItemSnapshot> _waiting = new List<QueueItemSnapshot>();

        public string Name { get; set; }

        public string LayerId { get; set; }

        public bool Inert { get; set; }

        public bool Held { get; set; }

        public int GateCount { get; set; }

        public List<QueueHoldSnapshot> Holds
        {
            get { return _holds; }
        }

        // Presenting is meaningful only when HasPresenting is true.
        public bool HasPresenting { get; set; }

        public QueueItemSnapshot Presenting { get; set; }

        // In presentation order: priority, then enqueue order.
        public List<QueueItemSnapshot> Waiting
        {
            get { return _waiting; }
        }

        // Empties every list and value.
        public void Reset()
        {
            _holds.Clear();
            _waiting.Clear();
            Name = null;
            LayerId = null;
            Inert = false;
            Held = false;
            GateCount = 0;
            HasPresenting = false;
            Presenting = default(QueueItemSnapshot);
        }
    }
}
