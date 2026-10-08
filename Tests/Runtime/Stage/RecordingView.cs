using UnityEngine;

namespace Ecanakli.UIDirector.Tests
{
    // A view without arguments or result that records its Unity messages and hooks for PlayMode tests.
    internal sealed class RecordingView : ViewBehaviour
    {
        public int AwakeCount;
        public int EnableCount;
        public int DisableCount;
        public int OpenCount;
        public int VisibleCount;
        public int BackCount;

        // Captured at the last OnOpen.
        public bool ActiveDuringOpen;
        public bool CanvasEnabledDuringOpen;
        public int AwakeCountAtOpen;

        public ViewScope LastOpenScope;
        public ViewScope LastVisibleScope;

        // What OnBack answers.
        public bool HandlesBack;

        public INavigator NavigatorSeen
        {
            get { return Navigator; }
        }

        public void CloseFromView()
        {
            Close();
        }

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            OpenCount++;
            ActiveDuringOpen = gameObject.activeInHierarchy;
            var canvas = GetComponent<Canvas>();
            CanvasEnabledDuringOpen = canvas != null && canvas.enabled;
            AwakeCountAtOpen = AwakeCount;
            LastOpenScope = open;
        }

        protected override void OnVisible(ViewScope visible)
        {
            VisibleCount++;
            LastVisibleScope = visible;
        }

        protected override bool OnBack()
        {
            BackCount++;
            return HandlesBack;
        }

        private void Awake()
        {
            AwakeCount++;
        }

        private void OnEnable()
        {
            EnableCount++;
        }

        private void OnDisable()
        {
            DisableCount++;
        }
    }
}
