namespace Ecanakli.UIDirector
{
    /// <summary>The kind of fact a view event reports.</summary>
    public enum ViewEventKind
    {
        /// <summary>A queue accepted the item; published by the queue only.</summary>
        Enqueued,

        /// <summary>The navigator accepted the request.</summary>
        Requested,

        /// <summary>The show transition ended and the view is open.</summary>
        Shown,

        /// <summary>The view ended after being shown. Its instance was released, or parked for reuse when the row says KeepInstance.</summary>
        Closed,

        /// <summary>The request ended without being shown.</summary>
        NotShown,

        /// <summary>The navigator went from busy to settled; the request id is zero.</summary>
        Settled
    }
}
