using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>Stand-in settings storage that only logs and waits.</summary>
    public sealed class SettingsStore : ISettingsStore
    {
        public void ResetToDefaults()
        {
            Debug.Log("[settings] reset to defaults");
        }

        public UniTask SaveAsync(CancellationToken ct)
        {
            // Pretends to write somewhere slow; unscaled, so a paused game still finishes.
            return UniTask.Delay(TimeSpan.FromSeconds(1.5), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
        }
    }
}
