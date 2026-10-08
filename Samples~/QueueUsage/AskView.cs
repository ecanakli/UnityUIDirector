using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>A yes or no popup; the answer is the view's result. Closing on the backdrop gives no answer.</summary>
    public sealed class AskView : ViewBehaviour<AskArgs, bool>
    {
        [SerializeField] private TMP_Text _question;
        [SerializeField] private Button _yes;
        [SerializeField] private Button _no;

        protected override void OnOpen(AskArgs args, ViewScope open)
        {
            _question.text = args.Question;

            open.Bind(_yes.onClick, OnYes);
            open.Bind(_no.onClick, OnNo);
        }

        private void OnYes()
        {
            Complete(true);
        }

        private void OnNo()
        {
            Complete(false);
        }
    }
}
