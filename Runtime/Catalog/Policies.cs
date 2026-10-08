namespace Ecanakli.UIDirector
{
    /// <summary>What happens to the instance when its view closes.</summary>
    public enum ViewRetention
    {
        /// <summary>The instance is released when the view closes.</summary>
        ReleaseOnClose,

        /// <summary>The instance is kept hidden and reused by the next open of the same id.</summary>
        KeepInstance
    }

    /// <summary>What Back() does when this view is the back target.</summary>
    public enum BackPolicy
    {
        /// <summary>Follow the layer: close on a layer that takes back.</summary>
        LayerDefault,

        /// <summary>Close the view with reason Back on a layer that takes back; the bottom view of a history layer stays and Back() returns false.</summary>
        Close,

        /// <summary>Swallow the back request and keep the view open.</summary>
        Ignore,

        /// <summary>Ask the view through its back hook; when the hook declines, follow the layer as LayerDefault does.</summary>
        HandledByView
    }

    /// <summary>Whether the view hides what is below it once it is fully open.</summary>
    public enum CoverPolicy
    {
        /// <summary>Follow the layer: a history layer covers, other layers do not.</summary>
        LayerDefault,

        /// <summary>Everything below this view is hidden once it is fully open.</summary>
        Covers,

        /// <summary>Views below stay visible.</summary>
        Transparent
    }

    /// <summary>How the view is hidden while it is covered.</summary>
    public enum CoveredMode
    {
        /// <summary>Every canvas under the view is disabled.</summary>
        DisableCanvas,

        /// <summary>The view object is deactivated.</summary>
        Deactivate
    }
}
