using UnityEngine;
using UnityEngine.EventSystems;
#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>Creates an EventSystem when the scene has none; the sample scene ships none because the right input module depends on the project's input handling.</summary>
    public static class SampleEventSystem
    {
        public static void EnsureExists()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return;

            var host = new GameObject("EventSystem", typeof(EventSystem));
#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            host.AddComponent<InputSystemUIInputModule>();
#else
            host.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
