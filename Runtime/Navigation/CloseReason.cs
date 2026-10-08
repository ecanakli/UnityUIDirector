namespace Ecanakli.UIDirector
{
    /// <summary>How a shown view closed, or NotShown.</summary>
    public enum CloseReason
    {
        /// <summary>The request ended before the view was shown.</summary>
        NotShown,

        /// <summary>The view closed itself with a value.</summary>
        Completed,

        /// <summary>The view closed itself without a value.</summary>
        Dismissed,

        /// <summary>Back() closed the view.</summary>
        Back,

        /// <summary>A tap on the backdrop closed the view.</summary>
        Backdrop,

        /// <summary>Code closed the view through a handle, CloseTo or CloseAll.</summary>
        Code,

        /// <summary>Navigation replaced the view: a screen change closed it, or it was reopened in place.</summary>
        Navigated,

        /// <summary>The owner token of the request ended.</summary>
        OwnerEnded,

        /// <summary>The instance was destroyed or deactivated outside the package, or the root was destroyed.</summary>
        Destroyed
    }
}
