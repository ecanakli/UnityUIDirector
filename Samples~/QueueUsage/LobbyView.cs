using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>The sample's only screen: buttons that fill the popup queue, start the tutorial and add or remove the news feature.</summary>
    public sealed class LobbyView : ViewBehaviour, IQueueServiceReceiver
    {
        [SerializeField] private Button _showNotices;
        [SerializeField] private Button _askFeedback;
        [SerializeField] private Button _runTutorial;
        [SerializeField] private Button _toggleNews;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private TMP_Text _newsLabel;

        private QueueServices _services;

        // The root's inject seam calls this on every new instance, before OnOpen.
        public void Receive(QueueServices services)
        {
            _services = services;
        }

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            // A kept instance opens again, so every field is reset here.
            _status.text = "Pick an action.";
            RefreshNewsLabel();

            open.Bind(_showNotices.onClick, OnShowNotices);
            open.Bind(_askFeedback.onClick, OnAskFeedback);
            open.Bind(_runTutorial.onClick, OnRunTutorial);
            open.Bind(_toggleNews.onClick, OnToggleNews);
        }

        private void OnShowNotices()
        {
            _services.Notices.EnqueueAll();
            _status.text = "Notices queued. They appear one at a time, most urgent first.";
        }

        private void OnAskFeedback()
        {
            _status.text = "Waiting for the question to reach the screen...";

            // The scope token cancels the wait if the lobby closes first.
            OpenScope.Run(AskAsync);
        }

        private void OnRunTutorial()
        {
            if (_services.Tutorial.IsRunning)
            {
                _status.text = "The tutorial is already running.";
                return;
            }

            _status.text = "Tutorial started. Queued notices wait until it ends.";
            OpenScope.Run(_services.Tutorial.RunAsync);
        }

        private void OnToggleNews()
        {
            if (_services.News.IsAdded)
                _services.News.Remove();
            else
                _services.News.Add();

            RefreshNewsLabel();
        }

        private async UniTask AskAsync(CancellationToken ct)
        {
            var liked = await _services.Notices.AskForFeedbackAsync();
            if (ct.IsCancellationRequested)
                return;

            _status.text = liked ? "Thanks!" : "Maybe later.";
        }

        private void RefreshNewsLabel()
        {
            _newsLabel.text = _services.News.IsAdded ? "Remove news feature" : "Add news feature";
        }
    }
}
