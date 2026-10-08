using UnityEngine;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>Stand-in for a popup from an older UI system that the package does not run.</summary>
    public sealed class LegacyPopup : MonoBehaviour
    {
        public bool IsOpen
        {
            get { return gameObject.activeSelf; }
        }

        public void Show()
        {
            gameObject.SetActive(true);
        }

        public bool TryClose()
        {
            if (!IsOpen)
                return false;

            gameObject.SetActive(false);
            return true;
        }
    }
}
