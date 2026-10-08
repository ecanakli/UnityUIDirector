namespace Ecanakli.UIDirector
{
    /// <summary>Per-call options of an open request.</summary>
    public struct OpenOptions
    {
        /// <summary>Free tag carried by events and diagnostics.</summary>
        public string Source;

        /// <summary>Refuse instead of waiting when the navigator is busy.</summary>
        public bool RejectWhenBusy;

        /// <summary>Zero to allocate an id; otherwise an id from ReserveRequestId.</summary>
        public int RequestId;

        // Set by a queue: the item's priority, carried by the request's events.
        internal int Priority;

        // Set by a queue: the layer id the request was queued for; the intake refuses a row on another layer.
        internal string ExpectedLayerId;
    }
}
