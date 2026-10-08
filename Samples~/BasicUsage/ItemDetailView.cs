using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>Shows one item and lets the player rename it, go home or return to the library.</summary>
    public sealed class ItemDetailView : ViewBehaviour<ItemArgs>, IServiceReceiver
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private Button _home;
        [SerializeField] private Button _toLibrary;
        [SerializeField] private Button _rename;
        private IItemLibrary _library;
        private string _itemId;

        public void Receive(AppServices services) => _library = services.Library;

        protected override void OnOpen(ItemArgs args, ViewScope open)
        {
            _itemId = args.ItemId;
            _title.text = _library.GetTitle(_itemId);

            // The package removes the C# event handler when this open ends.
            open.Subscribe<string>(handler => _library.Renamed += handler, handler => _library.Renamed -= handler, OnRenamed);

            // A reused instance must not show the previous item for a frame.
            open.OnEnd(() => _title.text = string.Empty);

            open.Bind(_home.onClick, GoHome);
            open.Bind(_toLibrary.onClick, GoToLibrary);
            open.Bind(_rename.onClick, OnRenameClicked);
        }

        // Home is already in the stack: this returns to it, and Home's OnOpen does not run again.
        private void GoHome() =>
            Navigator.Open(AppViews.Home, Navigator.RootToken, new OpenOptions { Source = "item.home" });

        // Pops everything above the library.
        private void GoToLibrary() => Navigator.CloseTo(AppViews.Library);

        private void OnRenameClicked() => OpenScope.Run(RenameAsync);

        private async UniTask RenameAsync(CancellationToken ct)
        {
            // The owner is this open's token: the popup cannot outlive this screen.
            var result = await Navigator.ShowAsync(
                AppViews.Rename,
                new RenameArgs(_itemId, _library.GetTitle(_itemId)),
                ct,
                new OpenOptions { Source = "item.rename" });

            // No value: the player went back, or the item cannot be renamed.
            if (!result.TryGetValue(out var title)) return;

            _library.Rename(_itemId, title);
        }

        private void OnRenamed(string itemId)
        {
            if (itemId == _itemId) _title.text = _library.GetTitle(itemId);
        }
    }
}
