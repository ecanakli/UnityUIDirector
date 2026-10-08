using Ecanakli.UIDirector;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.DOTweenUsage
{
    /// <summary>The only screen: one button opens a popup animated by the package's TweenTransition, the other one animated by PunchTransition.</summary>
    public sealed class WelcomeView : ViewBehaviour
    {
        [SerializeField] private Button _showHint;
        [SerializeField] private Button _showCard;

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            open.Bind(_showHint.onClick, ShowHint);
            open.Bind(_showCard.onClick, ShowCard);
        }

        // The owner is the root: a popup opened from a button does not depend on the screen that holds the button.
        private void ShowHint() =>
            Navigator.Open(TweenViews.Hint, new HintArgs("TweenTransition fades and scales this popup."), Navigator.RootToken,
                new OpenOptions { Source = "welcome.hint" });

        private void ShowCard() =>
            Navigator.Open(TweenViews.Card, new HintArgs("PunchTransition is a custom transition built on DOTween."), Navigator.RootToken,
                new OpenOptions { Source = "welcome.card" });
    }
}
