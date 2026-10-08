namespace Ecanakli.UIDirector
{
    /// <summary>The untyped outcome seen by an observer of a request.</summary>
    public readonly struct ViewClosed
    {
        internal ViewClosed(bool wasShown, CloseReason reason, RejectReason rejected, string detail)
        {
            WasShown = wasShown;
            Reason = reason;
            Rejected = rejected;
            Detail = detail;
        }

        /// <summary>True when the view began to show (its show transition started) before the request ended.</summary>
        public bool WasShown { get; }

        /// <summary>How the view closed; NotShown when it was never shown.</summary>
        public CloseReason Reason { get; }

        /// <summary>Why the request ended without being shown; None when it was shown.</summary>
        public RejectReason Rejected { get; }

        /// <summary>Optional text with more detail about the outcome.</summary>
        public string Detail { get; }
    }
}
