using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector.Tests
{
    // Thrown by the test views and the manual stage; the checker accepts only this type among routed errors.
    internal sealed class InvariantInjectedException : Exception
    {
        public InvariantInjectedException(string where)
            : base("Injected failure in " + where + ".")
        {
        }
    }

    // One violated invariant; Count says how often it was seen in the run.
    internal sealed class InvariantViolation
    {
        public readonly string Id;
        public readonly int Step;
        public readonly string Message;
        public int Count = 1;

        public InvariantViolation(string id, int step, string message)
        {
            Id = id;
            Step = step;
            Message = message;
        }

        public override string ToString()
        {
            return Id + " at step " + Step + (Count > 1 ? " (seen " + Count + " times)" : string.Empty) + ": " + Message;
        }
    }

    // Switches for one run; a disabled id is not reported. Everything is on by default.
    internal sealed class InvariantOptions
    {
        public readonly HashSet<string> Disabled = new HashSet<string>(StringComparer.Ordinal);

        public bool IsEnabled(string id)
        {
            return !Disabled.Contains(id);
        }
    }

    // One catalog row the run registers; the view behind it is an InvariantView.
    internal sealed class InvariantRow
    {
        public readonly string Id;
        public readonly ViewLayer Layer;
        public readonly ViewRetention Retention;
        public readonly BackPolicy Back;
        public readonly CoverPolicy Cover;
        public readonly CoveredMode WhenCovered;
        public readonly bool CloseOnBackdrop;
        public readonly bool AllowMultiple;

        // True for the row whose key carries an int argument.
        public readonly bool TakesArgs;

        public InvariantRow(
            string id,
            ViewLayer layer,
            ViewRetention retention = ViewRetention.ReleaseOnClose,
            BackPolicy back = BackPolicy.LayerDefault,
            CoverPolicy cover = CoverPolicy.LayerDefault,
            CoveredMode whenCovered = CoveredMode.DisableCanvas,
            bool closeOnBackdrop = false,
            bool allowMultiple = false,
            bool takesArgs = false)
        {
            Id = id;
            Layer = layer;
            Retention = retention;
            Back = back;
            Cover = cover;
            WhenCovered = whenCovered;
            CloseOnBackdrop = closeOnBackdrop;
            AllowMultiple = allowMultiple;
            TakesArgs = takesArgs;
        }

        // Effective cover policy: a history layer covers by default, the others do not.
        public bool Covers
        {
            get
            {
                if (Cover == CoverPolicy.Covers)
                    return true;
                if (Cover == CoverPolicy.Transparent)
                    return false;
                return (Layer.Rules & ViewLayerRules.History) != 0;
            }
        }

        public override string ToString()
        {
            return Layer.Id + "/" + Id;
        }
    }

    // What the views reported for one request: hook and scope counters.
    internal sealed class InvariantTrace
    {
        public readonly int RequestId;
        public readonly string ViewId;
        public int OpenCalls;
        public int OpenEnds;
        public int VisibleBegins;
        public int VisibleEnds;
        public readonly Action OnOpenEnd;
        public readonly Action OnVisibleEnd;

        public InvariantTrace(int requestId, string viewId)
        {
            RequestId = requestId;
            ViewId = viewId;
            OnOpenEnd = () => OpenEnds++;
            OnVisibleEnd = () => VisibleEnds++;
        }
    }

    // A HoldInput registration and what the test knows about its life, independent of the navigator.
    internal sealed class InvariantHold
    {
        public readonly int Number;
        public readonly ViewRegistration Registration;
        public readonly CancellationTokenSource Owner;
        public bool Removed;

        // True for a queue hold whose owner exists only for it, so a step may cancel that owner to end it.
        public bool Dedicated;

        public InvariantHold(int number, ViewRegistration registration, CancellationTokenSource owner, bool removed)
        {
            Number = number;
            Registration = registration;
            Owner = owner;
            Removed = removed;
        }

        // Live until the test removes it or its owner ends; a null owner is the harness owner.
        public bool ModelLive
        {
            get { return !Removed && (Owner == null || !Owner.IsCancellationRequested); }
        }
    }

    // An awaiter the run keeps: a ShowAsync task, an EnqueueAsync task (in Show) or a WaitForCloseAsync task.
    internal sealed class InvariantAwaiter
    {
        public readonly int Number;
        public readonly string ViewId;
        public readonly bool IsWait;

        // True when Show holds an EnqueueAsync task.
        public bool IsEnqueue;

        // The source of the waiter token; null when the waiter cannot be cancelled.
        public readonly CancellationTokenSource Waiter;

        public UniTask<ViewResult<NoResult>> Show;
        public UniTask<ViewClosed> Wait;

        // The request the awaiter belongs to; zero while unknown.
        public int RequestId;

        public bool Verified;
        public ViewResult<NoResult> ShowResult;
        public ViewClosed WaitResult;

        public InvariantAwaiter(int number, string viewId, bool isWait, CancellationTokenSource waiter)
        {
            Number = number;
            ViewId = viewId;
            IsWait = isWait;
            Waiter = waiter;
        }

        public UniTaskStatus Status
        {
            get { return IsWait ? Wait.Status : Show.Status; }
        }

        public override string ToString()
        {
            return (IsWait ? "wait" : IsEnqueue ? "enqueue" : "show") + "#" + Number + " " + ViewId + " request " + RequestId;
        }
    }

    internal enum InvariantOpenBehavior
    {
        Normal,
        Decline,
        Throw,
        CompleteInOpen,
        CloseInOpen
    }

    // The view every row uses: it reports hooks and scopes to the world and misbehaves at random, from its own seed.
    internal abstract class InvariantViewBase : IViewVisible, IViewBack
    {
        private readonly InvariantWorld _world;
        private readonly InvariantOpenBehavior _behavior;

        protected InvariantViewBase(InvariantWorld world, string viewId, InvariantOpenBehavior behavior)
        {
            _world = world;
            ViewId = viewId;
            _behavior = behavior;
        }

        public string ViewId { get; }

        // The context of the newest open; null until the first one.
        public ViewContext<NoResult> Context { get; private set; }

        protected void HandleOpen(ViewContext<NoResult> context)
        {
            Context = context;

            var trace = _world.TraceFor(context.Handle.RequestId, ViewId);
            trace.OpenCalls++;
            context.Open.OnEnd(trace.OnOpenEnd);

            switch (_behavior)
            {
                case InvariantOpenBehavior.Decline:
                    context.Decline("random decline");
                    break;
                case InvariantOpenBehavior.Throw:
                    throw new InvariantInjectedException("OnOpen of " + ViewId);
                case InvariantOpenBehavior.CompleteInOpen:
                    context.Complete(default(NoResult));
                    break;
                case InvariantOpenBehavior.CloseInOpen:
                    context.Close();
                    break;
            }
        }

        public void OnVisible(ViewScope visible)
        {
            var context = Context;
            if (context == null)
            {
                _world.ReportFromView("I3", "OnVisible of '" + ViewId + "' ran before any OnOpen.");
                return;
            }

            var trace = _world.TraceFor(context.Handle.RequestId, ViewId);
            trace.VisibleBegins++;
            visible.OnEnd(trace.OnVisibleEnd);

            if (_world.Chaos && _world.BehaviorRandom.NextDouble() < 0.02)
                throw new InvariantInjectedException("OnVisible of " + ViewId);
        }

        public bool OnBack()
        {
            if (!_world.Chaos)
                return false;

            if (_world.BehaviorRandom.NextDouble() < 0.03)
                throw new InvariantInjectedException("OnBack of " + ViewId);

            return _world.BehaviorRandom.NextDouble() < 0.5;
        }
    }

    internal sealed class InvariantView<TArgs> : InvariantViewBase, IView<TArgs, NoResult>
    {
        public InvariantView(InvariantWorld world, string viewId, InvariantOpenBehavior behavior)
            : base(world, viewId, behavior)
        {
        }

        public void OnOpen(TArgs args, ViewContext<NoResult> context)
        {
            HandleOpen(context);
        }
    }

    // One published event and who published it; the run keeps both publishers in one list, in publication order.
    internal readonly struct InvariantLoggedEvent
    {
        public readonly ViewEvent Event;
        public readonly bool FromQueue;

        public InvariantLoggedEvent(in ViewEvent viewEvent, bool fromQueue)
        {
            Event = viewEvent;
            FromQueue = fromQueue;
        }
    }

    // What the queue's lifetime token is: the RootToken itself, a source of its own, or a source linked to the RootToken.
    internal enum InvariantQueueLifetimeKind
    {
        RootToken,
        Independent,
        Linked
    }

    internal enum InvariantQueueItemState
    {
        Waiting,
        Presented,
        Ended
    }

    // What the published events say about one queued item, kept up to date while events are published.
    internal sealed class InvariantQueueItem
    {
        public readonly int RequestId;
        public readonly string ViewId;
        public readonly int Priority;

        // Enqueue order from the Enqueued events; zero for an item that never entered the queue.
        public long Sequence;

        public InvariantQueueItemState State;

        // True from the first navigator event for the id: the queue handed the item to the navigator.
        public bool ReachedNavigator;
        public bool PresentationChecked;

        // Closed or NotShown events seen for the id, from either publisher.
        public int Terminals;

        // The Enqueue call that produced the id; null when the queue published an id no call produced.
        public InvariantEnqueueCall Call;

        // The first terminal event: its index in the event list (-1 while none), publisher, kind and rejection.
        public int TerminalIndex = -1;
        public bool TerminalFromQueue;
        public ViewEventKind TerminalKind;
        public RejectReason TerminalRejected;
        public string TerminalDetail;

        // The gate check at presentation that this item's ShowAsync followed; null when none was tied to it.
        public InvariantPresentation Presentation;

        public InvariantQueueItem(int requestId, string viewId, int priority)
        {
            RequestId = requestId;
            ViewId = viewId;
            Priority = priority;
        }

        public override string ToString()
        {
            return "#" + RequestId + " " + ViewId + " priority " + Priority + " seq " + Sequence + " " + State;
        }
    }

    // The state at the moment the gates were asked about an item right before presentation.
    internal sealed class InvariantPresentation
    {
        public int RequestId;
        public int Step;
        public bool QueueHeld;
        public int LiveHolds;
        public bool NavigatorClear;

        // Clear by the navigator's own counters: IsBusy, RequestCount.
        public bool StateClear;
        public bool Busy;
        public int LayerRequests;
        public bool ModalAbove;

        // Clear by the test's model: stage operations and input holds for busy, the event ledgers for requests.
        public bool ModelClear;
        public bool ModelBusy;
        public string ModelBusyWhy;
        public int LedgerPopups;
        public bool LedgerModalAbove;

        // Where the check happened: the event count at that moment and the scheduler tick number.
        public int EventIndex;
        public int Tick;

        // Other items the model had in the navigator at that moment.
        public int InFlight;

        // A waiting item that should have gone first (Q4); null when none.
        public string Better;

        // True when the queue asked about an id it never published Enqueued for.
        public bool Unknown;
    }

    // One Enqueue or EnqueueAsync call of the driver.
    internal sealed class InvariantEnqueueCall
    {
        public readonly int Number;
        public readonly string ViewId;
        public readonly int Priority;
        public readonly bool IsAsync;

        // The id the call's first queue event carried, or the returned id when it published none.
        public int RequestId;
        public int Returned;

        // QueueOptions.Source of the call.
        public string Source;

        // The owner slot's source at the call; null is the harness owner.
        public CancellationTokenSource Owner;

        // True when the owner was already cancelled when the call was made.
        public bool OwnerCancelledAtCall;

        // The EnqueueAsync awaiter; null for Enqueue.
        public InvariantAwaiter Awaiter;

        public InvariantEnqueueCall(int number, string viewId, int priority, bool isAsync)
        {
            Number = number;
            ViewId = viewId;
            Priority = priority;
            IsAsync = isAsync;
        }
    }

    // What the queue model held right before a call that ends items: the event count, the waiting and the presented items.
    internal sealed class InvariantQueueMark
    {
        public int EventIndex;
        public readonly List<InvariantQueueItem> Waiting = new List<InvariantQueueItem>();
        public readonly List<InvariantQueueItem> Presented = new List<InvariantQueueItem>();
    }

    // One exception a toggle gate threw, and how often it reached the error handler.
    internal sealed class InvariantGateFault
    {
        public readonly Exception Exception;
        public readonly int Gate;
        public readonly int RequestId;
        public readonly string ViewId;
        public readonly bool AtPresentation;
        public int Routed;

        public InvariantGateFault(Exception exception, int gate, int requestId, string viewId, bool atPresentation)
        {
            Exception = exception;
            Gate = gate;
            RequestId = requestId;
            ViewId = viewId;
            AtPresentation = atPresentation;
        }

        public override string ToString()
        {
            return "gate" + Gate + " threw for #" + RequestId + " (" + ViewId + ") at " + (AtPresentation ? "presentation" : "enqueue");
        }
    }

    // Feeds the shared event list and the queue model; one instance on the navigator, one on the queue.
    internal sealed class InvariantQueueProbe : IViewObserver
    {
        private readonly InvariantWorld _world;
        private readonly bool _fromQueue;

        public InvariantQueueProbe(InvariantWorld world, bool fromQueue)
        {
            _world = world;
            _fromQueue = fromQueue;
        }

        public void OnViewEvent(in ViewEvent viewEvent)
        {
            try
            {
                _world.TrackEvent(in viewEvent, _fromQueue);
            }
            catch (Exception exception)
            {
                _world.ReportFromView("M.probe", "The queue probe failed: " + exception.Message);
            }
        }
    }

    // Always allows; added last, so it is reached only when every other gate allowed, and records the moment.
    internal sealed class InvariantRecordingGate : IViewGate
    {
        private readonly InvariantWorld _world;

        public InvariantRecordingGate(InvariantWorld world)
        {
            _world = world;
        }

        public GateDecision Evaluate(in GateRequest request)
        {
            try
            {
                _world.RecordGate(in request);
            }
            catch (Exception exception)
            {
                _world.ReportFromView("M.gate", "The recording gate failed: " + exception.Message);
            }

            return GateDecision.Allow;
        }
    }

    // A gate the driver toggles: it denies (or throws for) a fixed subset of view ids, at enqueue, at presentation or both.
    internal sealed class InvariantToggleGate : IViewGate
    {
        private readonly InvariantWorld _world;
        private readonly HashSet<string> _denied;

        public readonly int Number;
        public readonly bool AtEnqueue;
        public readonly bool AtPresentation;
        public readonly bool Throws;
        public readonly int Order;

        // The owner slot's source the gate was added with; null is the harness owner.
        public readonly CancellationTokenSource Owner;

        public ViewRegistration Registration;

        // True once the driver removed the registration.
        public bool Removed;

        public InvariantToggleGate(
            InvariantWorld world,
            int number,
            IEnumerable<string> denied,
            bool atEnqueue,
            bool atPresentation,
            bool throws,
            int order,
            CancellationTokenSource owner)
        {
            _world = world;
            Number = number;
            _denied = new HashSet<string>(denied, StringComparer.Ordinal);
            AtEnqueue = atEnqueue;
            AtPresentation = atPresentation;
            Throws = throws;
            Order = order;
            Owner = owner;
        }

        // The deny reason this gate gives.
        public string Reason
        {
            get { return "gate" + Number; }
        }

        // True when the gate denies (or throws for) the view id at that stage.
        public bool Denies(string viewId, bool atPresentation)
        {
            var asked = atPresentation ? AtPresentation : AtEnqueue;
            return asked && viewId != null && _denied.Contains(viewId);
        }

        public GateDecision Evaluate(in GateRequest request)
        {
            if (!Denies(request.ViewId, request.AtPresentation))
                return GateDecision.Allow;

            if (Throws)
            {
                var exception = new InvariantInjectedException(Reason);
                _world.GateFaults.Add(new InvariantGateFault(exception, Number, request.RequestId, request.ViewId, request.AtPresentation));
                throw exception;
            }

            return GateDecision.Deny(Reason);
        }

        public override string ToString()
        {
            var ids = new List<string>(_denied);
            ids.Sort(StringComparer.Ordinal);
            return "gate" + Number + " denies [" + string.Join(",", ids.ToArray()) + "]"
                + (AtEnqueue ? " at enqueue" : string.Empty) + (AtPresentation ? " at presentation" : string.Empty)
                + (Throws ? " by throwing" : string.Empty) + " order " + Order;
        }
    }

    // One run's fixture: a harness on four layers, every row mapped to an InvariantView, manual loads and transitions,
    // and the bookkeeping the checker and the driver share.
    internal sealed class InvariantWorld : IDisposable
    {
        // A project layer without rules: it shields nothing, so I2 has a layer that must not count.
        public static readonly ViewLayer Hud = new ViewLayer("hud", 3, ViewLayerRules.None);

        private static readonly ViewLayer[] LayerArray = { ViewLayers.Screens, ViewLayers.Popups, ViewLayers.System, Hud };

        private readonly List<InvariantRow> _rows = new List<InvariantRow>();
        private readonly Dictionary<string, InvariantRow> _rowById = new Dictionary<string, InvariantRow>(StringComparer.Ordinal);
        private readonly Dictionary<string, ViewLayer> _layerById = new Dictionary<string, ViewLayer>(StringComparer.Ordinal);

        public readonly NavigatorHarness Harness;
        public readonly Random BehaviorRandom;
        public readonly List<InvariantViewBase> Views = new List<InvariantViewBase>();
        public readonly Dictionary<int, InvariantTrace> Traces = new Dictionary<int, InvariantTrace>();
        public readonly List<InvariantAwaiter> Awaiters = new List<InvariantAwaiter>();
        public readonly List<ViewHandle> Handles = new List<ViewHandle>();
        public readonly List<InvariantHold> Holds = new List<InvariantHold>();

        // Owner slots; a null entry is the harness owner, which nothing cancels before the end.
        public readonly List<CancellationTokenSource> Owners = new List<CancellationTokenSource>();

        // Violations found while the navigator ran, by the views and the event probe; the checker drains them.
        public readonly List<InvariantViolation> PendingViolations = new List<InvariantViolation>();

        public int CurrentStep;

        // False makes every view behave: it never declines, throws, completes in OnOpen or answers back at random.
        public readonly bool Chaos;

        // The queue on the popups layer, ticked by the driver.
        public readonly ManualTicker Ticker = new ManualTicker();
        public readonly ViewQueue Queue;

        // Half the seeds tie the queue to RootToken, as a startup queue usually is; the others to a source of its own, plain or linked to RootToken.
        public readonly InvariantQueueLifetimeKind QueueLifetimeKind;
        public readonly CancellationToken QueueLifetime;
        private readonly CancellationTokenSource _queueLifetimeSource;

        // Every event of both publishers, in publication order.
        public readonly List<InvariantLoggedEvent> Events = new List<InvariantLoggedEvent>();

        // Queue model, by request id, in the order the ids first appeared.
        public readonly Dictionary<int, InvariantQueueItem> QueueItems = new Dictionary<int, InvariantQueueItem>();
        public readonly Dictionary<int, InvariantPresentation> Presentations = new Dictionary<int, InvariantPresentation>();
        public readonly HashSet<int> AskedAtEnqueue = new HashSet<int>();
        public readonly List<InvariantEnqueueCall> EnqueueCalls = new List<InvariantEnqueueCall>();
        public readonly List<InvariantHold> QueueHolds = new List<InvariantHold>();

        // Gates the driver may still remove; AllGates keeps every gate ever added, removed ones too.
        public readonly List<InvariantToggleGate> Gates = new List<InvariantToggleGate>();
        public readonly List<InvariantToggleGate> AllGates = new List<InvariantToggleGate>();
        public readonly List<InvariantGateFault> GateFaults = new List<InvariantGateFault>();

        // The Enqueue call in progress; the first queue event for a new id binds to it.
        public InvariantEnqueueCall PendingEnqueue;

        // True while the driver runs a scheduler tick; TickNumber counts the ticks.
        public bool InTick;
        public int TickNumber;

        // The newest gate check at presentation, consumed by the next item that reaches the navigator.
        public InvariantPresentation LatestPresentation;

        // Set by the checker; RecordGate asks it whether the stage is busy.
        public InvariantChecker Checker;

        // Navigator requests with Requested and no terminal event yet, by id, with their layer id.
        private readonly Dictionary<int, string> _navigatorOpen = new Dictionary<int, string>();
        private long _queueSequence;

        public InvariantWorld(int seed, bool manualStage = true, bool chaos = true)
        {
            Chaos = chaos;
            BehaviorRandom = new Random(seed ^ 0x5bd1e995);
            Harness = new NavigatorHarness(layers: LayerArray);

            if (manualStage)
            {
                Harness.Stage.AcquireMode = StageMode.Manual;
                Harness.Stage.PlayMode = StageMode.Manual;
            }

            for (var i = 0; i < LayerArray.Length; i++)
                _layerById.Add(LayerArray[i].Id, LayerArray[i]);

            AddRows();

            Owners.Add(null);
            for (var i = 0; i < 3; i++)
                Owners.Add(Harness.NewOwner());

            Harness.Navigator.AddObserver(new InvariantEventProbe(this), Harness.Owner);
            Harness.Navigator.AddObserver(new InvariantQueueProbe(this, false), Harness.Owner);

            // Drawn from its own stream, so the driver's steps for a seed do not depend on it; the first draw keeps each seed's RootToken split.
            var lifetimeRandom = new Random(seed ^ 0x2545f491);
            if (lifetimeRandom.Next(2) == 0)
                QueueLifetimeKind = InvariantQueueLifetimeKind.RootToken;
            else
                QueueLifetimeKind = lifetimeRandom.Next(2) == 0 ? InvariantQueueLifetimeKind.Independent : InvariantQueueLifetimeKind.Linked;

            switch (QueueLifetimeKind)
            {
                case InvariantQueueLifetimeKind.RootToken:
                    QueueLifetime = Harness.Navigator.RootToken;
                    break;
                case InvariantQueueLifetimeKind.Linked:
                    _queueLifetimeSource = CancellationTokenSource.CreateLinkedTokenSource(Harness.Navigator.RootToken);
                    QueueLifetime = _queueLifetimeSource.Token;
                    break;
                default:
                    _queueLifetimeSource = new CancellationTokenSource();
                    QueueLifetime = _queueLifetimeSource.Token;
                    break;
            }

            Queue = new ViewQueue(
                Harness.Navigator,
                ViewLayers.Popups,
                QueueLifetime,
                "invariant",
                Ticker,
                Harness.MainThread,
                Harness.Navigator.Errors,
                Harness.Navigator.Diagnostics);

            Queue.AddObserver(new InvariantQueueProbe(this, true), Harness.Owner);
            Queue.AddGate(new InvariantRecordingGate(this), Harness.Owner, int.MaxValue);
        }

        public bool QueueOnRootToken
        {
            get { return QueueLifetimeKind == InvariantQueueLifetimeKind.RootToken; }
        }

        public Navigator Navigator
        {
            get { return Harness.Navigator; }
        }

        public FakeStage Stage
        {
            get { return Harness.Stage; }
        }

        public IReadOnlyList<ViewLayer> Layers
        {
            get { return LayerArray; }
        }

        public IReadOnlyList<InvariantRow> Rows
        {
            get { return _rows; }
        }

        public InvariantRow RowById(string id)
        {
            InvariantRow row;
            return id != null && _rowById.TryGetValue(id, out row) ? row : null;
        }

        public ViewLayer LayerById(string id)
        {
            ViewLayer layer;
            return id != null && _layerById.TryGetValue(id, out layer) ? layer : null;
        }

        public int HighestLayerOrder
        {
            get { return LayerArray[LayerArray.Length - 1].Order; }
        }

        public bool Shields(ViewLayer layer)
        {
            return (layer.Rules & ViewLayerRules.ShieldsInput) != 0;
        }

        public bool IsModal(ViewLayer layer)
        {
            return (layer.Rules & ViewLayerRules.Modal) != 0;
        }

        // Owners

        public CancellationToken OwnerToken(int slot)
        {
            return Owners[slot] == null ? Harness.Owner : Owners[slot].Token;
        }

        public string OwnerName(int slot)
        {
            return slot == 0 ? "root" : "o" + slot + (Owners[slot].IsCancellationRequested ? "(cancelled)" : string.Empty);
        }

        // Bookkeeping

        public InvariantTrace TraceFor(int requestId, string viewId)
        {
            InvariantTrace trace;
            if (!Traces.TryGetValue(requestId, out trace))
            {
                trace = new InvariantTrace(requestId, viewId);
                Traces.Add(requestId, trace);
            }

            return trace;
        }

        public int LiveHoldCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < Holds.Count; i++)
                {
                    if (Holds[i].ModelLive)
                        count++;
                }

                return count;
            }
        }

        public void ReportFromView(string id, string message)
        {
            PendingViolations.Add(new InvariantViolation(id, CurrentStep, message));
        }

        public void Dispose()
        {
            Harness.Dispose();

            if (_queueLifetimeSource != null)
                _queueLifetimeSource.Dispose();
        }

        // Queue

        // Ends the queue's own lifetime; nothing to do when the queue lives on RootToken.
        public void EndQueueLifetime()
        {
            if (_queueLifetimeSource != null && !_queueLifetimeSource.IsCancellationRequested)
                _queueLifetimeSource.Cancel();
        }

        // True once the queue's lifetime or the navigator's root has ended; either ends the queue.
        public bool IsQueueEnded
        {
            get { return QueueLifetime.IsCancellationRequested || Harness.Navigator.RootToken.IsCancellationRequested; }
        }

        // Live while not removed, its owner has not ended and the queue has not ended.
        public bool IsQueueHoldLive(InvariantHold hold)
        {
            return hold.ModelLive && !IsQueueEnded;
        }

        public int LiveQueueHoldCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < QueueHolds.Count; i++)
                {
                    if (IsQueueHoldLive(QueueHolds[i]))
                        count++;
                }

                return count;
            }
        }

        // Runs one scheduler tick with the tick flag set, so the probes know a pump may run.
        public void Tick()
        {
            TickNumber++;
            InTick = true;
            try
            {
                Ticker.Tick();
            }
            finally
            {
                InTick = false;
            }
        }

        public void TickUntilIdle(int maxTicks = 100)
        {
            for (var i = 0; i < maxTicks && Ticker.PendingTicks > 0; i++)
                Tick();
        }

        // Live while not removed, its owner has not ended and the queue has not ended.
        public bool IsGateLive(InvariantToggleGate gate)
        {
            return !gate.Removed && (gate.Owner == null || !gate.Owner.IsCancellationRequested) && !IsQueueEnded;
        }

        // The gate that decides for the view id at that stage: the first live denying gate by ascending order, ties by add order.
        public InvariantToggleGate FirstDenyingGate(string viewId, bool atPresentation)
        {
            InvariantToggleGate first = null;
            for (var i = 0; i < AllGates.Count; i++)
            {
                var gate = AllGates[i];
                if (!IsGateLive(gate) || !gate.Denies(viewId, atPresentation))
                    continue;

                if (first == null || gate.Order < first.Order || (gate.Order == first.Order && gate.Number < first.Number))
                    first = gate;
            }

            return first;
        }

        // The waiting and presented items of the model now; with an owner, only the waiting items of that owner.
        public InvariantQueueMark MarkQueue(CancellationTokenSource owner = null)
        {
            var mark = new InvariantQueueMark { EventIndex = Events.Count };
            foreach (var pair in QueueItems)
            {
                var item = pair.Value;
                if (item.State == InvariantQueueItemState.Waiting)
                {
                    if (owner == null || (item.Call != null && ReferenceEquals(item.Call.Owner, owner)))
                        mark.Waiting.Add(item);
                }
                else if (item.State == InvariantQueueItemState.Presented && owner == null)
                {
                    mark.Presented.Add(item);
                }
            }

            return mark;
        }

        // Requests on the layer and modal requests above it, from the navigator's Requested and terminal events only.
        public void LedgerLayerState(ViewLayer layer, out int onLayer, out bool modalAbove)
        {
            onLayer = 0;
            modalAbove = false;
            foreach (var pair in _navigatorOpen)
            {
                var other = LayerById(pair.Value);
                if (other == null)
                    continue;

                if (string.Equals(other.Id, layer.Id, StringComparison.Ordinal))
                    onLayer++;
                else if (other.Order > layer.Order && IsModal(other))
                    modalAbove = true;
            }
        }

        // "Layer is clear" from the navigator's own counters, without IsLayerClear: not busy, no popup request, nothing modal above.
        public bool ModelPopupsClear(out bool modalAbove)
        {
            modalAbove = false;
            var popups = ViewLayers.Popups;
            for (var i = 0; i < LayerArray.Length; i++)
            {
                var layer = LayerArray[i];
                if (layer.Order > popups.Order && IsModal(layer) && Harness.Navigator.RequestCount(layer) > 0)
                    modalAbove = true;
            }

            return !Harness.Navigator.IsBusy && Harness.Navigator.RequestCount(popups) == 0 && !modalAbove;
        }

        // Called by both probes for every event, while it is published.
        public void TrackEvent(in ViewEvent viewEvent, bool fromQueue)
        {
            Events.Add(new InvariantLoggedEvent(in viewEvent, fromQueue));
            var index = Events.Count - 1;

            if (viewEvent.Kind == ViewEventKind.Settled)
                return;

            var terminal = viewEvent.Kind == ViewEventKind.NotShown || viewEvent.Kind == ViewEventKind.Closed;

            // The request ledger the Q2 model reads, from the navigator's events only.
            if (!fromQueue)
            {
                if (viewEvent.Kind == ViewEventKind.Requested)
                    _navigatorOpen[viewEvent.RequestId] = viewEvent.LayerId;
                else if (terminal)
                    _navigatorOpen.Remove(viewEvent.RequestId);
            }

            InvariantQueueItem item;
            QueueItems.TryGetValue(viewEvent.RequestId, out item);

            if (fromQueue)
            {
                if (item == null)
                    item = AddQueueItem(in viewEvent);

                CheckQueuePayload(item, in viewEvent);

                if (viewEvent.Kind == ViewEventKind.Enqueued)
                {
                    if (item.Sequence == 0)
                        item.Sequence = ++_queueSequence;

                    // Gates run at enqueue; the recording gate is last, so an allowed item reached it.
                    if (!AskedAtEnqueue.Contains(item.RequestId))
                        ReportFromView("X.gates", "Enqueued for #" + item.RequestId + " was published without the gates being asked at enqueue.");

                    // A deny at enqueue means the item never enters the queue.
                    var denier = FirstDenyingGate(item.ViewId, false);
                    if (denier != null)
                        ReportFromView("X.gate", "Enqueued for #" + item.RequestId + " (" + item.ViewId + ") was published although the live " + denier + " denies it at enqueue.");
                }
                else if (terminal)
                {
                    NoteTerminal(item, in viewEvent, index, true);

                    if (viewEvent.Kind == ViewEventKind.NotShown && viewEvent.Rejected == RejectReason.Denied)
                        CheckDenied(item, in viewEvent);
                }

                return;
            }

            // A navigator event for an id the queue never published is not a queued request.
            if (item == null)
                return;

            if (!item.ReachedNavigator)
            {
                item.ReachedNavigator = true;

                // Navigator events of a queued request carry the item's priority.
                if (viewEvent.Priority != item.Priority)
                    ReportFromView("X.priority", viewEvent.Kind + " for queued #" + item.RequestId + " carries priority " + viewEvent.Priority + ", the item has " + item.Priority + ".");

                // The queue presents only from a pump, and a pump runs on the next tick.
                if (!InTick)
                    ReportFromView("X.tick", "Queued #" + item.RequestId + " (" + item.ViewId + ") reached the navigator (" + viewEvent.Kind + ") outside a scheduler tick.");

                BindPresentation(item, index);
            }

            switch (viewEvent.Kind)
            {
                case ViewEventKind.Requested:
                    if (item.State == InvariantQueueItemState.Waiting)
                        item.State = InvariantQueueItemState.Presented;
                    break;

                case ViewEventKind.Closed:
                case ViewEventKind.NotShown:
                    NoteTerminal(item, in viewEvent, index, false);
                    break;
            }
        }

        // The first queue event of a new id belongs to the Enqueue call in progress; the call, not the event, gives priority and view.
        private InvariantQueueItem AddQueueItem(in ViewEvent viewEvent)
        {
            InvariantQueueItem item;
            var call = PendingEnqueue;
            if (call != null && call.RequestId == 0)
            {
                call.RequestId = viewEvent.RequestId;
                item = new InvariantQueueItem(viewEvent.RequestId, call.ViewId, call.Priority);
                item.Call = call;
            }
            else
            {
                item = new InvariantQueueItem(viewEvent.RequestId, viewEvent.ViewId, viewEvent.Priority);
                ReportFromView("X.enqueue", "The queue published " + viewEvent.Kind + " for #" + viewEvent.RequestId + " (" + viewEvent.ViewId + "), which no Enqueue call produced.");
            }

            QueueItems.Add(item.RequestId, item);
            return item;
        }

        // One id follows a request; the queue's events carry the call's view, layer and source, and Enqueued its priority.
        private void CheckQueuePayload(InvariantQueueItem item, in ViewEvent viewEvent)
        {
            var call = item.Call;
            if (call == null)
                return;

            var where = "Queue " + viewEvent.Kind + " for #" + item.RequestId + " (Enqueue call " + call.Number + ")";

            if (!string.Equals(viewEvent.ViewId, call.ViewId, StringComparison.Ordinal))
                ReportFromView("X.payload", where + " carries view '" + viewEvent.ViewId + "', the call asked for '" + call.ViewId + "'.");

            if (!string.Equals(viewEvent.LayerId, ViewLayers.Popups.Id, StringComparison.Ordinal))
                ReportFromView("X.payload", where + " carries layer '" + viewEvent.LayerId + "', the queue presents on '" + ViewLayers.Popups.Id + "'.");

            if (!string.Equals(viewEvent.Source, call.Source, StringComparison.Ordinal))
                ReportFromView("X.payload", where + " carries source '" + viewEvent.Source + "', the call passed '" + call.Source + "'.");

            if (viewEvent.Kind == ViewEventKind.Enqueued && viewEvent.Priority != call.Priority)
                ReportFromView("X.payload", where + " carries priority " + viewEvent.Priority + ", the call passed " + call.Priority + ".");
        }

        private static void NoteTerminal(InvariantQueueItem item, in ViewEvent viewEvent, int index, bool fromQueue)
        {
            item.Terminals++;
            item.State = InvariantQueueItemState.Ended;

            if (item.TerminalIndex >= 0)
                return;

            item.TerminalIndex = index;
            item.TerminalFromQueue = fromQueue;
            item.TerminalKind = viewEvent.Kind;
            item.TerminalRejected = viewEvent.Rejected;
            item.TerminalDetail = viewEvent.Detail;
        }

        // The first Deny in ascending order ends the item with that gate's reason; a throwing gate counts as Deny.
        private void CheckDenied(InvariantQueueItem item, in ViewEvent viewEvent)
        {
            var atPresentation = item.Sequence != 0;
            var stage = atPresentation ? "presentation" : "enqueue";
            var gate = FirstDenyingGate(item.ViewId, atPresentation);

            if (gate == null)
            {
                ReportFromView("X.gate", "Queued #" + item.RequestId + " (" + item.ViewId + ") ended NotShown / Denied ('" + viewEvent.Detail + "') at " + stage
                    + " although no live gate denies it there.");
                return;
            }

            // A throwing gate gives no reason of its own, so any detail is accepted for it.
            if (!gate.Throws && !string.Equals(viewEvent.Detail, gate.Reason, StringComparison.Ordinal))
            {
                ReportFromView("X.gate", "Queued #" + item.RequestId + " (" + item.ViewId + ") was denied at " + stage + " with detail '" + viewEvent.Detail
                    + "', the first live denying gate is " + gate + " with reason '" + gate.Reason + "'.");
            }
        }

        // The check and the ShowAsync call are one synchronous block, so the newest gate check is this item's, from the same tick,
        // with no navigator event for another request in between. The check is consumed either way.
        private void BindPresentation(InvariantQueueItem item, int index)
        {
            var latest = LatestPresentation;
            LatestPresentation = null;

            if (latest == null)
            {
                // With no check at all for the id, the checker reports X.gates.
                InvariantPresentation stale;
                if (Presentations.TryGetValue(item.RequestId, out stale))
                {
                    ReportFromView("Q2.atomic", "Queued #" + item.RequestId + " (" + item.ViewId + ") reached the navigator at step " + CurrentStep + ", tick " + TickNumber
                        + " with no gate check at presentation since the last presentation; its own check is from step " + stale.Step + ", tick " + stale.Tick + ".");
                }

                return;
            }

            if (latest.RequestId != item.RequestId)
            {
                ReportFromView("Q2.atomic", "Queued #" + item.RequestId + " (" + item.ViewId + ") reached the navigator, but the newest gate check at presentation was for #" + latest.RequestId + ".");
                return;
            }

            if (latest.Step != CurrentStep || latest.Tick != TickNumber)
            {
                ReportFromView("Q2.atomic", "Queued #" + item.RequestId + " (" + item.ViewId + ") was checked at step " + latest.Step + ", tick " + latest.Tick
                    + " but reached the navigator at step " + CurrentStep + ", tick " + TickNumber + ".");
                return;
            }

            for (var i = latest.EventIndex; i < index; i++)
            {
                var between = Events[i];
                if (between.FromQueue || between.Event.Kind == ViewEventKind.Settled || between.Event.RequestId == item.RequestId)
                    continue;

                ReportFromView("Q2.atomic", "Between the gate check and the presentation of queued #" + item.RequestId + " the navigator published "
                    + between.Event.Kind + " for #" + between.Event.RequestId + ".");
                return;
            }

            item.Presentation = latest;
        }

        // Called by the recording gate: at enqueue it notes the id, at presentation it captures the state for Q1, Q2 and Q4.
        public void RecordGate(in GateRequest request)
        {
            // Every gate before this one allowed, so no live gate denies the item at this stage; a gate stops counting once its registration owner ends.
            var denier = FirstDenyingGate(request.ViewId, request.AtPresentation);
            if (denier != null)
            {
                ReportFromView("X.gate", "The gates let #" + request.RequestId + " (" + request.ViewId + ") pass at " + (request.AtPresentation ? "presentation" : "enqueue")
                    + " although the live " + denier + " denies it.");
            }

            if (!request.AtPresentation)
            {
                AskedAtEnqueue.Add(request.RequestId);
                return;
            }

            // The gates run right before presentation, inside a pump, and a pump runs only on a tick.
            if (!InTick)
                ReportFromView("X.tick", "The gates were asked at presentation about #" + request.RequestId + " (" + request.ViewId + ") outside a scheduler tick.");

            bool modalAbove;
            var stateClear = ModelPopupsClear(out modalAbove);

            string busyWhy = null;
            var modelBusy = Checker != null && Checker.ModelBusy(out busyWhy);

            int ledgerPopups;
            bool ledgerModalAbove;
            LedgerLayerState(ViewLayers.Popups, out ledgerPopups, out ledgerModalAbove);

            var presentation = new InvariantPresentation
            {
                RequestId = request.RequestId,
                Step = CurrentStep,
                QueueHeld = Queue.IsHeld,
                LiveHolds = LiveQueueHoldCount,
                NavigatorClear = Harness.Navigator.IsLayerClear(ViewLayers.Popups),
                StateClear = stateClear,
                Busy = Harness.Navigator.IsBusy,
                LayerRequests = Harness.Navigator.RequestCount(ViewLayers.Popups),
                ModalAbove = modalAbove,
                ModelClear = !modelBusy && ledgerPopups == 0 && !ledgerModalAbove,
                ModelBusy = modelBusy,
                ModelBusyWhy = busyWhy,
                LedgerPopups = ledgerPopups,
                LedgerModalAbove = ledgerModalAbove,
                EventIndex = Events.Count,
                Tick = TickNumber
            };

            InvariantQueueItem self;
            if (!QueueItems.TryGetValue(request.RequestId, out self) || self.Sequence == 0)
                presentation.Unknown = true;

            foreach (var pair in QueueItems)
            {
                var other = pair.Value;
                if (other.RequestId == request.RequestId)
                    continue;

                if (other.State == InvariantQueueItemState.Presented)
                    presentation.InFlight++;

                if (presentation.Unknown || presentation.Better != null || other.State != InvariantQueueItemState.Waiting)
                    continue;

                if (other.Priority > self.Priority || (other.Priority == self.Priority && other.Sequence < self.Sequence))
                    presentation.Better = other.ToString();
            }

            Presentations[request.RequestId] = presentation;
            LatestPresentation = presentation;
        }

        // Rows

        private void AddRows()
        {
            var screens = ViewLayers.Screens;
            var popups = ViewLayers.Popups;
            var system = ViewLayers.System;

            AddRow(new InvariantRow("home", screens));
            AddRow(new InvariantRow("map", screens, retention: ViewRetention.KeepInstance, whenCovered: CoveredMode.Deactivate));
            AddRow(new InvariantRow("shop", screens, cover: CoverPolicy.Transparent));
            AddRow(new InvariantRow("item", screens, takesArgs: true));
            AddRow(new InvariantRow("settings", screens, back: BackPolicy.HandledByView));
            AddRow(new InvariantRow("gate", screens, back: BackPolicy.Ignore));

            AddRow(new InvariantRow("confirm", popups, closeOnBackdrop: true));
            AddRow(new InvariantRow("dialog", popups, retention: ViewRetention.KeepInstance));
            AddRow(new InvariantRow("tip", popups, cover: CoverPolicy.Covers));
            AddRow(new InvariantRow("sheet", popups, back: BackPolicy.Ignore, whenCovered: CoveredMode.Deactivate));
            AddRow(new InvariantRow("poll", popups, back: BackPolicy.HandledByView));
            AddRow(new InvariantRow("multi", popups, allowMultiple: true));

            AddRow(new InvariantRow("lock", system));
            AddRow(new InvariantRow("blocker", system, cover: CoverPolicy.Covers));

            AddRow(new InvariantRow("toast", Hud));
            AddRow(new InvariantRow("hint", Hud));
        }

        private void AddRow(InvariantRow row)
        {
            _rows.Add(row);
            _rowById.Add(row.Id, row);

            Harness.Row(
                row.Id,
                row.Layer,
                row.Retention,
                row.Back,
                row.CloseOnBackdrop,
                row.Cover,
                row.AllowMultiple,
                row.WhenCovered);

            Harness.Map(row.Id, () => CreateView(row));
        }

        // The behavior is drawn when the instance is made, so a kept instance misbehaves the same way on every open.
        private object CreateView(InvariantRow row)
        {
            var behavior = DrawBehavior();
            InvariantViewBase view;
            if (row.TakesArgs)
                view = new InvariantView<int>(this, row.Id, behavior);
            else
                view = new InvariantView<NoArgs>(this, row.Id, behavior);

            Views.Add(view);
            return view;
        }

        private InvariantOpenBehavior DrawBehavior()
        {
            if (!Chaos)
                return InvariantOpenBehavior.Normal;

            var roll = BehaviorRandom.NextDouble();
            if (roll < 0.03)
                return InvariantOpenBehavior.Decline;
            if (roll < 0.06)
                return InvariantOpenBehavior.Throw;
            if (roll < 0.09)
                return InvariantOpenBehavior.CompleteInOpen;
            if (roll < 0.12)
                return InvariantOpenBehavior.CloseInOpen;
            return InvariantOpenBehavior.Normal;
        }
    }
}
