using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>A system-layer overlay that blocks input and swallows back while a save runs.</summary>
    public sealed class SavingOverlay : ViewBehaviour
    {
        private const float DegreesPerSecond = 360f;

        [SerializeField] private TMP_Text _label;
        [SerializeField] private RectTransform _spinner;

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            _label.text = "Saving...";
        }

        protected override void OnVisible(ViewScope visible)
        {
            // Per-span work goes on the visible scope, so the spinner stops whenever the overlay is hidden or closed.
            visible.Run(SpinAsync);
        }

        private async UniTask SpinAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                // Unscaled time keeps the spinner turning while the game is paused.
                _spinner.Rotate(0f, 0f, -DegreesPerSecond * Time.unscaledDeltaTime);

                // A cancelled yield throws, and the scope swallows it as the normal end of the loop.
                await UniTask.Yield(ct);
            }
        }
    }
}
