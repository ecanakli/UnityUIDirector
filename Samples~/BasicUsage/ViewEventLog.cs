using UnityEngine;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>Prints one line per view event; the pattern for feeding analytics or a debug overlay.</summary>
    public sealed class ViewEventLog : IViewObserver
    {
        public void OnViewEvent(in ViewEvent viewEvent)
        {
            // Settled has no request and no view.
            if (viewEvent.Kind == ViewEventKind.Settled)
            {
                Debug.Log("[views] Settled");
                return;
            }

            var line = $"[views] {viewEvent.Kind} #{viewEvent.RequestId} view={viewEvent.ViewId} layer={viewEvent.LayerId} source={viewEvent.Source}";

            switch (viewEvent.Kind)
            {
                case ViewEventKind.Shown:
                    line += $" waited={viewEvent.SecondsWaiting:0.00}s";
                    break;
                case ViewEventKind.Closed:
                    line += $" reason={viewEvent.Reason} open={viewEvent.SecondsOpen:0.00}s";
                    break;
                case ViewEventKind.NotShown:
                    line += $" rejected={viewEvent.Rejected} detail={viewEvent.Detail}";
                    break;
            }

            Debug.Log(line);
        }
    }
}
