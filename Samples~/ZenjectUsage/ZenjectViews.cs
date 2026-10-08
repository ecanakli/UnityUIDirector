using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.ZenjectUsage
{
    /// <summary>The keys of this sample: a screen and a popup whose views receive their services from the container.</summary>
    [ViewKeys]
    public static class ZenjectViews
    {
        public static readonly ViewKey<NoArgs, NoResult> Scoreboard = ViewKey.Create("scoreboard");
        public static readonly ViewKey<NoArgs, bool> ResetScore = ViewKey.Create<NoArgs, bool>("reset-score");
    }
}
