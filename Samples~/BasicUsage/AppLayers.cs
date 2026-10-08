using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>Layers this sample adds to the three defaults.</summary>
    public static class AppLayers
    {
        // Above the system layer, no history, no back and no input shield: a toast never blocks a tap.
        public static readonly ViewLayer Toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);
    }
}
