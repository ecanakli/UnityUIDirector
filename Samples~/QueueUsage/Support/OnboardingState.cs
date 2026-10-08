namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>The sample's onboarding flag; the tutorial sets it and the gate reads it.</summary>
    public sealed class OnboardingState : IOnboardingState
    {
        public bool IsRunning { get; set; }
    }
}
