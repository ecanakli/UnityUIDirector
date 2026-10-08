using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>A popup with a title, a body and an OK button; the news feature reuses it.</summary>
    public sealed class NoticeView : ViewBehaviour<NoticeArgs>
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _body;
        [SerializeField] private Button _ok;

        protected override void OnOpen(NoticeArgs args, ViewScope open)
        {
            _title.text = args.Title;
            _body.text = args.Body;

            open.Bind(_ok.onClick, Close);
        }
    }
}
