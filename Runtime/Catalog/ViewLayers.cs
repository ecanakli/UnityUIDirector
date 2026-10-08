namespace Ecanakli.UIDirector
{
    /// <summary>The three layers every root has.</summary>
    public static class ViewLayers
    {
        /// <summary>Full screens: a history stack with back, shielded while it changes.</summary>
        public static readonly ViewLayer Screens = new ViewLayer(
            "screens", 0,
            ViewLayerRules.History | ViewLayerRules.TakesBack | ViewLayerRules.ShieldsInput);

        /// <summary>Modal popups: a shared backdrop, back closes the top one, closed when the screen changes.</summary>
        public static readonly ViewLayer Popups = new ViewLayer(
            "popups", 1,
            ViewLayerRules.Modal | ViewLayerRules.TakesBack | ViewLayerRules.ShieldsInput | ViewLayerRules.ClosedByNavigation);

        /// <summary>Blocking overlays above everything: modal, shielded, and back is swallowed.</summary>
        public static readonly ViewLayer System = new ViewLayer(
            "system", 2,
            ViewLayerRules.Modal | ViewLayerRules.ShieldsInput);
    }
}
