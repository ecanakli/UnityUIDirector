using System;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Counts lifecycle messages; OnDestroyAction runs inside OnDestroy.
    public sealed class ProbeView : MonoBehaviour
    {
        [NonSerialized] public int AwakeCount;
        [NonSerialized] public int EnableCount;
        [NonSerialized] public int DisableCount;
        [NonSerialized] public int DestroyCount;
        [NonSerialized] public Action OnDestroyAction;
        [NonSerialized] public Exception OnDestroyException;

        void Awake()
        {
            AwakeCount++;
        }

        void OnEnable()
        {
            EnableCount++;
        }

        void OnDisable()
        {
            DisableCount++;
        }

        void OnDestroy()
        {
            DestroyCount++;

            Action action = OnDestroyAction;
            if (action == null)
                return;

            try
            {
                action();
            }
            catch (Exception exception)
            {
                OnDestroyException = exception;
            }
        }
    }
}
