using UnityEngine;
using UnityEngine.EventSystems;
#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Ecanakli.UIDirector.Samples.AddressablesUsage
{
    /// <summary>Creates the EventSystem at start; the scene ships none because a baked input module is a missing script, or throws, in projects with the other input handling.</summary>
    public static class SampleEventSystem
    {
        public static void EnsureExists()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
#if UIDIRECTOR_SAMPLES_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            eventSystem.AddComponent<InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
