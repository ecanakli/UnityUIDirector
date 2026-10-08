using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    // The part of a request record that a layer list needs; ViewRecord implements it in the next step.
    internal interface ILayerRecord
    {
        int RequestId { get; }

        string ViewId { get; }

        ViewPhase Phase { get; }

        // The slot inside the layer's sorting band, 1 to 99; set by the list.
        int Slot { get; set; }

        // Increases with every acceptance, so "newest" never depends on reserved ids.
        long AcceptSequence { get; }

        ViewEntry Row { get; }
    }

    internal enum LayerOperationKind
    {
        Open,
        Close,
        PopTo,
        CloseAll
    }

    // One serialized unit of work on a layer; Record is the subject (the target for PopTo, none for CloseAll).
    internal sealed class LayerOperation<TRecord> where TRecord : class
    {
        public readonly LayerOperationKind Kind;
        public readonly TRecord Record;
        public readonly CloseReason Reason;

        // The record whose shield hold a pop-to or close-all relies on until it ends; null when it relies on none.
        public readonly TRecord HoldRecord;

        public LayerOperation(LayerOperationKind kind, TRecord record, CloseReason reason, TRecord holdRecord = null)
        {
            Kind = kind;
            Record = record;
            Reason = reason;
            HoldRecord = holdRecord;
        }
    }

    // The ordered records of one layer, its operation FIFO and its slot numbers; it knows no other layer.
    internal sealed class LayerState<TRecord> where TRecord : class, ILayerRecord
    {
        private readonly List<TRecord> _records = new List<TRecord>();
        private readonly Queue<LayerOperation<TRecord>> _operations = new Queue<LayerOperation<TRecord>>();

        public LayerState(ViewLayer layer)
        {
            Layer = layer;
        }

        public ViewLayer Layer { get; }

        public int Count
        {
            get { return _records.Count; }
        }

        public TRecord this[int index]
        {
            get { return _records[index]; }
        }

        // True from the moment a runner takes the layer until it finds no more work.
        public bool IsRunning { get; set; }

        public int PendingOperations
        {
            get { return _operations.Count; }
        }

        // Highest live slot plus one; 1 for an empty layer. The caller checks it against the band.
        public int NextSlot()
        {
            var highest = 0;
            for (var i = 0; i < _records.Count; i++)
            {
                if (_records[i].Slot > highest)
                    highest = _records[i].Slot;
            }

            return highest + 1;
        }

        // Appends the record with the slot the caller computed; the slot never changes while the record is live.
        public void Add(TRecord record, int slot)
        {
            record.Slot = slot;
            _records.Add(record);
        }

        public bool Remove(TRecord record)
        {
            var index = _records.IndexOf(record);
            if (index < 0)
                return false;

            _records.RemoveAt(index);
            return true;
        }

        public int IndexOf(TRecord record)
        {
            return _records.IndexOf(record);
        }

        public bool Contains(TRecord record)
        {
            return _records.IndexOf(record) >= 0;
        }

        // Puts the replacement at the old record's position with its slot and removes the old record; false when the old one is not here.
        public bool Replace(TRecord old, TRecord replacement)
        {
            if (_records.IndexOf(old) < 0)
                return false;

            var slot = old.Slot;
            var replacementIndex = _records.IndexOf(replacement);
            if (replacementIndex >= 0)
                _records.RemoveAt(replacementIndex);

            var oldIndex = _records.IndexOf(old);
            _records[oldIndex] = replacement;
            replacement.Slot = slot;
            return true;
        }

        // The last record that has an instance on screen (Opening, Open or Closing); null when none.
        public TRecord TopOnScreen()
        {
            for (var i = _records.Count - 1; i >= 0; i--)
            {
                if (Projection.IsOnScreen(_records[i].Phase))
                    return _records[i];
            }

            return null;
        }

        // The topmost record with the view id; null when none.
        public TRecord FindLastById(string viewId)
        {
            for (var i = _records.Count - 1; i >= 0; i--)
            {
                if (_records[i].ViewId == viewId)
                    return _records[i];
            }

            return null;
        }

        public void Enqueue(LayerOperation<TRecord> operation)
        {
            _operations.Enqueue(operation);
        }

        public bool TryDequeue(out LayerOperation<TRecord> operation)
        {
            if (_operations.Count == 0)
            {
                operation = null;
                return false;
            }

            operation = _operations.Dequeue();
            return true;
        }

        public void ClearOperations()
        {
            _operations.Clear();
        }
    }
}
