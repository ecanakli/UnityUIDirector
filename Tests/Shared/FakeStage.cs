using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests
{
    // How a stage operation completes: at once, or when the test completes it.
    internal enum StageMode
    {
        Auto,
        Manual
    }

    // An in-memory IViewStage: no GameObjects, every call recorded, completion under test control.
    internal sealed class FakeStage : IViewStage
    {
        private enum Outcome
        {
            Normal,
            ReturnNull,
            NoView,
            NoHost
        }

        // What one acquire, play or preload will do when it completes; taken when the call is made.
        private readonly struct Plan
        {
            public readonly Outcome Outcome;
            public readonly Exception Fault;

            public Plan(Outcome outcome, Exception fault)
            {
                Outcome = outcome;
                Fault = fault;
            }
        }

        // An operation in manual mode: the task the logic awaits and the token that stops it.
        private sealed class Pending<T>
        {
            public readonly ViewEntry Row;
            public readonly CancellationToken Token;
            public readonly Plan Plan;
            public readonly bool IgnoresCancellation;
            public readonly UniTaskCompletionSource<T> Source = new UniTaskCompletionSource<T>();
            public CancellationTokenRegistration Registration;

            public Pending(ViewEntry row, CancellationToken token, Plan plan, bool ignoresCancellation)
            {
                Row = row;
                Token = token;
                Plan = plan;
                IgnoresCancellation = ignoresCancellation;
            }
        }

        private sealed class BackdropState
        {
            public bool Active;
            public int Order;
        }

        private readonly List<StageCall> _calls = new List<StageCall>();
        private readonly List<FakeInstance> _instances = new List<FakeInstance>();
        private readonly List<FakeLease> _leases = new List<FakeLease>();
        private readonly Dictionary<string, Func<object>> _views = new Dictionary<string, Func<object>>(StringComparer.Ordinal);
        private readonly Dictionary<string, BackdropState> _backdrops = new Dictionary<string, BackdropState>(StringComparer.Ordinal);
        private readonly Dictionary<StageCallKind, Queue<Exception>> _faults = new Dictionary<StageCallKind, Queue<Exception>>();
        private readonly Dictionary<StageCallKind, Outcome> _nextOutcomes = new Dictionary<StageCallKind, Outcome>();
        private readonly List<Pending<object>> _pendingAcquires = new List<Pending<object>>();
        private readonly List<Pending<object>> _pendingPlays = new List<Pending<object>>();
        private readonly List<Pending<IDisposable>> _pendingPreloads = new List<Pending<IDisposable>>();
        private IStageEvents _events;
        private Exception _nextLeaseDisposeFault;
        private int _nextInstanceId = 1;

        // Mode per operation kind: Auto completes inside the call (the default), Manual waits for Complete or Fail.
        public StageMode AcquireMode { get; set; }
        public StageMode PlayMode { get; set; }
        public StageMode PreloadMode { get; set; }

        // Manual preloads started while true ignore their token: a later CompletePreload hands the lease back even after cancellation.
        public bool PreloadIgnoresCancellation { get; set; }

        // Calls

        public IReadOnlyList<StageCall> Calls
        {
            get { return _calls; }
        }

        public void ClearCalls()
        {
            _calls.Clear();
        }

        public int CountOf(StageCallKind kind)
        {
            var count = 0;
            for (var i = 0; i < _calls.Count; i++)
            {
                if (_calls[i].Kind == kind)
                    count++;
            }

            return count;
        }

        // The recorded calls of the given kinds, in order; every call when none is given.
        public StageCall[] CallsOf(params StageCallKind[] kinds)
        {
            var result = new List<StageCall>();
            for (var i = 0; i < _calls.Count; i++)
            {
                if (kinds.Length == 0 || Array.IndexOf(kinds, _calls[i].Kind) >= 0)
                    result.Add(_calls[i]);
            }

            return result.ToArray();
        }

        // The kinds of the recorded calls, in order, filtered like CallsOf.
        public StageCallKind[] Kinds(params StageCallKind[] only)
        {
            var calls = CallsOf(only);
            var kinds = new StageCallKind[calls.Length];
            for (var i = 0; i < calls.Length; i++)
                kinds[i] = calls[i].Kind;
            return kinds;
        }

        public string DescribeCalls()
        {
            var text = new StringBuilder("stage calls:");
            for (var i = 0; i < _calls.Count; i++)
                text.Append("\n  ").Append(i).Append(' ').Append(_calls[i]);
            return text.ToString();
        }

        // Instances

        // What a load of the row id produces as the view object; the harness maps ids for you.
        public void SetView(string viewId, Func<object> factory)
        {
            if (viewId == null)
                throw new ArgumentNullException(nameof(viewId));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            _views[viewId] = factory;
        }

        public bool HasView(string viewId)
        {
            return viewId != null && _views.ContainsKey(viewId);
        }

        // Every instance ever acquired, in order.
        public IReadOnlyList<FakeInstance> Instances
        {
            get { return _instances; }
        }

        // Acquired and neither released nor stashed.
        public IReadOnlyList<FakeInstance> LiveInstances
        {
            get
            {
                var live = new List<FakeInstance>();
                for (var i = 0; i < _instances.Count; i++)
                {
                    if (_instances[i].IsLive)
                        live.Add(_instances[i]);
                }

                return live;
            }
        }

        // Stashed and not yet taken back or released.
        public IReadOnlyList<FakeInstance> StashedInstances
        {
            get
            {
                var stashed = new List<FakeInstance>();
                for (var i = 0; i < _instances.Count; i++)
                {
                    if (_instances[i].Stashed)
                        stashed.Add(_instances[i]);
                }

                return stashed;
            }
        }

        // The newest instance acquired for the row id; null when there is none.
        public FakeInstance LastInstanceOf(string viewId)
        {
            for (var i = _instances.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_instances[i].ViewId, viewId, StringComparison.Ordinal))
                    return _instances[i];
            }

            return null;
        }

        // Fails unless every acquired instance was released or is stashed.
        public void AssertNoLiveInstances()
        {
            var live = LiveInstances;
            Assert.That(live, Is.Empty, "Instances neither released nor stashed: " + Join(live) + "\n" + DescribeCalls());
        }

        // Fails unless every acquired instance was released exactly once.
        public void AssertAllReleased()
        {
            var bad = new List<FakeInstance>();
            for (var i = 0; i < _instances.Count; i++)
            {
                if (_instances[i].ReleaseCount != 1)
                    bad.Add(_instances[i]);
            }

            Assert.That(bad, Is.Empty, "Instances not released exactly once: " + Join(bad) + "\n" + DescribeCalls());
        }

        // Applied state

        public bool ShieldBlocking { get; private set; }
        public int ShieldOrder { get; private set; }
        public int ShieldRaiseCount { get; private set; }
        public int ShieldDropCount { get; private set; }
        public bool LoadIndicatorVisible { get; private set; }

        public bool IsBackdropActive(ViewLayer layer)
        {
            BackdropState state;
            return _backdrops.TryGetValue(layer.Id, out state) && state.Active;
        }

        // The sorting order of the last active backdrop of the layer; zero when it never had one.
        public int BackdropOrder(ViewLayer layer)
        {
            BackdropState state;
            return _backdrops.TryGetValue(layer.Id, out state) ? state.Order : 0;
        }

        // Every preload lease handed out, disposed or not.
        public IReadOnlyList<FakeLease> Leases
        {
            get { return _leases; }
        }

        public int LiveLeaseCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _leases.Count; i++)
                {
                    if (!_leases[i].IsDisposed)
                        count++;
                }

                return count;
            }
        }

        // Manual mode: pending operations, completed oldest first unless an index is given

        public int PendingAcquireCount
        {
            get { return _pendingAcquires.Count; }
        }

        public int PendingPlayCount
        {
            get { return _pendingPlays.Count; }
        }

        public int PendingPreloadCount
        {
            get { return _pendingPreloads.Count; }
        }

        // Finishes a pending acquire with an instance; a cancelled one is released by the stage itself.
        public void CompleteAcquire(int index = 0)
        {
            var pending = Take(_pendingAcquires, index, "acquire");

            object result;
            try
            {
                result = Produce(pending.Row, pending.Plan);
            }
            catch (Exception exception)
            {
                pending.Source.TrySetException(exception);
                return;
            }

            if (!pending.Token.IsCancellationRequested)
            {
                pending.Source.TrySetResult(result);
                return;
            }

            // A late result under a cancelled token: the contract says the stage releases it.
            var late = result as FakeInstance;
            if (late == null)
                return;

            late.ReleasedLate = true;
            Release(late);
        }

        public void FailAcquire(Exception exception = null, int index = 0)
        {
            var pending = Take(_pendingAcquires, index, "acquire");
            pending.Source.TrySetException(exception ?? DefaultFault(StageCallKind.Acquire));
        }

        public void CompletePlay(int index = 0)
        {
            var pending = Take(_pendingPlays, index, "play");
            if (pending.Plan.Fault != null)
                pending.Source.TrySetException(pending.Plan.Fault);
            else
                pending.Source.TrySetResult(null);
        }

        public void FailPlay(Exception exception = null, int index = 0)
        {
            var pending = Take(_pendingPlays, index, "play");
            pending.Source.TrySetException(exception ?? DefaultFault(StageCallKind.Play));
        }

        // Finishes a pending preload with a lease; a cancelled one is disposed by the stage itself unless it ignores cancellation.
        public void CompletePreload(int index = 0)
        {
            var pending = Take(_pendingPreloads, index, "preload");

            IDisposable result;
            try
            {
                result = ProduceLease(pending.Row, pending.Plan);
            }
            catch (Exception exception)
            {
                pending.Source.TrySetException(exception);
                return;
            }

            if (!pending.Token.IsCancellationRequested || pending.IgnoresCancellation)
            {
                pending.Source.TrySetResult(result);
                return;
            }

            if (result != null)
                result.Dispose();
        }

        public void FailPreload(Exception exception = null, int index = 0)
        {
            var pending = Take(_pendingPreloads, index, "preload");
            pending.Source.TrySetException(exception ?? DefaultFault(StageCallKind.Preload));
        }

        // One-shot faults and odd results: each applies to the next call of its kind only

        // The next call of the kind fails: sync members throw, Acquire, Play and Preload fault their task.
        public void FailNext(StageCallKind kind, Exception exception = null)
        {
            Queue<Exception> queue;
            if (!_faults.TryGetValue(kind, out queue))
            {
                queue = new Queue<Exception>();
                _faults.Add(kind, queue);
            }

            queue.Enqueue(exception ?? DefaultFault(kind));
        }

        public void FailNextAcquire(Exception exception = null)
        {
            FailNext(StageCallKind.Acquire, exception);
        }

        public void ReturnNullOnNextAcquire()
        {
            _nextOutcomes[StageCallKind.Acquire] = Outcome.ReturnNull;
        }

        // The next acquire returns an instance that has a host but no typed view: ResolveView says MissingView.
        public void NextAcquireReturnsNoView()
        {
            _nextOutcomes[StageCallKind.Acquire] = Outcome.NoView;
        }

        // The next acquire returns an instance with no view host: ResolveView says MissingHost.
        public void NextAcquireReturnsNoHost()
        {
            _nextOutcomes[StageCallKind.Acquire] = Outcome.NoHost;
        }

        public void ReturnNullOnNextPreload()
        {
            _nextOutcomes[StageCallKind.Preload] = Outcome.ReturnNull;
        }

        // The next lease the stage creates throws this from Dispose; set it before the preload completes.
        public void ThrowOnNextLeaseDispose(Exception exception)
        {
            _nextLeaseDisposeFault = exception ?? new InvalidOperationException("FakeStage: injected lease dispose failure.");
        }

        // Events the stage raises on its own

        // The sink the navigator gave to SetEvents; null before that.
        public IStageEvents Events
        {
            get { return _events; }
        }

        public int SetEventsCount { get; private set; }

        // Tells the navigator the instance was destroyed (true) or deactivated (false) by something else.
        public void RaiseInstanceLost(FakeInstance instance, bool destroyed)
        {
            RequireEvents();
            instance.Lost = true;
            instance.Destroyed = destroyed;
            _events.OnInstanceLost(instance, destroyed);
        }

        public void RaiseBackdropTapped(ViewLayer layer)
        {
            RequireEvents();
            _events.OnBackdropTapped(layer);
        }

        // IViewStage

        public void SetEvents(IStageEvents events)
        {
            _events = events;
            SetEventsCount++;
        }

        public UniTask<object> AcquireAsync(ViewEntry row, CancellationToken ct)
        {
            Add(new StageCall(StageCallKind.Acquire, viewId: row.Id, token: ct));
            var plan = TakePlan(StageCallKind.Acquire);

            if (AcquireMode == StageMode.Manual)
                return StartPending(_pendingAcquires, row, ct, plan, false).Source.Task;

            if (ct.IsCancellationRequested)
                return UniTask.FromCanceled<object>(ct);

            try
            {
                return UniTask.FromResult(Produce(row, plan));
            }
            catch (Exception exception)
            {
                return UniTask.FromException<object>(exception);
            }
        }

        public ViewResolution ResolveView<TArgs, TResult>(object instance, out ViewBinding<TArgs, TResult> binding)
        {
            var fake = Cast(instance);
            Add(new StageCall(StageCallKind.ResolveView, fake));
            binding = default(ViewBinding<TArgs, TResult>);
            ThrowIfFault(StageCallKind.ResolveView);

            if (!fake.HasHost)
                return ViewResolution.MissingHost;

            var view = fake.View as IView<TArgs, TResult>;
            if (view == null)
                return ViewResolution.MissingView;

            binding = new ViewBinding<TArgs, TResult>(view, fake.View as IViewVisible, fake.View as IViewBack);
            return ViewResolution.Resolved;
        }

        public void Inject(object instance)
        {
            var fake = Cast(instance);
            Add(new StageCall(StageCallKind.Inject, fake));
            ThrowIfFault(StageCallKind.Inject);
            fake.Injected = true;
        }

        public void Place(object instance, ViewLayer layer, ViewHandle handle)
        {
            var fake = Cast(instance);
            Add(new StageCall(StageCallKind.Place, fake, layer: layer));
            ThrowIfFault(StageCallKind.Place);
            fake.Placed = true;
            fake.Stashed = false;
            fake.Layer = layer;
            fake.Handle = handle;
        }

        public void Relink(object instance, ViewHandle handle)
        {
            var fake = Cast(instance);
            Add(new StageCall(StageCallKind.Relink, fake));
            ThrowIfFault(StageCallKind.Relink);
            fake.Handle = handle;
        }

        public void Apply(object instance, in ViewVisualState state)
        {
            var fake = Cast(instance);
            Add(new StageCall(StageCallKind.Apply, fake, visual: state));
            ThrowIfFault(StageCallKind.Apply);
            fake.Visual = state;
            fake.HasVisual = true;
            fake.ApplyCount++;
        }

        public void Snap(object instance, bool shown)
        {
            var fake = Cast(instance);
            Add(new StageCall(StageCallKind.Snap, fake, on: shown));
            ThrowIfFault(StageCallKind.Snap);
            fake.LastSnapShown = shown;
            fake.SnapCount++;
        }

        public UniTask PlayAsync(object instance, bool show, CancellationToken ct)
        {
            var fake = Cast(instance);
            Add(new StageCall(StageCallKind.Play, fake, on: show, token: ct));
            fake.PlayCount++;
            var plan = TakePlan(StageCallKind.Play);

            if (PlayMode == StageMode.Manual)
                return StartPending(_pendingPlays, null, ct, plan, false).Source.Task;

            if (ct.IsCancellationRequested)
                return UniTask.FromCanceled(ct);

            return plan.Fault != null ? UniTask.FromException(plan.Fault) : UniTask.CompletedTask;
        }

        public void ApplyBackdrop(ViewLayer layer, bool active, int sortingOrder)
        {
            Add(new StageCall(StageCallKind.ApplyBackdrop, on: active, order: sortingOrder, layer: layer));
            ThrowIfFault(StageCallKind.ApplyBackdrop);

            BackdropState state;
            if (!_backdrops.TryGetValue(layer.Id, out state))
            {
                state = new BackdropState();
                _backdrops.Add(layer.Id, state);
            }

            state.Active = active;
            if (active)
                state.Order = sortingOrder;
        }

        public void SetShield(bool blocking, int sortingOrder)
        {
            Add(new StageCall(StageCallKind.SetShield, on: blocking, order: sortingOrder));
            ThrowIfFault(StageCallKind.SetShield);
            ShieldBlocking = blocking;
            ShieldOrder = sortingOrder;
            if (blocking)
                ShieldRaiseCount++;
            else
                ShieldDropCount++;
        }

        public void SetLoadIndicator(bool visible)
        {
            Add(new StageCall(StageCallKind.SetLoadIndicator, on: visible));
            ThrowIfFault(StageCallKind.SetLoadIndicator);
            LoadIndicatorVisible = visible;
        }

        public void Stash(object instance)
        {
            var fake = Cast(instance);
            Add(new StageCall(StageCallKind.Stash, fake));
            ThrowIfFault(StageCallKind.Stash);
            fake.Stashed = true;
            fake.StashCount++;
        }

        public void Release(object instance)
        {
            var fake = Cast(instance);
            Add(new StageCall(StageCallKind.Release, fake));
            ThrowIfFault(StageCallKind.Release);
            fake.ReleaseCount++;
            fake.Stashed = false;
        }

        public UniTask<IDisposable> PreloadAsync(ViewEntry row, CancellationToken ct)
        {
            Add(new StageCall(StageCallKind.Preload, viewId: row.Id, token: ct));
            var plan = TakePlan(StageCallKind.Preload);

            if (PreloadMode == StageMode.Manual)
                return StartPending(_pendingPreloads, row, ct, plan, PreloadIgnoresCancellation).Source.Task;

            if (ct.IsCancellationRequested)
                return UniTask.FromCanceled<IDisposable>(ct);

            try
            {
                return UniTask.FromResult(ProduceLease(row, plan));
            }
            catch (Exception exception)
            {
                return UniTask.FromException<IDisposable>(exception);
            }
        }

        // There is no GameObject behind a fake instance.
        public bool TryGetGameObject(object instance, out GameObject gameObject)
        {
            gameObject = null;
            return false;
        }

        // Helpers

        private void Add(StageCall call)
        {
            _calls.Add(call);
        }

        private void RequireEvents()
        {
            if (_events == null)
                throw new InvalidOperationException("FakeStage: SetEvents was never called.");
        }

        private static FakeInstance Cast(object instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            var fake = instance as FakeInstance;
            if (fake == null)
                throw new ArgumentException("FakeStage only knows FakeInstance, got " + instance.GetType().Name + ".", nameof(instance));

            return fake;
        }

        private static Exception DefaultFault(StageCallKind kind)
        {
            return new InvalidOperationException("FakeStage: injected " + kind + " failure.");
        }

        private Exception TakeFault(StageCallKind kind)
        {
            Queue<Exception> queue;
            return _faults.TryGetValue(kind, out queue) && queue.Count > 0 ? queue.Dequeue() : null;
        }

        private void ThrowIfFault(StageCallKind kind)
        {
            var fault = TakeFault(kind);
            if (fault != null)
                throw fault;
        }

        private Plan TakePlan(StageCallKind kind)
        {
            Outcome outcome;
            if (_nextOutcomes.TryGetValue(kind, out outcome))
                _nextOutcomes.Remove(kind);
            else
                outcome = Outcome.Normal;

            return new Plan(outcome, TakeFault(kind));
        }

        // Makes the instance for an acquire now: it throws or returns null when the plan says so.
        private object Produce(ViewEntry row, Plan plan)
        {
            if (plan.Fault != null)
                throw plan.Fault;

            if (plan.Outcome == Outcome.ReturnNull)
                return null;

            var withView = plan.Outcome != Outcome.NoView && plan.Outcome != Outcome.NoHost;
            var view = withView ? CreateView(row.Id) : null;

            var instance = new FakeInstance(_nextInstanceId++, row.Id, view);
            instance.HasHost = plan.Outcome != Outcome.NoHost;
            _instances.Add(instance);
            return instance;
        }

        private IDisposable ProduceLease(ViewEntry row, Plan plan)
        {
            if (plan.Fault != null)
                throw plan.Fault;

            if (plan.Outcome == Outcome.ReturnNull)
                return null;

            var lease = new FakeLease(row.Id);
            lease.ThrowOnDispose = _nextLeaseDisposeFault;
            _nextLeaseDisposeFault = null;
            _leases.Add(lease);
            return lease;
        }

        private object CreateView(string viewId)
        {
            Func<object> factory;
            if (!_views.TryGetValue(viewId, out factory))
                throw new InvalidOperationException("FakeStage: no view is mapped for '" + viewId + "'; call SetView or NavigatorHarness.Map.");

            return factory();
        }

        // The token stops the task at once unless it is ignored; the entry stays pending until the test completes it.
        private static Pending<T> StartPending<T>(List<Pending<T>> list, ViewEntry row, CancellationToken ct, Plan plan, bool ignoresCancellation)
        {
            var pending = new Pending<T>(row, ct, plan, ignoresCancellation);
            list.Add(pending);

            if (ct.CanBeCanceled && !ignoresCancellation)
            {
                pending.Registration = ct.Register(state =>
                {
                    var entry = (Pending<T>)state;
                    entry.Source.TrySetCanceled(entry.Token);
                }, pending);
            }

            return pending;
        }

        private static Pending<T> Take<T>(List<Pending<T>> list, int index, string what)
        {
            if (index < 0 || index >= list.Count)
                throw new InvalidOperationException("FakeStage: no pending " + what + " at index " + index + " (" + list.Count + " pending).");

            var pending = list[index];
            list.RemoveAt(index);
            pending.Registration.Dispose();
            return pending;
        }

        private static string Join(IReadOnlyList<FakeInstance> instances)
        {
            if (instances.Count == 0)
                return "none";

            var text = new StringBuilder();
            for (var i = 0; i < instances.Count; i++)
            {
                if (i > 0)
                    text.Append(", ");
                text.Append(instances[i]).Append(" (released ").Append(instances[i].ReleaseCount).Append("x)");
            }

            return text.ToString();
        }
    }
}
