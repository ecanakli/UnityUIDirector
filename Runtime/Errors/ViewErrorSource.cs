namespace Ecanakli.UIDirector
{
    /// <summary>Which kind of user callback or package step an error came from.</summary>
    public enum ViewErrorSource
    {
        /// <summary>A hook on the view component threw.</summary>
        ViewHook,

        /// <summary>A scope cleanup threw while a scope ended.</summary>
        ScopeCleanup,

        /// <summary>A task started with a scope threw.</summary>
        ScopeTask,

        /// <summary>A show or hide transition threw.</summary>
        Transition,

        /// <summary>The asset provider threw, a preload failed, or binding or placing a view instance threw.</summary>
        Provider,

        /// <summary>The root's inject delegate (ViewRootSetup.Inject) threw on a new view instance; the request ends as not shown with LoadFailed.</summary>
        Inject,

        /// <summary>An observer threw while an event was dispatched.</summary>
        Observer,

        /// <summary>A queue gate threw.</summary>
        Gate
    }
}
