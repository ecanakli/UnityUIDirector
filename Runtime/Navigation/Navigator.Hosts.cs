using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    internal sealed partial class Navigator
    {
        // ITerminatorHost (Now is declared with IIntakeHost)

        public void RemoveFromLayer(ViewRecord record)
        {
            _applied.Remove(record);
            _layerList[record.LayerIndex].Remove(record);
        }

        public void ReleaseShield(int holderId)
        {
            _shield.Release(holderId);
        }

        public ViewVisualState HiddenStateOf(ViewRecord record)
        {
            var order = Projection.OrderFor(_sortingBase, _layerList[record.LayerIndex].Layer.Order, record.Slot);
            return new ViewVisualState(true, false, order, record.Row.WhenCovered);
        }

        public string LayerIdOf(ViewRecord record)
        {
            return _layerList[record.LayerIndex].Layer.Id;
        }

        public bool IsRowRegistered(ViewEntry row)
        {
            return _registry.IsRegistered(row);
        }

        // The record ended before its open took the old request's place, so the old request is live and interactive again.
        public void ReleaseReplaced(ViewRecord record, List<ViewRecord> revealed)
        {
            var old = record.Replaced;
            if (old == null)
                return;

            record.Replaced = null;
            if (_rootGone || old.Phase == ViewPhase.Ended || !old.BeingReplaced)
                return;

            old.BeingReplaced = false;

            // BeginVisible declines a covered view; the pop that reveals it starts its scope later.
            if (!old.HasOutcome)
                revealed.Add(old);
        }

        // A queued request carries its item's priority; an immediate open has 0.
        public int PriorityOf(ViewRecord record)
        {
            return record.Priority;
        }

        // True once per busy-to-settled change.
        public bool TryTakeSettled()
        {
            // No Settled while the root is being torn down: holders are still being released.
            if (!_settledOwed || IsBusy || _rootGone)
                return false;

            _settledOwed = false;
            return true;
        }

        // Starts the visible scope of a view that is on screen and not covered; a second call for the same span does nothing.
        public void BeginVisible(ViewRecord record)
        {
            if (record == null || _rootGone)
                return;

            var phase = record.Phase;
            if (phase != ViewPhase.Opening && phase != ViewPhase.Open)
                return;

            // A request that waits to be replaced is not shown to user code again.
            if (record.BeingReplaced)
                return;

            var current = record.VisibleScope;
            if (current != null && !current.IsEnded)
                return;

            if (record.Instance == null)
                return;

            ViewVisualState applied;
            if (!_applied.TryGetValue(record, out applied))
                return;

            if (applied.Hidden)
                return;

            var scope = new ViewScope(_errors, record.ViewId, record.RequestId);

            UnityEngine.GameObject target;
            if (record.TryGetInstance(out target))
            {
                scope.Target = target;

                var open = record.OpenScope;
                if (open != null && open.Target == null)
                    open.Target = target;
            }

            record.VisibleScope = scope;
            record.InvokeOnVisible(scope);
        }

        // ILayerRunnerHost

        public bool TryTakeRetained(ViewRecord record, out object instance)
        {
            return _retained.TryTake(record.ViewId, out instance);
        }

        // The new request takes the old one's list position, slot and live instance; the old one then ends as Navigated.
        public void FinishReplaced(ViewRecord record)
        {
            var old = record.Replaced;
            if (old == null)
                return;

            record.Replaced = null;
            if (_rootGone)
                return;

            // The old request already ended and released its instance; the new request loads a fresh one.
            if (old.Phase == ViewPhase.Ended)
                return;

            // The new request ended first: the old one stays and is live again.
            if (record.Phase == ViewPhase.Ended)
            {
                old.BeingReplaced = false;
                ApplyProjection();
                return;
            }

            // The pop-to cleared what sat above and the new request is not open yet, so it can take the old position and slot.
            _layerList[record.LayerIndex].Replace(old, record);

            if (old.Instance != null && record.Instance == null)
            {
                record.Instance = old.Instance;
                old.Instance = null;
                MoveAppliedState(old, record);
            }

            _terminator.End(old, CloseReason.Navigated);
        }

        // The indicator shows after a delay while a request that holds the shield is loading.
        public void SetLoading(ViewRecord record, bool loading)
        {
            if (_rootGone || !_layerList[record.LayerIndex].Layer.Has(ViewLayerRules.ShieldsInput))
                return;

            if (loading)
            {
                _loadingCount++;
                if (_loadingCount == 1 && !_indicatorShown && _indicatorTimerId == 0)
                    _indicatorTimerId = _clock.Schedule(_loadIndicatorDelaySeconds, _onIndicatorDelay, null);

                return;
            }

            if (_loadingCount > 0)
                _loadingCount--;

            if (_loadingCount == 0)
                HideLoadIndicator();
        }

        private void OnIndicatorDelay(object state)
        {
            _indicatorTimerId = 0;
            if (_rootGone || _loadingCount <= 0 || _indicatorShown)
                return;

            _indicatorShown = true;
            try
            {
                _stage.SetLoadIndicator(true);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("load indicator", exception);
            }
        }

        private void HideLoadIndicator()
        {
            if (_indicatorTimerId != 0)
            {
                _clock.Cancel(_indicatorTimerId);
                _indicatorTimerId = 0;
            }

            if (!_indicatorShown)
                return;

            _indicatorShown = false;
            try
            {
                _stage.SetLoadIndicator(false);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("load indicator", exception);
            }
        }

        // Screens changed: older requests on layers closed by navigation end by snap; newer ones belong to the new screen.
        public void CloseViewsOnNavigation(ViewRecord opened)
        {
            if (opened == null || _rootGone)
                return;

            if (!_layerList[opened.LayerIndex].Layer.Has(ViewLayerRules.History))
                return;

            var doomed = RentList();
            try
            {
                for (var layerIndex = 0; layerIndex < _layerList.Count; layerIndex++)
                {
                    var state = _layerList[layerIndex];
                    if (!state.Layer.Has(ViewLayerRules.ClosedByNavigation))
                        continue;

                    for (var i = state.Count - 1; i >= 0; i--)
                    {
                        var record = state[i];
                        if (record.Phase != ViewPhase.Ended && record.AcceptSequence < opened.AcceptSequence)
                            doomed.Add(record);
                    }
                }

                for (var i = 0; i < doomed.Count; i++)
                {
                    var record = doomed[i];
                    if (record.Phase == ViewPhase.Ended)
                        continue;

                    if (Projection.IsOnScreen(record.Phase))
                        SnapInstance(record, false);

                    _terminator.End(record, CloseReason.Navigated);
                }
            }
            finally
            {
                ReturnList(doomed);
            }
        }

        public void OnShown(ViewRecord record)
        {
            // Inside an observer dispatch, Shown waits until the event in progress has reached every observer.
            if (record.Phase != ViewPhase.Ended)
                _outbox.RunOrDefer(_publishShown, record, ViewErrorSource.Observer, record.ViewId, record.RequestId);

            CheckSettled();
        }

        private void PublishShownNotification(object state)
        {
            _terminator.PublishShown((ViewRecord)state);
        }

        public void OnLayerIdle(ViewLayer layer)
        {
            CheckSettled();
        }

        // Takes the operation hold only when the record holds nothing yet; the record releases it when it ends.
        public void HoldForOperation(ViewRecord record)
        {
            if (record == null || record.Phase == ViewPhase.Ended || record.ShieldHolderId != 0 || record.CloseHolderId != 0)
                return;

            if (!_layerList[record.LayerIndex].Layer.Has(ViewLayerRules.ShieldsInput))
                return;

            record.ShieldHolderId = _shield.Acquire(ShieldHolderKind.Operation, record.RequestId, record.ViewId, record.CallerMember);
        }

        public void HoldForPendingClose(ViewRecord record)
        {
            if (record == null || _rootGone)
                return;

            TakePendingCloseHold(record);
        }

        // Runs through the outbox, so it comes after the notifications that are already pending.
        private void CheckSettled()
        {
            if (_rootGone)
                return;

            _outbox.Enqueue(_publishSettled, null, ViewErrorSource.Observer, null, 0);
            _outbox.Run();
        }

        private void PublishSettledNotification(object state)
        {
            if (!TryTakeSettled())
                return;

            var settled = new ViewEvent(
                ViewEventKind.Settled,
                0,
                null,
                null,
                null,
                0,
                default(CloseReason),
                RejectReason.None,
                null,
                0f,
                0f);
            _observers.Publish(in settled);
        }

        // Shield

        private void OnShieldBlockingChanged(bool blocking)
        {
            _stage.SetShield(blocking, _shieldOrder);
        }

        // IStageEvents

        // An instance went away without the package: a kept one is dropped, a live one ends its request as Destroyed.
        public void OnInstanceLost(object instance, bool destroyed)
        {
            if (_rootGone || instance == null)
                return;

            string keptId;
            if (_retained.TryDrop(instance, out keptId))
            {
                ReleaseInstance(instance, keptId);
                return;
            }

            var record = FindByInstance(instance);
            if (record == null)
                return;

            // Its state is no longer the package's, so it is released rather than kept.
            record.InstanceLost = true;

            _diagnostics.Warn(
                DiagnosticIds.ViewLostOutsidePackage,
                "The view '" + record.ViewId + "' (request " + record.RequestId + ") was "
                + (destroyed ? "destroyed" : "deactivated") + " outside the package and its request ended.");

            if (record.WasShown)
            {
                // A deactivated instance still exists, so it is snapped before the end; a destroyed one is gone.
                if (!destroyed && Projection.IsOnScreen(record.Phase))
                    SnapInstance(record, false);

                _terminator.End(record, CloseReason.Destroyed);
                return;
            }

            record.TryLatchRejected(RejectReason.Cancelled, "The instance was lost before it was shown.");
            _terminator.End(record, CloseReason.NotShown);
        }

        // A tap on the shared backdrop closes the top view when its row asks for it; otherwise it is swallowed.
        public void OnBackdropTapped(ViewLayer layer)
        {
            if (_rootGone || _shield.IsBlocking)
                return;

            var layerIndex = FindLayerIndex(layer);
            if (layerIndex < 0)
                return;

            var top = _layerList[layerIndex].TopOnScreen();
            if (top == null || top.Phase != ViewPhase.Open || !top.Row.CloseOnBackdrop)
                return;

            top.RequestClose(CloseReason.Backdrop);
        }

        private ViewRecord FindByInstance(object instance)
        {
            for (var layerIndex = 0; layerIndex < _layerList.Count; layerIndex++)
            {
                var state = _layerList[layerIndex];
                for (var i = 0; i < state.Count; i++)
                {
                    if (ReferenceEquals(state[i].Instance, instance))
                        return state[i];
                }
            }

            return null;
        }

        // Derived state

        // Recomputes, applies the difference to the stage, reports revealed views to the caller, then ends scopes of covered ones.
        public void RecomputeAndApply(List<ViewRecord> revealed)
        {
            // A call from inside a stage call is folded into the running one.
            if (_recomputing)
            {
                _recomputeAgain = true;
                return;
            }

            var covered = RentList();
            try
            {
                _recomputing = true;
                try
                {
                    var passes = 0;
                    do
                    {
                        _recomputeAgain = false;
                        ApplyOnce(revealed, covered);
                        passes++;
                    }
                    while (_recomputeAgain && passes < MaxRecomputePasses);
                }
                finally
                {
                    _recomputing = false;
                }

                // User cleanup runs after the state is in place.
                for (var i = 0; i < covered.Count; i++)
                    EndVisibleScopeIfHidden(covered[i]);
            }
            finally
            {
                ReturnList(covered);
            }
        }

        private void ApplyOnce(List<ViewRecord> revealed, List<ViewRecord> covered)
        {
            _items.Clear();
            _itemRecords.Clear();

            for (var layerIndex = 0; layerIndex < _layerList.Count; layerIndex++)
            {
                var state = _layerList[layerIndex];
                var layer = state.Layer;
                var modal = layer.Has(ViewLayerRules.Modal);

                for (var i = 0; i < state.Count; i++)
                {
                    var record = state[i];
                    var phase = record.Phase;
                    if (phase == ViewPhase.Ended)
                        continue;

                    // A reopened request holds the live instance while it waits and stands in for the view it replaced.
                    if (phase == ViewPhase.Waiting && record.Instance != null)
                        phase = ViewPhase.Open;

                    _items.Add(new ProjectionItem
                    {
                        LayerIndex = layerIndex,
                        LayerOrder = layer.Order,
                        LayerModal = modal,
                        Phase = phase,
                        CoversBelow = CoversBelow(layer, record.Row),
                        // A screen waiting to be replaced stays non-interactive, like a latched close.
                        CloseRequested = record.ClosePending || record.BeingReplaced,
                        Slot = record.Slot
                    });
                    _itemRecords.Add(record);
                }
            }

            Projection.Compute(_items, _sortingBase, _results, _backdrops);

            for (var i = 0; i < _items.Count; i++)
                ApplyRecord(_itemRecords[i], _items[i].Phase, _results[i], revealed, covered);

            for (var layerIndex = 0; layerIndex < _layerList.Count; layerIndex++)
                ApplyBackdrop(layerIndex);
        }

        private void ApplyRecord(
            ViewRecord record,
            ViewPhase projected,
            ProjectionResult result,
            List<ViewRecord> revealed,
            List<ViewRecord> covered)
        {
            var instance = record.Instance;
            if (instance == null || !Projection.IsOnScreen(projected))
                return;

            var next = new ViewVisualState(result.Hidden, result.Interactive, result.SortingOrder, record.Row.WhenCovered);

            ViewVisualState previous;
            var had = _applied.TryGetValue(record, out previous);
            if (had && previous.SameAs(in next))
                return;

            _applied[record] = next;

            try
            {
                _stage.Apply(instance, in next);
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.Transition, record.ViewId, record.RequestId, null);
            }

            if (!next.Hidden && (!had || previous.Hidden))
            {
                // A stand-in for a reopened view is not on screen yet; its own open starts the scope.
                if (Projection.IsOnScreen(record.Phase))
                    revealed.Add(record);
            }
            else if (next.Hidden && had && !previous.Hidden)
            {
                covered.Add(record);
            }
        }

        private void ApplyBackdrop(int layerIndex)
        {
            var layer = _layerList[layerIndex].Layer;
            if (!layer.Has(ViewLayerRules.Modal))
                return;

            var next = _backdrops[layerIndex];
            var previous = _appliedBackdrops[layerIndex];
            if (next.Active == previous.Active && (!next.Active || next.SortingOrder == previous.SortingOrder))
                return;

            _appliedBackdrops[layerIndex] = next;

            try
            {
                _stage.ApplyBackdrop(layer, next.Active, next.SortingOrder);
            }
            catch (Exception exception)
            {
                _diagnostics.ReportFault("backdrop", exception);
            }
        }

        private void EndVisibleScopeIfHidden(ViewRecord record)
        {
            // It became visible again while the state was applied.
            ViewVisualState applied;
            if (_applied.TryGetValue(record, out applied) && !applied.Hidden)
                return;

            var scope = record.VisibleScope;
            if (scope == null)
                return;

            record.VisibleScope = null;
            scope.End();
        }

        // Recomputes now and starts the visible scope of the views it revealed; for changes the runner did not cause.
        private void ApplyProjection()
        {
            var revealed = RentList();
            try
            {
                RecomputeAndApply(revealed);

                for (var i = 0; i < revealed.Count; i++)
                {
                    var other = revealed[i];
                    if (other == null || other.Phase == ViewPhase.Ended || other.Phase == ViewPhase.Closing)
                        continue;

                    try
                    {
                        BeginVisible(other);
                    }
                    catch (Exception exception)
                    {
                        _errors.Route(exception, ViewErrorSource.ViewHook, other.ViewId, other.RequestId, null);
                    }
                }
            }
            finally
            {
                ReturnList(revealed);
            }
        }

        // A history layer covers what is below; so do rows that say so.
        private static bool CoversBelow(ViewLayer layer, ViewEntry row)
        {
            switch (row.Cover)
            {
                case CoverPolicy.Covers:
                    return true;

                case CoverPolicy.Transparent:
                    return false;

                default:
                    return layer.Has(ViewLayerRules.History);
            }
        }

        // Registrations

        private sealed class RegistrationState
        {
            public RegistrationEntry Entry;
            public int HolderId;
            public ObserverEntry Observer;
            public CatalogRegistration Catalog;
        }

        public ViewRegistration HoldInput(
            string reason,
            CancellationToken owner,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0)
        {
            ThrowIfOffMainThread();
            if (reason == null)
                throw new ArgumentNullException(nameof(reason));

            if (_rootGone || owner.IsCancellationRequested)
                return default(ViewRegistration);

            var state = new RegistrationState();
            state.HolderId = _shield.Acquire(ShieldHolderKind.InputHold, 0, reason, CallSite(member, line));
            _settledOwed = true;

            return Register(state, _removeHold, owner);
        }

        public ViewRegistration AddObserver(
            IViewObserver observer,
            CancellationToken owner,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0)
        {
            ThrowIfOffMainThread();
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            if (_rootGone || owner.IsCancellationRequested)
                return default(ViewRegistration);

            var state = new RegistrationState();
            state.Observer = _observers.Add(observer);

            return Register(state, _removeObserver, owner);
        }

        public ViewRegistration AddCatalog(
            ViewCatalog catalog,
            CancellationToken owner,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0)
        {
            ThrowIfOffMainThread();
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            if (_rootGone || owner.IsCancellationRequested)
                return default(ViewRegistration);

            var state = new RegistrationState();
            state.Catalog = _registry.AddCatalog(catalog);

            return Register(state, _removeCatalog, owner);
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

        private void RemoveHold(object state)
        {
            var registration = (RegistrationState)state;
            _registrations.Remove(registration.Entry);
            _shield.Release(registration.HolderId);
            CheckSettled();
        }

        private void RemoveObserverRegistration(object state)
        {
            var registration = (RegistrationState)state;
            _registrations.Remove(registration.Entry);
            _observers.Remove(registration.Observer);
        }

        // The rows go; views already open keep theirs, and a kept instance of a removed row is released.
        private void RemoveCatalogRegistration(object state)
        {
            var registration = (RegistrationState)state;
            _registrations.Remove(registration.Entry);

            var removedIds = new List<string>();
            _registry.RemoveCatalog(registration.Catalog, removedIds);
            if (removedIds.Count == 0)
                return;

            for (var i = 0; i < removedIds.Count; i++)
            {
                var id = removedIds[i];
                object instance;
                if (_retained.TryTake(id, out instance))
                    ReleaseInstance(instance, id);
            }
        }

        // Preload and trim

        public UniTask<bool> PreloadAsync(
            string viewId,
            CancellationToken owner,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0)
        {
            ThrowIfOffMainThread();
            if (viewId == null)
                throw new ArgumentNullException(nameof(viewId));

            if (_rootGone || owner.IsCancellationRequested)
                return UniTask.FromResult(false);

            ViewEntry row;
            if (!_registry.TryGetRow(viewId, out row))
            {
                _diagnostics.Error(DiagnosticIds.UnknownViewId, "No catalog row has the id '" + viewId + "'.");
                return UniTask.FromResult(false);
            }

            return _preloads.PreloadAsync(row, owner);
        }

        public void Trim()
        {
            if (_rootGone)
                return;

            if (!_mainThread.IsMainThread)
            {
                _mainThread.Post(_trim);
                return;
            }

            ReleaseRetained();
        }

        private void ReleaseRetained()
        {
            if (_retained.Count == 0)
                return;

            var ids = new List<string>(_retained.Count);
            _retained.CaptureViewIds(ids);

            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                object instance;
                if (_retained.TryTake(id, out instance))
                    ReleaseInstance(instance, id);
            }
        }

        private void ReleaseInstance(object instance, string viewId)
        {
            try
            {
                _stage.Release(instance);
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.Provider, viewId, 0, null);
            }
        }
    }
}
