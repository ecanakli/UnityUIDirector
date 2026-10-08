using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    // The one loop behind the built-in transitions and the backdrop: unscaled time, one step per frame, no allocation when warm.
    internal static class UnscaledAnimation
    {
        // One frame advances at most this much; below 30 fps animations run longer than their duration.
        public const float MaxStepSeconds = 1f / 30f;

        // Calls step(state, t) with t from 0 to 1 once per frame; the last call has t == 1; returns early without throwing when ct is cancelled.
        public static async UniTask RunAsync<TState>(float durationSeconds, TState state, Action<TState, float> step, CancellationToken ct)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));
            if (ct.IsCancellationRequested)
                return;

            // Zero, negative and NaN durations finish at once.
            if (!(durationSeconds > 0f))
            {
                step(state, 1f);
                return;
            }

            step(state, 0f);

            var elapsed = 0f;
            while (true)
            {
                // The token-less NextFrame is pooled, so a warm loop allocates nothing; cancellation is checked after it.
                await UniTask.NextFrame(PlayerLoopTiming.Update);
                if (ct.IsCancellationRequested)
                    return;

                var delta = Time.unscaledDeltaTime;
                if (delta > MaxStepSeconds)
                    delta = MaxStepSeconds;
                else if (!(delta > 0f))
                    delta = 0f;

                elapsed += delta;
                if (elapsed >= durationSeconds)
                {
                    step(state, 1f);
                    return;
                }

                step(state, elapsed / durationSeconds);
            }
        }
    }
}
