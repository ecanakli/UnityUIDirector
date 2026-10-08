using Ecanakli.UIDirector;
using UnityEngine;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>Prints one console line per view event; register one on the queue and one on the navigator to see the whole story.</summary>
    public sealed class QueueEventLog : IViewObserver
    {
        private readonly string _tag;

        // A queue observer sees only Enqueued and the queue's own NotShown; everything after presentation is a navigator event.
        public QueueEventLog(string tag)
        {
            _tag = tag;
        }

        public void OnViewEvent(in ViewEvent viewEvent)
        {
            var line = $"{_tag} {viewEvent.Kind} #{viewEvent.RequestId} {viewEvent.ViewId} priority={viewEvent.Priority} source={viewEvent.Source}";

            if (viewEvent.Kind == ViewEventKind.NotShown)
                line += $" rejected={viewEvent.Rejected} detail={viewEvent.Detail}";
            else if (viewEvent.Kind == ViewEventKind.Closed)
                line += $" reason={viewEvent.Reason}";

            Debug.Log(line);
        }
    }
}
