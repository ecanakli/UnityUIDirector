using UnityEngine;

namespace Ecanakli.UIDirector
{
    // Instances of rows with a direct prefab reference; the prefab is already resident, so nothing loads.
    internal sealed class DirectViewProvider
    {
        // Synchronous Instantiate, since the prefab is already loaded; the inactive parent keeps Awake from running.
        public GameObject Acquire(GameObject prefab, Transform inactiveParent)
        {
            return Object.Instantiate(prefab, inactiveParent, false);
        }

        // Safe on a destroyed instance.
        public void Release(GameObject instance)
        {
            if (instance != null)
                Object.Destroy(instance);
        }
    }
}
