namespace Ecanakli.UIDirector
{
    // Why the stage could not hand a typed view to the logic.
    internal enum ViewResolution
    {
        Resolved,
        MissingHost,
        MissingView
    }

    // The view object on an instance for one key, with its optional hooks (null when absent).
    internal readonly struct ViewBinding<TArgs, TResult>
    {
        public readonly IView<TArgs, TResult> View;
        public readonly IViewVisible Visible;
        public readonly IViewBack Back;

        public ViewBinding(IView<TArgs, TResult> view, IViewVisible visible, IViewBack back)
        {
            View = view;
            Visible = visible;
            Back = back;
        }
    }
}
