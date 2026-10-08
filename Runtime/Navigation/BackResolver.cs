using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    internal enum BackKind
    {
        // Nothing handled back; Back() returns false and the game decides.
        NotHandled,

        // Back is consumed and nothing happens; Back() returns true.
        Swallow,

        // The target request is withdrawn before it is shown (NotShown / Cancelled); Back() returns true.
        Withdraw,

        // The target view is asked through its back hook; Fallback applies when the hook returns false.
        AskView,

        // The target view closes with reason Back; Back() returns true.
        Close
    }

    internal readonly struct BackDecision<TRecord> where TRecord : class
    {
        public readonly BackKind Kind;
        public readonly TRecord Target;
        public readonly BackKind Fallback;

        public BackDecision(BackKind kind, TRecord target, BackKind fallback)
        {
            Kind = kind;
            Target = target;
            Fallback = fallback;
        }
    }

    // Pure: turns the layer lists into one back decision by five rules checked in order; allocates nothing.
    internal static class BackResolver
    {
        // layers must be ordered by ascending layer order; rule 1 (root gone, nothing found) is the caller's.
        public static BackDecision<TRecord> Resolve<TRecord>(List<LayerState<TRecord>> layers, int shieldCount)
            where TRecord : class, ILayerRecord
        {
            // Rule 2: the newest request still waiting or loading on a layer that takes back.
            var pending = FindNewestPending(layers);
            if (pending != null)
                return new BackDecision<TRecord>(BackKind.Withdraw, pending, BackKind.NotHandled);

            // Rule 3: while the shield is up, back is swallowed.
            if (shieldCount > 0)
                return new BackDecision<TRecord>(BackKind.Swallow, null, BackKind.NotHandled);

            // Rule 4: the first layer from the top with a view on screen that is modal or takes back.
            for (var i = layers.Count - 1; i >= 0; i--)
            {
                var state = layers[i];
                var layer = state.Layer;
                var takesBack = layer.Has(ViewLayerRules.TakesBack);
                if (!takesBack && !layer.Has(ViewLayerRules.Modal))
                    continue;

                var top = state.TopOnScreen();
                if (top == null)
                    continue;

                // A view in a transition cannot be asked or closed; back waits for it.
                if (top.Phase != ViewPhase.Open)
                    return new BackDecision<TRecord>(BackKind.Swallow, null, BackKind.NotHandled);

                var policy = top.Row.Back;
                if (!takesBack)
                {
                    return policy == BackPolicy.HandledByView
                        ? new BackDecision<TRecord>(BackKind.AskView, top, BackKind.Swallow)
                        : new BackDecision<TRecord>(BackKind.Swallow, null, BackKind.NotHandled);
                }

                if (policy == BackPolicy.Ignore)
                    return new BackDecision<TRecord>(BackKind.Swallow, null, BackKind.NotHandled);

                var closeKind = IsRootOfHistory(state, top) ? BackKind.NotHandled : BackKind.Close;
                if (policy == BackPolicy.HandledByView)
                    return new BackDecision<TRecord>(BackKind.AskView, top, closeKind);

                return closeKind == BackKind.Close
                    ? new BackDecision<TRecord>(BackKind.Close, top, BackKind.NotHandled)
                    : new BackDecision<TRecord>(BackKind.NotHandled, null, BackKind.NotHandled);
            }

            // Rule 5: nothing matched, so back is not handled.
            return new BackDecision<TRecord>(BackKind.NotHandled, null, BackKind.NotHandled);
        }

        private static TRecord FindNewestPending<TRecord>(List<LayerState<TRecord>> layers)
            where TRecord : class, ILayerRecord
        {
            TRecord newest = null;
            for (var i = 0; i < layers.Count; i++)
            {
                var state = layers[i];
                if (!state.Layer.Has(ViewLayerRules.TakesBack))
                    continue;

                for (var j = 0; j < state.Count; j++)
                {
                    var record = state[j];
                    if (record.Phase != ViewPhase.Waiting && record.Phase != ViewPhase.Loading)
                        continue;
                    if (record.Row.Back == BackPolicy.Ignore)
                        continue;

                    if (newest == null || record.AcceptSequence > newest.AcceptSequence)
                        newest = record;
                }
            }

            return newest;
        }

        // The bottom view of a history layer is the root for Back().
        private static bool IsRootOfHistory<TRecord>(LayerState<TRecord> state, TRecord target)
            where TRecord : class, ILayerRecord
        {
            return state.Layer.Has(ViewLayerRules.History) && state.IndexOf(target) == 0;
        }
    }
}
