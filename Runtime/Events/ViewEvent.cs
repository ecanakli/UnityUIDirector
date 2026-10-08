namespace Ecanakli.UIDirector
{
    /// <summary>One fact about one request; published after the state it describes is in place.</summary>
    public readonly struct ViewEvent
    {
        internal ViewEvent(
            ViewEventKind kind,
            int requestId,
            string viewId,
            string layerId,
            string source,
            int priority,
            CloseReason reason,
            RejectReason rejected,
            string detail,
            float secondsWaiting,
            float secondsOpen)
        {
            Kind = kind;
            RequestId = requestId;
            ViewId = viewId;
            LayerId = layerId;
            Source = source;
            Priority = priority;
            Reason = reason;
            Rejected = rejected;
            Detail = detail;
            SecondsWaiting = secondsWaiting;
            SecondsOpen = secondsOpen;
        }

        /// <summary>The kind of fact.</summary>
        public ViewEventKind Kind { get; }

        /// <summary>The id of the request; zero for Settled.</summary>
        public int RequestId { get; }

        /// <summary>The id of the view; null for Settled.</summary>
        public string ViewId { get; }

        /// <summary>The id of the layer the view is on; null for Settled.</summary>
        public string LayerId { get; }

        /// <summary>The free tag the caller put in the open options.</summary>
        public string Source { get; }

        /// <summary>The queue priority of the item; zero for requests that did not come from a queue.</summary>
        public int Priority { get; }

        /// <summary>How the view closed; set on Closed and NotShown, and CloseReason.NotShown (the default) on every other kind.</summary>
        public CloseReason Reason { get; }

        /// <summary>Why the request was not shown; set on NotShown.</summary>
        public RejectReason Rejected { get; }

        /// <summary>Optional text with more detail about the outcome.</summary>
        public string Detail { get; }

        /// <summary>Seconds from the navigator's acceptance to Shown, without time spent in a queue before it or while the app was suspended; set on Shown.</summary>
        public float SecondsWaiting { get; }

        /// <summary>Seconds from Shown to Closed, excluding time the app was suspended; set on Closed.</summary>
        public float SecondsOpen { get; }
    }
}
