using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    /// <summary>Presents waiting requests on one layer, one at a time, by priority; uses only INavigator.</summary>
    public sealed class ViewQueue
    {
        private const string LifetimeEndedDetail = "The queue's lifetime ended.";
        private const string RootGoneDetail = "The root was destroyed.";
        private const string ClearedDetail = "The queue was cleared.";
        private const string OwnerEndedAtEnqueueDetail = "The owner had already ended.";
        private const string OwnerEndedWhileWaitingDetail = "The owner ended while the item waited.";
        private const string PresentFaultedDetail = "The navigator failed while the item was presented.";

        private readonly INavigator _navigator;
        private readonly ViewLayer _layer;
        private readonly string _name;
        private readonly IQueueScheduler _scheduler;
        private readonly IMainThread _mainThread;
        private readonly ErrorRouter _errors;
        private readonly Diagnostics _diagnostics;
        private readonly HoldSet _holds;
        private readonly GateChain _gates;
        private readonly ObserverList _observers;

        // Set only when the navigator is the package's own; it lists the queue and records its events.
        private readonly Navigator _packageNavigator;

        // Waiting items by priority, then enqueue order; the presenting item is not in it.
        private readonly List<QueueItem> _waiting = new List<QueueItem>();
        private readonly List<QueueItem> _walk = new List<QueueItem>();
        private readonly List<RegistrationEntry> _registrations = new List<RegistrationEntry>();

        private readonly Action<object> _pump;
        private readonly Action<object> _onItemOwnerCancelled;
        private readonly Action<object> _onLifetimeCancelled;
        private readonly Action<object> _removeHold;
        private readonly Action<object> _removeGate;
        private readonly Action<object> _removeObserver;
        private readonly Action _clear;
        private readonly Action _endLifetime;

        // The navigator's root token, read once; the queue also ends when it is cancelled.
        private readonly CancellationToken _rootToken;

        private CancellationTokenRegistration _lifetimeRegistration;
        private CancellationTokenRegistration _rootRegistration;
        private RejectReason _endReason = RejectReason.Cancelled;
        private string _endDetail = LifetimeEndedDetail;
        private ViewRegistration _navigatorObserver;
        private QueueItem _presenting;
        private long _sequence;
        private bool _pumpScheduled;
        private bool _pumping;
        private bool _inert;

        /// <summary>Creates a queue that presents on the layer until the lifetime ends or the navigator's root is destroyed; call it on the main thread. Throws ArgumentException for a layer the root's navigator does not have.</summary>
        public ViewQueue(INavigator navigator, ViewLayer layer, CancellationToken lifetime, string name = null)
            : this(navigator, layer, lifetime, name, null, null, null, null)
        {
        }

        // Tests pass fakes; null arguments get the real defaults.
        internal ViewQueue(
            INavigator navigator,
            ViewLayer layer,
            CancellationToken lifetime,
            string name,
            IQueueScheduler scheduler,
            IMainThread mainThread,
            ErrorRouter errors,
            Diagnostics diagnostics)
        {
            if (navigator == null)
                throw new ArgumentNullException(nameof(navigator));
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));

            // The package's own navigator shares its error router, diagnostics and clock.
            var packageNavigator = navigator as Navigator;

            // Checked before anything registers, so a refused construction leaves nothing behind.
            if (packageNavigator != null && !packageNavigator.HasLayer(layer))
                throw new ArgumentException("The navigator has no layer '" + layer.Id + "'. Use one of ViewLayers or a layer passed in ViewRootSetup.ExtraLayers.", nameof(layer));

            _navigator = navigator;
            _layer = layer;
            _name = string.IsNullOrEmpty(name) ? layer.Id : name;

            _packageNavigator = packageNavigator;
            _diagnostics = diagnostics ?? (packageNavigator != null ? packageNavigator.Diagnostics : new Diagnostics());
            _errors = errors ?? (packageNavigator != null ? packageNavigator.Errors : new ErrorRouter(null, _diagnostics));
            _scheduler = scheduler ?? new PlayerLoopQueueScheduler(lifetime, _diagnostics, packageNavigator != null ? packageNavigator.Clock : null);
            _mainThread = mainThread ?? new UniTaskMainThread();

            _pump = Pump;
            _onItemOwnerCancelled = OnItemOwnerCancelled;
            _onLifetimeCancelled = OnEndTokenCancelled;
            _removeHold = RemoveHold;
            _removeGate = RemoveGate;
            _removeObserver = RemoveObserverRegistration;
            _clear = Clear;
            _endLifetime = EndLifetime;

            _holds = new HoldSet(_scheduler, _diagnostics, SchedulePump);
            _gates = new GateChain(_errors, _diagnostics);
            _observers = new ObserverList(_errors);

            _rootToken = navigator.RootToken;

            // A lifetime or root that already ended leaves the queue inert from the start.
            if (lifetime.IsCancellationRequested || _rootToken.IsCancellationRequested)
            {
                MarkEnded();
                return;
            }

            _navigatorObserver = _navigator.AddObserver(new NavigatorListener(this), lifetime);

            // Listed before the tokens bind, so a token ending inline unlists it again.
            if (_packageNavigator != null)
                _packageNavigator.RegisterQueue(this);

            // Registered last: a token cancelled meanwhile ends the queue inline, with every field in place.
            if (_rootToken.CanBeCanceled && !_rootToken.Equals(lifetime))
                _rootRegistration = _rootToken.RegisterWithoutCaptureExecutionContext(_onLifetimeCancelled, null);

            if (lifetime.CanBeCanceled)
                _lifetimeRegistration = lifetime.RegisterWithoutCaptureExecutionContext(_onLifetimeCancelled, null);

            // A registration stored after an inline end is released here.
            if (_inert)
                DisposeEndRegistrations();
        }

        /// <summary>The number of waiting items; the item being presented is not counted.</summary>
        public int Count
        {
            get { return _waiting.Count; }
        }

        /// <summary>True while at least one hold exists; waiting items are not presented then.</summary>
        public bool IsHeld
        {
            get { return _holds.Count > 0; }
        }

        // True while the lifetime or root token still holds a callback into this queue.
        internal bool HasEndRegistrations
        {
            get { return !_lifetimeRegistration.Equals(default(CancellationTokenRegistration)) || !_rootRegistration.Equals(default(CancellationTokenRegistration)); }
        }

        /// <summary>Queues a request and completes with its outcome, also when it ends in the queue without being shown.</summary>
        public UniTask<ViewResult<TResult>> EnqueueAsync<TArgs, TResult>(
            ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            QueueOptions options = default)
        {
            var item = Submit(key, args, owner, options, true);
            return item.Completion;
        }

        /// <summary>Queues a request without waiting for it and returns the request id its events carry.</summary>
        public int Enqueue<TArgs, TResult>(
            ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            QueueOptions options = default)
        {
            var item = Submit(key, args, owner, options, false);
            return item.RequestId;
        }

        /// <summary>Keeps waiting items from being presented until the owner ends or the registration is removed; the caller's member and line are recorded for the long-hold warning and the window.</summary>
        public ViewRegistration Hold(
            string reason,
            CancellationToken owner,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0)
        {
            ThrowIfOffMainThread();
            if (reason == null)
                throw new ArgumentNullException(nameof(reason));

            if (_inert || owner.IsCancellationRequested)
                return default(ViewRegistration);

            var state = new RegistrationState();
            state.HolderId = _holds.Add(reason, Navigator.CallSite(member, line));

            return Register(state, _removeHold, owner);
        }

        /// <summary>Asks the gate about every item at enqueue and before presentation, in ascending order, until the owner ends.</summary>
        public ViewRegistration AddGate(IViewGate gate, CancellationToken owner, int order = 0)
        {
            ThrowIfOffMainThread();
            if (gate == null)
                throw new ArgumentNullException(nameof(gate));

            if (_inert || owner.IsCancellationRequested)
                return default(ViewRegistration);

            var state = new RegistrationState();
            state.Gate = _gates.Add(gate, order);

            var registration = Register(state, _removeGate, owner);
            SchedulePump();
            return registration;
        }

        /// <summary>Delivers this queue's Enqueued and NotShown events to the observer until the owner ends.</summary>
        public ViewRegistration AddObserver(IViewObserver observer, CancellationToken owner)
        {
            ThrowIfOffMainThread();
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            if (_inert || owner.IsCancellationRequested)
                return default(ViewRegistration);

            var state = new RegistrationState();
            state.Observer = _observers.Add(observer);

            return Register(state, _removeObserver, owner);
        }

        /// <summary>Ends every waiting item as NotShown / Cancelled; an item already presented is not touched. Safe from any thread.</summary>
        public void Clear()
        {
            if (!_mainThread.IsMainThread)
            {
                _mainThread.Post(_clear);
                return;
            }

            EndAllWaiting(RejectReason.Cancelled, ClearedDetail);
        }

        /// <summary>Writes the queue's state, holds, gates, presenting item and waiting items as text.</summary>
        public void Describe(StringBuilder output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            output.Append("queue ").Append(_name)
                .Append(" layer ").Append(_layer.Id)
                .Append(_inert ? " inert" : " active")
                .Append(IsHeld ? " held" : " free")
                .Append('\n');

            _holds.Describe(output);
            output.Append("gates: ").Append(_gates.Count).Append('\n');

            output.Append("presenting: ");
            if (_presenting != null)
                AppendItem(output, _presenting, false);
            else
                output.Append("none");
            output.Append('\n');

            output.Append("waiting: ").Append(_waiting.Count).Append('\n');
            for (var i = 0; i < _waiting.Count; i++)
            {
                output.Append("  ");
                AppendItem(output, _waiting[i], true);
                output.Append('\n');
            }
        }

        // The name given at construction, or the layer id when none was given.
        internal string Name
        {
            get { return _name; }
        }

        // Clears and refills into; only reads, and a warm capture allocates nothing.
        internal void CaptureState(QueueState into)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Reset();
            var now = _scheduler.Now;

            into.Name = _name;
            into.LayerId = _layer.Id;
            into.Inert = _inert;
            into.Held = IsHeld;
            into.GateCount = _gates.Count;
            _holds.CaptureHolds(into.Holds, now);

            if (_presenting != null)
            {
                into.HasPresenting = true;
                into.Presenting = Snapshot(_presenting, now);
            }

            var waiting = into.Waiting;
            for (var i = 0; i < _waiting.Count; i++)
                waiting.Add(Snapshot(_waiting[i], now));
        }

        // Enqueue

        private QueueItem<TArgs, TResult> Submit<TArgs, TResult>(
            ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            QueueOptions options,
            bool awaitable)
        {
            ThrowIfOffMainThread();
            if (key.Id == null)
                throw new ArgumentNullException(nameof(key));

            var requestId = _navigator.ReserveRequestId();
            var item = new QueueItem<TArgs, TResult>(
                requestId,
                key,
                args,
                options.Priority,
                options.Source,
                ++_sequence,
                _scheduler.Now,
                owner,
                awaitable);

            if (_inert)
            {
                Reject(item, _endReason, _endDetail);
                return item;
            }

            if (owner.IsCancellationRequested)
            {
                Reject(item, RejectReason.OwnerEnded, OwnerEndedAtEnqueueDetail);
                return item;
            }

            // Gates run before anything is published; a deny means the item never entered the queue.
            GateDecision decision;
            if (!PassesGates(item, false, out decision))
            {
                Reject(item, RejectReason.Denied, decision.Reason);
                return item;
            }

            // A gate may have ended the lifetime or the root.
            if (_inert)
            {
                Reject(item, _endReason, _endDetail);
                return item;
            }

            Insert(item);
            Publish(ViewEventKind.Enqueued, item, RejectReason.None, null);

            // Registered last, so a token that fires during registration ends the item after Enqueued.
            if (item.State == QueueItemState.Waiting && owner.CanBeCanceled)
            {
                item.OwnerRegistration = owner.RegisterWithoutCaptureExecutionContext(_onItemOwnerCancelled, item);
                if (item.State != QueueItemState.Waiting)
                    DisposeOwnerRegistration(item);
            }

            SchedulePump();
            return item;
        }

        // Higher priority first; an equal priority goes after the items already waiting.
        private void Insert(QueueItem item)
        {
            var index = _waiting.Count;
            for (var i = 0; i < _waiting.Count; i++)
            {
                if (_waiting[i].Priority < item.Priority)
                {
                    index = i;
                    break;
                }
            }

            _waiting.Insert(index, item);
        }

        // An item that never entered the queue: only NotShown is published.
        private void Reject(QueueItem item, RejectReason reason, string detail)
        {
            item.State = QueueItemState.Done;
            Publish(ViewEventKind.NotShown, item, reason, detail);
            item.EndNotShown(reason, detail);
        }

        private bool PassesGates(QueueItem item, bool atPresentation, out GateDecision decision)
        {
            if (_gates.Count == 0)
            {
                decision = GateDecision.Allow;
                return true;
            }

            var request = new GateRequest(item.RequestId, item.ViewId, _layer, item.Priority, item.Source, atPresentation);
            decision = _gates.Evaluate(in request);
            return decision.IsAllowed;
        }

        // Pump

        private void SchedulePump()
        {
            if (_inert || _pumpScheduled)
                return;

            _pumpScheduled = true;
            _scheduler.NextTick(_pump, null);
        }

        private void Pump(object state)
        {
            _pumpScheduled = false;
            if (_pumping || !CanPresent())
                return;

            _pumping = true;
            try
            {
                if (!_navigator.IsLayerClear(_layer))
                    return;

                // A snapshot, so a gate or observer that enqueues or clears cannot break the walk.
                _walk.AddRange(_waiting);
                for (var i = 0; i < _walk.Count; i++)
                {
                    var item = _walk[i];
                    if (item.State != QueueItemState.Waiting)
                        continue;

                    if (!CanPresent())
                        return;

                    if (item.Owner.IsCancellationRequested)
                    {
                        EndWaiting(item, RejectReason.OwnerEnded, OwnerEndedWhileWaitingDetail);
                        continue;
                    }

                    GateDecision decision;
                    if (!PassesGates(item, true, out decision))
                    {
                        EndWaiting(item, RejectReason.Denied, decision.Reason);
                        continue;
                    }

                    // Gate code may have changed the queue; the check and the show are one synchronous block.
                    if (item.State != QueueItemState.Waiting || !CanPresent() || !_navigator.IsLayerClear(_layer))
                        return;

                    Present(item);
                    return;
                }
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("queue pump", exception);
            }
            finally
            {
                _walk.Clear();
                _pumping = false;
            }
        }

        private bool CanPresent()
        {
            return !_inert && _presenting == null && _holds.Count == 0 && _waiting.Count > 0;
        }

        private void Present(QueueItem item)
        {
            _waiting.Remove(item);
            item.State = QueueItemState.Presenting;
            _presenting = item;

            // The navigator owns the item's owner token from now on.
            DisposeOwnerRegistration(item);

            UniTask presentation;
            try
            {
                presentation = item.PresentAsync(_navigator, _layer);
            }
            catch (Exception exception)
            {
                // The navigator never took the request, so the queue reports its end.
                _diagnostics.ReportFault("queue presentation", exception);
                item.State = QueueItemState.Done;
                _presenting = null;
                Publish(ViewEventKind.NotShown, item, RejectReason.Faulted, PresentFaultedDetail);
                item.EndNotShown(RejectReason.Faulted, PresentFaultedDetail);
                SchedulePump();
                return;
            }

            // Not awaited: the pump returns now and the item ends through this continuation.
            AwaitPresentation(item, presentation).Forget();
        }

        private async UniTaskVoid AwaitPresentation(QueueItem item, UniTask presentation)
        {
            var faulted = false;
            try
            {
                await presentation;
            }
            catch (Exception exception)
            {
                // The continuation may run off the main thread, so the end is published in OnPresentationEnded.
                _diagnostics.ReportFault("queue presentation", exception);
                faulted = true;
            }
            finally
            {
                try
                {
                    if (_mainThread.IsMainThread)
                        OnPresentationEnded(item, faulted);
                    else
                        PostPresentationEnded(item, faulted);
                }
                catch (Exception exception)
                {
                    _diagnostics.ReportFault("queue presentation end", exception);
                }
            }
        }

        // A separate method, so the closure is allocated only on the off-thread path.
        private void PostPresentationEnded(QueueItem item, bool faulted)
        {
            _mainThread.Post(() => OnPresentationEnded(item, faulted));
        }

        // Same order as a synchronous throw: state, publish, complete, pump.
        private void OnPresentationEnded(QueueItem item, bool faulted)
        {
            item.State = QueueItemState.Done;
            if (ReferenceEquals(_presenting, item))
                _presenting = null;

            if (faulted)
            {
                if (!item.NavigatorEnded)
                    Publish(ViewEventKind.NotShown, item, RejectReason.Faulted, PresentFaultedDetail);
                item.EndNotShown(RejectReason.Faulted, PresentFaultedDetail);
            }

            SchedulePump();
        }

        // Item ends

        private void EndWaiting(QueueItem item, RejectReason reason, string detail)
        {
            if (item.State != QueueItemState.Waiting)
                return;

            item.State = QueueItemState.Done;
            _waiting.Remove(item);
            DisposeOwnerRegistration(item);

            Publish(ViewEventKind.NotShown, item, reason, detail);
            item.EndNotShown(reason, detail);
            SchedulePump();
        }

        // Every waiting item is latched first, then each one is reported.
        private void EndAllWaiting(RejectReason reason, string detail)
        {
            if (_waiting.Count == 0)
                return;

            var ended = _waiting.ToArray();
            _waiting.Clear();

            for (var i = 0; i < ended.Length; i++)
            {
                ended[i].State = QueueItemState.Done;
                DisposeOwnerRegistration(ended[i]);
            }

            for (var i = 0; i < ended.Length; i++)
            {
                Publish(ViewEventKind.NotShown, ended[i], reason, detail);
                ended[i].EndNotShown(reason, detail);
            }

            SchedulePump();
        }

        private void OnItemOwnerCancelled(object state)
        {
            var item = (QueueItem)state;
            if (_mainThread.IsMainThread)
            {
                EndOwnerEnded(item);
                return;
            }

            _diagnostics.Warn(
                DiagnosticIds.OwnerCancelledOffThread,
                "A queued item's owner token was cancelled off the main thread; the item ends on the main thread.");
            _mainThread.Post(() => EndOwnerEnded(item));
        }

        private void EndOwnerEnded(QueueItem item)
        {
            EndWaiting(item, RejectReason.OwnerEnded, OwnerEndedWhileWaitingDetail);
        }

        private void DisposeOwnerRegistration(QueueItem item)
        {
            try
            {
                item.OwnerRegistration.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("queue item owner registration dispose", exception);
            }

            item.OwnerRegistration = default(CancellationTokenRegistration);
        }

        // Lifetime

        private void OnEndTokenCancelled(object state)
        {
            if (_mainThread.IsMainThread)
            {
                EndLifetime();
                return;
            }

            _diagnostics.Warn(
                DiagnosticIds.OwnerCancelledOffThread,
                "A queue's lifetime or root token was cancelled off the main thread; the queue ends on the main thread.");
            _mainThread.Post(_endLifetime);
        }

        // The reason comes from token state, not from which callback ran first.
        private void MarkEnded()
        {
            _inert = true;
            if (_rootToken.IsCancellationRequested)
            {
                _endReason = RejectReason.RootGone;
                _endDetail = RootGoneDetail;
            }
            else
            {
                _endReason = RejectReason.Cancelled;
                _endDetail = LifetimeEndedDetail;
            }
        }

        // Waiting items end as cancelled or root gone, every registration ends, and the queue stays inert; a presented view is left to its owner.
        private void EndLifetime()
        {
            if (_inert)
                return;

            MarkEnded();

            // UnregisterQueue does not throw; the queue's own events still reach the ring below.
            if (_packageNavigator != null)
                _packageNavigator.UnregisterQueue(this);

            try
            {
                EndAllWaiting(_endReason, _endDetail);

                var registrations = _registrations.ToArray();
                for (var i = 0; i < registrations.Length; i++)
                    registrations[i].Remove();

                _holds.RemoveAll();
                _gates.RemoveAll();
                _observers.RemoveAll();
                _navigatorObserver.Remove();
                _navigatorObserver = default(ViewRegistration);
                DisposeEndRegistrations();
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("queue lifetime end", exception);
            }
        }

        // Neither token keeps an ended queue alive.
        private void DisposeEndRegistrations()
        {
            try
            {
                _lifetimeRegistration.Dispose();
                _rootRegistration.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("queue end registration dispose", exception);
            }

            _lifetimeRegistration = default(CancellationTokenRegistration);
            _rootRegistration = default(CancellationTokenRegistration);
        }

        // Registrations

        private sealed class RegistrationState
        {
            public RegistrationEntry Entry;
            public int HolderId;
            public object Gate;
            public ObserverEntry Observer;
        }

        // The entry ends once, on Remove or when the owner is cancelled; the state is already in place when the owner binds.
        private ViewRegistration Register(RegistrationState state, Action<object> remove, CancellationToken owner)
        {
            var entry = new RegistrationEntry(_mainThread, _diagnostics, remove, state);
            state.Entry = entry;
            _registrations.Add(entry);
            entry.BindOwner(owner);
            return new ViewRegistration(entry);
        }

        // The hold set reports the drop, which schedules a pump.
        private void RemoveHold(object state)
        {
            var registration = (RegistrationState)state;
            _registrations.Remove(registration.Entry);
            _holds.Remove(registration.HolderId);
        }

        private void RemoveGate(object state)
        {
            var registration = (RegistrationState)state;
            _registrations.Remove(registration.Entry);
            _gates.Remove(registration.Gate);
            SchedulePump();
        }

        private void RemoveObserverRegistration(object state)
        {
            var registration = (RegistrationState)state;
            _registrations.Remove(registration.Entry);
            _observers.Remove(registration.Observer);
        }

        // Events and helpers

        private void Publish(ViewEventKind kind, QueueItem item, RejectReason rejected, string detail)
        {
            var viewEvent = new ViewEvent(
                kind,
                item.RequestId,
                item.ViewId,
                _layer.Id,
                item.Source,
                item.Priority,
                kind == ViewEventKind.NotShown ? CloseReason.NotShown : default(CloseReason),
                rejected,
                detail,
                0f,
                0f);

            // Recorded first, as the navigator's ring sees its own events before its observers.
            if (_packageNavigator != null)
                _packageNavigator.RecordQueueEvent(in viewEvent);

            _observers.Publish(in viewEvent);
        }

        private void AppendItem(StringBuilder output, QueueItem item, bool withAge)
        {
            output.Append('#').Append(item.RequestId)
                .Append(' ').Append(item.ViewId)
                .Append(" priority ").Append(item.Priority)
                .Append(" source ").Append(item.Source ?? "-");

            if (withAge)
            {
                var age = _scheduler.Now - item.EnqueuedAt;
                output.Append(" age ").Append(age.ToString("0.0", CultureInfo.InvariantCulture)).Append('s');
            }
        }

        private static QueueItemSnapshot Snapshot(QueueItem item, double now)
        {
            return new QueueItemSnapshot
            {
                RequestId = item.RequestId,
                ViewId = item.ViewId,
                Priority = item.Priority,
                Source = item.Source,
                AgeSeconds = now - item.EnqueuedAt
            };
        }

        private void ThrowIfOffMainThread()
        {
            if (_mainThread.IsMainThread)
                return;

            throw new InvalidOperationException("The queue must be used on the main thread.");
        }

        // A navigator end for the presented request marks it, so a later fault of its ShowAsync publishes no second end.
        private void OnNavigatorEvent(in ViewEvent viewEvent)
        {
            var kind = viewEvent.Kind;
            if (kind != ViewEventKind.Settled && kind != ViewEventKind.Closed && kind != ViewEventKind.NotShown)
                return;

            if (kind != ViewEventKind.Settled && _presenting != null && viewEvent.RequestId == _presenting.RequestId)
                _presenting.NavigatorEnded = true;

            SchedulePump();
        }

        // The queue's one navigator observer: a settled navigator may have a clear layer.
        // Any request that ends can clear the layer, also one ended without an operation (owner, loss, navigation), which publishes no Settled.
        private sealed class NavigatorListener : IViewObserver
        {
            private readonly ViewQueue _queue;

            public NavigatorListener(ViewQueue queue)
            {
                _queue = queue;
            }

            public void OnViewEvent(in ViewEvent viewEvent)
            {
                _queue.OnNavigatorEvent(in viewEvent);
            }
        }
    }
}
