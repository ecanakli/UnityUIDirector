namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>Tells the gate whether the tutorial is running; the sample's implementation is <c>OnboardingState</c> in Support.</summary>
    public interface IOnboardingState
    {
        bool IsRunning { get; }
    }

    /// <summary>What the lobby's buttons drive, handed over by the bootstrap.</summary>
    public sealed class QueueServices
    {
        public StartupNotices Notices;
        public TutorialFlow Tutorial;
        public NewsFeature News;
    }

    /// <summary>A view that needs the services implements this; the root's inject seam calls it on every new instance.</summary>
    public interface IQueueServiceReceiver
    {
        void Receive(QueueServices services);
    }
}
