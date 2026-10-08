using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>
    /// Fits a RectTransform's anchors to Screen.safeArea and refits when it changes; assumes the parent rect covers the whole screen.
    /// Offsets stay as authored, and an edge that is turned off keeps its anchor at the screen edge.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        [SerializeField, Tooltip("Keeps the top edge inside the safe area.")]
        private bool _top = true;

        [SerializeField, Tooltip("Keeps the bottom edge inside the safe area.")]
        private bool _bottom = true;

        [SerializeField, Tooltip("Keeps the left edge inside the safe area.")]
        private bool _left = true;

        [SerializeField, Tooltip("Keeps the right edge inside the safe area.")]
        private bool _right = true;

        private RectTransform _rect;
        private ViewRoot _root;
        private Rect _lastSafeArea;
        private Vector2 _lastScreenSize;
        private FallbackTicker _ticker;

        // Test seam; the root also calls it when the safe area or screen size changes.
        internal void Fit(Rect safeArea, Vector2 screenSize)
        {
            if (screenSize.x <= 0f || screenSize.y <= 0f)
                return;

            if (_rect == null)
                _rect = (RectTransform)transform;

            var min = new Vector2(
                _left ? safeArea.xMin / screenSize.x : 0f,
                _bottom ? safeArea.yMin / screenSize.y : 0f);
            var max = new Vector2(
                _right ? safeArea.xMax / screenSize.x : 1f,
                _top ? safeArea.yMax / screenSize.y : 1f);

            // Skipped when unchanged, so no layout rebuild is triggered.
            if (_rect.anchorMin == min && _rect.anchorMax == max)
                return;

            _rect.anchorMin = min;
            _rect.anchorMax = max;
        }

        private void OnEnable()
        {
            var root = GetComponentInParent<ViewRoot>();
            if (root != null)
            {
                // The root fits it at once and compares once per frame for every registered fitter.
                _root = root;
                root.AddFitter(this);
                return;
            }

            _lastSafeArea = Screen.safeArea;
            _lastScreenSize = new Vector2(Screen.width, Screen.height);
            Fit(_lastSafeArea, _lastScreenSize);

            if (_ticker == null)
                _ticker = new FallbackTicker { Owner = this };

            _ticker.Active = true;

            // A disable and enable in one frame reuses the item that is still scheduled.
            if (_ticker.Scheduled)
                return;

            _ticker.Scheduled = true;
            PlayerLoopHelper.AddAction(PlayerLoopTiming.Update, _ticker);
        }

        private void OnDisable()
        {
            if (_ticker != null)
                _ticker.Active = false;

            if (_root != null)
                _root.RemoveFitter(this);

            _root = null;
        }

        private void CheckScreen()
        {
            var safeArea = Screen.safeArea;
            var screenSize = new Vector2(Screen.width, Screen.height);
            if (safeArea == _lastSafeArea && screenSize == _lastScreenSize)
                return;

            _lastSafeArea = safeArea;
            _lastScreenSize = screenSize;
            Fit(safeArea, screenSize);
        }

        // Compares the safe area each frame for a fitter with no root; removes itself once the fitter is disabled.
        private sealed class FallbackTicker : IPlayerLoopItem
        {
            public SafeAreaFitter Owner;
            public bool Active;
            public bool Scheduled;

            public bool MoveNext()
            {
                if (!Active || Owner == null)
                {
                    Scheduled = false;
                    return false;
                }

                // An escaped exception would remove the item while Scheduled stayed true.
                try
                {
                    Owner.CheckScreen();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, Owner);
                }

                return true;
            }
        }
    }
}
