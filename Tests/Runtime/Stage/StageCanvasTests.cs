using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests
{
    // Stage canvases: sorting order, the derived hidden and interactive state on every canvas and raycaster of a view, and the backdrop.
    [TestFixture]
    public sealed class StageCanvasTests
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 1f;
        private const int BaseOrder = 100;
        private const string CanvasTargetName = "CanvasTarget";
        private const string RaycasterTargetName = "RaycasterTarget";
        private const string ButtonName = "Button";

        // A loss is flushed one frame after the report; this window outlasts it.
        private const int FlushFrames = 3;

        // A plain layer above the defaults: it covers without raising the input shield, which clears the selection on its own.
        private static readonly ViewLayer OverlayLayer = new ViewLayer("test-overlay", 3, ViewLayerRules.None);

        // The moment a view switches off one of its own nested components before it is first shown.
        public enum SwitchMoment
        {
            Awake,
            OnEnable,
            OnOpen
        }

        private StageWorld _world;
        private LogRecorder _log;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_world != null)
            {
                _world.Dispose();
                _world = null;

                // Destroy is deferred; the next test must not see this world's objects.
                yield return null;
            }

            // Last, so lines the teardown writes are still recorded.
            if (_log != null)
            {
                _log.Dispose();
                _log = null;
            }
        }

        [UnityTest]
        public IEnumerator Apply_ScreenCovered_DisablesEveryCanvasUnderIt()
        {
            _world = new StageWorld();
            _world.Row("below", _world.Template<RecordingView>("Below", AddNestedCanvases));
            _world.Row("above", _world.Template<RecordingView>("Above"));

            var below = _world.Navigator.Open(ViewKey.Create("below"), _world.Owner);
            yield return WaitFor(() => below.Phase == ViewPhase.Open, "the lower screen to open");
            var instance = StageWorld.InstanceOf(below);
            var above = _world.Navigator.Open(ViewKey.Create("above"), _world.Owner);
            yield return WaitFor(() => above.Phase == ViewPhase.Open, "the upper screen to open");

            var canvases = instance.GetComponentsInChildren<Canvas>(true);
            Assert.That(canvases, Has.Length.EqualTo(4), "canvases under the covered view");
            Assert.That(canvases, Has.All.Matches<Canvas>(canvas => !canvas.enabled), "every canvas disabled");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_CoveringScreenClosed_RestoresEachCanvasToItsAuthoredState()
        {
            _world = new StageWorld();
            _world.Row("below", _world.Template<RecordingView>("Below", AddNestedCanvases));
            _world.Row("above", _world.Template<RecordingView>("Above"));

            var below = _world.Navigator.Open(ViewKey.Create("below"), _world.Owner);
            yield return WaitFor(() => below.Phase == ViewPhase.Open, "the lower screen to open");
            var instance = StageWorld.InstanceOf(below);
            var above = _world.Navigator.Open(ViewKey.Create("above"), _world.Owner);
            yield return WaitFor(() => above.Phase == ViewPhase.Open, "the upper screen to open");
            above.Close();
            yield return WaitFor(() => above.Phase == ViewPhase.Ended, "the upper screen to close");

            Assert.That(instance.GetComponent<Canvas>().enabled, Is.True, "own canvas");
            Assert.That(ChildComponent<Canvas>(instance, "Nested").enabled, Is.True, "nested canvas");
            Assert.That(ChildComponent<Canvas>(instance, "NestedOff").enabled, Is.False, "nested canvas authored disabled");
            Assert.That(ChildComponent<Canvas>(instance, "InactiveNested").enabled, Is.True, "canvas on an inactive child");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_ScreenCoveredInDeactivateMode_DeactivatesItAndLeavesCanvasesAlone()
        {
            var log = StartLogRecorder();
            _world = new StageWorld();
            _world.Row("below", _world.Template<RecordingView>("Below", AddNestedCanvases), whenCovered: CoveredMode.Deactivate);
            _world.Row("above", _world.Template<RecordingView>("Above"));

            var below = _world.Navigator.Open(ViewKey.Create("below"), _world.Owner);
            yield return WaitFor(() => below.Phase == ViewPhase.Open, "the lower screen to open");
            var instance = StageWorld.InstanceOf(below);
            var above = _world.Navigator.Open(ViewKey.Create("above"), _world.Owner);
            yield return WaitFor(() => above.Phase == ViewPhase.Open, "the upper screen to open");

            // Proving that no loss is reported needs a fixed window; the flush runs one frame after the deactivation.
            for (var frame = 0; frame < FlushFrames; frame++)
                yield return null;

            Assert.That(instance.activeSelf, Is.False, "covered view active");
            Assert.That(instance.GetComponent<Canvas>().enabled, Is.True, "own canvas");
            Assert.That(ChildComponent<Canvas>(instance, "Nested").enabled, Is.True, "nested canvas");
            Assert.That(below.Phase, Is.EqualTo(ViewPhase.Open), "covered request phase");
            Assert.That(above.Phase, Is.EqualTo(ViewPhase.Open), "covering request phase");
            Assert.That(log.Count(DiagnosticIds.ViewLostOutsidePackage), Is.EqualTo(0), "UIDIRECTOR107 lines:\n" + log.Describe());
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_CoveringScreenClosedInDeactivateMode_ReactivatesScreen()
        {
            var log = StartLogRecorder();
            _world = new StageWorld();
            _world.Row("below", _world.Template<RecordingView>("Below"), whenCovered: CoveredMode.Deactivate);
            _world.Row("above", _world.Template<RecordingView>("Above"));

            var below = _world.Navigator.Open(ViewKey.Create("below"), _world.Owner);
            yield return WaitFor(() => below.Phase == ViewPhase.Open, "the lower screen to open");
            var instance = StageWorld.InstanceOf(below);
            var above = _world.Navigator.Open(ViewKey.Create("above"), _world.Owner);
            yield return WaitFor(() => above.Phase == ViewPhase.Open, "the upper screen to open");

            // Held covered past the flush, so a deactivation the guard missed would end the request here.
            for (var frame = 0; frame < FlushFrames; frame++)
                yield return null;

            Assert.That(instance.activeSelf, Is.False, "covered view active while held covered");
            Assert.That(below.Phase, Is.EqualTo(ViewPhase.Open), "covered request phase while held covered");
            above.Close();
            yield return WaitFor(() => above.Phase == ViewPhase.Ended, "the upper screen to close");

            for (var frame = 0; frame < FlushFrames; frame++)
                yield return null;

            Assert.That(instance.activeSelf, Is.True, "revealed view active");
            Assert.That(below.Phase, Is.EqualTo(ViewPhase.Open), "revealed request phase");
            Assert.That(log.Count(DiagnosticIds.ViewLostOutsidePackage), Is.EqualTo(0), "UIDIRECTOR107 lines:\n" + log.Describe());
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_ViewUnderModalPopup_DisablesEveryRaycasterUnderIt()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", AddNestedRaycasters));
            _world.Row("popup", _world.Template<RecordingView>("Popup"), ViewLayers.Popups);

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");
            var instance = StageWorld.InstanceOf(screen);
            var popup = _world.Navigator.Open(ViewKey.Create("popup"), _world.Owner);
            yield return WaitFor(() => popup.Phase == ViewPhase.Open, "the popup to open");

            var raycasters = instance.GetComponentsInChildren<GraphicRaycaster>(true);
            Assert.That(raycasters, Has.Length.EqualTo(3), "raycasters under the view");
            Assert.That(raycasters, Has.All.Matches<GraphicRaycaster>(raycaster => !raycaster.enabled), "every raycaster disabled");
            Assert.That(instance.GetComponent<Canvas>().enabled, Is.True, "a popup does not cover the screen");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_ModalPopupClosed_RestoresEachRaycasterToItsAuthoredState()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", AddNestedRaycasters));
            _world.Row("popup", _world.Template<RecordingView>("Popup"), ViewLayers.Popups);

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");
            var instance = StageWorld.InstanceOf(screen);
            var popup = _world.Navigator.Open(ViewKey.Create("popup"), _world.Owner);
            yield return WaitFor(() => popup.Phase == ViewPhase.Open, "the popup to open");
            popup.Close();
            yield return WaitFor(() => popup.Phase == ViewPhase.Ended, "the popup to close");

            Assert.That(instance.GetComponent<GraphicRaycaster>().enabled, Is.True, "own raycaster");
            Assert.That(ChildComponent<GraphicRaycaster>(instance, "NestedHit").enabled, Is.True, "nested raycaster");
            Assert.That(ChildComponent<GraphicRaycaster>(instance, "NestedHitOff").enabled, Is.False, "nested raycaster authored disabled");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_NestedCanvasDisabledByViewBeforeFirstShow_StaysDisabledWhenOpen([Values] SwitchMoment moment)
        {
            _world = new StageWorld();
            _world.Row("screen", SwitchingTemplate(moment, "Screen", AddCanvasTarget));

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");

            var instance = StageWorld.InstanceOf(screen);
            Assert.That(ChildComponent<Canvas>(instance, CanvasTargetName).enabled, Is.False, "nested canvas the view disabled");
            Assert.That(instance.GetComponent<Canvas>().enabled, Is.True, "own canvas");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_NestedRaycasterDisabledByViewBeforeFirstShow_StaysDisabledWhenOpen([Values] SwitchMoment moment)
        {
            _world = new StageWorld();
            _world.Row("screen", SwitchingTemplate(moment, "Screen", AddRaycasterTarget));

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");

            var instance = StageWorld.InstanceOf(screen);
            Assert.That(ChildComponent<GraphicRaycaster>(instance, RaycasterTargetName).enabled, Is.False, "nested raycaster the view disabled");
            Assert.That(instance.GetComponent<GraphicRaycaster>().enabled, Is.True, "own raycaster");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_FirstApplyHiddenThenRevealed_KeepsCanvasTheViewDisabledInOnOpen()
        {
            _world = new StageWorld(setup: OverlaySetup());
            _world.Row("cover", _world.Template<RecordingView>("Cover"), OverlayLayer, cover: CoverPolicy.Covers);
            _world.Row("screen", _world.Template<SwitchesOffInOnOpenView>("Screen", AddCanvasTargetAndNested));

            var cover = _world.Navigator.Open(ViewKey.Create("cover"), _world.Owner);
            yield return WaitFor(() => cover.Phase == ViewPhase.Open, "the covering view to open");
            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open below the cover");
            var instance = StageWorld.InstanceOf(screen);
            Assume.That(instance.GetComponent<Canvas>().enabled, Is.False, "control: the screen opened covered");
            cover.Close();
            yield return WaitFor(() => cover.Phase == ViewPhase.Ended, "the covering view to close");

            Assert.That(ChildComponent<Canvas>(instance, CanvasTargetName).enabled, Is.False, "nested canvas the view disabled in OnOpen");
            Assert.That(instance.GetComponent<Canvas>().enabled, Is.True, "own canvas");
            Assert.That(ChildComponent<Canvas>(instance, "Nested").enabled, Is.True, "other nested canvas");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_NestedCanvasEnabledByViewWhileCovered_StaysEnabledAfterReveal()
        {
            _world = new StageWorld();
            _world.Row("below", _world.Template<RecordingView>("Below", AddNestedCanvases));
            _world.Row("above", _world.Template<RecordingView>("Above"));

            var below = _world.Navigator.Open(ViewKey.Create("below"), _world.Owner);
            yield return WaitFor(() => below.Phase == ViewPhase.Open, "the lower screen to open");
            var instance = StageWorld.InstanceOf(below);
            var above = _world.Navigator.Open(ViewKey.Create("above"), _world.Owner);
            yield return WaitFor(() => above.Phase == ViewPhase.Open, "the upper screen to open");
            Assume.That(instance.GetComponent<Canvas>().enabled, Is.False, "control: the lower screen is covered");
            ChildComponent<Canvas>(instance, "NestedOff").enabled = true;
            above.Close();
            yield return WaitFor(() => above.Phase == ViewPhase.Ended, "the upper screen to close");

            Assert.That(ChildComponent<Canvas>(instance, "NestedOff").enabled, Is.True, "nested canvas the view enabled while covered");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_NestedCanvasDisabledByViewWhileShown_StaysDisabledAfterSecondCoverAndReveal()
        {
            _world = new StageWorld();
            _world.Row("below", _world.Template<RecordingView>("Below", AddNestedCanvases));
            _world.Row("above", _world.Template<RecordingView>("Above"));

            var below = _world.Navigator.Open(ViewKey.Create("below"), _world.Owner);
            yield return WaitFor(() => below.Phase == ViewPhase.Open, "the lower screen to open");
            var instance = StageWorld.InstanceOf(below);
            var firstAbove = _world.Navigator.Open(ViewKey.Create("above"), _world.Owner);
            yield return WaitFor(() => firstAbove.Phase == ViewPhase.Open, "the first upper screen to open");
            firstAbove.Close();
            yield return WaitFor(() => firstAbove.Phase == ViewPhase.Ended, "the first upper screen to close");
            Assume.That(ChildComponent<Canvas>(instance, "Nested").enabled, Is.True, "control: the first reveal restored the nested canvas");
            ChildComponent<Canvas>(instance, "Nested").enabled = false;
            var secondAbove = _world.Navigator.Open(ViewKey.Create("above"), _world.Owner);
            yield return WaitFor(() => secondAbove.Phase == ViewPhase.Open, "the second upper screen to open");
            Assume.That(instance.GetComponent<Canvas>().enabled, Is.False, "control: the lower screen is covered again");
            secondAbove.Close();
            yield return WaitFor(() => secondAbove.Phase == ViewPhase.Ended, "the second upper screen to close");

            Assert.That(ChildComponent<Canvas>(instance, "Nested").enabled, Is.False, "nested canvas the view disabled while shown");
            Assert.That(instance.GetComponent<Canvas>().enabled, Is.True, "own canvas");
            Assert.That(ChildComponent<Canvas>(instance, "InactiveNested").enabled, Is.True, "canvas on an inactive child");
            Assert.That(ChildComponent<Canvas>(instance, "NestedOff").enabled, Is.False, "nested canvas authored disabled");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_NestedRaycasterEnabledByViewWhileUnderModalPopup_StaysEnabledAfterPopupCloses()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", AddNestedRaycasters));
            _world.Row("popup", _world.Template<RecordingView>("Popup"), ViewLayers.Popups);

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");
            var instance = StageWorld.InstanceOf(screen);
            var popup = _world.Navigator.Open(ViewKey.Create("popup"), _world.Owner);
            yield return WaitFor(() => popup.Phase == ViewPhase.Open, "the popup to open");
            Assume.That(instance.GetComponent<GraphicRaycaster>().enabled, Is.False, "control: the screen is not interactive");
            ChildComponent<GraphicRaycaster>(instance, "NestedHitOff").enabled = true;
            popup.Close();
            yield return WaitFor(() => popup.Phase == ViewPhase.Ended, "the popup to close");

            Assert.That(ChildComponent<GraphicRaycaster>(instance, "NestedHitOff").enabled, Is.True, "nested raycaster the view enabled under the popup");
            Assert.That(instance.GetComponent<GraphicRaycaster>().enabled, Is.True, "own raycaster");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_ButtonSelectedInOnOpen_StaysSelectedWhenOpen()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<SelectsButtonInOnOpenView>("Screen", AddButton));

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");

            var button = ChildComponent<Button>(StageWorld.InstanceOf(screen), ButtonName);
            var eventSystem = _world.EventSystemObject.GetComponent<EventSystem>();
            Assume.That(EventSystem.current, Is.SameAs(eventSystem), "control: the world's event system is the current one");
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject), "selection the view made in OnOpen");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_ViewLosesInputWhileSelectionIsOutsideIt_KeepsSelection()
        {
            _world = new StageWorld(setup: OverlaySetup());
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Row("cover", _world.Template<SelectsButtonInOnOpenView>("Cover", AddButton), OverlayLayer, cover: CoverPolicy.Covers);

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");
            var screenInstance = StageWorld.InstanceOf(screen);
            var cover = _world.Navigator.Open(ViewKey.Create("cover"), _world.Owner);
            yield return WaitFor(() => cover.Phase == ViewPhase.Open, "the covering view to open");

            var button = ChildComponent<Button>(StageWorld.InstanceOf(cover), ButtonName);
            var eventSystem = _world.EventSystemObject.GetComponent<EventSystem>();
            Assume.That(EventSystem.current, Is.SameAs(eventSystem), "control: the world's event system is the current one");
            Assume.That(screenInstance.GetComponent<GraphicRaycaster>().enabled, Is.False, "control: the screen lost input");
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject), "selection the covering view made in OnOpen");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_OpenViewCoveredWithItsButtonSelected_ClearsSelection()
        {
            _world = new StageWorld(setup: OverlaySetup());
            _world.Row("screen", _world.Template<RecordingView>("Screen", AddButton));
            _world.Row("cover", _world.Template<RecordingView>("Cover"), OverlayLayer, cover: CoverPolicy.Covers);

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");
            var eventSystem = _world.EventSystemObject.GetComponent<EventSystem>();
            var button = ChildComponent<Button>(StageWorld.InstanceOf(screen), ButtonName);
            eventSystem.SetSelectedGameObject(button.gameObject);
            Assume.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject), "control: the button is selected");
            var cover = _world.Navigator.Open(ViewKey.Create("cover"), _world.Owner);
            yield return WaitFor(() => cover.Phase == ViewPhase.Open, "the covering view to open");

            Assert.That(eventSystem.currentSelectedGameObject, Is.Null, "selection of the covered view");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Apply_ViewsOnTwoLayers_SortingOrdersFollowBaseLayerAndSlot()
        {
            _world = new StageWorld(root => StageWorld.SetField(root, "_baseSortingOrder", BaseOrder));
            _world.Row("first", _world.Template<RecordingView>("First"));
            _world.Row("second", _world.Template<RecordingView>("Second"));
            _world.Row("popup", _world.Template<RecordingView>("Popup"), ViewLayers.Popups);

            var first = _world.Navigator.Open(ViewKey.Create("first"), _world.Owner);
            yield return WaitFor(() => first.Phase == ViewPhase.Open, "the first screen to open");
            var second = _world.Navigator.Open(ViewKey.Create("second"), _world.Owner);
            yield return WaitFor(() => second.Phase == ViewPhase.Open, "the second screen to open");
            var popup = _world.Navigator.Open(ViewKey.Create("popup"), _world.Owner);
            yield return WaitFor(() => popup.Phase == ViewPhase.Open, "the popup to open");

            // order = base + layer.Order x 1000 + slot x 10, slots counted from 1 per layer.
            Assert.That(OwnCanvasOf(first).sortingOrder, Is.EqualTo(BaseOrder + ViewLayers.Screens.Order * 1000 + 1 * 10), "first screen");
            Assert.That(OwnCanvasOf(second).sortingOrder, Is.EqualTo(BaseOrder + ViewLayers.Screens.Order * 1000 + 2 * 10), "second screen");
            Assert.That(OwnCanvasOf(popup).sortingOrder, Is.EqualTo(BaseOrder + ViewLayers.Popups.Order * 1000 + 1 * 10), "popup");
            LogAssert.NoUnexpectedReceived();
        }

        // A non-default sorting layer on a camera-mode root (an overlay root has none), so a stage that ignored it could not pass.
        private static void UseTestSortingLayer(ViewRoot root)
        {
            StageWorld.SetField(root, "_baseSortingOrder", BaseOrder);
            var id = SortingLayer.NameToID("UIDirectorTest");
            Assume.That(id, Is.Not.EqualTo(0), "the host project must define the UIDirectorTest sorting layer; layers loaded: " + SortingLayer.layers.Length);
            var canvas = root.GetComponent<Canvas>();
            canvas.sortingLayerID = id;
            Assume.That(canvas.sortingLayerID, Is.EqualTo(id), "the root canvas took the sorting layer");
        }

        [UnityTest]
        public IEnumerator Apply_OpenView_OverridesSortingOnRootSortingLayer()
        {
            _world = new StageWorld(UseTestSortingLayer, cameraMode: true);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");

            var canvas = OwnCanvasOf(screen);
            Assert.That(canvas.overrideSorting, Is.True, "override sorting");
            Assert.That(canvas.sortingLayerID, Is.EqualTo(_world.RootCanvas.sortingLayerID), "sorting layer");
            Assert.That(canvas.GetComponent<ViewHost>().SortingOrder, Is.EqualTo(canvas.sortingOrder), "ViewHost.SortingOrder");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BackdropOf_NoModalViewOpened_IsOff()
        {
            _world = new StageWorld();
            yield return null;

            var backdrop = _world.Stage.BackdropOf(ViewLayers.Popups);
            Assert.That(backdrop.GetComponent<Canvas>().enabled, Is.False, "backdrop canvas");
            Assert.That(backdrop.GetComponent<GraphicRaycaster>().enabled, Is.False, "backdrop raycaster");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ApplyBackdrop_StackedPopups_SitsFiveBelowTopPopup()
        {
            _world = new StageWorld(UseTestSortingLayer, cameraMode: true);
            _world.Row("lower", _world.Template<RecordingView>("Lower"), ViewLayers.Popups);
            _world.Row("upper", _world.Template<RecordingView>("Upper"), ViewLayers.Popups);

            var lower = _world.Navigator.Open(ViewKey.Create("lower"), _world.Owner);
            yield return WaitFor(() => lower.Phase == ViewPhase.Open, "the lower popup to open");
            var upper = _world.Navigator.Open(ViewKey.Create("upper"), _world.Owner);
            yield return WaitFor(() => upper.Phase == ViewPhase.Open, "the upper popup to open");

            var canvas = _world.Stage.BackdropOf(ViewLayers.Popups).GetComponent<Canvas>();
            Assert.That(canvas.enabled, Is.True, "backdrop canvas");
            Assert.That(canvas.sortingOrder, Is.EqualTo(BaseOrder + ViewLayers.Popups.Order * 1000 + 2 * 10 - 5), "backdrop order");
            Assert.That(canvas.overrideSorting, Is.True, "backdrop override sorting");
            Assert.That(canvas.sortingLayerID, Is.EqualTo(_world.RootCanvas.sortingLayerID), "backdrop sorting layer");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ApplyBackdrop_TopPopupClosed_MovesBelowNextPopup()
        {
            _world = new StageWorld(root => StageWorld.SetField(root, "_baseSortingOrder", BaseOrder));
            _world.Row("lower", _world.Template<RecordingView>("Lower"), ViewLayers.Popups);
            _world.Row("upper", _world.Template<RecordingView>("Upper"), ViewLayers.Popups);

            var lower = _world.Navigator.Open(ViewKey.Create("lower"), _world.Owner);
            yield return WaitFor(() => lower.Phase == ViewPhase.Open, "the lower popup to open");
            var upper = _world.Navigator.Open(ViewKey.Create("upper"), _world.Owner);
            yield return WaitFor(() => upper.Phase == ViewPhase.Open, "the upper popup to open");
            upper.Close();
            yield return WaitFor(() => upper.Phase == ViewPhase.Ended, "the upper popup to close");

            var canvas = _world.Stage.BackdropOf(ViewLayers.Popups).GetComponent<Canvas>();
            Assert.That(canvas.enabled, Is.True, "backdrop canvas");
            Assert.That(canvas.sortingOrder, Is.EqualTo(BaseOrder + ViewLayers.Popups.Order * 1000 + 1 * 10 - 5), "backdrop order");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ApplyBackdrop_LastPopupClosed_TurnsBackdropOff()
        {
            _world = new StageWorld();
            _world.Row("popup", _world.Template<RecordingView>("Popup"), ViewLayers.Popups);

            var popup = _world.Navigator.Open(ViewKey.Create("popup"), _world.Owner);
            yield return WaitFor(() => popup.Phase == ViewPhase.Open, "the popup to open");
            popup.Close();
            yield return WaitFor(() => popup.Phase == ViewPhase.Ended, "the popup to close");

            var backdrop = _world.Stage.BackdropOf(ViewLayers.Popups);
            Assert.That(backdrop.GetComponent<GraphicRaycaster>().enabled, Is.False, "backdrop raycaster right after the close");

            // The canvas turns off when the 0.2 s fade ends, not at the close.
            var canvas = backdrop.GetComponent<Canvas>();
            yield return WaitFor(() => !canvas.enabled, "the backdrop canvas to turn off");
            LogAssert.NoUnexpectedReceived();
        }

        // Three nested canvases: enabled, authored disabled, and enabled on an inactive child.
        private static void AddNestedCanvases(GameObject view)
        {
            AddChild(view, "Nested", true).AddComponent<Canvas>();
            AddChild(view, "NestedOff", true).AddComponent<Canvas>().enabled = false;
            AddChild(view, "InactiveNested", false).AddComponent<Canvas>();
        }

        // Two nested canvases with raycasters: one enabled, one authored disabled.
        private static void AddNestedRaycasters(GameObject view)
        {
            var hit = AddChild(view, "NestedHit", true);
            hit.AddComponent<Canvas>();
            hit.AddComponent<GraphicRaycaster>();

            var hitOff = AddChild(view, "NestedHitOff", true);
            hitOff.AddComponent<Canvas>();
            hitOff.AddComponent<GraphicRaycaster>().enabled = false;
        }

        // One enabled nested canvas the switching views turn off.
        private static void AddCanvasTarget(GameObject view)
        {
            AddChild(view, CanvasTargetName, true).AddComponent<Canvas>();
        }

        // One enabled nested canvas with an enabled raycaster the switching views turn off.
        private static void AddRaycasterTarget(GameObject view)
        {
            var target = AddChild(view, RaycasterTargetName, true);
            target.AddComponent<Canvas>();
            target.AddComponent<GraphicRaycaster>();
        }

        private static void AddCanvasTargetAndNested(GameObject view)
        {
            AddCanvasTarget(view);
            AddChild(view, "Nested", true).AddComponent<Canvas>();
        }

        private static void AddButton(GameObject view)
        {
            var child = AddChild(view, ButtonName, true);
            var image = child.AddComponent<Image>();
            var button = child.AddComponent<Button>();
            button.targetGraphic = image;
        }

        private LogRecorder StartLogRecorder()
        {
            _log = new LogRecorder();
            return _log;
        }

        private static ViewRootSetup OverlaySetup()
        {
            return new ViewRootSetup { ExtraLayers = new[] { OverlayLayer } };
        }

        private GameObject SwitchingTemplate(SwitchMoment moment, string name, Action<GameObject> build)
        {
            switch (moment)
            {
                case SwitchMoment.Awake:
                    return _world.Template<SwitchesOffInAwakeView>(name, build);
                case SwitchMoment.OnEnable:
                    return _world.Template<SwitchesOffInOnEnableView>(name, build);
                default:
                    return _world.Template<SwitchesOffInOnOpenView>(name, build);
            }
        }

        // Turns off whichever switch targets the view has.
        private static void SwitchOffTargets(GameObject view)
        {
            var canvasTarget = view.transform.Find(CanvasTargetName);
            if (canvasTarget != null)
                canvasTarget.GetComponent<Canvas>().enabled = false;

            var raycasterTarget = view.transform.Find(RaycasterTargetName);
            if (raycasterTarget != null)
                raycasterTarget.GetComponent<GraphicRaycaster>().enabled = false;
        }

        private static GameObject AddChild(GameObject parent, string name, bool active)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent.transform, false);
            child.SetActive(active);
            return child;
        }

        private static T ChildComponent<T>(GameObject instance, string childName) where T : Component
        {
            var child = instance.transform.Find(childName);
            Assert.That(child, Is.Not.Null, "child " + childName);
            return child.GetComponent<T>();
        }

        private static Canvas OwnCanvasOf(ViewHandle handle)
        {
            var instance = StageWorld.InstanceOf(handle);
            Assert.That(instance, Is.Not.Null, "instance of " + handle.ViewId);
            return instance.GetComponent<Canvas>();
        }

        // Bounded by frames and a real-time floor, so fast batch-mode frames do not time out a timed fade.
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

        [AddComponentMenu("")]
        private sealed class SwitchesOffInAwakeView : ViewBehaviour
        {
            protected override void OnOpen(NoArgs args, ViewScope open)
            {
            }

            private void Awake()
            {
                SwitchOffTargets(gameObject);
            }
        }

        [AddComponentMenu("")]
        private sealed class SwitchesOffInOnEnableView : ViewBehaviour
        {
            protected override void OnOpen(NoArgs args, ViewScope open)
            {
            }

            private void OnEnable()
            {
                SwitchOffTargets(gameObject);
            }
        }

        [AddComponentMenu("")]
        private sealed class SwitchesOffInOnOpenView : ViewBehaviour
        {
            protected override void OnOpen(NoArgs args, ViewScope open)
            {
                SwitchOffTargets(gameObject);
            }
        }

        [AddComponentMenu("")]
        private sealed class SelectsButtonInOnOpenView : ViewBehaviour
        {
            protected override void OnOpen(NoArgs args, ViewScope open)
            {
                // A missing current event system is reported by the test's control, not as an OnOpen fault.
                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                    eventSystem.SetSelectedGameObject(transform.Find(ButtonName).gameObject);
            }
        }
    }
}
