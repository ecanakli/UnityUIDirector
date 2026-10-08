using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.DependencyInjection.Tests
{
    [TestFixture]
    public sealed class NavigatorInstallerTests
    {
        private const float DeadlineSeconds = 5f;
        private const string ViewId = "injected";
        private const string ChildName = "InjectedChild";
        private const string ToastsLayerId = "toasts";
        private const string AssetKey = "views/injected";

        private static readonly ViewKey<NoArgs, NoResult> InjectedKey = ViewKey.Create(ViewId);

        private readonly List<Object> _created = new List<Object>();
        private CancellationTokenSource _owner;
        private Application.LogCallback _logListener;

        [SetUp]
        public void SetUp()
        {
            _owner = new CancellationTokenSource();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _owner.Cancel();
            _owner.Dispose();

            if (_logListener != null)
            {
                Application.logMessageReceived -= _logListener;
                _logListener = null;
            }

            for (var i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.Destroy(_created[i]);
            }

            _created.Clear();

            // Destroy is deferred to the end of the frame.
            yield return null;
        }

        [Test]
        public void Install_OnRuntimeContainer_InitialisesTheRoot()
        {
            var container = new DiContainer();
            var root = CreateRoot();

            NavigatorInstaller.Install(container, root);

            Assert.That(root.IsInitialized, Is.True);
        }

        [Test]
        public void Install_OnRuntimeContainer_BindsTheRootsOwnNavigatorAndTheRoot()
        {
            var container = new DiContainer();
            var root = CreateRoot();

            NavigatorInstaller.Install(container, root);

            Assert.That(container.Resolve<INavigator>(), Is.SameAs(root.Navigator), "INavigator");
            Assert.That(container.Resolve<ViewRoot>(), Is.SameAs(root), "ViewRoot");
        }

        [Test]
        public void Install_OnRuntimeContainer_BindsNavigatorForConstructorInjection()
        {
            var container = new DiContainer();
            var root = CreateRoot();
            NavigatorInstaller.Install(container, root);
            container.Bind<NavigatorConsumer>().AsSingle();

            var consumer = container.Resolve<NavigatorConsumer>();

            Assert.That(consumer.Navigator, Is.SameAs(root.Navigator), "INavigator");
            Assert.That(consumer.Root, Is.SameAs(root), "ViewRoot");
        }

        [UnityTest]
        public IEnumerator Install_ViewOpenedThroughNavigator_IsInjectedBeforeAwakeAndOnOpen()
        {
            var container = new DiContainer();
            var value = new InjectedTestValue("app-value");
            container.BindInstance(value);
            var root = CreateRoot();
            NavigatorInstaller.Install(container, root);
            var navigator = container.Resolve<INavigator>();
            var template = CreateTemplate("InjectedTemplate");
            Register(navigator, template, ViewRetention.ReleaseOnClose);
            // A real prefab is active itself; the template's own view must never have woken.
            Assume.That(template.activeSelf, Is.True, "template active itself");
            Assume.That(template.activeInHierarchy, Is.False, "template asleep under its holder");
            Assume.That(template.GetComponent<InjectedTestView>().Events, Is.Empty, "template events");

            var handle = navigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");

            var view = ViewOf(handle);
            Assert.That(view.Events.Count, Is.GreaterThan(0), "events recorded");
            Assert.That(view.Events[0], Is.EqualTo(InjectedTestView.InjectEvent), "first event: " + string.Join("|", view.Events));
            Assert.That(view.Events, Is.EquivalentTo(new[] { InjectedTestView.InjectEvent, InjectedTestView.AwakeEvent, InjectedTestView.OnOpenEvent }), "events: " + string.Join("|", view.Events));
            Assert.That(view.ActiveInHierarchyAtInject, Is.False, "active in hierarchy at inject (activeSelf " + view.ActiveSelfAtInject + ")");
            Assert.That(view.Value, Is.SameAs(value), "injected value");
            Assert.That(view.ValueSeenByAwake, Is.SameAs(value), "value seen by Awake");
            Assert.That(view.ValueSeenByOnOpen, Is.SameAs(value), "value seen by OnOpen");
        }

        [UnityTest]
        public IEnumerator Install_ViewWithInjectedChild_ChildIsInjectedBeforeItsAwake()
        {
            var container = new DiContainer();
            var value = new InjectedTestValue("app-value");
            container.BindInstance(value);
            var root = CreateRoot();
            NavigatorInstaller.Install(container, root);
            var navigator = container.Resolve<INavigator>();
            Register(navigator, CreateTemplate("ParentTemplate", withInjectedChild: true), ViewRetention.ReleaseOnClose);

            var handle = navigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");

            var view = ViewOf(handle);
            var childTransform = view.transform.Find(ChildName);
            Assert.That(childTransform, Is.Not.Null, "child object on the instance");
            var child = childTransform.GetComponent<InjectedTestView>();
            Assert.That(child, Is.Not.Null, "injected component on the child");
            Assert.That(child.Value, Is.SameAs(value), "child's injected value");
            Assert.That(child.InjectCount, Is.EqualTo(1), "child injections: " + string.Join("|", child.Events));
            Assert.That(child.Events.Count, Is.GreaterThan(0), "child events recorded");
            Assert.That(child.Events[0], Is.EqualTo(InjectedTestView.InjectEvent), "child first event: " + string.Join("|", child.Events));
            Assert.That(child.Events, Does.Contain(InjectedTestView.AwakeEvent), "child woke: " + string.Join("|", child.Events));
            Assert.That(child.ValueSeenByAwake, Is.SameAs(value), "value seen by the child's Awake");
            Assert.That(child.ActiveInHierarchyAtInject, Is.False, "child active in hierarchy at inject");
            Assert.That(view.Value, Is.SameAs(value), "view's injected value");
            Assert.That(value.InjectCount, Is.EqualTo(2), "injections across view and child");
        }

        [UnityTest]
        public IEnumerator Install_ReleasedViewReopened_NewInstanceIsInjected()
        {
            var container = new DiContainer();
            var value = new InjectedTestValue("app-value");
            container.BindInstance(value);
            var root = CreateRoot();
            NavigatorInstaller.Install(container, root);
            var navigator = container.Resolve<INavigator>();
            Register(navigator, CreateTemplate("ReleasedTemplate"), ViewRetention.ReleaseOnClose);

            var first = navigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => first.Phase == ViewPhase.Open, "the first open");
            var firstView = ViewOf(first);
            first.Close();
            yield return WaitFor(() => first.Phase == ViewPhase.Ended, "the first close");
            var second = navigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => second.Phase == ViewPhase.Open, "the reopen");

            var secondView = ViewOf(second);
            Assert.That(ReferenceEquals(secondView, firstView), Is.False, "released instance replaced by a new one");
            Assert.That(secondView.InjectCount, Is.EqualTo(1), "injections of the new instance: " + string.Join("|", secondView.Events));
            Assert.That(secondView.Events.Count, Is.GreaterThan(0), "new instance events recorded");
            Assert.That(secondView.Events[0], Is.EqualTo(InjectedTestView.InjectEvent), "new instance first event: " + string.Join("|", secondView.Events));
            Assert.That(secondView.Value, Is.SameAs(value), "new instance's injected value");
            Assert.That(value.InjectCount, Is.EqualTo(2), "injections across all instances");
        }

        [UnityTest]
        public IEnumerator Install_OnAppContainerWithSceneSubContainer_ViewIsInjectedFromTheAppContainer()
        {
            var appContainer = new DiContainer();
            var appValue = new InjectedTestValue("app");
            appContainer.BindInstance(appValue);
            var root = CreateRoot();
            NavigatorInstaller.Install(appContainer, root);
            var sceneContainer = appContainer.CreateSubContainer();
            var sceneValue = new InjectedTestValue("scene");
            sceneContainer.BindInstance(sceneValue);
            // A scene class gets the app-level navigator through its own container.
            var navigator = sceneContainer.Resolve<INavigator>();
            Assume.That(navigator, Is.SameAs(root.Navigator), "scene container sees the app navigator");
            Register(navigator, CreateTemplate("AppTemplate"), ViewRetention.ReleaseOnClose);

            var handle = navigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");

            var view = ViewOf(handle);
            Assert.That(view.Value, Is.SameAs(appValue), "injected value: " + (view.Value == null ? "null" : view.Value.Name));
            Assert.That(view.Value, Is.Not.SameAs(sceneValue), "scene binding reached the view");
            Assert.That(appValue.InjectCount, Is.EqualTo(1), "app container's injections");
            Assert.That(sceneValue.InjectCount, Is.EqualTo(0), "scene container's injections");
        }

        [Test]
        public void Install_OnValidatingContainer_BindsNavigatorAndRoot()
        {
            var container = new DiContainer(true);
            var root = CreateRoot();

            NavigatorInstaller.Install(container, root);

            Assert.That(container.HasBinding<INavigator>(), Is.True, "INavigator");
            Assert.That(container.HasBinding<ViewRoot>(), Is.True, "ViewRoot");
        }

        [Test]
        public void Install_OnValidatingContainer_LeavesTheRootUninitialised()
        {
            var container = new DiContainer(true);
            var root = CreateRoot();

            NavigatorInstaller.Install(container, root);

            Assert.That(root.IsInitialized, Is.False);
        }

        [Test]
        public void ResolveRoots_OnValidatingContainerAfterInstall_PassesWithoutInitialisingTheRoot()
        {
            var container = new DiContainer(true);
            // Throw instead of log, and resolve every binding rather than only the non-lazy ones.
            container.Settings = new ZenjectSettings(ValidationErrorResponses.Throw, RootResolveMethods.All);
            var root = CreateRoot();
            NavigatorInstaller.Install(container, root);
            container.Bind<NavigatorConsumer>().AsSingle().NonLazy();

            Assert.That(() => container.ResolveRoots(), Throws.Nothing, "validation");
            Assert.That(root.IsInitialized, Is.False, "root initialised by validation");
        }

        [Test]
        public void Install_OnRootInitialisedOnAwake_ThrowsInvalidOperationExceptionNamingTheSetting()
        {
            var container = new DiContainer();
            var root = CreateRoot(initializeOnAwake: true);

            var exception = Assert.Throws<InvalidOperationException>(() => NavigatorInstaller.Install(container, root));

            Assert.That(exception.Message, Does.Contain("Initialize On Awake"));
            Assert.That(exception.Message.ToLowerInvariant(), Does.Contain("turn off"), "instruction in: " + exception.Message);
        }

        [Test]
        public void Install_OnRootInitialisedByNavigatorAccess_ThrowsInvalidOperationExceptionNamingTheSetting()
        {
            var container = new DiContainer();
            var root = CreateRoot();
            Assume.That(root.Navigator, Is.Not.Null);

            var exception = Assert.Throws<InvalidOperationException>(() => NavigatorInstaller.Install(container, root));

            Assert.That(exception.Message, Does.Contain("Initialize On Awake"));
            Assert.That(exception.Message.ToLowerInvariant(), Does.Contain("turn off"), "instruction in: " + exception.Message);
        }

        [Test]
        public void Install_NullContainer_ThrowsArgumentNullException()
        {
            var root = CreateRoot();

            var exception = Assert.Throws<ArgumentNullException>(() => NavigatorInstaller.Install(null, root));

            Assert.That(exception.ParamName, Is.EqualTo("container"));
        }

        [Test]
        public void Install_NullRoot_ThrowsArgumentNullException()
        {
            var container = new DiContainer();

            var exception = Assert.Throws<ArgumentNullException>(() => NavigatorInstaller.Install(container, null));

            Assert.That(exception.ParamName, Is.EqualTo("root"));
        }

        [UnityTest]
        public IEnumerator Install_KeptViewReopened_IsNotInjectedAgain()
        {
            var container = new DiContainer();
            var value = new InjectedTestValue("app-value");
            container.BindInstance(value);
            var root = CreateRoot();
            NavigatorInstaller.Install(container, root);
            var navigator = container.Resolve<INavigator>();
            Register(navigator, CreateTemplate("KeptTemplate"), ViewRetention.KeepInstance);

            var first = navigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => first.Phase == ViewPhase.Open, "the first open");
            var firstView = ViewOf(first);
            first.Close();
            yield return WaitFor(() => first.Phase == ViewPhase.Ended, "the first close");
            var second = navigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => second.Phase == ViewPhase.Open, "the reopen");

            var secondView = ViewOf(second);
            Assert.That(secondView, Is.SameAs(firstView), "kept instance reused");
            Assert.That(secondView.InjectCount, Is.EqualTo(1), "injections of the kept instance: " + string.Join("|", secondView.Events));
            Assert.That(value.InjectCount, Is.EqualTo(1), "injections across all instances");
        }

        [Test]
        public void Install_TwoRootsWithTwoContainers_EachContainerResolvesItsOwnRoot()
        {
            var firstContainer = new DiContainer();
            var secondContainer = new DiContainer();
            var firstRoot = CreateRoot("FirstRoot");
            var secondRoot = CreateRoot("SecondRoot");

            NavigatorInstaller.Install(firstContainer, firstRoot);
            NavigatorInstaller.Install(secondContainer, secondRoot);

            Assert.That(firstContainer.Resolve<INavigator>(), Is.SameAs(firstRoot.Navigator), "first INavigator");
            Assert.That(secondContainer.Resolve<INavigator>(), Is.SameAs(secondRoot.Navigator), "second INavigator");
            Assert.That(firstRoot.Navigator, Is.Not.SameAs(secondRoot.Navigator), "distinct navigators");
            Assert.That(firstContainer.Resolve<ViewRoot>(), Is.SameAs(firstRoot), "first ViewRoot");
            Assert.That(secondContainer.Resolve<ViewRoot>(), Is.SameAs(secondRoot), "second ViewRoot");
        }

        [UnityTest]
        public IEnumerator Install_TwoRootsWithTwoContainers_EachViewIsInjectedByItsOwnContainer()
        {
            var firstContainer = new DiContainer();
            var secondContainer = new DiContainer();
            var firstValue = new InjectedTestValue("first");
            var secondValue = new InjectedTestValue("second");
            firstContainer.BindInstance(firstValue);
            secondContainer.BindInstance(secondValue);
            var firstRoot = CreateRoot("FirstRoot");
            var secondRoot = CreateRoot("SecondRoot");
            NavigatorInstaller.Install(firstContainer, firstRoot);
            NavigatorInstaller.Install(secondContainer, secondRoot);
            var firstNavigator = firstContainer.Resolve<INavigator>();
            var secondNavigator = secondContainer.Resolve<INavigator>();
            var template = CreateTemplate("SharedTemplate");
            Register(firstNavigator, template, ViewRetention.ReleaseOnClose);
            Register(secondNavigator, template, ViewRetention.ReleaseOnClose);

            var firstHandle = firstNavigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => firstHandle.Phase == ViewPhase.Open, "the view on the first root");
            var secondOpenBeforeItsOwnOpen = secondNavigator.IsOpen(ViewId);
            var secondHandle = secondNavigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => secondHandle.Phase == ViewPhase.Open, "the view on the second root");

            var firstView = ViewOf(firstHandle);
            var secondView = ViewOf(secondHandle);
            Assert.That(secondOpenBeforeItsOwnOpen, Is.False, "second root open before its own open");
            Assert.That(firstView.Value, Is.SameAs(firstValue), "first view's value");
            Assert.That(secondView.Value, Is.SameAs(secondValue), "second view's value");
            Assert.That(firstValue.InjectCount, Is.EqualTo(1), "first container's injections");
            Assert.That(secondValue.InjectCount, Is.EqualTo(1), "second container's injections");
            Assert.That(firstView.transform.IsChildOf(firstRoot.transform), Is.True, "first view under the first root");
            Assert.That(secondView.transform.IsChildOf(secondRoot.transform), Is.True, "second view under the second root");
        }

        [UnityTest]
        public IEnumerator Install_WithSetupExtraLayer_OpensARowOnThatLayer()
        {
            var container = new DiContainer();
            var value = new InjectedTestValue("app-value");
            container.BindInstance(value);
            var root = CreateRoot();
            var diagnosticIds = RecordDiagnosticIds(root);
            var toastsLayer = new ViewLayer(ToastsLayerId, 3, ViewLayerRules.None);
            var setup = new ViewRootSetup { ExtraLayers = new[] { toastsLayer } };
            NavigatorInstaller.Install(container, root, setup);
            var navigator = container.Resolve<INavigator>();
            AddRow(navigator, CreateTemplate("ToastTemplate"), null, ToastsLayerId, ViewRetention.ReleaseOnClose);

            var handle = navigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the view on the extra layer to open");

            var view = ViewOf(handle);
            var toastsRect = root.Stage.LayerRect(toastsLayer);
            Assert.That(diagnosticIds, Does.Not.Contain(DiagnosticIds.UnknownLayer), "the row on the extra layer was refused as an unknown layer");
            Assert.That(toastsRect, Is.Not.Null, "the stage built a layer for the extra layer");
            Assert.That(view.transform.parent, Is.SameAs(toastsRect), "the view sits directly under the extra layer's rect");
            Assert.That(view.Value, Is.SameAs(value), "the view on the extra layer is injected by the container");
            Assert.That(view.Events, Does.Contain(InjectedTestView.OnOpenEvent), "the view on the extra layer reaches OnOpen: " + string.Join("|", view.Events));
        }

        [UnityTest]
        public IEnumerator Install_WithSetupErrorHandler_RoutesAViewExceptionToIt()
        {
            var container = new DiContainer();
            var root = CreateRoot();
            // The thrown OnOpen also reports UIDIRECTOR117, which would otherwise reach the console as an error.
            var diagnostics = RecordDiagnostics(root);
            var spy = new ErrorHandlerSpy();
            var setup = new ViewRootSetup { ErrorHandler = spy.Handle };
            NavigatorInstaller.Install(container, root, setup);
            var navigator = container.Resolve<INavigator>();
            var template = CreateBareTemplate("ThrowingTemplate");
            template.AddComponent<ThrowingOnOpenView>();
            Register(navigator, template, ViewRetention.ReleaseOnClose);
            var exceptionLogs = CaptureExceptionLogs();

            var handle = navigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => handle.Phase == ViewPhase.Ended, "the failed open to end");

            Assert.That(
                spy.Received,
                Has.Exactly(1).Matches<Exception>(exception => exception is InvalidOperationException && exception.Message == ThrowingOnOpenView.FailureMessage),
                "the handler receives the view's OnOpen exception once; received: " + spy.Describe());
            Assert.That(exceptionLogs, Is.Empty, "the exception also went to Debug.LogException");
            Assert.That(
                diagnostics,
                Has.Some.Matches<DiagnosticEntry>(entry => entry.Id == DiagnosticIds.OnOpenThrew && entry.Message != null && entry.Message.Contains("'" + ViewId + "'")),
                "UIDIRECTOR117 names the view whose OnOpen threw; recorded: " + Describe(diagnostics));
        }

        [UnityTest]
        public IEnumerator Install_ViewWithAnUnboundDependency_EndsNotShownLoadFailed_AndRoutesTheExceptionAsInject()
        {
            UnboundDependencyView.ResetCounts();
            var container = new DiContainer();
            var root = CreateRoot();
            // The failed inject also reports UIDIRECTOR104, which would otherwise reach the console as an error.
            var diagnostics = RecordDiagnostics(root);
            var spy = new ErrorHandlerSpy();
            var setup = new ViewRootSetup { ErrorHandler = spy.Handle };
            NavigatorInstaller.Install(container, root, setup);
            var navigator = container.Resolve<INavigator>();
            var template = CreateBareTemplate("UnboundTemplate");
            template.AddComponent<UnboundDependencyView>();
            Register(navigator, template, ViewRetention.ReleaseOnClose);

            var completed = false;
            var result = default(ViewResult<NoResult>);
            navigator.ShowAsync(InjectedKey, _owner.Token)
                .ContinueWith(outcome =>
                {
                    result = outcome;
                    completed = true;
                })
                .Forget(); // Observed through the completed flag; ShowAsync never throws for the view.
            yield return WaitFor(() => completed, "the failed show to end");

            Assert.That(result.WasShown, Is.False, "the view was shown");
            Assert.That(result.Reason, Is.EqualTo(CloseReason.NotShown), "close reason");
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.LoadFailed), "reject reason");
            Assert.That(spy.Received.Count, Is.EqualTo(1), "the handler receives the inject exception once; received: " + spy.Describe());
            Assert.That(spy.Received[0], Is.InstanceOf<ZenjectException>(), "the routed exception comes from the container");
            Assert.That(spy.Contexts[0].Source, Is.EqualTo(ViewErrorSource.Inject), "the routed error's source");
            Assert.That(spy.Contexts[0].ViewId, Is.EqualTo(ViewId), "the routed error's view id");
            Assert.That(spy.Contexts[0].RequestId, Is.EqualTo(result.RequestId), "the routed error's request id");
            Assert.That(result.Detail, Is.EqualTo(spy.Received[0].Message), "the result's detail carries the exception message");
            Assert.That(
                diagnostics,
                Has.Exactly(1).Matches<DiagnosticEntry>(entry => entry.Id == DiagnosticIds.LoadFailed && entry.Message != null && entry.Message.StartsWith("Injecting '" + ViewId + "' failed", StringComparison.Ordinal)),
                "UIDIRECTOR104 says injecting the view failed, once; recorded: " + Describe(diagnostics));
            Assert.That(UnboundDependencyView.AwakeCount, Is.EqualTo(0), "Awake ran on a view whose injection failed");
            Assert.That(UnboundDependencyView.OnOpenCount, Is.EqualTo(0), "OnOpen ran on a view whose injection failed");
        }

        [UnityTest]
        public IEnumerator Install_WithSetupAssetProvider_LoadsAssetKeyRowsThroughItAndInjectsTheInstance()
        {
            var container = new DiContainer();
            var value = new InjectedTestValue("app-value");
            container.BindInstance(value);
            var root = CreateRoot();
            var provider = new StubViewProvider(CreateTemplate("ProvidedTemplate"));
            var setup = new ViewRootSetup { AssetProvider = provider };
            NavigatorInstaller.Install(container, root, setup);
            var navigator = container.Resolve<INavigator>();
            AddRow(navigator, null, AssetKey, ViewLayers.Screens.Id, ViewRetention.ReleaseOnClose);

            var handle = navigator.Open(InjectedKey, _owner.Token);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the asset key view to open");

            var view = ViewOf(handle);
            Assert.That(provider.RequestedKeys, Is.EqualTo(new[] { AssetKey }), "the provider is asked once for the row's key");
            Assert.That(view.Events.Count, Is.GreaterThan(0), "events recorded");
            Assert.That(view.Events[0], Is.EqualTo(InjectedTestView.InjectEvent), "the provided instance is injected before Awake: " + string.Join("|", view.Events));
            Assert.That(view.ActiveInHierarchyAtInject, Is.False, "the provided instance is asleep at inject");
            Assert.That(view.ValueSeenByAwake, Is.SameAs(value), "Awake sees the injected value");
        }

        [Test]
        public void Install_SetupWithInject_ThrowsArgumentException([Values] bool validating)
        {
            var container = new DiContainer(validating);
            var root = CreateRoot();
            var setup = new ViewRootSetup { Inject = _ => { } };

            var exception = Assert.Throws<ArgumentException>(() => NavigatorInstaller.Install(container, root, setup));

            Assert.That(exception.ParamName, Is.EqualTo("setup"), "parameter name");
            Assert.That(root.IsInitialized, Is.False, "a rejected setup leaves the root uninitialised");
            Assert.That(container.HasBinding<ViewRoot>(), Is.False, "a rejected setup binds nothing");
        }

        [Test]
        public void Install_NullSetup_ThrowsArgumentNullException([Values] bool validating)
        {
            var container = new DiContainer(validating);
            var root = CreateRoot();

            var exception = Assert.Throws<ArgumentNullException>(() => NavigatorInstaller.Install(container, root, null));

            Assert.That(exception.ParamName, Is.EqualTo("setup"));
        }

        [Test]
        public void Install_NullContainerAndNullSetup_ThrowsArgumentNullExceptionForContainer()
        {
            var root = CreateRoot();

            var exception = Assert.Throws<ArgumentNullException>(() => NavigatorInstaller.Install(null, root, null));

            Assert.That(exception.ParamName, Is.EqualTo("container"));
        }

        [Test]
        public void Install_NullRootAndNullSetup_ThrowsArgumentNullExceptionForRoot()
        {
            var container = new DiContainer();

            var exception = Assert.Throws<ArgumentNullException>(() => NavigatorInstaller.Install(container, null, null));

            Assert.That(exception.ParamName, Is.EqualTo("root"));
        }

        [Test]
        public void Install_SetupWithInjectAndNullRoot_ThrowsArgumentNullExceptionForRoot()
        {
            var container = new DiContainer();
            var setup = new ViewRootSetup { Inject = _ => { } };

            var exception = Assert.Throws<ArgumentNullException>(() => NavigatorInstaller.Install(container, null, setup));

            Assert.That(exception.ParamName, Is.EqualTo("root"));
        }

        [Test]
        public void Install_WithSetupOnRootInitialisedByNavigatorAccess_ThrowsInvalidOperationExceptionNamingTheSetting()
        {
            var container = new DiContainer();
            var root = CreateRoot();
            Assume.That(root.Navigator, Is.Not.Null);
            var setup = new ViewRootSetup { ErrorHandler = new ErrorHandlerSpy().Handle };

            var exception = Assert.Throws<InvalidOperationException>(() => NavigatorInstaller.Install(container, root, setup));

            Assert.That(exception.Message, Does.Contain("Initialize On Awake"));
            Assert.That(exception.Message.ToLowerInvariant(), Does.Contain("turn off"), "instruction in: " + exception.Message);
            Assert.That(setup.Inject, Is.Null, "the installer wrote its injector into the caller's setup");
        }

        [Test]
        public void Install_WithSetup_LeavesTheCallersSetupUnchanged()
        {
            var container = new DiContainer();
            var root = CreateRoot();
            var layers = new[] { new ViewLayer(ToastsLayerId, 3, ViewLayerRules.None) };
            var spy = new ErrorHandlerSpy();
            ViewErrorHandler handler = spy.Handle;
            var provider = new StubViewProvider(null);
            var setup = new ViewRootSetup { ExtraLayers = layers, ErrorHandler = handler, AssetProvider = provider };

            NavigatorInstaller.Install(container, root, setup);

            Assert.That(root.IsInitialized, Is.True, "root initialised");
            Assert.That(setup.Inject, Is.Null, "the installer wrote its injector into the caller's setup");
            Assert.That(setup.ExtraLayers, Is.SameAs(layers), "the caller's extra layers");
            Assert.That(setup.ErrorHandler, Is.SameAs(handler), "the caller's error handler");
            Assert.That(setup.AssetProvider, Is.SameAs(provider), "the caller's asset provider");
        }

        [Test]
        public void Install_SameSetupUsedForTwoRoots_InitialisesBoth()
        {
            var firstContainer = new DiContainer();
            var secondContainer = new DiContainer();
            var firstRoot = CreateRoot("FirstRoot");
            var secondRoot = CreateRoot("SecondRoot");
            var setup = new ViewRootSetup
            {
                ExtraLayers = new[] { new ViewLayer(ToastsLayerId, 3, ViewLayerRules.None) },
                ErrorHandler = new ErrorHandlerSpy().Handle
            };

            Assert.That(() => NavigatorInstaller.Install(firstContainer, firstRoot, setup), Throws.Nothing, "first install");
            Assert.That(() => NavigatorInstaller.Install(secondContainer, secondRoot, setup), Throws.Nothing, "second install with the same setup");

            Assert.That(firstRoot.IsInitialized, Is.True, "first root initialised");
            Assert.That(secondRoot.IsInitialized, Is.True, "second root initialised");
            Assert.That(setup.Inject, Is.Null, "the installer wrote its injector into the shared setup");
        }

        [Test]
        public void Install_WithSetupOnValidatingContainer_BindsAndLeavesTheRootUninitialised()
        {
            var container = new DiContainer(true);
            var root = CreateRoot();

            NavigatorInstaller.Install(container, root, new ViewRootSetup());

            Assert.That(container.HasBinding<INavigator>(), Is.True, "INavigator");
            Assert.That(container.HasBinding<ViewRoot>(), Is.True, "ViewRoot");
            Assert.That(root.IsInitialized, Is.False, "root initialised while validating");
        }

        [Test]
        public void ResolveRoots_OnValidatingContainerAfterInstallWithSetup_PassesWithoutInitialisingTheRoot()
        {
            var container = new DiContainer(true);
            // Throw instead of log, and resolve every binding rather than only the non-lazy ones.
            container.Settings = new ZenjectSettings(ValidationErrorResponses.Throw, RootResolveMethods.All);
            var root = CreateRoot();
            NavigatorInstaller.Install(container, root, new ViewRootSetup());
            container.Bind<NavigatorConsumer>().AsSingle().NonLazy();

            Assert.That(() => container.ResolveRoots(), Throws.Nothing, "validation");
            Assert.That(root.IsInitialized, Is.False, "root initialised by validation");
        }

        [Test]
        public void ViewRootSetup_PublicFields_AreExactlyTheOnesTheInstallerForwards()
        {
            var names = new List<string>();
            foreach (var field in typeof(ViewRootSetup).GetFields(BindingFlags.Public | BindingFlags.Instance))
                names.Add(field.Name);

            Assert.That(
                names,
                Is.EquivalentTo(new[] { "Inject", "AssetProvider", "ExtraLayers", "ErrorHandler" }),
                "ViewRootSetup's public fields changed; forward every new member in NavigatorInstaller.Install's copy of the setup, then update this list.");
        }

        // An uninitialised root unless initializeOnAwake: the object starts inactive so the field is set before Awake.
        private ViewRoot CreateRoot(string name = "TestRoot", bool initializeOnAwake = false)
        {
            var rootObject = Track(new GameObject(name, typeof(RectTransform)));
            rootObject.SetActive(false);

            var canvas = rootObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var root = rootObject.AddComponent<ViewRoot>();
            SetField(root, "_initializeOnAwake", initializeOnAwake);

            rootObject.SetActive(true);
            return root;
        }

        // A template shaped like a prefab: active itself, parented under an inactive holder before any component, so it never wakes.
        // ViewHost's RequireComponent adds the Canvas and GraphicRaycaster.
        private GameObject CreateTemplate(string name, bool withInjectedChild = false)
        {
            var template = CreateBareTemplate(name);
            template.AddComponent<InjectedTestView>();

            if (withInjectedChild)
            {
                var child = new GameObject(ChildName, typeof(RectTransform));
                child.transform.SetParent(template.transform, false);
                child.AddComponent<InjectedTestView>();
            }

            return template;
        }

        // The template with its ViewHost and no view component yet.
        private GameObject CreateBareTemplate(string name)
        {
            var holder = Track(new GameObject(name + "Holder"));
            holder.SetActive(false);

            var template = new GameObject(name, typeof(RectTransform));
            template.transform.SetParent(holder.transform, false);

            var rect = (RectTransform)template.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            template.AddComponent<ViewHost>();
            return template;
        }

        private void Register(INavigator navigator, GameObject prefab, ViewRetention retention)
        {
            AddRow(navigator, prefab, null, ViewLayers.Screens.Id, retention);
        }

        private void AddRow(INavigator navigator, GameObject prefab, string assetKey, string layerId, ViewRetention retention)
        {
            var row = new ViewEntry(
                ViewId,
                prefab,
                assetKey,
                layerId,
                retention,
                BackPolicy.LayerDefault,
                false,
                CoverPolicy.LayerDefault,
                CoveredMode.DisableCanvas,
                false);
            var catalog = Track(ViewCatalog.Create(new[] { row }));
            navigator.AddCatalog(catalog, _owner.Token);
        }

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        private static InjectedTestView ViewOf(ViewHandle handle)
        {
            GameObject instance;
            Assert.That(handle.TryGetInstance(out instance), Is.True, "live instance of " + handle.ViewId);
            var view = instance.GetComponent<InjectedTestView>();
            Assert.That(view, Is.Not.Null, "InjectedTestView on the instance");
            return view;
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, name);

            field.SetValue(target, value);
        }

        // Routes the root's diagnostics to a list of ids instead of the console; set before the root initialises.
        private static List<string> RecordDiagnosticIds(ViewRoot root)
        {
            var field = typeof(ViewRoot).GetField("_diagnostics", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(nameof(ViewRoot), "_diagnostics");

            var ids = new List<string>();
            var diagnostics = (Diagnostics)field.GetValue(root);
            diagnostics.Hook = entry => ids.Add(entry.Id ?? "(fault)");
            return ids;
        }

        // Routes the root's diagnostics to a list of entries instead of the console; set before the root initialises.
        private static List<DiagnosticEntry> RecordDiagnostics(ViewRoot root)
        {
            var field = typeof(ViewRoot).GetField("_diagnostics", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(nameof(ViewRoot), "_diagnostics");

            var entries = new List<DiagnosticEntry>();
            var diagnostics = (Diagnostics)field.GetValue(root);
            diagnostics.Hook = entry => entries.Add(entry);
            return entries;
        }

        private static string Describe(List<DiagnosticEntry> entries)
        {
            var parts = new List<string>(entries.Count);
            foreach (var entry in entries)
                parts.Add((entry.Id ?? "(fault)") + ": " + entry.Message);
            return entries.Count + " [" + string.Join(" | ", parts) + "]";
        }

        // Collects every exception logged to the console until tear-down.
        private List<string> CaptureExceptionLogs()
        {
            var logs = new List<string>();
            _logListener = (condition, stackTrace, type) =>
            {
                if (type == LogType.Exception)
                    logs.Add(condition);
            };
            Application.logMessageReceived += _logListener;
            return logs;
        }

        // Yields a frame at a time; fails after a real-time deadline instead of hanging the run.
        private static IEnumerator WaitFor(Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail("Timed out after " + DeadlineSeconds + " s waiting for " + what);
                yield return null;
            }
        }

        // A service that takes the bindings through its constructor, as an app-level service would.
        public sealed class NavigatorConsumer
        {
            public NavigatorConsumer(INavigator navigator, ViewRoot root)
            {
                Navigator = navigator;
                Root = root;
            }

            public INavigator Navigator { get; }
            public ViewRoot Root { get; }
        }

        // A view whose OnOpen always throws.
        [AddComponentMenu("")]
        private sealed class ThrowingOnOpenView : ViewBehaviour
        {
            public const string FailureMessage = "OnOpen failed on purpose.";

            protected override void OnOpen(NoArgs args, ViewScope open)
            {
                throw new InvalidOperationException(FailureMessage);
            }
        }

        // A view whose [Inject] method needs a type no container binds; counts its Awake and OnOpen across instances.
        [AddComponentMenu("")]
        private sealed class UnboundDependencyView : ViewBehaviour
        {
            public static int AwakeCount { get; private set; }
            public static int OnOpenCount { get; private set; }

            public static void ResetCounts()
            {
                AwakeCount = 0;
                OnOpenCount = 0;
            }

            [Inject]
            public void Construct(UnboundService service)
            {
            }

            private void Awake()
            {
                AwakeCount++;
            }

            protected override void OnOpen(NoArgs args, ViewScope open)
            {
                OnOpenCount++;
            }
        }

        // Never bound, so injecting UnboundDependencyView fails.
        private sealed class UnboundService
        {
        }

        // Records every exception the root routes to the game's handler, with its context.
        private sealed class ErrorHandlerSpy
        {
            public readonly List<Exception> Received = new List<Exception>();
            public readonly List<ViewErrorContext> Contexts = new List<ViewErrorContext>();

            public void Handle(Exception exception, in ViewErrorContext context)
            {
                Received.Add(exception);
                Contexts.Add(context);
            }

            public string Describe()
            {
                var parts = new List<string>(Received.Count);
                foreach (var exception in Received)
                    parts.Add(exception.GetType().Name + ": " + exception.Message);
                return Received.Count + " [" + string.Join(" | ", parts) + "]";
            }
        }

        // Instantiates the template under the given inactive parent and records the requested keys.
        private sealed class StubViewProvider : IViewProvider
        {
            private readonly GameObject _template;

            public StubViewProvider(GameObject template)
            {
                _template = template;
            }

            public List<string> RequestedKeys { get; } = new List<string>();

            public UniTask<GameObject> AcquireAsync(string assetKey, Transform inactiveParent, CancellationToken ct)
            {
                RequestedKeys.Add(assetKey);
                var instance = Object.Instantiate(_template, inactiveParent, false);
                return UniTask.FromResult(instance);
            }

            public void Release(GameObject instance)
            {
                if (instance != null)
                    Object.Destroy(instance);
            }

            public UniTask<IDisposable> PreloadAsync(string assetKey, CancellationToken ct)
            {
                return UniTask.FromResult<IDisposable>(new EmptyLease());
            }
        }

        private sealed class EmptyLease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
