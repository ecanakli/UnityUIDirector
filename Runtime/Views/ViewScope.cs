using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Events;

namespace Ecanakli.UIDirector
{
    /// <summary>Owns bindings and tasks for one open or one visible span; the package ends it.</summary>
    public sealed class ViewScope
    {
        private readonly ErrorRouter _errors;
        private readonly string _viewId;
        private readonly int _requestId;
        private CancellationTokenSource _tokenSource;
        private List<ScopeEntry> _entries;
        private bool _ended;

        internal ViewScope(ErrorRouter errors, string viewId, int requestId)
        {
            _errors = errors;
            _viewId = viewId;
            _requestId = requestId;
        }

        /// <summary>Cancelled when the scope ends; valid, and already cancelled, if read after the end.</summary>
        public CancellationToken Token
        {
            get
            {
                if (_tokenSource != null)
                    return _tokenSource.Token;

                // A token read after the end is a plain cancelled token; before the end the source is created lazily.
                if (_ended)
                    return new CancellationToken(true);

                _tokenSource = new CancellationTokenSource();
                return _tokenSource.Token;
            }
        }

        /// <summary>True once the scope has ended; a stale reference to an ended scope is inert.</summary>
        public bool IsEnded
        {
            get { return _ended; }
        }

        // The instance named in routed errors; set by the record once it has one.
        internal UnityEngine.Object Target { get; set; }

        /// <summary>Adds the handler to the event now and removes it when the scope ends; a duplicate pair is ignored.</summary>
        public void Bind(UnityEvent source, UnityAction handler)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_ended || IsBound(source, handler))
                return;

