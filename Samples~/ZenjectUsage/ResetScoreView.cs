using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Ecanakli.UIDirector.Samples.ZenjectUsage
{
    /// <summary>A confirm popup that names the current score, completes with true or false.</summary>
    public sealed class ResetScoreView : ViewBehaviour<NoArgs, bool>
    {
        [SerializeField] private TMP_Text _question;
        [SerializeField] private Button _yes;
        [SerializeField] private Button _no;

        private ScoreService _scores;

        // Runs before Awake, while the new instance is still inactive.
        [Inject]
        private void Construct(ScoreService scores)
        {
            _scores = scores;
        }

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            _question.text = $"Reset the score of {_scores.Points}?";

            open.Bind(_yes.onClick, Accept);
            open.Bind(_no.onClick, Refuse);
        }

        private void Accept() => Complete(true);

        private void Refuse() => Complete(false);
    }
}
