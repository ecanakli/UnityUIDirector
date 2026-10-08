using UnityEngine;

namespace Ecanakli.UIDirector
{
    // The opaque instance the logic holds for one acquired view object.
    internal sealed class StageInstance
    {
        public readonly GameObject GameObject;

        // Null when the instance root has no ViewHost.
        public readonly ViewHost Host;

        // The asset provider that created the instance and alone releases it; null for a direct prefab.
        public readonly IViewProvider Provider;

        public bool Released;

        public StageInstance(GameObject gameObject, ViewHost host, IViewProvider provider)
        {
            GameObject = gameObject;
            Host = host;
            Provider = provider;
        }
    }

    // Where a ViewHost reports that its object went away without the package.
    internal interface IViewHostSink
    {
        // The host's GameObject was destroyed (true) or deactivated (false) by something other than the package.
        void OnHostLost(object instance, bool destroyed);
    }
}
