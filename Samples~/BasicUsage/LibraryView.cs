using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>Lists the items and opens the detail screen of the one that is tapped.</summary>
    public sealed class LibraryView : ViewBehaviour, IServiceReceiver
    {
        [SerializeField] private TMP_Text _count;
        [SerializeField] private Button _guide;
        [SerializeField] private Button _notes;
        [SerializeField] private Button _archive;
        [SerializeField] private Button _back;
        private IItemLibrary _library;

        public void Receive(AppServices services) => _library = services.Library;

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            open.Bind(_guide.onClick, OpenGuide);
            open.Bind(_notes.onClick, OpenNotes);
            open.Bind(_archive.onClick, OpenArchive);
            open.Bind(_back.onClick, GoBack);
        }

        // Runs on the first show and again each time this screen is revealed.
        protected override void OnVisible(ViewScope visible) => _count.text = $"{_library.Count} items";

        private void OpenGuide() => OpenItem("guide");

        private void OpenNotes() => OpenItem("notes");

        private void OpenArchive() => OpenItem("archive");

        // The other screens use a ViewButton set to Back; this one shows the call.
        private void GoBack() => Navigator.Back();

        private void OpenItem(string itemId) =>
            Navigator.Open(AppViews.ItemDetail, new ItemArgs(itemId), Navigator.RootToken,
                new OpenOptions { Source = "library.item" });
    }
}
