using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>The settings screen: reset behind a confirm popup, save behind a system overlay.</summary>
    public sealed class SettingsView : ViewBehaviour, IServiceReceiver
    {
        [SerializeField] private Button _reset;
        [SerializeField] private Button _save;
        [SerializeField] private TMP_Text _status;

        private ISettingsStore _settings;

        public void Receive(AppServices services)
        {
            _settings = services.Settings;
        }

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            // The screen is kept between opens, so the last status must not show again.
            _status.text = string.Empty;
            open.Bind(_reset.onClick, OnResetClicked);
            open.Bind(_save.onClick, OnSaveClicked);
        }

        private void OnResetClicked()
        {
            OpenScope.Run(ConfirmResetAsync);
        }

        private void OnSaveClicked()
        {
            OpenScope.Run(SaveAsync);
        }

        private async UniTask ConfirmResetAsync(CancellationToken ct)
        {
            // The owner is this screen's scope token: the popup cannot outlive the screen.
            var answer = await Navigator.ShowAsync(
                AppViews.Confirm,
                new ConfirmArgs("Reset settings", "All settings return to their defaults."),
                ct,
                new OpenOptions { Source = "settings.reset", RejectWhenBusy = true });

            // Refused at once instead of waiting behind a view that is still opening.
            if (answer.Rejected == RejectReason.Busy)
            {
                _status.text = "Busy, try again.";
                return;
            }

            // Back, backdrop and a closed screen all end with no value, so this line also guards the screen.
            if (!answer.HasValue || !answer.Value)
                return;

            _settings.ResetToDefaults();
            _status.text = "Settings were reset.";

            // The toast outlives this screen, so the root owns it.
            Navigator.Open(
                AppViews.Toast,
                new ToastArgs("Settings were reset."),
                Navigator.RootToken,
                new OpenOptions { Source = "settings.reset" });
        }

        private async UniTask SaveAsync(CancellationToken ct)
        {
            // The root owns the overlay, so the finally below is what guarantees it never stays up.
            var overlay = Navigator.Open(AppViews.Saving, Navigator.RootToken, new OpenOptions { Source = "settings.save" });

            try
            {
                await _settings.SaveAsync(ct);
            }
            finally
            {
                overlay.Close();
            }

            // Wait for the hide transition, so the status appears once the overlay is gone.
            var closed = await overlay.WaitForCloseAsync(ct);
            _status.text = closed.Reason == CloseReason.Code ? "Saved." : "Saved. The overlay ended early.";
        }
    }
}
