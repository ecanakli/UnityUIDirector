namespace Ecanakli.UIDirector
{
    /// <summary>What a gate is asked about: one queued request, at enqueue or right before it is presented.</summary>
    public readonly struct GateRequest
    {
        internal GateRequest(int requestId, string viewId, ViewLayer layer, int priority, string source, bool atPresentation)
        {
            RequestId = requestId;
            ViewId = viewId;
            Layer = layer;
            Priority = priority;
            Source = source;
            AtPresentation = atPresentation;
        }

        /// <summary>The id the request keeps from enqueue to close.</summary>
        public int RequestId { get; }

        /// <summary>The requested view id.</summary>
        public string ViewId { get; }

        /// <summary>The layer the queue presents on.</summary>
        public ViewLayer Layer { get; }

        /// <summary>The item's priority.</summary>
        public int Priority { get; }

        /// <summary>The item's source tag; may be null.</summary>
        public string Source { get; }

        /// <summary>False at enqueue, true right before the item is presented.</summary>
        public bool AtPresentation { get; }
    }
}
