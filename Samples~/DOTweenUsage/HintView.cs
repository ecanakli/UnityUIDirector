using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.DOTweenUsage
{
    /// <summary>A popup with a line of text and an OK button; the hint and the card are two prefabs of it with different transitions.</summary>
    public sealed class HintView : ViewBehaviour<HintArgs>
    {
        [SerializeField] private TMP_Text _text;
        [SerializeField] private Button _ok;

        protected override void OnOpen(HintArgs args, ViewScope open)
        {
            _text.text = args.Text;
            open.Bind(_ok.onClick, Close);
        }
    }
}
