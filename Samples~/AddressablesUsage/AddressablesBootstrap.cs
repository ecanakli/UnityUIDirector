using Cysharp.Threading.Tasks;
using Ecanakli.UIDirector;
using UnityEngine;

namespace Ecanakli.UIDirector.Samples.AddressablesUsage
{
    /// <summary>Starts the sample: initialises the root and opens the gallery, naming the setup step when the asset fails to load.</summary>
    public sealed class AddressablesBootstrap : MonoBehaviour
    {
        // "Initialize On Awake" is off on this root; the bootstrap initialises it in Awake.
        [SerializeField] private ViewRoot _viewRoot;

        private void Awake()
        {
            // Before the root initialises, so the root finds an EventSystem.
            SampleEventSystem.EnsureExists();

            // AssetProvider stays empty, so the AddressablesViewProvider on the root's GameObject is used.
            _viewRoot.Initialize(new ViewRootSetup());
        }

        private void Start()
        {
            // Not awaited: Start cannot wait, and the method reads the result itself and never throws.
            OpenGalleryAsync().Forget();
        }

        private async UniTaskVoid OpenGalleryAsync()
        {
            var navigator = _viewRoot.Navigator;

            // Owned by the root, so the gallery ends only when the root does.
            var result = await navigator.ShowAsync(
                RemoteViews.Gallery,
                navigator.RootToken,
                new OpenOptions { Source = "boot" });

            // A gallery that was shown and later closed is not a failure.
            if (result.Rejected == RejectReason.None)
                return;

            Debug.LogWarning(
                $"The gallery did not open ({result.Rejected}): {result.Detail}. " +
                "Mark Prefabs/Gallery and Prefabs/Photo as addressable, as the sample README describes, " +
                "and create the Addressables settings if the project has none.");
        }
    }
}
