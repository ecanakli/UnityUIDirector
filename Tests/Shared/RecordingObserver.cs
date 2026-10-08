using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // An observer that keeps every event it receives, in order.
    internal sealed class RecordingObserver : IViewObserver
    {
        private readonly List<ViewEvent> _events = new List<ViewEvent>();

        // When set, thrown after the event is recorded; for tests of observer isolation.
        public Exception ThrowOnEvent { get; set; }

        public IReadOnlyList<ViewEvent> Events
        {
            get { return _events; }
        }

        public int Count
        {
            get { return _events.Count; }
        }

        public void OnViewEvent(in ViewEvent viewEvent)
        {
            _events.Add(viewEvent);

            if (ThrowOnEvent != null)
                throw ThrowOnEvent;
        }

        public void Clear()
        {
            _events.Clear();
        }

        public int CountOf(ViewEventKind kind)
        {
            var count = 0;
            for (var i = 0; i < _events.Count; i++)
            {
                if (_events[i].Kind == kind)
                    count++;
            }

            return count;
        }

        // The events of one request, in order; Settled has request id 0.
        public ViewEvent[] EventsFor(int requestId)
        {
            var result = new List<ViewEvent>();
            for (var i = 0; i < _events.Count; i++)
            {
                if (_events[i].RequestId == requestId)
                    result.Add(_events[i]);
            }

            return result.ToArray();
        }

        // The event kinds of one request, in order.
        public ViewEventKind[] KindsFor(int requestId)
        {
            return Kinds(EventsFor(requestId));
        }

        // The event kinds of every request for one view id, in order.
        public ViewEventKind[] KindsForView(string viewId)
        {
            var result = new List<ViewEvent>();
            for (var i = 0; i < _events.Count; i++)
            {
                if (string.Equals(_events[i].ViewId, viewId, StringComparison.Ordinal))
                    result.Add(_events[i]);
            }

            return Kinds(result.ToArray());
        }

        // Every event kind, in order.
        public ViewEventKind[] AllKinds()
        {
            return Kinds(_events.ToArray());
        }

        // The newest event of the kind; fails the test when there is none.
        public ViewEvent LastOf(ViewEventKind kind)
        {
            for (var i = _events.Count - 1; i >= 0; i--)
            {
                if (_events[i].Kind == kind)
                    return _events[i];
            }

            Assert.Fail("No " + kind + " event was recorded.");
            return default(ViewEvent);
        }

        private static ViewEventKind[] Kinds(ViewEvent[] events)
        {
            var kinds = new ViewEventKind[events.Length];
            for (var i = 0; i < events.Length; i++)
                kinds[i] = events[i].Kind;
            return kinds;
        }
    }
}
