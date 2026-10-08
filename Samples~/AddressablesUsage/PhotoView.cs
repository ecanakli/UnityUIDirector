using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.AddressablesUsage
{
    /// <summary>A photo popup loaded from an Addressables address; shows a title and closes on a button.</summary>
    public sealed class PhotoView : ViewBehaviour<PhotoArgs>
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private Button _close;

        protected override void OnOpen(PhotoArgs args, ViewScope open)
        {
            _title.text = args.Title;
            open.Bind(_close.onClick, Close);
        }
    }
}
