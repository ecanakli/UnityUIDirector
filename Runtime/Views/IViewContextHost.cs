namespace Ecanakli.UIDirector
{
    // What a ViewContext calls into; the request record implements it.
    internal interface IViewContextHost<TResult>
    {
        INavigator Navigator { get; }

        ViewHandle Handle { get; }

        ViewScope OpenScope { get; }

        void CompleteView(TResult value);

        void CloseView();

        void DeclineOpen(string reason);
    }
}
