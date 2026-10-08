namespace Ecanakli.UIDirector
{
    /// <summary>Where a request is in its life.</summary>
    public enum ViewPhase
    {
        /// <summary>Accepted and waiting for its layer to reach it.</summary>
        Waiting,

        /// <summary>The instance is being acquired and prepared.</summary>
        Loading,

        /// <summary>The show transition is running.</summary>
        Opening,

        /// <summary>The view is fully open.</summary>
        Open,

        /// <summary>The hide transition is running.</summary>
        Closing,

        /// <summary>The request has ended; its handle is inert.</summary>
        Ended
    }
}
