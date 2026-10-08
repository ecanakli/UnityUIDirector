using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    // One request as tools see it, copied out of its record.
    internal struct RequestSnapshot
    {
        public int RequestId;
        public string ViewId;
        public ViewPhase Phase;
        public int Slot;
        public bool ClosePending;

        // OutcomeReason and OutcomeRejected are meaningful only when HasOutcome is true.
        public bool HasOutcome;
        public CloseReason OutcomeReason;
        public RejectReason OutcomeRejected;
        public int Priority;
        public string Source;

        // An owner that can end the request is registered (not CancellationToken.None).
        public bool OwnerBound;
    }

    // One shield holder; age is in seconds on the navigator clock.
    internal struct ShieldHolderSnapshot
    {
        public int HolderId;
        public ShieldHolderKind Kind;
        public int RequestId;
        public string Reason;
        public double AgeSeconds;
        public string CallSite;
    }

    // One layer and its requests in list order; reused across captures.
    internal sealed class LayerSnapshot
    {
        private readonly List<RequestSnapshot> _requests = new List<RequestSnapshot>();

        public string LayerId { get; set; }

        public int Order { get; set; }

        public ViewLayerRules Rules { get; set; }

        public bool IsRunning { get; set; }

        public int PendingOperations { get; set; }

        public List<RequestSnapshot> Requests
        {
            get { return _requests; }
        }
    }

    // One published event; Sequence grows by one per recorded event, FromQueue marks events a queue wrote.
    internal readonly struct RecentEvent
    {
        public RecentEvent(in ViewEvent viewEvent, bool fromQueue, long sequence)
        {
            Event = viewEvent;
            FromQueue = fromQueue;
            Sequence = sequence;
        }

        public ViewEvent Event { get; }

        public bool FromQueue { get; }

        public long Sequence { get; }
    }

    // A navigator read-out for tools; the caller keeps one and every capture clears and refills it.
    internal sealed class NavigatorState
    {
        private readonly List<LayerSnapshot> _layers = new List<LayerSnapshot>();
        // Every layer object ever made, reused in order; a capture after the first allocates nothing.
        private readonly List<LayerSnapshot> _layerPool = new List<LayerSnapshot>();
        private int _layersUsed;
        private readonly List<ShieldHolderSnapshot> _shieldHolders = new List<ShieldHolderSnapshot>();
        private readonly List<string> _keptViewIds = new List<string>();
        private readonly List<string> _preloadViewIds = new List<string>();

        public bool RootGone { get; set; }

        public bool Busy { get; set; }

        public int ShieldCount { get; set; }

        public int ObserverCount { get; set; }

        public int RegistrationCount { get; set; }

        public List<LayerSnapshot> Layers
        {
            get { return _layers; }
        }

        public List<ShieldHolderSnapshot> ShieldHolders
        {
            get { return _shieldHolders; }
        }

        public List<string> KeptViewIds
        {
            get { return _keptViewIds; }
        }

        public List<string> PreloadViewIds
        {
            get { return _preloadViewIds; }
        }

        // Empties every list and value; the layer objects stay in the pool for the next capture.
        public void Reset()
        {
            _layersUsed = 0;
            _layers.Clear();
            _shieldHolders.Clear();
            _keptViewIds.Clear();
            _preloadViewIds.Clear();
            RootGone = false;
            Busy = false;
            ShieldCount = 0;
            ObserverCount = 0;
            RegistrationCount = 0;
        }

        // Appends an empty layer, reusing a pooled one when there is one.
        public LayerSnapshot AddLayer()
        {
            LayerSnapshot layer;
            if (_layersUsed < _layerPool.Count)
            {
                layer = _layerPool[_layersUsed];
            }
            else
            {
                layer = new LayerSnapshot();
                _layerPool.Add(layer);
            }

            _layersUsed++;
            layer.Requests.Clear();
            layer.LayerId = null;
            layer.Order = 0;
            layer.Rules = default(ViewLayerRules);
            layer.IsRunning = false;
            layer.PendingOperations = 0;
            _layers.Add(layer);
            return layer;
        }
    }
}
