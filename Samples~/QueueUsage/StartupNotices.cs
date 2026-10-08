using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>Queues the startup popups by priority, behind the onboarding gate, for as long as the UI exists.</summary>
    public sealed class StartupNotices
    {
        private readonly ViewQueue _queue;
        private readonly CancellationToken _lifetime;

        public StartupNotices(INavigator navigator, IOnboardingState onboarding)
        {
            _lifetime = navigator.RootToken;
            _queue = new ViewQueue(navigator, ViewLayers.Popups, _lifetime, "startup");
            _queue.AddGate(new OnboardingGate(onboarding), _lifetime);
        }

        public ViewQueue Queue => _queue;

        public void EnqueueAll()
        {
            _queue.Enqueue(QueueViews.Notice, new NoticeArgs("What is new", "Two new guides were added."), _lifetime,
                new QueueOptions { Priority = 10, Source = "startup.news" });

            // Enqueued second but presented first: the higher priority wins.
            _queue.Enqueue(QueueViews.Notice, new NoticeArgs("Maintenance", "The service pauses at 02:00."), _lifetime,
                new QueueOptions { Priority = 100, Source = "startup.maintenance" });
        }

        public async UniTask<bool> AskForFeedbackAsync()
        {
            // Completes also when the item never shows (denied, cleared), so the answer is read only through HasValue.
            var answer = await _queue.EnqueueAsync(QueueViews.Ask,
                new AskArgs("Would you like to tell us what to improve?"), _lifetime,
                new QueueOptions { Priority = 1, Source = "startup.feedback" });

            return answer.HasValue && answer.Value;
        }
    }
}
