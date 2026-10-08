using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // ViewRootSetup on a real root: extra layers, and an Initialize that fails on a repeated layer and can be retried.
    [TestFixture]
    public sealed class ViewRootSetupTests
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 1f;

        private readonly List<Object> _created = new List<Object>();
        private StageWorld _world;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_world != null)
            {
                _world.Dispose();
                _world = null;
            }

            for (var i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.Destroy(_created[i]);
            }

            _created.Clear();

            // Destroy is deferred; the next test must not see these objects.
            yield return null;
        }

        [UnityTest]
        public IEnumerator Initialize_ExtraLayersWithEqualOrder_KeepGivenOrderAndAreSortedAmongTheDefaults()
        {
            var extras = new[]
            {
                new ViewLayer("zeta", 1, ViewLayerRules.None),
                new ViewLayer("early", -1, ViewLayerRules.None),
                new ViewLayer("alpha", 1, ViewLayerRules.None)
            };

            _world = new StageWorld(setup: new ViewRootSetup { ExtraLayers = extras });
            yield return null;

            var described = new StringBuilder();
            _world.Navigator.Describe(described);
            Assert.That(
                LayerChildNames(_world.Root.transform),
                Is.EqualTo(new[] { "Layer early", "Layer screens", "Layer popups", "Layer zeta", "Layer alpha", "Layer system" }),
                "layer objects under the root, in sibling order");
            Assert.That(
                described.ToString(),
                Does.Contain("layers: early(-1) screens(0) popups(1) zeta(1) alpha(1) system(2)\n"),
                "the navigator's layer order");
        }

        [UnityTest]
        public IEnumerator Initialize_ExtraLayersContainNull_SkipsIt()
        {
            var toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);

            _world = new StageWorld(setup: new ViewRootSetup { ExtraLayers = new[] { null, toasts } });
            yield return null;

            Assert.That(
                LayerChildNames(_world.Root.transform),
                Is.EqualTo(new[] { "Layer screens", "Layer popups", "Layer system", "Layer toasts" }),
                "layer objects under the root, in sibling order");

            var described = new StringBuilder();
            _world.Navigator.Describe(described);
            Assert.That(
                described.ToString(),
                Does.Contain("layers: screens(0) popups(1) system(2) toasts(3)\n"),
                "the navigator's layer order");
        }

        [UnityTest]
        public IEnumerator Initialize_ModalExtraLayer_BuildsABackdrop()
        {
            var sheets = new ViewLayer("sheets", 3, ViewLayerRules.Modal);

            _world = new StageWorld(setup: new ViewRootSetup { ExtraLayers = new[] { sheets } });
            yield return null;

            Assert.That(_world.Stage.LayerRect(sheets).GetComponentsInChildren<Backdrop>(true), Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Initialize_PlainExtraLayer_BuildsNoBackdrop()
        {
            var toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);

            _world = new StageWorld(setup: new ViewRootSetup { ExtraLayers = new[] { toasts } });
            yield return null;

            Assert.That(_world.Stage.LayerRect(toasts).GetComponentsInChildren<Backdrop>(true), Is.Empty);
        }

        [UnityTest]
        public IEnumerator Open_RowOnADeclaredExtraLayer_OpensUnderThatLayerRect()
        {
            var toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);
            _world = new StageWorld(setup: new ViewRootSetup { ExtraLayers = new[] { toasts } });
            _world.Row("toast", _world.Template<RecordingView>("Toast"), toasts);

            var handle = _world.Navigator.Open(ViewKey.Create("toast"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the toast to open");

            var instance = StageWorld.InstanceOf(handle);
            Assert.That(instance, Is.Not.Null, "live instance of the open toast");
            Assert.That(instance.transform.parent, Is.SameAs(_world.Stage.LayerRect(toasts)), "parent of the toast");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Initialize_DuplicateLayerIdOnDistinctInstances_ThrowsArgumentExceptionAndBuildsNothing()
        {
            var root = CreateLazyRoot();
            yield return null;
            var setup = new ViewRootSetup
            {
                ExtraLayers = new[] { new ViewLayer("toasts", 3, ViewLayerRules.None), new ViewLayer("toasts", 4, ViewLayerRules.Modal) }
            };

            var exception = Assert.Throws<ArgumentException>(() => root.Initialize(setup));

            // Checked in the same frame: the layers are validated before anything is built, so nothing waits on a deferred Destroy.
            Assert.That(exception.ParamName, Is.EqualTo("setup"), "parameter name");
            Assert.That(exception.Message, Does.Contain("'toasts'"), "message names the repeated id");
            Assert.That(root.transform.childCount, Is.EqualTo(0), "children under the root");
            Assert.That(root.IsInitialized, Is.False, "initialised");
            Assert.That(root.IsLowMemorySubscribed, Is.False, "low memory subscription");
        }

        [UnityTest]
        public IEnumerator Initialize_SameLayerInstanceTwice_ThrowsArgumentExceptionAndLeavesNothing()
        {
            var root = CreateLazyRoot();
            yield return null;
            var toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);
            var setup = new ViewRootSetup { ExtraLayers = new[] { toasts, toasts } };

            var exception = Assert.Throws<ArgumentException>(() => root.Initialize(setup));

            Assert.That(exception.ParamName, Is.EqualTo("setup"), "parameter name");
            Assert.That(exception.Message, Does.Contain("'toasts'"), "message names the repeated id");
            Assert.That(root.transform.childCount, Is.EqualTo(0), "children under the root");
            Assert.That(root.IsInitialized, Is.False, "initialised");
            Assert.That(root.IsLowMemorySubscribed, Is.False, "low memory subscription");
        }

        [UnityTest]
        public IEnumerator Initialize_ExtraLayersRepeatADefaultLayer_ThrowsArgumentExceptionAndLeavesNothing()
        {
            var root = CreateLazyRoot();
            yield return null;
            var setup = new ViewRootSetup { ExtraLayers = new[] { ViewLayers.Popups } };

            var exception = Assert.Throws<ArgumentException>(() => root.Initialize(setup));

            Assert.That(exception.ParamName, Is.EqualTo("setup"), "parameter name");
            Assert.That(exception.Message, Does.Contain("'popups'"), "message names the repeated id");
            Assert.That(root.transform.childCount, Is.EqualTo(0), "children under the root");
            Assert.That(root.IsInitialized, Is.False, "initialised");
            Assert.That(root.IsLowMemorySubscribed, Is.False, "low memory subscription");
        }

        [UnityTest]
        public IEnumerator Initialize_AfterAFailedInitialize_WithCorrectedSetup_BuildsExactlyOneSetOfLayers()
        {
            var root = CreateLazyRoot();
            yield return null;
            var toasts = new ViewLayer("toasts", 3, ViewLayerRules.None);
            Assert.Throws<ArgumentException>(
                () => root.Initialize(new ViewRootSetup { ExtraLayers = new[] { toasts, ViewLayers.Popups } }),
                "the first Initialize with a repeated layer");
            Assert.That(root.transform.childCount, Is.EqualTo(0), "children under the root before the retry");
            Assert.That(root.IsLowMemorySubscribed, Is.False, "low memory subscription before the retry");

            root.Initialize(new ViewRootSetup { ExtraLayers = new[] { toasts } });
            yield return null;

            Assert.That(root.IsInitialized, Is.True, "initialised after the retry");
            Assert.That(
                LayerChildNames(root.transform),
                Is.EqualTo(new[] { "Layer screens", "Layer popups", "Layer system", "Layer toasts" }),
                "layer objects under the root, in sibling order");
            Assert.That(CountChildrenNamed(root.transform, "Staging"), Is.EqualTo(1), "Staging objects under the root");
            Assert.That(CountChildrenNamed(root.transform, "Shield"), Is.EqualTo(1), "Shield objects under the root");
            Assert.That(root.IsLowMemorySubscribed, Is.True, "low memory subscription");
            LogAssert.NoUnexpectedReceived();
        }

        // StageWorld always calls Initialize, so the failing cases need a root built here: initialise on awake off, never initialised.
        private ViewRoot CreateLazyRoot()
        {
            var eventSystem = Track(new GameObject("TestEventSystem"));
            eventSystem.AddComponent<EventSystem>();

            var rootObject = Track(new GameObject("LazyRoot", typeof(RectTransform)));
            rootObject.SetActive(false);
            rootObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var root = rootObject.AddComponent<ViewRoot>();
            StageWorld.SetField(root, "_initializeOnAwake", false);
            rootObject.SetActive(true);
            return root;
        }

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        private static List<string> ChildNames(Transform parent)
        {
            var names = new List<string>(parent.childCount);
            for (var i = 0; i < parent.childCount; i++)
                names.Add(parent.GetChild(i).name);

            return names;
        }

        private static int CountChildrenNamed(Transform parent, string name)
        {
            var count = 0;
            for (var i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == name)
                    count++;
            }

            return count;
        }

        private static List<string> LayerChildNames(Transform parent)
        {
            var names = ChildNames(parent);
            names.RemoveAll(name => !name.StartsWith("Layer ", StringComparison.Ordinal));
            return names;
        }

        // Bounded by frames and a real-time floor.
        private static IEnumerator WaitFor(Func<bool> condition, string what)
        {
            var started = Time.realtimeSinceStartup;
            for (var frame = 0; !condition(); frame++)
            {
                if (frame >= MaxFrames && Time.realtimeSinceStartup - started >= MinWaitSeconds)
                    Assert.Fail("Timed out after " + frame + " frames waiting for " + what + ".");

                yield return null;
            }
        }
    }
}
