using System;
using UnityEngine;
using UnityEngine.Events;
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ecanakli.UIDirector
{
    /// <summary>Optional: polls the back key (Escape, the Android back button) and calls Back() on its root's navigator; raises Unhandled when nothing handled it.</summary>
    [DisallowMultipleComponent]
    public sealed class BackInput : MonoBehaviour
    {
        [SerializeField, Tooltip("Root whose navigator receives Back(); empty uses a ViewRoot on this object or its parents.")]
        private ViewRoot _root;

        [SerializeField, Tooltip("Raised when a back press was not handled by any view; the game decides, for example by asking to quit.")]
        private UnityEvent _unhandled = new UnityEvent();

        private ViewRoot _resolvedRoot;
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        private InputAction _backAction;
#endif

        /// <summary>Raised when Back() returns false, so that the game decides what a back press means.</summary>
        public UnityEvent Unhandled
        {
            get { return _unhandled; }
        }

        // Test seam: one back press.
        internal void HandleBack()
        {
            var root = ResolveRoot();
            if (root == null)
            {
                Debug.LogWarning("BackInput on '" + name + "' found no ViewRoot; assign one or place it on or under a root.", this);
                InvokeUnhandled();
                return;
            }

            // Reading Navigator here would initialise the root with defaults before the bootstrap does.
            if (!root.IsInitialized)
                return;

            bool handled;
            try
            {
                handled = root.Navigator.Back();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return;
            }

            if (!handled)
                InvokeUnhandled();
        }

#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        private void OnEnable()
        {
            // Binds every keyboard, including ones added later; a down and an up in one input update still count.
            if (_backAction == null)
                _backAction = new InputAction("UIDirector Back", InputActionType.Button, "<Keyboard>/escape");

            _backAction.Enable();
        }

        private void OnDisable()
        {
            if (_backAction != null)
                _backAction.Disable();
        }

        private void OnDestroy()
        {
            if (_backAction != null)
            {
                _backAction.Dispose();
                _backAction = null;
            }
        }
#endif

        private void Update()
        {
            // 0 B per frame.
#if ECANAKLI_UIDIRECTOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            if (_backAction != null && _backAction.WasPressedThisFrame())
                HandleBack();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
                HandleBack();
#endif
        }

        // Resolved on first use and again after the resolved root was destroyed.
        private ViewRoot ResolveRoot()
        {
            if (_resolvedRoot != null)
                return _resolvedRoot;

            if (_root != null)
                _resolvedRoot = _root;
            else if (TryGetComponent(out ViewRoot own))
                _resolvedRoot = own;
            else
                _resolvedRoot = GetComponentInParent<ViewRoot>();

            return _resolvedRoot;
        }

        private void InvokeUnhandled()
        {
            if (_unhandled == null)
                return;

            try
            {
                _unhandled.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }
}
