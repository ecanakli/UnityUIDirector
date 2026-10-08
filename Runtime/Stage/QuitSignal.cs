using UnityEngine;

namespace Ecanakli.UIDirector
{
    // True from Application.quitting until the next play session; one subscription for the process.
    // In the Editor it stays set after Play Mode ends until the next play session.
    internal static class QuitSignal
    {
        internal static bool IsQuitting { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset()
        {
            IsQuitting = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting()
        {
            IsQuitting = true;
        }

#if UNITY_INCLUDE_TESTS
        internal static void MarkQuittingForTests()
        {
            IsQuitting = true;
        }
#endif
    }
}
