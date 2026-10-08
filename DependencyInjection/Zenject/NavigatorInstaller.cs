using System;
using Zenject;

namespace Ecanakli.UIDirector.DependencyInjection
{
    /// <summary>Connects a <see cref="ViewRoot"/> to a Zenject container: the container injects every view instance, and <see cref="INavigator"/> and the root can be injected.</summary>
    public static class NavigatorInstaller
    {
        /// <summary>
        /// Initialises the root with the container as its injector and binds <see cref="INavigator"/> and <see cref="ViewRoot"/>.
        /// Call it from <c>InstallBindings</c>, with "Initialize On Awake" turned off on the root. While the container is
        /// validating it only binds and leaves the root untouched.
        /// </summary>
        /// <param name="container">The container that injects the views and receives the bindings.</param>
        /// <param name="root">The root to initialise; it must not be initialised yet.</param>
        /// <exception cref="ArgumentNullException">The container or the root is null.</exception>
        /// <exception cref="InvalidOperationException">The root was already initialised, for example by "Initialize On Awake".</exception>
        public static void Install(DiContainer container, ViewRoot root)
        {
            Install(container, root, new ViewRootSetup());
        }

        /// <summary>
        /// Initialises the root with the container as its injector and the given setup, and binds <see cref="INavigator"/> and <see cref="ViewRoot"/>.
        /// Call it from <c>InstallBindings</c>, with "Initialize On Awake" turned off on the root. The setup is copied, never changed.
        /// While the container is validating it only checks the arguments and binds, and leaves the root untouched.
        /// </summary>
        /// <param name="container">The container that injects the views and receives the bindings.</param>
        /// <param name="root">The root to initialise; it must not be initialised yet.</param>
        /// <param name="setup">Layers, error handler and asset provider for the root; Inject must be null.</param>
        /// <exception cref="ArgumentNullException">The container, the root or the setup is null.</exception>
        /// <exception cref="ArgumentException">The setup's <see cref="ViewRootSetup.Inject"/> is not null; the installer sets it to the container.</exception>
        /// <exception cref="InvalidOperationException">The root was already initialised, for example by "Initialize On Awake".</exception>
        public static void Install(DiContainer container, ViewRoot root, ViewRootSetup setup)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (setup == null)
                throw new ArgumentNullException(nameof(setup));
            if (setup.Inject != null)
                throw new ArgumentException("NavigatorInstaller sets ViewRootSetup.Inject to the container; leave it null.", nameof(setup));

            container.Bind<ViewRoot>().FromInstance(root).AsSingle();

            if (container.IsValidating)
            {
                // Validation never calls the method, so validation needs no initialised root.
                container.Bind<INavigator>().FromMethod(_ => root.Navigator).AsSingle();
                return;
            }

            if (root.IsInitialized)
            {
                throw new InvalidOperationException(
                    "The ViewRoot '" + root.name + "' was initialised before NavigatorInstaller.Install ran, so its views would not be injected. Turn off \"Initialize On Awake\" on it.");
            }

            root.Initialize(new ViewRootSetup
            {
                Inject = container.InjectGameObject,
                AssetProvider = setup.AssetProvider,
                ExtraLayers = setup.ExtraLayers,
                ErrorHandler = setup.ErrorHandler,
            });
            container.Bind<INavigator>().FromInstance(root.Navigator).AsSingle();
        }
    }
}
