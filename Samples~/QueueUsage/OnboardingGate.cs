using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>Lets only urgent queued requests through while the tutorial runs.</summary>
    public sealed class OnboardingGate : IViewGate
    {
        private const int UrgentPriority = 100;

        private readonly IOnboardingState _onboarding;

        public OnboardingGate(IOnboardingState onboarding) => _onboarding = onboarding;

        // Asked at enqueue and again right before presentation, so a notice queued earlier is caught too.
        public GateDecision Evaluate(in GateRequest request) =>
            _onboarding.IsRunning && request.Priority < UrgentPriority
                ? GateDecision.Deny("onboarding is running")
                : GateDecision.Allow;
    }
}
