using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>One tutorial tip with a step counter and a Next button.</summary>
    public sealed class TutorialTipView : ViewBehaviour<TipArgs>
    {
        [SerializeField] private TMP_Text _text;
        [SerializeField] private TMP_Text _progress;
        [SerializeField] private Button _next;

        protected override void OnOpen(TipArgs args, ViewScope open)
        {
            _text.text = args.Text;
            _progress.text = "Step " + args.Step + " of " + args.StepCount;

            open.Bind(_next.onClick, Close);
        }
    }
}
