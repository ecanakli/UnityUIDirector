using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>A flow that needs the screen for itself: it holds the queue and shows its tips directly.</summary>
    public sealed class TutorialFlow
    {
        private static readonly string[] Tips =
        {
            "Queued notices wait while this tutorial runs.",
            "Notices that are not urgent are turned away until it ends.",
            "Close this tip and the waiting notices follow."
        };

        private readonly INavigator _navigator;
        private readonly ViewQueue _queue;
        private readonly OnboardingState _onboarding;

        public TutorialFlow(INavigator navigator, ViewQueue queue, OnboardingState onboarding)
        {
            _navigator = navigator;
            _queue = queue;
            _onboarding = onboarding;
        }

        public bool IsRunning { get; private set; }

        public async UniTask RunAsync(CancellationToken ct)
        {
            if (IsRunning)
                return;

            IsRunning = true;
            _onboarding.IsRunning = true;

            // Queued items wait; the flow's own opens below are not affected by the hold.
            var hold = _queue.Hold("tutorial", ct);
            try
            {
                for (var step = 1; step <= Tips.Length; step++)
                {
                    var result = await _navigator.ShowAsync(QueueViews.TutorialTip,
                        new TipArgs(Tips[step - 1], step, Tips.Length), ct,
                        new OpenOptions { Source = "tutorial" });

                    // Not shown, backed out, replaced or owner ended: the player left the tutorial.
                    if (result.Reason != CloseReason.Dismissed && result.Reason != CloseReason.Completed)
                        return;
                }
            }
            finally
            {
                // Ending the hold lets the queue continue; while the tutorial ran, the gate denied notices below priority 100.
                hold.Remove();
                _onboarding.IsRunning = false;
                IsRunning = false;
            }
        }
    }
}
