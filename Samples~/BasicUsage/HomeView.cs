using Ecanakli.UIDirector;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>The first screen: two buttons that open the library and the settings.</summary>
    public sealed class HomeView : ViewBehaviour
    {
        [SerializeField] private Button _library;
        [SerializeField] private Button _settings;

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            open.Bind(_library.onClick, OpenLibrary);
            open.Bind(_settings.onClick, OpenSettings);
        }

        // The owner is the root: a screen opened from a button outlives the screen that holds the button.
        private void OpenLibrary() =>
            Navigator.Open(AppViews.Library, Navigator.RootToken, new OpenOptions { Source = "home.library" });

        private void OpenSettings() =>
            Navigator.Open(AppViews.Settings, Navigator.RootToken, new OpenOptions { Source = "home.settings" });
    }
}
