using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Ecanakli.UIDirector.EditorTools
{
    // What the UIDirector window draws: captured state of one navigator, plus the text formats the window and tests share.
    internal sealed class UIDirectorWindowModel
    {
        internal const int MaxEvents = 100;

        internal const string NoRootStatus = "No view root in the loaded scenes.";
        internal const string NotInitializedStatus = "The selected root is not initialised yet.";
        internal const string NoNavigatorStatus = "No navigator to show.";

        private readonly NavigatorState _state = new NavigatorState();
        private readonly List<ViewQueue> _queueSources = new List<ViewQueue>();
        private readonly List<QueueState> _queues = new List<QueueState>();
        private readonly Stack<QueueState> _spareQueues = new Stack<QueueState>();
        private readonly List<RecentEvent> _events = new List<RecentEvent>(MaxEvents);
        private readonly string[] _eventLines = new string[MaxEvents];

        private Navigator _navigator;
        private string _emptyStatus = NoNavigatorStatus;

        // False when there is nothing to show; StatusLine then says why.
        internal bool HasNavigator
        {
            get { return _navigator != null; }
        }

        internal NavigatorState State
        {
            get { return _state; }
        }

        internal IReadOnlyList<QueueState> Queues
        {
            get { return _queues; }
        }

        // Newest first, at most MaxEvents.
        internal IReadOnlyList<RecentEvent> Events
        {
            get { return _events; }
        }

        // Picks the navigator of a root, or an empty model whose status says why there is none.
        internal void Refresh(ViewRoot root)
        {
            if (root == null)
            {
                Clear(NoRootStatus);
                return;
            }

            if (!root.IsInitialized || root.Core == null)
            {
                Clear(NotInitializedStatus);
                return;
            }

            Refresh(root.Core);
        }

        // Captures the state, the queues and the recent events; a null navigator gives an empty model.
        internal void Refresh(Navigator navigator)
        {
            if (navigator == null)
            {
                Clear(NoNavigatorStatus);
                return;
            }

            _navigator = navigator;
            navigator.CaptureState(_state);
            CaptureQueues(navigator);
            navigator.CopyRecentEvents(_events);
            if (_events.Count > MaxEvents)
                _events.RemoveRange(MaxEvents, _events.Count - MaxEvents);

            // Lines are built on demand, once per capture.
            Array.Clear(_eventLines, 0, _eventLines.Length);
        }

        // Empties the model and releases the navigator.
        internal void Clear()
        {
            Clear(NoNavigatorStatus);
        }

        // One line: settled or busy with the shield count, "Root gone", or the reason there is nothing to show.
        internal string StatusLine()
        {
            if (_navigator == null)
                return _emptyStatus;

            if (_state.RootGone)
                return "Root gone";

            return (_state.Busy ? "Busy" : "Settled") + ", shield " + _state.ShieldCount.ToString(CultureInfo.InvariantCulture);
        }

        // The navigator's own text trace (what Copy state puts on the clipboard); the status line when there is none.
        internal string Describe()
        {
            if (_navigator == null)
                return _emptyStatus;

            var output = new StringBuilder(512);
            _navigator.Describe(output);
            return output.ToString();
        }

        // Text of the event at index (newest first), cached until the next capture.
        internal string GetEventLine(int index)
        {
            if (index < 0 || index >= _events.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var line = _eventLines[index];
            if (line == null)
            {
                line = FormatEvent(_events[index]);
                _eventLines[index] = line;
            }

            return line;
        }

        // Formats

        // "#12 shop Shown slot 0", plus " close pending: Back" when a close is pending.
        internal static string FormatRequest(in RequestSnapshot request)
        {
            var output = new StringBuilder(64);
            output.Append('#').Append(request.RequestId)
                .Append(' ').Append(request.ViewId)
                .Append(' ').Append(request.Phase.ToString())
                .Append(" slot ").Append(request.Slot);
            if (request.ClosePending)
                output.Append(" close pending: ").Append(request.OutcomeReason.ToString());

            return output.ToString();
        }

        // "Popups: 2 requests, 0 operations, idle".
        internal static string FormatLayerHeader(LayerSnapshot layer)
        {
            return layer.LayerId + ": " + layer.Requests.Count.ToString(CultureInfo.InvariantCulture)
                + (layer.Requests.Count == 1 ? " request, " : " requests, ")
                + layer.PendingOperations.ToString(CultureInfo.InvariantCulture)
                + (layer.PendingOperations == 1 ? " operation, " : " operations, ")
                + (layer.IsRunning ? "running" : "idle");
        }

        // "Kind #3 reason 1.2s at call site"; the request id is left out when it is zero.
        internal static string FormatShieldHolder(in ShieldHolderSnapshot holder)
        {
            var output = new StringBuilder(96);
            output.Append(holder.Kind.ToString());
            if (holder.RequestId != 0)
                output.Append(" #").Append(holder.RequestId);

            AppendReason(output, holder.Reason);
            output.Append(' ').Append(FormatSeconds(holder.AgeSeconds, "0.0"));
            AppendCallSite(output, holder.CallSite);
            return output.ToString();
        }

        // "reason 1.2s at call site" for a queue hold.
        internal static string FormatQueueHold(in QueueHoldSnapshot hold)
        {
            var output = new StringBuilder(96);
            output.Append(string.IsNullOrEmpty(hold.Reason) ? "(no reason)" : hold.Reason);
            output.Append(' ').Append(FormatSeconds(hold.AgeSeconds, "0.0"));
            AppendCallSite(output, hold.CallSite);
            return output.ToString();
        }

        // "#5 shop priority 10 source tutorial age 2.3s".
        internal static string FormatQueueItem(in QueueItemSnapshot item)
        {
            var output = new StringBuilder(80);
            output.Append('#').Append(item.RequestId)
                .Append(' ').Append(item.ViewId)
                .Append(" priority ").Append(item.Priority);
            if (!string.IsNullOrEmpty(item.Source))
                output.Append(" source ").Append(item.Source);

            output.Append(" age ").Append(FormatSeconds(item.AgeSeconds, "0.0"));
            return output.ToString();
        }

        // "Name (layer Popups), held, inert" with the waiting count.
        internal static string FormatQueueHeader(QueueState queue)
        {
            var output = new StringBuilder(80);
            output.Append(string.IsNullOrEmpty(queue.Name) ? queue.LayerId : queue.Name);
            if (!string.IsNullOrEmpty(queue.Name) && !string.IsNullOrEmpty(queue.LayerId))
                output.Append(" (layer ").Append(queue.LayerId).Append(')');

            if (queue.Held)
                output.Append(", held");
            if (queue.Inert)
                output.Append(", inert");

            output.Append(", waiting ").Append(queue.Waiting.Count);
            return output.ToString();
        }

        // "[queue] Enqueued #5 shop layer Popups source tutorial priority 3"; Settled has no request.
        internal static string FormatEvent(in RecentEvent recent)
        {
            var viewEvent = recent.Event;
            var output = new StringBuilder(112);
            if (recent.FromQueue)
                output.Append("[queue] ");

            output.Append(viewEvent.Kind.ToString());
            if (viewEvent.Kind == ViewEventKind.Settled)
                return output.ToString();

            output.Append(" #").Append(viewEvent.RequestId)
                .Append(' ').Append(viewEvent.ViewId);
            if (!string.IsNullOrEmpty(viewEvent.LayerId))
                output.Append(" layer ").Append(viewEvent.LayerId);
            if (!string.IsNullOrEmpty(viewEvent.Source))
                output.Append(" source ").Append(viewEvent.Source);
            if (viewEvent.Kind == ViewEventKind.Enqueued || viewEvent.Priority != 0)
                output.Append(" priority ").Append(viewEvent.Priority);

            switch (viewEvent.Kind)
            {
                case ViewEventKind.Shown:
                    output.Append(" waited ").Append(FormatSeconds(viewEvent.SecondsWaiting, "0.00"));
                    break;
                case ViewEventKind.Closed:
                    output.Append(" reason ").Append(viewEvent.Reason.ToString());
                    break;
                case ViewEventKind.NotShown:
                    output.Append(" rejected ").Append(viewEvent.Rejected.ToString());
                    if (viewEvent.Reason != CloseReason.NotShown)
                        output.Append(" reason ").Append(viewEvent.Reason.ToString());
                    break;
            }

            if (!string.IsNullOrEmpty(viewEvent.Detail))
                output.Append(" detail ").Append(viewEvent.Detail);

            if (viewEvent.Kind == ViewEventKind.Closed)
                output.Append(" open ").Append(FormatSeconds(viewEvent.SecondsOpen, "0.00"));

            return output.ToString();
        }

        // Helpers

        private void Clear(string status)
        {
            _navigator = null;
            _emptyStatus = status;
            _state.Reset();
            for (var i = 0; i < _queues.Count; i++)
                _spareQueues.Push(_queues[i]);

            _queues.Clear();
            _queueSources.Clear();
            _events.Clear();
            Array.Clear(_eventLines, 0, _eventLines.Length);
        }

        // Reuses QueueState objects; queue references are not kept after the capture.
        private void CaptureQueues(Navigator navigator)
        {
            var count = navigator.CopyQueues(_queueSources);

            while (_queues.Count > count)
            {
                var last = _queues.Count - 1;
                _spareQueues.Push(_queues[last]);
                _queues.RemoveAt(last);
            }

            while (_queues.Count < count)
                _queues.Add(_spareQueues.Count > 0 ? _spareQueues.Pop() : new QueueState());

            for (var i = 0; i < count; i++)
            {
                _queues[i].Reset();
                _queueSources[i].CaptureState(_queues[i]);
            }

            _queueSources.Clear();
        }

        private static string FormatSeconds(double seconds, string format)
        {
            return seconds.ToString(format, CultureInfo.InvariantCulture) + "s";
        }

        private static void AppendReason(StringBuilder output, string reason)
        {
            if (!string.IsNullOrEmpty(reason))
                output.Append(' ').Append(reason);
        }

        private static void AppendCallSite(StringBuilder output, string callSite)
        {
            if (!string.IsNullOrEmpty(callSite))
                output.Append(" at ").Append(callSite);
        }
    }
}
