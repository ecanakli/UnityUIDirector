using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>Starts the sample: builds the services, initialises the root, opens Home and decides what an unhandled back press does.</summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        // "Initialize On Awake" is off on this root; the bootstrap initialises it in Awake.
        [SerializeField] private ViewRoot _viewRoot;
        [SerializeField] private BackInput _backInput;

        private readonly List<IServiceReceiver> _receivers = new List<IServiceReceiver>();
        private AppServices _services;
        private INavigator _navigator;

        private void Awake()
        {
            // Before the root initialises, so the root finds an EventSystem.
            SampleEventSystem.EnsureExists();

            _services = new AppServices { Settings = new SettingsStore(), Library = new ItemLibrary() };

            _viewRoot.Initialize(new ViewRootSetup
            {
                Inject = InjectServices,
                ErrorHandler = OnViewError,
                ExtraLayers = new[] { AppLayers.Toasts }
            });
            _navigator = _viewRoot.Navigator;

            // The observer ends with the root: RootToken is cancelled when the root is destroyed.
            _navigator.AddObserver(new ViewEventLog(), _navigator.RootToken);

            // No removal needed: the bootstrap and the root live and die together.
            _backInput.Unhandled.AddListener(OnBackUnhandled);
        }

        private void Start()
        {
            // Owned by the root, so Home ends only when the root does.
            _navigator.Open(AppViews.Home, _navigator.RootToken, new OpenOptions { Source = "boot" });
        }

        // Runs on every new view instance while it is still inactive.
        private void InjectServices(GameObject instance)
        {
            instance.GetComponentsInChildren(true, _receivers);
            foreach (var receiver in _receivers)
                receiver.Receive(_services);

            _receivers.Clear();
        }

        // A handler replaces the package's own log, so it logs the exception itself.
        private static void OnViewError(Exception exception, in ViewErrorContext context)
        {
            Debug.LogException(exception, context.Target);
            Debug.LogWarning($"[views] {context.Source} failed: view '{context.ViewId}', request {context.RequestId}");
        }

        private void OnBackUnhandled()
        {
            // A Unity event cannot await; the quit answer is read inside.
            ConfirmQuitAsync().Forget();
        }

        private async UniTaskVoid ConfirmQuitAsync()
        {
            var answer = await _navigator.ShowAsync(
                AppViews.Confirm,
                new ConfirmArgs("Quit", "Close the application?"),
                _navigator.RootToken,
                new OpenOptions { Source = "back.root" });

            if (!answer.HasValue || !answer.Value)
                return;

            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
