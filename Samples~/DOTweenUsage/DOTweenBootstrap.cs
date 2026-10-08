using UnityEngine;

namespace Ecanakli.UIDirector.Samples.DOTweenUsage
{
    /// <summary>Starts the sample: initialises the root and opens the welcome screen.</summary>
    public sealed class DOTweenBootstrap : MonoBehaviour
    {
        // "Initialize On Awake" is off on this root; the bootstrap initialises it in Awake.
        [SerializeField] private ViewRoot _viewRoot;

        private void Awake()
        {
            // Before the root initialises, so the root finds an EventSystem.
            SampleEventSystem.EnsureExists();
            _viewRoot.Initialize(new ViewRootSetup());
        }

        private void Start()
        {
            // Owned by the root, so the welcome screen ends only when the root does.
            var navigator = _viewRoot.Navigator;
            navigator.Open(TweenViews.Welcome, navigator.RootToken, new OpenOptions { Source = "boot" });
        }
    }
}
