using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector.Tests
{
    // A view with every hook and a visible callback; hooks of FakeView stay available.
    internal sealed class ContractView : FakeView<NoArgs, NoResult>, IViewVisible, IViewBack
    {
        // Called inside OnVisible after the call was recorded.
        public Action<ViewScope> OnVisibleHook { get; set; }

        public void OnVisible(ViewScope visible)
        {
            RecordVisible(visible);

            if (OnVisibleHook != null)
                OnVisibleHook(visible);
        }

        public bool OnBack()
        {
            return RecordBack();
        }
    }

    // An observer that runs a callback for every event.
    internal sealed class ContractActionObserver : IViewObserver
    {
        private readonly Action<ViewEvent> _onEvent;

        public ContractActionObserver(Action<ViewEvent> onEvent)
        {
            _onEvent = onEvent;
        }

        public void OnViewEvent(in ViewEvent viewEvent)
        {
            _onEvent(viewEvent);
        }
    }

    // A disposable that counts and can run a callback, for example to throw.
    internal sealed class ContractDisposable : IDisposable
    {
        private readonly Action _onDispose;

        public ContractDisposable(Action onDispose = null)
        {
            _onDispose = onDispose;
        }

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;

            if (_onDispose != null)
                _onDispose();
        }
    }

    // Follows the await protocol on a task and counts the resumes; nothing here waits for a tick.
    internal sealed class ContractAwaitProbe<T>
    {
        private readonly Action _onResume;
        private UniTask<T>.Awaiter _awaiter;

        public ContractAwaitProbe(UniTask<T> task, Action onResume)
        {
            _onResume = onResume;
            _awaiter = task.GetAwaiter();

            if (_awaiter.IsCompleted)
                Resume();
            else
                _awaiter.UnsafeOnCompleted(Resume);
        }

        // How many times the awaiting code resumed; a correct awaiter resumes exactly once.
        public int ResumeCount { get; private set; }

        public bool IsPending
        {
            get { return ResumeCount == 0; }
        }

        public T Result { get; private set; }

        // The exception GetResult threw, such as a cancellation; null for a normal completion.
        public Exception Error { get; private set; }

        public bool IsCanceled
        {
            get { return Error is OperationCanceledException; }
        }

        private void Resume()
        {
            ResumeCount++;

            try
            {
                Result = _awaiter.GetResult();
            }
            catch (Exception exception)
            {
                Error = exception;
            }

            if (_onResume != null)
                _onResume();
        }
    }

    internal static class ContractAwait
    {
        // Starts watching the task; the callback runs inside the resume, after the result is read.
        public static ContractAwaitProbe<T> Watch<T>(UniTask<T> task, Action onResume = null)
        {
            return new ContractAwaitProbe<T>(task, onResume);
        }
    }

    // Short forms of the calls the contract tests make on a view without arguments or result.
    internal static class ContractNavigatorExtensions
    {
        public static UniTask<ViewResult<NoResult>> Show(
            this INavigator navigator,
            string id,
            CancellationToken owner,
            OpenOptions options = default)
        {
            return navigator.ShowAsync(ViewKey.Create(id), default(NoArgs), owner, options);
        }

        public static ViewHandle OpenView(
            this INavigator navigator,
            string id,
            CancellationToken owner,
            OpenOptions options = default)
        {
            return navigator.Open(ViewKey.Create(id), default(NoArgs), owner, options);
        }
    }

    // A scope on its own error router, so scope rules are tested without a navigator.
    internal sealed class ContractScopeRig
    {
        private readonly ErrorRouter _router;
        private readonly List<RecordedError> _errors = new List<RecordedError>();
        private readonly bool _handlerThrows;

        public ContractScopeRig(bool handlerThrows = false)
        {
            _handlerThrows = handlerThrows;
            Diagnostics = new RecordingDiagnostics();
            _router = new ErrorRouter(OnError, Diagnostics.Diagnostics);
        }

        public RecordingDiagnostics Diagnostics { get; }

        public IReadOnlyList<RecordedError> Errors
        {
            get { return _errors; }
        }

        public ViewScope NewScope(string viewId = "scope-view", int requestId = 7)
        {
            return new ViewScope(_router, viewId, requestId);
        }

        private void OnError(Exception exception, in ViewErrorContext context)
        {
            _errors.Add(new RecordedError(exception, context));

            if (_handlerThrows)
                throw new InvalidOperationException("The error handler failed.");
        }
    }

    // Catalog rows and catalogs built by hand, for tests that need a registration handle.
    internal static class ContractRows
    {
        public static ViewEntry Entry(string id, string layerId, ViewRetention retention = ViewRetention.ReleaseOnClose)
        {
            return new ViewEntry(
                id,
                null,
                id,
                layerId,
                retention,
                BackPolicy.LayerDefault,
                false,
                CoverPolicy.LayerDefault,
                CoveredMode.DisableCanvas,
                false);
        }

        public static ViewCatalog Catalog(params ViewEntry[] entries)
        {
            return ViewCatalog.Create(entries);
        }
    }
}
