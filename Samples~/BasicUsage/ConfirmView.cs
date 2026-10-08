using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>A confirm popup: shows a title and a message, completes with true or false.</summary>
    public sealed class ConfirmView : ViewBehaviour<ConfirmArgs, bool>
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _message;
        [SerializeField] private Button _accept;
        [SerializeField] private Button _decline;

        protected override void OnOpen(ConfirmArgs args, ViewScope open)
        {
            _title.text = args.Title;
            _message.text = args.Message;

            // The package removes both listeners when this open ends.
            open.Bind(_accept.onClick, Accept);
            open.Bind(_decline.onClick, Refuse);
        }

        private void Accept()
        {
            Complete(true);
        }

        private void Refuse()
        {
            Complete(false);
        }
    }
}
