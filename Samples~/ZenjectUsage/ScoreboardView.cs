using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Ecanakli.UIDirector.Samples.ZenjectUsage
{
    /// <summary>The scoreboard screen: adds points and resets the score behind a confirm popup.</summary>
    public sealed class ScoreboardView : ViewBehaviour
    {
        [SerializeField] private TMP_Text _score;
        [SerializeField] private Button _addPoint;
        [SerializeField] private Button _reset;

        private ScoreService _scores;

        // Runs before Awake, while the new instance is still inactive; a kept instance is not injected again.
        [Inject]
        private void Construct(ScoreService scores)
        {
            _scores = scores;
        }

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            open.Bind(_addPoint.onClick, AddPoint);
            open.Bind(_reset.onClick, OnResetClicked);
        }

        // The screen is kept between opens, so the score is read again every time it comes on screen.
        protected override void OnVisible(ViewScope visible) => Refresh();

        private void AddPoint()
        {
            _scores.Add(1);
            Refresh();
        }

        private void OnResetClicked() => OpenScope.Run(ResetAsync);

        private async UniTask ResetAsync(CancellationToken ct)
        {
            // The owner is this open's token: the popup cannot outlive the screen.
            var answer = await Navigator.ShowAsync(
                ZenjectViews.ResetScore,
                ct,
                new OpenOptions { Source = "scoreboard.reset" });

            // Back, backdrop and a closed screen all end with no value.
            if (!answer.HasValue || !answer.Value)
                return;

            _scores.Reset();
            Refresh();
        }

        private void Refresh() => _score.text = _scores.Points.ToString();
    }
}
