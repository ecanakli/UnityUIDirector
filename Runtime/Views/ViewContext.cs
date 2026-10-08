namespace Ecanakli.UIDirector
{
    /// <summary>What one open hands to its view.</summary>
    public sealed class ViewContext<TResult>
    {
        private readonly IViewContextHost<TResult> _host;

        internal ViewContext(IViewContextHost<TResult> host)
        {
            _host = host;
        }

        /// <summary>The navigator of the root that opened this view.</summary>
        public INavigator Navigator
        {
            get { return _host.Navigator; }
        }

        /// <summary>The handle of this request.</summary>
        public ViewHandle Handle
        {
            get { return _host.Handle; }
        }

        /// <summary>The scope that lives from the open hook to the close; bindings on it end with the open.</summary>
        public ViewScope Open
        {
            get { return _host.OpenScope; }
        }

        /// <summary>Closes the view with a value; the first close cause wins.</summary>
        public void Complete(TResult value)
        {
            _host.CompleteView(value);
        }

        /// <summary>Closes the view without a value.</summary>
        public void Close()
        {
            _host.CloseView();
        }

        /// <summary>Inside OnOpen only: ends the request before anything is shown.</summary>
        public void Decline(string reason = null)
        {
            _host.DeclineOpen(reason);
        }
    }
}
