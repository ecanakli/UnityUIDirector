using System;
using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    // Read-outs for editor tools: structured state, the recent event ring and the queues built on this navigator.
    internal sealed partial class Navigator
    {
        // Queues built on this navigator; teardown clears it.
        private readonly List<ViewQueue> _queues = new List<ViewQueue>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const int RecentEventCapacity = 100;

        private readonly EventRing _eventRing = new EventRing(RecentEventCapacity);
        private ObserverEntry _eventRingEntry;
#endif

        // Observers added through AddObserver; the event ring is not one of them.
        internal int ObserverCount
        {
            get
            {
                var count = _observers.Count;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_eventRingEntry != null && _eventRingEntry.Observer != null)
                    count--;
#endif
                return count;
            }
        }

        // Clears and refills into; after teardown only RootGone is set.
        internal void CaptureState(NavigatorState into)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Reset();
            into.RootGone = _rootGone;
            if (_rootGone)
                return;

            into.Busy = IsBusy;
            into.ShieldCount = _shield.Count;
            into.ObserverCount = ObserverCount;
            into.RegistrationCount = _registrations.Count;

            for (var i = 0; i < _layerList.Count; i++)
            {
                var state = _layerList[i];
                var layer = into.AddLayer();
                layer.LayerId = state.Layer.Id;
                layer.Order = state.Layer.Order;
                layer.Rules = state.Layer.Rules;
                layer.IsRunning = state.IsRunning;
                layer.PendingOperations = state.PendingOperations;

                var requests = layer.Requests;
                for (var j = 0; j < state.Count; j++)
                {
                    var record = state[j];
                    requests.Add(new RequestSnapshot
                    {
                        RequestId = record.RequestId,
                        ViewId = record.ViewId,
                        Phase = record.Phase,
                        Slot = record.Slot,
                        ClosePending = record.ClosePending,
                        HasOutcome = record.HasOutcome,
                        OutcomeReason = record.OutcomeReason,
                        OutcomeRejected = record.OutcomeRejected,
                        Priority = record.Priority,
                        Source = record.SourceTag,
                        OwnerBound = record.OwnerBound
                    });
                }
            }

            _shield.CaptureHolders(into.ShieldHolders);
            _retained.CaptureViewIds(into.KeptViewIds);
            _preloads.CaptureViewIds(into.PreloadViewIds);
        }

        // Event ring

        // Called once in the constructor, before any observer is added, so the ring sees every event published to _observers.
        private void AttachEventRing()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _eventRingEntry = _observers.Add(_eventRing);
#endif
        }

        // Clears into and fills it newest first; always empty in release builds.
        internal int CopyRecentEvents(List<RecentEvent> into)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _eventRing.CopyTo(into);
#endif
            return into.Count;
        }

        // A queue on this navigator records its own events here, marked as queue events; does nothing in release builds.
        internal void RecordQueueEvent(in ViewEvent viewEvent)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _eventRing.Record(in viewEvent, true);
#endif
        }

        // Queue registry

        internal void RegisterQueue(ViewQueue queue)
        {
            if (queue == null || _rootGone || _queues.Contains(queue))
                return;

            _queues.Add(queue);
        }

        internal void UnregisterQueue(ViewQueue queue)
        {
            if (queue == null)
                return;

            _queues.Remove(queue);
        }

        // Clears into and fills it in registration order.
        internal int CopyQueues(List<ViewQueue> into)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();
            for (var i = 0; i < _queues.Count; i++)
                into.Add(_queues[i]);

            return into.Count;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // A fixed ring of the last events; a write overwrites the oldest slot and allocates nothing.
        private sealed class EventRing : IViewObserver
        {
            private readonly RecentEvent[] _slots;
            private int _next;
            private int _count;
            private long _sequence;

            public EventRing(int capacity)
            {
                _slots = new RecentEvent[capacity];
            }

            public void OnViewEvent(in ViewEvent viewEvent)
            {
                Record(in viewEvent, false);
            }

            public void Record(in ViewEvent viewEvent, bool fromQueue)
            {
                _sequence++;
                _slots[_next] = new RecentEvent(in viewEvent, fromQueue, _sequence);
                _next = (_next + 1) % _slots.Length;
                if (_count < _slots.Length)
                    _count++;
            }

            // Newest first.
            public void CopyTo(List<RecentEvent> into)
            {
                var length = _slots.Length;
                for (var i = 1; i <= _count; i++)
                    into.Add(_slots[(_next - i + length) % length]);
            }
        }
#endif
    }
}
