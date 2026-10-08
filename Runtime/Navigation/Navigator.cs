using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Ecanakli.UIDirector
{
    // The logic core of one root: it wires the pieces and is the host they call back into.
    internal sealed partial class Navigator : INavigator, IIntakeHost, ILayerRunnerHost, IStageEvents
    {
        internal const float DefaultLoadIndicatorDelaySeconds = 0.4f;
        private const int MaxRecomputePasses = 4;
        private const string RootGoneDetail = "The root was destroyed.";

        private readonly IViewStage _stage;
        private readonly IMainThread _mainThread;
        private readonly NavigatorClock _clock;
        private readonly Diagnostics _diagnostics;
        private readonly ErrorRouter _errors;
        private readonly ViewRegistry _registry;
        private readonly List<LayerState<ViewRecord>> _layerList;
        private readonly LayerRunner[] _runners;
        private readonly InputShield _shield;
        private readonly Outbox _outbox;
        private readonly ObserverList _observers;
        private readonly RetainedInstances _retained;
        private readonly PreloadLeases _preloads;
        private readonly Terminator _terminator;
        private readonly RequestIntake _intake;
        private readonly CancellationTokenSource _rootSource = new CancellationTokenSource();
        private readonly int _sortingBase;
        private readonly int _shieldOrder;
        private readonly float _loadIndicatorDelaySeconds;

        // Derived state: scratch lists and the last state applied to the stage.
        private readonly List<ProjectionItem> _items = new List<ProjectionItem>(16);
        private readonly List<ProjectionResult> _results = new List<ProjectionResult>(16);
        private readonly List<ViewRecord> _itemRecords = new List<ViewRecord>(16);
        private readonly BackdropPlacement[] _backdrops;
        private readonly BackdropPlacement[] _appliedBackdrops;
        private readonly Dictionary<ViewRecord, ViewVisualState> _applied = new Dictionary<ViewRecord, ViewVisualState>();
        private readonly Stack<List<ViewRecord>> _listPool = new Stack<List<ViewRecord>>();
        private bool _recomputing;
        private bool _recomputeAgain;

        // Load indicator and settled tracking.
        private readonly Action<object> _onIndicatorDelay;
        private readonly Action<object> _publishSettled;
        private readonly Action<object> _publishShown;
        private int _loadingCount;
        private int _indicatorTimerId;
        private bool _indicatorShown;
        private bool _settledOwed;

        // Registrations made through this navigator, so teardown can end them.
        private readonly List<RegistrationEntry> _registrations = new List<RegistrationEntry>();
        private readonly Action<object> _removeHold;
        private readonly Action<object> _removeObserver;
        private readonly Action<object> _removeCatalog;
        private readonly Action _trim;

        private bool _rootGone;

        // Tests pass fakes; null arguments get the real defaults. Pass the same Diagnostics to the clock and here.
        internal Navigator(
            IViewStage stage,
            NavigatorClock clock = null,
            IMainThread mainThread = null,
            IReadOnlyList<ViewCatalog> catalogs = null,
            IReadOnlyList<ViewLayer> layers = null,
            float loadTimeoutSeconds = LayerRunner.DefaultLoadTimeoutSeconds,
            float transitionTimeoutSeconds = LayerRunner.DefaultTransitionTimeoutSeconds,
            ViewErrorHandler errorHandler = null,
            Diagnostics diagnostics = null,
            int sortingBase = 0,
            float loadIndicatorDelaySeconds = DefaultLoadIndicatorDelaySeconds,
            int waitingCap = RequestIntake.DefaultWaitingCap)
        {
            if (stage == null)
                throw new ArgumentNullException(nameof(stage));

            _stage = stage;
            _sortingBase = sortingBase;
            _loadIndicatorDelaySeconds = loadIndicatorDelaySeconds > 0f ? loadIndicatorDelaySeconds : 0f;
            _diagnostics = diagnostics ?? new Diagnostics();
            _clock = clock ?? new NavigatorClock(_diagnostics);
            _mainThread = mainThread ?? new UniTaskMainThread();
            _errors = new ErrorRouter(errorHandler, _diagnostics);
            _observers = new ObserverList(_errors);
            AttachEventRing();
            _outbox = new Outbox(_errors, _observers);
            _retained = new RetainedInstances();
            _preloads = new PreloadLeases(stage, _mainThread, _diagnostics, _errors);
            _registry = new ViewRegistry(layers ?? DefaultLayers(), _diagnostics);

            _onIndicatorDelay = OnIndicatorDelay;
            _publishSettled = PublishSettledNotification;
            _publishShown = PublishShownNotification;
            _removeHold = RemoveHold;
            _removeObserver = RemoveObserverRegistration;
            _removeCatalog = RemoveCatalogRegistration;
            _trim = Trim;

            _shield = new InputShield(_clock, _diagnostics, OnShieldBlockingChanged);
            _shieldOrder = Projection.ShieldOrder(sortingBase, _registry.HighestLayerOrder());

            var layerCount = _registry.LayerCount;
            _layerList = new List<LayerState<ViewRecord>>(layerCount);
            for (var i = 0; i < layerCount; i++)
                _layerList.Add(new LayerState<ViewRecord>(_registry.LayerAt(i)));

            _backdrops = new BackdropPlacement[layerCount];
            _appliedBackdrops = new BackdropPlacement[layerCount];

            _terminator = new Terminator(stage, _errors, _observers, _retained, _outbox, this);

            _runners = new LayerRunner[layerCount];
            for (var i = 0; i < layerCount; i++)
            {
                _runners[i] = new LayerRunner(
                    _layerList[i],
                    stage,
                    _terminator,
                    _clock,
                    _errors,
                    _diagnostics,
                    this,
                    loadTimeoutSeconds,
                    transitionTimeoutSeconds);
            }

            _intake = new RequestIntake(
                _registry,
                _layerList,
                _shield,
                _outbox,
                _diagnostics,
                _errors,
                stage,
                this,
                sortingBase,
                this,
                waitingCap);

            if (catalogs != null)
            {
                for (var i = 0; i < catalogs.Count; i++)
                {
                    var catalog = catalogs[i];
                    if (catalog != null)
                        _registry.AddCatalog(catalog);
                }
            }

            stage.SetEvents(this);
        }

        // What tests tick and read.
        internal NavigatorClock Clock
        {
            get { return _clock; }
        }

        internal Diagnostics Diagnostics
        {
            get { return _diagnostics; }
        }

        // A queue on this navigator routes its gate and observer errors here.
        internal ErrorRouter Errors
        {
            get { return _errors; }
        }

        // INavigator state

        public bool IsBusy
        {
            get
            {
                if (_shield.InputHoldCount > 0)
                    return true;

                for (var i = 0; i < _layerList.Count; i++)
                {
                    var state = _layerList[i];
                    if (state.IsRunning || state.PendingOperations > 0)
                        return true;
                }

                return false;
            }
        }

        public bool IsLayerClear(ViewLayer layer)
        {
            if (_rootGone)
                return false;

            var index = FindLayerIndex(layer);
            if (index < 0 || IsBusy || _layerList[index].Count > 0)
                return false;

            for (var i = index + 1; i < _layerList.Count; i++)
            {
                var above = _layerList[i];
                if (above.Layer.Has(ViewLayerRules.Modal) && above.Count > 0)
                    return false;
            }

            return true;
        }

        public CancellationToken RootToken
        {
            get { return _rootSource.Token; }
        }

        public int ReserveRequestId()
        {
            return _intake.ReserveRequestId();
        }

        // Shield holders now, for tests and diagnostics.
        internal int ShieldCount
        {
            get { return _shield.Count; }
        }

        // Requests a layer holds now (waiting, loading or on screen); -1 for an unknown layer.
        internal int RequestCount(ViewLayer layer)
        {
            int index;
            return layer != null && _registry.TryGetLayerIndex(layer, out index) ? _layerList[index].Count : -1;
        }

        public void Describe(StringBuilder output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            output.Append("navigator: ").Append(_rootGone ? "destroyed" : (IsBusy ? "busy" : "settled")).Append('\n');
            _registry.Describe(output);

            for (var i = 0; i < _layerList.Count; i++)
            {
                var state = _layerList[i];
                output.Append("layer ").Append(state.Layer.Id)
                    .Append(" requests ").Append(state.Count)
                    .Append(" operations ").Append(state.PendingOperations)
                    .Append(state.IsRunning ? " running" : " idle")
                    .Append('\n');

                for (var j = 0; j < state.Count; j++)
                {
                    var record = state[j];
                    output.Append("  #").Append(record.RequestId)
                        .Append(' ').Append(record.ViewId)
                        .Append(' ').Append(record.Phase)
                        .Append(" slot ").Append(record.Slot);
                    if (record.ClosePending)
                        output.Append(" close pending: ").Append(record.OutcomeReason);
                    if (record.BeingReplaced)
                        output.Append(" being replaced");
                    output.Append('\n');
                }
            }

            _shield.Describe(output);
            _retained.Describe(output);
            _preloads.Describe(output);
            output.Append("observers: ").Append(ObserverCount).Append('\n');
            output.Append("registrations: ").Append(_registrations.Count).Append('\n');
        }

        // Teardown

        // Called once when the root is destroyed; later calls do nothing and every entry point answers "gone".
        internal void Teardown()
        {
            if (_rootGone)
                return;

            _rootGone = true;
            _queues.Clear();

            try
            {
                // A runner resumed inside an End finds nothing left to dequeue.
                for (var i = 0; i < _layerList.Count; i++)
                    _layerList[i].ClearOperations();

                EndEveryRecord();

                _applied.Clear();
                _loadingCount = 0;
                HideLoadIndicator();
                _shield.ReleaseAll();
                _settledOwed = false;

                var registrations = _registrations.ToArray();
                for (var i = 0; i < registrations.Length; i++)
                    registrations[i].Remove();

                _preloads.DisposeAll();
                ReleaseRetained();
                _observers.RemoveAll();
                _clock.Clear();
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("navigator teardown", exception);
            }
            finally
            {
                // Last: whoever waits on the root token finds a settled navigator.
                try
                {
                    _rootSource.Cancel();
                }
                catch (Exception exception)
                {
                    _diagnostics.ReportFault("root token", exception);
                }
            }
        }

        // Shown requests end as Destroyed, the rest as not shown with RootGone; awaiters complete inside End.
        private void EndEveryRecord()
        {
            var doomed = RentList();
            try
            {
                for (var i = _layerList.Count - 1; i >= 0; i--)
                {
                    var state = _layerList[i];
                    for (var j = state.Count - 1; j >= 0; j--)
                        doomed.Add(state[j]);
                }

                for (var i = 0; i < doomed.Count; i++)
                {
                    var record = doomed[i];
                    if (record.Phase == ViewPhase.Ended)
                        continue;

                    if (record.WasShown)
                    {
                        _terminator.End(record, CloseReason.Destroyed);
                        continue;
                    }

                    record.TryLatchRejected(RejectReason.RootGone, RootGoneDetail);
                    _terminator.End(record, CloseReason.NotShown);
                }
            }
            finally
            {
                ReturnList(doomed);
            }
        }

        // Helpers

        private static IReadOnlyList<ViewLayer> DefaultLayers()
        {
            return new[] { ViewLayers.Screens, ViewLayers.Popups, ViewLayers.System };
        }

        // Looked up by id, like the registry; still answers after teardown.
        internal bool HasLayer(ViewLayer layer)
        {
            return FindLayerIndex(layer) >= 0;
        }

        private int FindLayerIndex(ViewLayer layer)
        {
            if (layer == null)
                return -1;

            int index;
            if (!_registry.TryGetLayerIndex(layer, out index) || index >= _layerList.Count)
                return -1;

            return index;
        }

        // Main-thread violations throw; after teardown nothing throws.
        private void ThrowIfOffMainThread()
        {
            if (_rootGone || _mainThread.IsMainThread)
                return;

            throw new InvalidOperationException("The navigator must be used on the main thread.");
        }

        private List<ViewRecord> RentList()
        {
            return _listPool.Count > 0 ? _listPool.Pop() : new List<ViewRecord>(4);
        }

        private void ReturnList(List<ViewRecord> list)
        {
            list.Clear();
            _listPool.Push(list);
        }

        // "Member (line N)", shared by input holds and queue holds.
        internal static string CallSite(string member, int line)
        {
            return member == null ? null : member + " (line " + line + ")";
        }
    }
}
