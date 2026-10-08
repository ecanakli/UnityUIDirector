using System;

namespace Ecanakli.UIDirector
{
    /// <summary>Rules a layer applies to the views opened on it.</summary>
    [Flags]
    public enum ViewLayerRules
    {
        /// <summary>No rules: a plain layer that shields nothing and takes no back.</summary>
        None = 0,

        /// <summary>The list is a stack and is the back history; reopening an id in the stack returns to it.</summary>
        History = 1,

        /// <summary>The layer has a backdrop under its top view and everything below that view is non-interactive.</summary>
        Modal = 2,

        /// <summary>Back may close the top view of the layer.</summary>
        TakesBack = 4,

        /// <summary>Opens and closes on this layer hold the input shield.</summary>
        ShieldsInput = 8,

        /// <summary>Views on this layer accepted before a view that opens on a history layer close when it opens; pops, Back and CloseTo leave them.</summary>
        ClosedByNavigation = 16
    }
}
