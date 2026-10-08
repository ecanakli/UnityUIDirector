using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.AddressablesUsage
{
    /// <summary>The gallery screen: opens photo popups from remote assets and shows what the preload and the trim do.</summary>
    public sealed class GalleryView : ViewBehaviour
    {
        [SerializeField] private Button _photo1;
        [SerializeField] private Button _photo2;
        [SerializeField] private Button _photo3;
        [SerializeField] private Button _trim;
        [SerializeField] private TMP_Text _status;

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            // The screen is kept between opens, so the last status must not show again.
            _status.text = string.Empty;
            open.Bind(_photo1.onClick, OpenPhoto1);
            open.Bind(_photo2.onClick, OpenPhoto2);
            open.Bind(_photo3.onClick, OpenPhoto3);
            open.Bind(_trim.onClick, TrimKept);
        }

        // The lease lives on the visible scope: it is released when this screen is covered or closed.
        protected override void OnVisible(ViewScope visible)
        {
            visible.Run(PreloadAsync);
        }

        private async UniTask PreloadAsync(CancellationToken ct)
        {
            // With the photo asset held, the first photo below opens without a load wait.
            var loaded = await Navigator.PreloadAsync(RemoteViews.Photo.Id, ct);
            _status.text = loaded ? "Photo popup preloaded." : "Photo popup could not be preloaded, see the console.";
        }

        private void OpenPhoto1() => OpenScope.Run("Sunrise", ShowPhotoAsync);

        private void OpenPhoto2() => OpenScope.Run("Harbor", ShowPhotoAsync);

        private void OpenPhoto3() => OpenScope.Run("Glacier", ShowPhotoAsync);

        private async UniTask ShowPhotoAsync(string title, CancellationToken ct)
        {
            // The owner is this screen's open scope: the popup cannot outlive the screen.
            var result = await Navigator.ShowAsync(
                RemoteViews.Photo,
                new PhotoArgs(title),
                ct,
                new OpenOptions { Source = "gallery.photo" });

            if (result.Rejected != RejectReason.None)
                _status.text = $"Photo '{title}' did not open: {result.Rejected}.";
        }

        // Frees kept instances that are not on screen; this screen is open, so it stays.
        private void TrimKept()
        {
            Navigator.Trim();
            _status.text = "Trimmed: hidden kept instances were released.";
        }
    }
}
