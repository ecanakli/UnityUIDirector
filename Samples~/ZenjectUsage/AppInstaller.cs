using Ecanakli.UIDirector.DependencyInjection;
using UnityEngine;
using Zenject;

namespace Ecanakli.UIDirector.Samples.ZenjectUsage
{
    /// <summary>Binds the sample's services and the navigator; add it to the Mono Installers list of the scene's SceneContext.</summary>
    public sealed class AppInstaller : MonoInstaller
    {
        // "Initialize On Awake" must be off on this root: Install initializes it with the container as the injector, and throws otherwise.
        [SerializeField] private ViewRoot _viewRoot;

        public override void InstallBindings()
        {
            // Before the root initialises, which warns when no EventSystem exists; never while validating.
            if (!Container.IsValidating)
                SampleEventSystem.EnsureExists();

            Container.Bind<ScoreService>().AsSingle();
            NavigatorInstaller.Install(Container, _viewRoot);
            Container.BindInterfacesTo<AppStart>().AsSingle();
        }
    }
}
