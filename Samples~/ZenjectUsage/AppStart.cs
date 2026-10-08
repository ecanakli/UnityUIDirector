using Zenject;

namespace Ecanakli.UIDirector.Samples.ZenjectUsage
{
    /// <summary>Opens the first screen once the container is built.</summary>
    public sealed class AppStart : IInitializable
    {
        private readonly INavigator _navigator;

        public AppStart(INavigator navigator)
        {
            _navigator = navigator;
        }

        public void Initialize()
        {
            // Owned by the root, so the scoreboard ends only when the root does.
            _navigator.Open(ZenjectViews.Scoreboard, _navigator.RootToken, new OpenOptions { Source = "boot" });
        }
    }
}
