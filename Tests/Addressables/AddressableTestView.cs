using System;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests
{
    // The view on the provider test prefab; the class name matches the file because the saved prefab references it.
    [AddComponentMenu("")]
    public sealed class AddressableTestView : MonoBehaviour, IView<NoArgs, NoResult>
    {
        [NonSerialized] public int AwakeCount;
        [NonSerialized] public int OpenCount;

        // Awake calls across every instance since the last reset, so a destroyed instance's Awake still counts.
        public static int AwakeTotal { get; private set; }

        public static void ResetAwakeTotal()
        {
            AwakeTotal = 0;
        }

        public void OnOpen(NoArgs args, ViewContext<NoResult> context)
        {
            OpenCount++;
        }

        private void Awake()
        {
            AwakeCount++;
            AwakeTotal++;
        }
    }
}
