using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>Optional base class for a view with arguments and a result, built only on the view interfaces.</summary>
    public abstract class ViewBehaviour<TArgs, TResult> : MonoBehaviour, IView<TArgs, TResult>, IViewVisible, IViewBack
    {
        private ViewContext<TResult> _context;

        /// <summary>The navigator of the root that opened this view; null before the first open.</summary>
        protected INavigator Navigator
        {
            get { return _context == null ? null : _context.Navigator; }
        }

        /// <summary>The scope of the current open; null before the first open.</summary>
        protected ViewScope OpenScope
        {
            get { return _context == null ? null : _context.Open; }
        }

        /// <summary>Receives the arguments of this open; set up the view and bind on the open scope.</summary>
        protected abstract void OnOpen(TArgs args, ViewScope open);

        /// <summary>The view came on screen; work started on the scope ends when it is covered or closes.</summary>
        protected virtual void OnVisible(ViewScope visible)
        {
        }

        /// <summary>Asked when Back() targets this view and its policy is HandledByView; true means handled.</summary>
        protected virtual bool OnBack()
        {
            return false;
        }

        /// <summary>Closes the view with a value; the first close cause wins.</summary>
        protected void Complete(TResult value)
        {
            if (_context != null)
                _context.Complete(value);
        }

        /// <summary>Closes the view without a value.</summary>
        protected void Close()
        {
            if (_context != null)
                _context.Close();
        }

        /// <summary>Inside OnOpen only: ends the request before anything is shown.</summary>
        protected void Decline(string reason = null)
        {
            if (_context != null)
                _context.Decline(reason);
        }

        void IView<TArgs, TResult>.OnOpen(TArgs args, ViewContext<TResult> context)
        {
            _context = context;
            OnOpen(args, context.Open);
        }

        void IViewVisible.OnVisible(ViewScope visible)
        {
            OnVisible(visible);
        }

        bool IViewBack.OnBack()
        {
            return OnBack();
        }
    }

    /// <summary>Optional base class for a view with arguments and no result.</summary>
    public abstract class ViewBehaviour<TArgs> : ViewBehaviour<TArgs, NoResult>
    {
    }

    /// <summary>Optional base class for a view with no arguments and no result.</summary>
    public abstract class ViewBehaviour : ViewBehaviour<NoArgs, NoResult>
    {
    }
}
