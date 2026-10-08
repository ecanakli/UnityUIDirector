using System.Collections.Generic;
using Ecanakli.UIDirector;
using UnityEngine;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>Starts the sample: initialises the root, builds the services, opens the lobby and queues the startup notices.</summary>
    public sealed class QueueBootstrap : MonoBehaviour
    {
        [SerializeField] private ViewRoot _viewRoot; // Initialize On Awake is off on this root
        [SerializeField] private ViewCatalog _newsCatalog; // not on the root; the news feature adds it

        private readonly List<IQueueServiceReceiver> _receivers = new List<IQueueServiceReceiver>();
        private INavigator _navigator;
        private QueueServices _services;

        private void Awake()
        {
            SampleEventSystem.EnsureExists();
            _viewRoot.Initialize(new ViewRootSetup { Inject = InjectServices });
            _navigator = _viewRoot.Navigator;
        }

        private void Start()
        {
            var onboarding = new OnboardingState();
            var notices = new StartupNotices(_navigator, onboarding);
            var tutorial = new TutorialFlow(_navigator, notices.Queue, onboarding);
            var news = new NewsFeature(_navigator, _newsCatalog, notices.Queue);

            _services = new QueueServices { Notices = notices, Tutorial = tutorial, News = news };

            // Events of a queued request come from two publishers, so a log watches each.
            _navigator.AddObserver(new QueueEventLog("[views]"), _navigator.RootToken);
            notices.Queue.AddObserver(new QueueEventLog("[queue]"), _navigator.RootToken);

            // The services exist before the lobby opens: the inject seam runs when its instance is created.
            _navigator.Open(QueueViews.Lobby, _navigator.RootToken, new OpenOptions { Source = "boot" });

            // The notices wait until the lobby is shown, then present by priority.
            notices.EnqueueAll();
        }

        // Runs on every new view instance while it is still inactive.
        private void InjectServices(GameObject instance)
        {
            instance.GetComponentsInChildren(true, _receivers);
            foreach (var receiver in _receivers)
                receiver.Receive(_services);

            _receivers.Clear();
        }
    }
}
