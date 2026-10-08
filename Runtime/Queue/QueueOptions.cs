namespace Ecanakli.UIDirector
{
    /// <summary>Per-item options of a queued request.</summary>
    public struct QueueOptions
    {
        /// <summary>Higher is presented first; equal priorities keep their enqueue order.</summary>
        public int Priority;

        /// <summary>Free tag carried by events and gate requests.</summary>
        public string Source;
    }
}
