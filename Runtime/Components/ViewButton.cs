using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Ecanakli.UIDirector
{
    /// <summary>What a ViewButton does when it is clicked.</summary>
    public enum ViewButtonAction
    {
        /// <summary>Closes the view the button belongs to, as the user dismissing it.</summary>
        CloseView,

        /// <summary>Calls Back() on the navigator of the root above the button.</summary>
        Back,

        /// <summary>Opens the view with the given id, without arguments and without a result, owned by the root.</summary>
        OpenView
    }

    /// <summary>Inspector entry point on a Button: closes its view, calls Back(), or opens a view without arguments.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class ViewButton : MonoBehaviour
    {
        [SerializeField, Tooltip("What a click does: close this button's view, call Back(), or open the view below.")]
        private ViewButtonAction _action;

        [SerializeField, Tooltip("View id opened by OpenView, as written in the catalog; the view takes no arguments and returns no value.")]
        private string _viewId;

        private Button _button;
        private UnityAction _onClick;
        private ViewRoot _root;

        // Test seam: one click.
        internal void Click()
        {
            OnClick();
        }

        private void Awake()
        {
            _onClick = OnClick;
            if (TryGetComponent(out _button))
                _button.onClick.AddListener(_onClick);
        }

        private void OnDestroy()
        {
            if (_button != null && _onClick != null)
                _button.onClick.RemoveListener(_onClick);
        }

        // Never throws: a click handler that throws would break the button's other listeners.
        private void OnClick()
        {
            try
            {
                switch (_action)
                {
                    case ViewButtonAction.CloseView:
                        CloseView();
                        break;
                    case ViewButtonAction.Back:
                        Back();
                        break;
                    case ViewButtonAction.OpenView:
                        OpenView();
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void CloseView()
        {
            var host = GetComponentInParent<ViewHost>();
            if (host == null)
            {
                Debug.LogWarning("ViewButton on '" + name + "' found no ViewHost above it; nothing to close.", this);
                return;
            }

            host.Close();
        }

        private void Back()
        {
            var navigator = ResolveNavigator();
            if (navigator != null)
                navigator.Back();
        }

        private void OpenView()
        {
            if (string.IsNullOrWhiteSpace(_viewId))
            {
                Debug.LogWarning("ViewButton on '" + name + "' has no view id; nothing opens.", this);
                return;
            }

            var navigator = ResolveNavigator();
            if (navigator == null)
                return;

            navigator.Open(new ViewKey<NoArgs, NoResult>(_viewId), default(NoArgs), navigator.RootToken);
        }

        // Looked up on the first click and again after the root was destroyed.
        private INavigator ResolveNavigator()
        {
            if (_root == null)
                _root = GetComponentInParent<ViewRoot>();

            if (_root == null)
            {
                Debug.LogWarning("ViewButton on '" + name + "' found no ViewRoot above it.", this);
                return null;
            }

            // Reading Navigator here would initialise the root with defaults before the bootstrap does.
            if (!_root.IsInitialized)
            {
                Debug.LogWarning("ViewButton on '" + name + "' was clicked before its ViewRoot was initialised; nothing happens.", this);
                return null;
            }

            return _root.Navigator;
        }
    }
}
