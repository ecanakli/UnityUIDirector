using System.Collections.Generic;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Records Awake, OnEnable and Start in call order.
    public sealed class ProbeLifecycleCounter : MonoBehaviour
    {
        public readonly List<string> Events = new List<string>();

        void Awake()
        {
            Events.Add("Awake");
        }

        void OnEnable()
        {
            Events.Add("OnEnable");
        }

        void Start()
        {
            Events.Add("Start");
        }

        public int Count(string eventName)
        {
            var count = 0;
            foreach (var recorded in Events)
            {
                if (recorded == eventName)
                    count++;
            }

            return count;
        }

        public string Describe()
        {
            return Events.Count == 0 ? "-" : string.Join("|", Events);
        }
    }
}
