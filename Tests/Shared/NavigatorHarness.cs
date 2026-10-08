using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // One error the navigator routed to the error handler.
    internal readonly struct RecordedError
    {
        public readonly Exception Exception;
        public readonly ViewErrorContext Context;

        public RecordedError(Exception exception, ViewErrorContext context)
        {
            Exception = exception;
            Context = context;
        }
    }

    // A ready navigator on fakes: stage, main thread, tick-driven clock, recording observer and diagnostics.
    internal sealed class NavigatorHarness : IDisposable
    {
        private readonly List<ViewCatalog> _catalogs = new List<ViewCatalog>();
        private readonly List<CancellationTokenSource> _owners = new List<CancellationTokenSource>();
        private readonly List<RecordedError> _errors = new List<RecordedError>();
        private readonly CancellationTokenSource _harnessOwner = new CancellationTokenSource();
        private bool _disposed;

        public NavigatorHarness(
            float loadTimeoutSeconds = LayerRunner.DefaultLoadTimeoutSeconds,
            float transitionTimeoutSeconds = LayerRunner.DefaultTransitionTimeoutSeconds,
            IReadOnlyList<ViewLayer> layers = null,
            int sortingBase = 0,
            float loadIndicatorDelaySeconds = global::Ecanakli.UIDirector.Navigator.DefaultLoadIndicatorDelaySeconds)
        {
            Diagnostics = new RecordingDiagnostics();
            Clock = new NavigatorClock(Diagnostics.Diagnostics);
            Stage = new FakeStage();
            MainThread = new FakeMainThread();
            Observer = new RecordingObserver();

            Navigator = new Navigator(
                Stage,
                clock: Clock,
                mainThread: MainThread,
                layers: layers,
                loadTimeoutSeconds: loadTimeoutSeconds,
                transitionTimeoutSeconds: transitionTimeoutSeconds,
                errorHandler: OnError,
                diagnostics: Diagnostics.Diagnostics,
                sortingBase: sortingBase,
                loadIndicatorDelaySeconds: loadIndicatorDelaySeconds);

            Navigator.AddObserver(Observer, Owner);
        }

        public Navigator Navigator { get; }
        public FakeStage Stage { get; }
        public FakeMainThread MainThread { get; }
        public NavigatorClock Clock { get; }
        public RecordingObserver Observer { get; }

        // Every diagnostic the navigator reported, counted by id.
        public RecordingDiagnostics Diagnostics { get; }

        // Exceptions the navigator isolated and routed to the error handler.
        public IReadOnlyList<RecordedError> Errors
        {
            get { return _errors; }
        }

        // An owner token that is never cancelled before Dispose; the observer and the rows use it too.
        public CancellationToken Owner
        {
            get { return _harnessOwner.Token; }
        }

        // A source for one request's owner; the harness cancels and disposes it, so do not dispose it yourself.
        public CancellationTokenSource NewOwner()
        {
            var owner = new CancellationTokenSource();
            _owners.Add(owner);
            return owner;
        }

        // Rows and views

        // Adds one catalog row; its view defaults to a FakeView with no arguments and no result.
        public NavigatorHarness Row(
            string id,
            ViewLayer layer = null,
            ViewRetention retention = ViewRetention.ReleaseOnClose,
            BackPolicy back = BackPolicy.LayerDefault,
            bool closeOnBackdrop = false,
            CoverPolicy cover = CoverPolicy.LayerDefault,
            bool allowMultiple = false,
            CoveredMode whenCovered = CoveredMode.DisableCanvas)
        {
            var layerId = (layer ?? ViewLayers.Screens).Id;
            var row = new ViewEntry(id, null, id, layerId, retention, back, closeOnBackdrop, cover, whenCovered, allowMultiple);

            var catalog = ViewCatalog.Create(new[] { row });
            _catalogs.Add(catalog);
            Navigator.AddCatalog(catalog, Owner);

            if (!Stage.HasView(id))
                Stage.SetView(id, () => new FakeView<NoArgs, NoResult>());

            return this;
        }

        // Makes every load of the id produce the object the factory returns.
        public NavigatorHarness Map(string id, Func<object> factory)
        {
            Stage.SetView(id, factory);
            return this;
        }

        // Makes every load of the id produce a plain FakeView with the given argument and result types.
        public NavigatorHarness Map<TArgs, TResult>(string id)
        {
            return Map(id, () => new FakeView<TArgs, TResult>());
        }

        // The view object of the newest instance loaded for the id; fails the test when there is none.
        public TView ViewOf<TView>(string id) where TView : class
        {
            var instance = Stage.LastInstanceOf(id);
            Assert.That(instance, Is.Not.Null, "No instance was acquired for '" + id + "'.\n" + Stage.DescribeCalls());

            var view = instance.View as TView;
            Assert.That(view, Is.Not.Null, "The view of '" + id + "' is not a " + typeof(TView).Name + ".");
            return view;
        }

        // Time

        // Ticks the clock in steps of at most 0.1 s, because the clock clamps each tick to that.
        public void Advance(float seconds)
        {
            var steps = (int)Math.Floor(seconds / NavigatorClock.MaxStepSeconds + 1e-4);
            for (var i = 0; i < steps; i++)
                Clock.Tick(NavigatorClock.MaxStepSeconds);

            var rest = seconds - steps * NavigatorClock.MaxStepSeconds;
            if (rest > 1e-4f)
                Clock.Tick(rest);
        }

        // Navigator state read through Describe, as the navigator exposes no counters

        public string Describe()
        {
            var text = new StringBuilder();
            Navigator.Describe(text);
            return text.ToString();
        }

        // Shield holders now; zero means the shield is down.
        public int ShieldCount
        {
            get { return Navigator.ShieldCount; }
        }

        // Requests the layer holds now, waiting, loading or on screen: the depth of a screen stack.
        public int RequestCount(ViewLayer layer)
        {
            return Navigator.RequestCount(layer);
        }

        // Teardown

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            Navigator.Teardown();

            for (var i = 0; i < _owners.Count; i++)
            {
                _owners[i].Cancel();
                _owners[i].Dispose();
            }

            _harnessOwner.Cancel();
            _harnessOwner.Dispose();

            for (var i = 0; i < _catalogs.Count; i++)
                UnityEngine.Object.DestroyImmediate(_catalogs[i]);
        }

        private void OnError(Exception exception, in ViewErrorContext context)
        {
            _errors.Add(new RecordedError(exception, context));
        }
    }
}
