using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // A view with the open hook only; the test drives it through Context. Use the subclasses for the optional hooks.
    internal class FakeView<TArgs, TResult> : IView<TArgs, TResult>
    {
        private readonly List<TArgs> _openArgs = new List<TArgs>();
        private readonly List<ViewScope> _visibleScopes = new List<ViewScope>();
        private readonly List<string> _calls = new List<string>();

        // Behavior, set before the view opens

        // Thrown from OnOpen after the bind and run actions ran.
        public Exception ThrowOnOpen { get; set; }

        // Called inside OnOpen with the open scope, to bind something on it.
        public Action<ViewScope> BindOnOpen { get; set; }

        // Called inside OnOpen with the context, for example to Decline or Complete.
        public Action<ViewContext<TResult>> RunOnOpen { get; set; }

        // Thrown from OnVisible; only the hook subclasses have the hook.
        public Exception ThrowOnVisible { get; set; }

        // Thrown from OnBack; only the hook subclasses have the hook.
        public Exception ThrowOnBack { get; set; }

        // What OnBack answers: true means the view handled back itself.
        public bool BackResult { get; set; }

        // What happened

        public int OpenCount
        {
            get { return _openArgs.Count; }
        }

        public IReadOnlyList<TArgs> OpenArgs
        {
            get { return _openArgs; }
        }

        public TArgs LastArgs
        {
            get { return _openArgs.Count > 0 ? _openArgs[_openArgs.Count - 1] : default(TArgs); }
        }

        // The context of the newest open; null until the first open.
        public ViewContext<TResult> Context { get; private set; }

        public int VisibleCount
        {
            get { return _visibleScopes.Count; }
        }

        public IReadOnlyList<ViewScope> VisibleScopes
        {
            get { return _visibleScopes; }
        }

        public ViewScope LastVisible
        {
            get { return _visibleScopes.Count > 0 ? _visibleScopes[_visibleScopes.Count - 1] : null; }
        }

        public int BackCount { get; private set; }

        // The hook calls in order: OnOpen, OnVisible, OnBack.
        public IReadOnlyList<string> Calls
        {
            get { return _calls; }
        }

        public virtual void OnOpen(TArgs args, ViewContext<TResult> context)
        {
            _calls.Add("OnOpen");
            _openArgs.Add(args);
            Context = context;

            if (BindOnOpen != null)
                BindOnOpen(context.Open);

            if (RunOnOpen != null)
                RunOnOpen(context);

            if (ThrowOnOpen != null)
                throw ThrowOnOpen;
        }

        // Drive the view the way its own code would

        public void Complete(TResult value)
        {
            RequireContext().Complete(value);
        }

        public void Close()
        {
            RequireContext().Close();
        }

        public void Decline(string reason = null)
        {
            RequireContext().Decline(reason);
        }

        protected void RecordVisible(ViewScope visible)
        {
            _calls.Add("OnVisible");
            _visibleScopes.Add(visible);

            if (ThrowOnVisible != null)
                throw ThrowOnVisible;
        }

        protected bool RecordBack()
        {
            _calls.Add("OnBack");
            BackCount++;

            if (ThrowOnBack != null)
                throw ThrowOnBack;

            return BackResult;
        }

        private ViewContext<TResult> RequireContext()
        {
            Assert.That(Context, Is.Not.Null, "The view was never opened, so it has no context.");
            return Context;
        }
    }

    // A view that also has the visible hook.
    internal class FakeVisibleView<TArgs, TResult> : FakeView<TArgs, TResult>, IViewVisible
    {
        public void OnVisible(ViewScope visible)
        {
            RecordVisible(visible);
        }
    }

    // A view that also has the back hook.
    internal class FakeBackView<TArgs, TResult> : FakeView<TArgs, TResult>, IViewBack
    {
        public bool OnBack()
        {
            return RecordBack();
        }
    }

    // A view with both optional hooks.
    internal class FakeFullView<TArgs, TResult> : FakeView<TArgs, TResult>, IViewVisible, IViewBack
    {
        public void OnVisible(ViewScope visible)
        {
            RecordVisible(visible);
        }

        public bool OnBack()
        {
            return RecordBack();
        }
    }
}
