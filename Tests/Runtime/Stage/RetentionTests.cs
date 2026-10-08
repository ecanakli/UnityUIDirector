using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests
{
    [TestFixture]
    public sealed class RetentionTests
    {
        private const string KeptId = "kept-view";

        private StageWorld _world;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_world != null)
            {
                _world.Dispose();
                _world = null;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Close_KeepInstance_ParksTheInstanceInactiveUnderStaging()
        {
            var world = CreateKeptWorld();

            var handle = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");
            var instance = StageWorld.InstanceOf(handle);
            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");
            // A released instance would be destroyed at the end of the frame.
            yield return null;

            Assert.That((bool)instance, Is.True, "instance alive");
            Assert.That(instance.activeSelf, Is.False, "instance active");
            Assert.That(instance.transform.parent, Is.EqualTo(world.Stage.Staging), "instance parent");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_KeptInstanceReopened_ReusesTheSameInstanceWithoutASecondAwake()
        {
            var world = CreateKeptWorld();
            var first = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => first.Phase == ViewPhase.Open, "the first open");
            var firstId = StageWorld.InstanceOf(first).GetInstanceID();
            first.Close();
            yield return TransitionConformance.WaitFor(() => !first.IsAlive, "the first close");

            var second = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => second.Phase == ViewPhase.Open, "the reopen");

            var reopened = StageWorld.InstanceOf(second);
            var view = reopened.GetComponent<RecordingView>();
            Assert.That(reopened.GetInstanceID(), Is.EqualTo(firstId), "instance id");
            Assert.That(view.AwakeCount, Is.EqualTo(1), "Awake count");
            Assert.That(view.OpenCount, Is.EqualTo(2), "OnOpen count");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Bind_OnOpenScopeOfReopenedKeptInstance_FiresOncePerClick()
        {
            var world = CreateKeptWorld(AddButton);
            var clicks = 0;
            var first = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => first.Phase == ViewPhase.Open, "the first open");
            var view = StageWorld.InstanceOf(first).GetComponent<RecordingView>();
            var button = view.GetComponentInChildren<Button>(true);

            // The listener count is the subject, not input delivery, so the event is invoked directly.
            view.LastOpenScope.Bind(button.onClick, () => clicks++);
            button.onClick.Invoke();
            var clicksWhileFirstOpen = clicks;
            first.Close();
            yield return TransitionConformance.WaitFor(() => !first.IsAlive, "the first close");
            button.onClick.Invoke();
            var clicksWhileParked = clicks - clicksWhileFirstOpen;
            var second = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => second.Phase == ViewPhase.Open, "the reopen");
            view.LastOpenScope.Bind(button.onClick, () => clicks++);
            var clicksBeforeReopenClick = clicks;
            button.onClick.Invoke();

            Assert.That(clicksWhileFirstOpen, Is.EqualTo(1), "calls per click while first open");
            Assert.That(clicksWhileParked, Is.EqualTo(0), "calls per click while parked");
            Assert.That(clicks - clicksBeforeReopenClick, Is.EqualTo(1), "calls per click after the reopen");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Trim_IdleKeptInstance_DestroysIt()
        {
            var world = CreateKeptWorld();
            var handle = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");
            var instance = StageWorld.InstanceOf(handle);
            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");
            yield return null;
            var keptBeforeTrim = (bool)instance;

            world.Navigator.Trim();
            // Destroy takes effect at the end of the frame.
            yield return null;

            Assert.That(keptBeforeTrim, Is.True, "instance kept before the trim");
            Assert.That((bool)instance, Is.False, "instance alive after the trim");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator LowMemory_IdleKeptInstance_TrimsIt()
        {
            var world = CreateKeptWorld();
            var handle = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");
            var instance = StageWorld.InstanceOf(handle);
            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");
            yield return null;
            var keptBeforeLowMemory = (bool)instance;

            RaiseLowMemory(world.Root);
            yield return null;

            Assert.That(keptBeforeLowMemory, Is.True, "instance kept before the low-memory callback");
            Assert.That((bool)instance, Is.False, "instance alive after the low-memory callback");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Trim_KeptInstanceOpenAgain_LeavesItOpen()
        {
            var world = CreateKeptWorld();
            var first = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => first.Phase == ViewPhase.Open, "the first open");
            first.Close();
            yield return TransitionConformance.WaitFor(() => !first.IsAlive, "the first close");
            var second = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => second.Phase == ViewPhase.Open, "the reopen");
            var instance = StageWorld.InstanceOf(second);

            world.Navigator.Trim();
            yield return null;

            Assert.That((bool)instance, Is.True, "instance alive");
            Assert.That(instance.activeInHierarchy, Is.True, "instance active");
            Assert.That(second.Phase, Is.EqualTo(ViewPhase.Open), "request phase");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Stash_KeptInstanceWithSelectionInside_ClearsSelection()
        {
            var world = CreateKeptWorld(AddButton);
            var handle = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");
            var eventSystem = world.EventSystemObject.GetComponent<EventSystem>();
            var instance = StageWorld.InstanceOf(handle);
            var button = instance.GetComponentInChildren<Button>(true);
            eventSystem.SetSelectedGameObject(button.gameObject);
            Assume.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject), "control: the button is selected");

            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");
            yield return TransitionConformance.WaitFor(() => instance.transform.parent == world.Stage.Staging, "the instance to be stashed");

            Assert.That(eventSystem.currentSelectedGameObject, Is.Null, "selection inside the stashed view");
            LogAssert.NoUnexpectedReceived();
        }

        // The close itself (shield, loss of input) also clears the selection, so the stash step is called directly here.
        [UnityTest]
        public IEnumerator PrepareForStash_OpenViewWithSelectionInside_ClearsSelection()
        {
            var world = CreateKeptWorld(AddButton);
            var handle = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");
            var eventSystem = world.EventSystemObject.GetComponent<EventSystem>();
            var instance = StageWorld.InstanceOf(handle);
            var button = instance.GetComponentInChildren<Button>(true);
            eventSystem.SetSelectedGameObject(button.gameObject);
            Assume.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject), "control: the button is selected");
            Assume.That(EventSystem.current, Is.SameAs(eventSystem), "control: the world's event system is the current one");

            instance.GetComponent<ViewHost>().PrepareForStash();

            Assert.That(eventSystem.currentSelectedGameObject, Is.Null, "selection inside the stashed view");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Close_KeepInstanceWithNestedCanvasAuthoredOff_ParksWithCanvasesAsAuthored()
        {
            var world = CreateKeptWorld(AddNestedCanvasesAndRaycasters);
            var handle = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");
            var instance = StageWorld.InstanceOf(handle);
            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");
            yield return TransitionConformance.WaitFor(() => instance.transform.parent == world.Stage.Staging, "the instance to be stashed");

            Assert.That(instance.GetComponent<Canvas>().enabled, Is.True, "own canvas");
            Assert.That(instance.GetComponent<GraphicRaycaster>().enabled, Is.True, "own raycaster");
            Assert.That(ChildComponent<Canvas>(instance, "Nested").enabled, Is.True, "nested canvas");
            Assert.That(ChildComponent<Canvas>(instance, "NestedOff").enabled, Is.False, "nested canvas authored disabled");
            Assert.That(ChildComponent<GraphicRaycaster>(instance, "NestedHit").enabled, Is.True, "nested raycaster");
            Assert.That(ChildComponent<GraphicRaycaster>(instance, "NestedHitOff").enabled, Is.False, "nested raycaster authored disabled");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Close_KeepInstanceWhoseOnOpenEnabledAnAuthoredOffCanvas_ParksWithItEnabled()
        {
            _world = new StageWorld();
            _world.Row(KeptId, _world.Template<EnablesNestedOffInOnOpenView>("KeptView", AddNestedCanvasesAndRaycasters), retention: ViewRetention.KeepInstance);
            var world = _world;
            var handle = OpenKept(world);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");
            var instance = StageWorld.InstanceOf(handle);
            Assume.That(ChildComponent<Canvas>(instance, "NestedOff").enabled, Is.True, "control: OnOpen enabled the canvas");
            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");
            yield return TransitionConformance.WaitFor(() => instance.transform.parent == world.Stage.Staging, "the instance to be stashed");

            Assert.That(ChildComponent<Canvas>(instance, "NestedOff").enabled, Is.True, "nested canvas the view enabled in OnOpen");
            Assert.That(instance.GetComponent<Canvas>().enabled, Is.True, "own canvas");
            LogAssert.NoUnexpectedReceived();
        }

        // Helpers

        private StageWorld CreateKeptWorld(System.Action<GameObject> build = null)
        {
            _world = new StageWorld();
            _world.Row(KeptId, _world.Template<RecordingView>("KeptView", build), retention: ViewRetention.KeepInstance);
            return _world;
        }

        // Nested canvases and raycasters, one enabled and one authored disabled of each.
        private static void AddNestedCanvasesAndRaycasters(GameObject template)
        {
            AddChild(template, "Nested").AddComponent<Canvas>();
            AddChild(template, "NestedOff").AddComponent<Canvas>().enabled = false;

            var hit = AddChild(template, "NestedHit");
            hit.AddComponent<Canvas>();
            hit.AddComponent<GraphicRaycaster>();

            var hitOff = AddChild(template, "NestedHitOff");
            hitOff.AddComponent<Canvas>();
            hitOff.AddComponent<GraphicRaycaster>().enabled = false;
        }

        private static GameObject AddChild(GameObject parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static T ChildComponent<T>(GameObject instance, string childName) where T : Component
        {
            var child = instance.transform.Find(childName);
            Assert.That(child, Is.Not.Null, "child " + childName);
            return child.GetComponent<T>();
        }

        private static ViewHandle OpenKept(StageWorld world)
        {
            return world.Navigator.Open(ViewKey.Create(KeptId), world.Owner);
        }

        private static void AddButton(GameObject template)
        {
            var child = new GameObject("Button", typeof(RectTransform));
            child.transform.SetParent(template.transform, false);
            var image = child.AddComponent<Image>();
            var button = child.AddComponent<Button>();
            button.targetGraphic = image;
        }

        // Application.lowMemory cannot be raised from a test, so the root's private handler is called directly.
        private static void RaiseLowMemory(ViewRoot root)
        {
            var handler = typeof(ViewRoot).GetMethod("OnLowMemory", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(handler, Is.Not.Null, "ViewRoot.OnLowMemory");
            handler.Invoke(root, null);
        }

        [AddComponentMenu("")]
        private sealed class EnablesNestedOffInOnOpenView : ViewBehaviour
        {
            protected override void OnOpen(NoArgs args, ViewScope open)
            {
                transform.Find("NestedOff").GetComponent<Canvas>().enabled = true;
            }
        }
    }
}
