namespace Ecanakli.UIDirector
{
    // The derived state of one view as the stage must show it; computed by Projection, applied by the stage.
    internal readonly struct ViewVisualState
    {
        // Every canvas under the view is off (or the view is inactive, per CoveredMode).
        public readonly bool Hidden;

        // Every raycaster under the view is on; false means every one is off.
        public readonly bool Interactive;

        // The sorting order of the view's own canvas; the view owns +1 to +4 above it.
        public readonly int SortingOrder;

        // How Hidden is realised for this view.
        public readonly CoveredMode CoveredMode;

        public ViewVisualState(bool hidden, bool interactive, int sortingOrder, CoveredMode coveredMode)
        {
            Hidden = hidden;
            Interactive = interactive;
            SortingOrder = sortingOrder;
            CoveredMode = coveredMode;
        }

        public bool SameAs(in ViewVisualState other)
        {
            return Hidden == other.Hidden
                && Interactive == other.Interactive
                && SortingOrder == other.SortingOrder
                && CoveredMode == other.CoveredMode;
        }
    }
}
