using System;
using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    // Pending notifications run one at a time; one added while another runs goes next, in the order added.
    // Nothing runs while an observer dispatch is in progress; the code that owns that dispatch drains afterwards.
    internal sealed class Outbox
    {
        private struct Entry
        {
            public Action<object> Action;
            public object State;
            public ViewEvent Event;
            public bool IsEvent;
            public ViewErrorSource Source;
            public string ViewId;
            public int RequestId;
        }

        private readonly ErrorRouter _errors;
        private readonly ObserverList _observers;
        private readonly List<Entry> _entries = new List<Entry>(16);
        private int _head;
        private int _insertOffset;
        private bool _running;

        public Outbox(ErrorRouter errors, ObserverList observers)
        {
            _errors = errors;
            _observers = observers;
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        // Notifications not started yet.
        public int Count
        {
            get { return _entries.Count - _head; }
        }

        // The action should be a cached delegate and the state an existing object, so nothing is allocated here.
        public void Enqueue(Action<object> action, object state, ViewErrorSource source, string viewId, int requestId)
        {
            if (action == null)
                return;

            var entry = new Entry
            {
                Action = action,
                State = state,
                Source = source,
                ViewId = viewId,
                RequestId = requestId
            };

            Insert(in entry);
        }

        // Publishes now, or holds the event back until the observer dispatch in progress has reached every observer.
        public void PublishOrDefer(in ViewEvent viewEvent)
        {
            if (_observers.IsDispatching)
                EnqueueEvent(in viewEvent);
            else
                _observers.Publish(in viewEvent);

            Run();
        }

        // Runs the action now, or holds it back until the observer dispatch in progress has reached every observer.
        public void RunOrDefer(Action<object> action, object state, ViewErrorSource source, string viewId, int requestId)
        {
            if (_observers.IsDispatching)
            {
                Enqueue(action, state, source, viewId, requestId);
            }
            else if (action != null)
            {
                try
                {
                    action(state);
                }
                catch (Exception exception)
                {
                    _errors.Route(exception, source, viewId, requestId, null);
                }
            }

            Run();
        }

        // Drains until empty; a call while draining or dispatching does nothing, the owner of either drains afterwards.
        public void Run()
        {
            if (_running || _observers.IsDispatching)
                return;

            _running = true;
            try
            {
                while (_head < _entries.Count)
                {
                    var entry = _entries[_head];
                    _entries[_head] = default(Entry);
                    _head++;
                    _insertOffset = 0;

                    try
                    {
                        if (entry.IsEvent)
                            _observers.Publish(in entry.Event);
                        else
                            entry.Action(entry.State);
                    }
                    catch (Exception exception)
                    {
                        _errors.Route(exception, entry.Source, entry.ViewId, entry.RequestId, null);
                    }
                }

                _entries.Clear();
                _head = 0;
            }
            finally
            {
                _insertOffset = 0;
                _running = false;
            }
        }

        private void EnqueueEvent(in ViewEvent viewEvent)
        {
            var entry = new Entry
            {
                Event = viewEvent,
                IsEvent = true,
                Source = ViewErrorSource.Observer,
                ViewId = viewEvent.ViewId,
                RequestId = viewEvent.RequestId
            };

            Insert(in entry);
        }

        private void Insert(in Entry entry)
        {
            if (_running)
            {
                // Right after the running one, behind the ones it already added.
                _entries.Insert(_head + _insertOffset, entry);
                _insertOffset++;
            }
            else
            {
                _entries.Add(entry);
            }
        }
    }
}