            source.AddListener(handler);
            AddEntry(new EventBinding(source, handler));
        }

        /// <summary>Adds the handler to the typed event now and removes it when the scope ends; a duplicate pair is ignored.</summary>
        public void Bind<T>(UnityEvent<T> source, UnityAction<T> handler)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_ended || IsBound(source, handler))
                return;

            source.AddListener(handler);
            AddEntry(new EventBinding<T>(source, handler));
        }

        /// <summary>Subscribes the handler through the given add and remove delegates; the remove runs when the scope ends.</summary>
        public void Subscribe(Action<Action> add, Action<Action> remove, Action handler)
        {
            if (add == null)
                throw new ArgumentNullException(nameof(add));
            if (remove == null)
                throw new ArgumentNullException(nameof(remove));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_ended)
                return;

            add(handler);
            AddEntry(new Subscription(remove, handler));
        }

        /// <summary>Subscribes the typed handler through the given add and remove delegates; the remove runs when the scope ends.</summary>
        public void Subscribe<T>(Action<Action<T>> add, Action<Action<T>> remove, Action<T> handler)
        {
            if (add == null)
                throw new ArgumentNullException(nameof(add));
            if (remove == null)
                throw new ArgumentNullException(nameof(remove));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_ended)
                return;

            add(handler);
            AddEntry(new Subscription<T>(remove, handler));
        }

        /// <summary>Disposes the object when the scope ends; an ended scope disposes it at once.</summary>
        public void Add(IDisposable disposable)
        {
            if (disposable == null)
                throw new ArgumentNullException(nameof(disposable));

            var entry = new DisposableEntry(disposable);
            if (_ended)
                RunCleanup(entry);
            else
                AddEntry(entry);
        }

        /// <summary>Runs the action when the scope ends; an ended scope runs it at once.</summary>
        public void OnEnd(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var entry = new ActionEntry(action);
            if (_ended)
                RunCleanup(entry);
            else
                AddEntry(entry);
        }

        /// <summary>Runs the action with the state when the scope ends, without allocating a closure.</summary>
        public void OnEnd<TState>(TState state, Action<TState> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var entry = new StateActionEntry<TState>(state, action);
            if (_ended)
                RunCleanup(entry);
            else
                AddEntry(entry);
        }

        /// <summary>Starts the work with the scope token; cancellation is swallowed and other errors are routed.</summary>
        public void Run(Func<CancellationToken, UniTask> work)
        {
            if (work == null)
                throw new ArgumentNullException(nameof(work));
            if (_ended)
                return;

            RunWork(work).Forget();
        }

        /// <summary>Starts the work with a state and the scope token; cancellation is swallowed and other errors are routed.</summary>
        public void Run<TState>(TState state, Func<TState, CancellationToken, UniTask> work)
        {
            if (work == null)
                throw new ArgumentNullException(nameof(work));
            if (_ended)
                return;

            RunWork(state, work).Forget();
        }

        // Ends the scope exactly once: the token first, then the entries newest first. Never throws.
        internal void End()
        {
            if (_ended)
                return;

            _ended = true;
            CancelToken();

            var entries = _entries;
            _entries = null;
            if (entries == null)
                return;

            for (var i = entries.Count - 1; i >= 0; i--)
                RunCleanup(entries[i]);
        }

        private async UniTaskVoid RunWork(Func<CancellationToken, UniTask> work)
        {
            try
            {
                await work(Token);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the normal end of scope work.
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.ScopeTask, _viewId, _requestId, Target);
            }
        }

        private async UniTaskVoid RunWork<TState>(TState state, Func<TState, CancellationToken, UniTask> work)
        {
            try
            {
                await work(state, Token);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the normal end of scope work.
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.ScopeTask, _viewId, _requestId, Target);
            }
        }

        private void CancelToken()
        {
            var source = _tokenSource;
            if (source == null)
                return;

            try
            {
                source.Cancel();
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.ScopeCleanup, _viewId, _requestId, Target);
            }
        }

        private void RunCleanup(ScopeEntry entry)
        {
            try
            {
                entry.Cleanup();
            }
            catch (Exception exception)
            {
                _errors.Route(exception, ViewErrorSource.ScopeCleanup, _viewId, _requestId, Target);
            }
        }

        private bool IsBound(object source, Delegate handler)
        {
            if (_entries == null)
                return false;

            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].IsBinding(source, handler))
                    return true;
            }

            return false;
        }

        private void AddEntry(ScopeEntry entry)
        {
            if (_entries == null)
                _entries = new List<ScopeEntry>();

            _entries.Add(entry);
        }

        private abstract class ScopeEntry
        {
            public abstract void Cleanup();

            public virtual bool IsBinding(object source, Delegate handler)
            {
                return false;
            }
        }

        private sealed class EventBinding : ScopeEntry
        {
            private readonly UnityEvent _source;
            private readonly UnityAction _handler;

            public EventBinding(UnityEvent source, UnityAction handler)
            {
                _source = source;
                _handler = handler;
            }

            public override void Cleanup()
            {
                _source.RemoveListener(_handler);
            }

            public override bool IsBinding(object source, Delegate handler)
            {
                return ReferenceEquals(_source, source) && _handler.Equals(handler);
            }
        }

        private sealed class EventBinding<T> : ScopeEntry
        {
            private readonly UnityEvent<T> _source;
            private readonly UnityAction<T> _handler;

            public EventBinding(UnityEvent<T> source, UnityAction<T> handler)
            {
                _source = source;
                _handler = handler;
            }

            public override void Cleanup()
            {
                _source.RemoveListener(_handler);
            }

            public override bool IsBinding(object source, Delegate handler)
            {
                return ReferenceEquals(_source, source) && _handler.Equals(handler);
            }
        }

        private sealed class Subscription : ScopeEntry
        {
            private readonly Action<Action> _remove;
            private readonly Action _handler;

            public Subscription(Action<Action> remove, Action handler)
            {
                _remove = remove;
                _handler = handler;
            }

            public override void Cleanup()
            {
                _remove(_handler);
            }
        }

        private sealed class Subscription<T> : ScopeEntry
        {
            private readonly Action<Action<T>> _remove;
            private readonly Action<T> _handler;

            public Subscription(Action<Action<T>> remove, Action<T> handler)
            {
                _remove = remove;
                _handler = handler;
            }

            public override void Cleanup()
            {
                _remove(_handler);
            }
        }

        private sealed class DisposableEntry : ScopeEntry
        {
            private readonly IDisposable _disposable;

            public DisposableEntry(IDisposable disposable)
            {
                _disposable = disposable;
            }

            public override void Cleanup()
            {
                _disposable.Dispose();
            }
        }

        private sealed class ActionEntry : ScopeEntry
        {
            private readonly Action _action;

            public ActionEntry(Action action)
            {
                _action = action;
            }

            public override void Cleanup()
            {
                _action();
            }
        }

        private sealed class StateActionEntry<TState> : ScopeEntry
        {
            private readonly TState _state;
            private readonly Action<TState> _action;

            public StateActionEntry(TState state, Action<TState> action)
            {
                _state = state;
                _action = action;
            }

            public override void Cleanup()
            {
                _action(_state);
            }
        }
    }
}
