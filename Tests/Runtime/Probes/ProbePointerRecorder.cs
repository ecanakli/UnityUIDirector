using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Records the order in which pointers are released on this object.
    public sealed class ProbePointerRecorder : MonoBehaviour, IPointerUpHandler
    {
        List<string> m_Sink;
        string m_Label;

        public void Init(string label, List<string> sink)
        {
            m_Label = label;
            m_Sink = sink;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            m_Sink?.Add(m_Label);
        }
    }
}
