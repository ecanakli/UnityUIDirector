using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ecanakli.UIDirector.Tests
{
    // A tap through EventSystem.RaycastAll, then down, up and click sent to the first hit only; works with any input setup.
    internal static class UiTap
    {
        // Taps the topmost hit at the screen point and returns it; null when nothing was hit.
        public static GameObject At(Vector2 screenPoint)
        {
            return Click(Raycast(screenPoint));
        }

        // The topmost hit at the screen point; its gameObject is null when nothing was hit.
        public static RaycastResult Raycast(Vector2 screenPoint)
        {
            var eventSystem = Current();
            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screenPoint }, hits);
            return hits.Count == 0 ? default(RaycastResult) : hits[0];
        }

        // Sends down, up and click to a hit taken earlier and returns its object; null when the hit is empty.
        public static GameObject Click(RaycastResult hit)
        {
            var eventSystem = Current();
            var target = hit.gameObject;
            if (target == null)
                return null;

            var pointer = new PointerEventData(eventSystem)
            {
                position = hit.screenPosition,
                pressPosition = hit.screenPosition,
                button = PointerEventData.InputButton.Left,
                pointerCurrentRaycast = hit,
                pointerPressRaycast = hit,
                eligibleForClick = true,
                clickCount = 1,
                clickTime = Time.unscaledTime,
                rawPointerPress = target
            };

            var pressed = ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler);
            pointer.pointerPress = pressed != null ? pressed : ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);

            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler);
            return target;
        }

        private static EventSystem Current()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                throw new InvalidOperationException("A tap needs a current EventSystem.");

            return eventSystem;
        }
    }
}
