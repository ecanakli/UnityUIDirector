using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Records OnDisable and OnDestroy and what the object looks like inside each.
    public sealed class ProbeMessageRecorder : MonoBehaviour
    {
        [NonSerialized] public List<string> Sink;
        [NonSerialized] public bool DestroyGameObjectOnDisable;

        public readonly List<Dictionary<string, string>> DisableSnapshots = new List<Dictionary<string, string>>();
        public readonly List<Dictionary<string, string>> DestroySnapshots = new List<Dictionary<string, string>>();

        bool m_DestroyRequested;

        void OnDisable()
        {
            DisableSnapshots.Add(Snapshot());
            Sink?.Add("OnDisable");

            if (DestroyGameObjectOnDisable && !m_DestroyRequested)
            {
                m_DestroyRequested = true;
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            DestroySnapshots.Add(Snapshot());
            Sink?.Add("OnDestroy");
        }

        Dictionary<string, string> Snapshot()
        {
            var snapshot = new Dictionary<string, string>
            {
                ["activeInHierarchy"] = gameObject.activeInHierarchy.ToString(),
                ["activeSelf"] = gameObject.activeSelf.ToString(),
                ["enabled"] = enabled.ToString(),
                ["isActiveAndEnabled"] = isActiveAndEnabled.ToString(),
                ["sceneIsLoaded"] = gameObject.scene.isLoaded.ToString(),
                ["sceneIsValid"] = gameObject.scene.IsValid().ToString(),
            };

            try
            {
                snapshot["destroyTokenCancelled"] = destroyCancellationToken.IsCancellationRequested.ToString();
            }
            catch (Exception exception)
            {
                snapshot["destroyTokenCancelled"] = "throws:" + exception.GetType().Name;
            }

            return snapshot;
        }
    }
}
