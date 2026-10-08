using System;
using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    // One added observer; the list clears Observer when it is removed.
    internal sealed class ObserverEntry
    {
        public IViewObserver Observer;
    }

    // Isolated, allocation-free dispatch. An observer added during a dispatch sees the next event; a removal takes effect at once.
    internal sealed class ObserverList
    {
        private readonly ErrorRouter _errors;
        private readonly List<ObserverEntry> _entries = new List<ObserverEntry>();
        private int _dispatchDepth;
        private bool _needsCompaction;

        public ObserverList(ErrorRouter errors)
        {
            _errors = errors;
        }

        public int Count
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Observer != null)
                        count++;
                }

                return count;
            }
        }

        // True while Publish is delivering an event to the observers.
        public bool IsDispatching
        {
            get { return _dispatchDepth > 0; }
        }

        public ObserverEntry Add(IViewObserver observer)
        {
            var entry = new ObserverEntry { Observer = observer };
            _entries.Add(entry);
            return entry;
        }

        public void Remove(ObserverEntry entry)
        {
            if (entry == null || entry.Observer == null)
                return;

            entry.Observer = null;
            if (_dispatchDepth > 0)
                _needsCompaction = true;
            else
                _entries.Remove(entry);
        }

        public void RemoveAll()
        {
            for (var i = 0; i < _entries.Count; i++)
                _entries[i].Observer = null;

            if (_dispatchDepth > 0)
                _needsCompaction = true;
            else
                _entries.Clear();
        }

        public void Publish(in ViewEvent viewEvent)
        {
            var limit = _entries.Count;
            _dispatchDepth++;
            try
            {
                for (var i = 0; i < limit && i < _entries.Count; i++)
                {
                    var observer = _entries[i].Observer;
                    if (observer == null)
                        continue;

                    try
                    {
                        observer.OnViewEvent(in viewEvent);
                    }
                    catch (Exception exception)
                    {
                        _errors.Route(exception, ViewErrorSource.Observer, viewEvent.ViewId, viewEvent.RequestId, null);
                    }
                }
            }
            finally
            {
                _dispatchDepth--;
                if (_dispatchDepth == 0 && _needsCompaction)
                    Compact();
            }
        }

        private void Compact()
        {
            _needsCompaction = false;
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Observer == null)
                    _entries.RemoveAt(i);
            }
        }
    }
}
