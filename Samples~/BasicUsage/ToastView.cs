using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>A toast on the custom toasts layer: shows one line and closes itself after two seconds.</summary>
    public sealed class ToastView : ViewBehaviour<ToastArgs>
    {
        private static readonly TimeSpan ShowTime = TimeSpan.FromSeconds(2);

        [SerializeField] private TMP_Text _message;

        protected override void OnOpen(ToastArgs args, ViewScope open)
        {
            _message.text = args.Message;

            // The scope cancels this task if the toast is closed or replaced first.
            open.Run(CloseAfterDelay);
        }

        private async UniTask CloseAfterDelay(CancellationToken ct)
        {
            // Unscaled time, so a paused game (timeScale 0) still dismisses the toast.
            await UniTask.Delay(ShowTime, DelayType.UnscaledDeltaTime, cancellationToken: ct);
            Close();
        }
    }
}
