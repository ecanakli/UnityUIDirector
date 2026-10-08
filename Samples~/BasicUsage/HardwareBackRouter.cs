using UnityEngine;
using UnityEngine.Events;
#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>Asks an older UI first on the back key, then the navigator; put it on the root instead of BackInput, never both.</summary>
    public sealed class HardwareBackRouter : MonoBehaviour
    {
        [SerializeField] private ViewRoot _root;
        [SerializeField] private LegacyPopup _legacyPopup;
        [SerializeField] private UnityEvent _unhandled = new UnityEvent();
#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        private InputAction _backAction;
#endif

        /// <summary>Raised when neither the older UI nor the navigator handled the back press.</summary>
        public UnityEvent Unhandled
        {
            get { return _unhandled; }
        }

        public void HandleBack()
        {
            // The older UI sits above the package's layers, so it answers first.
            if (_legacyPopup.TryClose())
                return;

            if (!_root.Navigator.Back())
                _unhandled.Invoke();
        }

#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        private void OnEnable()
        {
            // An owned action binds every keyboard and keeps a press whose down and up land in one input update.
            if (_backAction == null)
                _backAction = new InputAction("Sample Back", InputActionType.Button, "<Keyboard>/escape");

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
#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            if (_backAction != null && _backAction.WasPressedThisFrame())
                HandleBack();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
                HandleBack();
#endif
        }
    }
}
