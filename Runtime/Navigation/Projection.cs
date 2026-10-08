using System.Collections.Generic;

namespace Ecanakli.UIDirector
{
    // One request as Projection sees it; items are listed in global order: layers ascending, then list position.
    internal struct ProjectionItem
    {
        public int LayerIndex;
        public int LayerOrder;
        public bool LayerModal;
        public ViewPhase Phase;
        public bool CoversBelow;
        public bool CloseRequested;
        public int Slot;
    }

    internal struct ProjectionResult
    {
        public bool Hidden;
        public bool Interactive;
        public int SortingOrder;
    }

    internal struct BackdropPlacement
    {
        public bool Active;
        public int SortingOrder;
    }

    // Pure: derives hidden, interactive, order and backdrop placement from the lists and phases; nothing is accumulated.
    internal static class Projection
    {
        public const int SlotStep = 10;
        public const int MaxSlot = 99;
        public const int LayerStep = 1000;
        public const int ViewBandTop = 4;
        public const int BackdropOffset = 5;
        public const int SortingCeiling = 29999;
        public const int SortingFloor = -32768;

        public static int OrderFor(int baseOrder, int layerOrder, int slot)
        {
            return baseOrder + layerOrder * LayerStep + slot * SlotStep;
        }

        // The view owns orders +1 to +4 above its own, and its backdrop sits 5 below.
        public static bool FitsBand(int baseOrder, int layerOrder, int slot)
        {
            if (slot < 1 || slot > MaxSlot)
                return false;

            var order = OrderFor(baseOrder, layerOrder, slot);
            return order + ViewBandTop < SortingCeiling && order - BackdropOffset >= SortingFloor;
        }

        public static int ShieldOrder(int baseOrder, int highestLayerOrder)
        {
            return baseOrder + (highestLayerOrder + 1) * LayerStep;
        }

        public static bool ShieldFits(int baseOrder, int highestLayerOrder)
        {
            return ShieldOrder(baseOrder, highestLayerOrder) < SortingCeiling;
        }

        // Fills one result per item, in item order, and one backdrop per layer index.
        public static void Compute(
            List<ProjectionItem> items,
            int baseOrder,
            List<ProjectionResult> results,
            BackdropPlacement[] backdrops)
        {
            results.Clear();
            for (var i = 0; i < items.Count; i++)
                results.Add(default(ProjectionResult));

            for (var i = 0; i < backdrops.Length; i++)
                backdrops[i] = default(BackdropPlacement);

            var coveredAbove = false;
            var modalAbove = false;
            var currentLayer = -1;
            var layerDone = false;

            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                var order = OrderFor(baseOrder, item.LayerOrder, item.Slot);
                var onScreen = IsOnScreen(item.Phase);
                var hidden = !onScreen || coveredAbove;
                var interactive = item.Phase == ViewPhase.Open && !hidden && !modalAbove && !item.CloseRequested;

                results[i] = new ProjectionResult
                {
                    Hidden = hidden,
                    Interactive = interactive,
                    SortingOrder = order
                };

                if (item.LayerIndex != currentLayer)
                {
                    currentLayer = item.LayerIndex;
                    layerDone = false;
                }

                // The first on-screen view met from the top is the top view of its layer.
                if (item.LayerModal && onScreen && !layerDone && item.LayerIndex >= 0 && item.LayerIndex < backdrops.Length)
                {
                    backdrops[item.LayerIndex] = new BackdropPlacement
                    {
                        Active = !hidden,
                        SortingOrder = order - BackdropOffset
                    };
                    layerDone = true;
                }

                if (item.Phase == ViewPhase.Open && item.CoversBelow)
                    coveredAbove = true;

                if (item.LayerModal && onScreen)
                    modalAbove = true;
            }
        }

        // Opening, Open and Closing have an instance on screen; Waiting and Loading have none shown yet.
        public static bool IsOnScreen(ViewPhase phase)
        {
            return phase == ViewPhase.Opening || phase == ViewPhase.Open || phase == ViewPhase.Closing;
        }
    }
}
