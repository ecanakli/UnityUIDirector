using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // The value the probe binds in the container.
    public sealed class ProbeInjectedValue
    {
        public string Name;
    }

    // Records when injection and Awake run, and what Awake saw.
    public sealed class ProbeInjectTarget : MonoBehaviour
    {
        public readonly List<string> Events = new List<string>();

        public ProbeInjectedValue Injected;
        public ProbeInjectedValue InjectedSeenByAwake;

        [Inject]
        public void Construct(ProbeInjectedValue value)
        {
            Injected = value;
            Events.Add("Inject");
        }

        void Awake()
        {
            InjectedSeenByAwake = Injected;
            Events.Add("Awake");
        }
    }
}
