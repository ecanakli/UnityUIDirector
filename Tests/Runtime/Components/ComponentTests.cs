using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Ecanakli.UIDirector.Tests.Probes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // ViewButton, ViewHost and SafeAreaFitter on a real root; no input device is needed, so it runs in every input configuration.
    public sealed class ComponentTests
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 1f;
        private const string ButtonName = "ViewButton";

        private static readonly Vector2 ScrambledMin = new Vector2(0.3f, 0.3f);
        private static readonly Vector2 ScrambledMax = new Vector2(0.6f, 0.6f);
        private static readonly Rect OutdatedSafeArea = new Rect(1f, 2f, 3f, 4f);

        private static readonly Regex NotInitializedWarning =
            new Regex(Regex.Escape("ViewButton on '" + ButtonName + "' was clicked before its ViewRoot was initialised"));

        private static readonly Regex NoViewIdWarning = new Regex(Regex.Escape("ViewButton on '" + ButtonName + "' has no view id"));

        private readonly List<Object> _created = new List<Object>();
        private StageWorld _world;

        [TearDown]
        public void TearDown()
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
        }

        // Destroy is deferred; the next test must not see this test's objects.
        [UnityTearDown]
        public IEnumerator WaitForDestroy()
        {
            yield return null;
        }

        // ViewButton

        [UnityTest]
        public IEnumerator Click_CloseViewAction_ClosesItsViewAsDismissed()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithViewButton(ViewButtonAction.CloseView)));
            var result = _world.Navigator.ShowAsync(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");

            ViewButtonOf("screen").Click();
            yield return WaitFor(() => !result.IsPending(), "the screen to close");

            Assert.That(result.ResultNow().Reason, Is.EqualTo(CloseReason.Dismissed));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Click_BackActionOnPushedScreen_ClosesItWithBack()
        {
            _world = new StageWorld();
            _world.Row("first", _world.Template<RecordingView>("First"));
            _world.Row("second", _world.Template<RecordingView>("Second", WithViewButton(ViewButtonAction.Back)));
            _world.Navigator.Open(ViewKey.Create("first"), _world.Owner);
            yield return WaitSettled("first");
            var second = _world.Navigator.ShowAsync(ViewKey.Create("second"), _world.Owner);
            yield return WaitSettled("second");

            ViewButtonOf("second").Click();
            yield return WaitFor(() => !second.IsPending(), "the second screen to close");

            Assert.That(second.ResultNow().Reason, Is.EqualTo(CloseReason.Back));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Click_OpenViewAction_OpensViewById()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithViewButton(ViewButtonAction.OpenView, "next")));
            _world.Row("next", _world.Template<RecordingView>("Next"), ViewLayers.System);
            _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");

            ViewButtonOf("screen").Click();

            Assert.That(_world.Navigator.IsOpen("next"), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        // Owner is the root token: the opened view must not end with the view that holds the button.
        [UnityTest]
        public IEnumerator Click_OpenViewAction_OpenedViewOutlivesButtonsView()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithViewButton(ViewButtonAction.OpenView, "next")));
            _world.Row("next", _world.Template<RecordingView>("Next"), ViewLayers.System);
            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");
            ViewButtonOf("screen").Click();
            yield return WaitSettled("next");

            screen.Close();
            yield return WaitFor(() => screen.Phase == ViewPhase.Ended, "the button's screen to close");
            yield return null;

            Assert.That(_world.Navigator.IsOpen("next"), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnClickInvoke_CloseViewAction_ClosesItsViewAsDismissed()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithViewButton(ViewButtonAction.CloseView)));
            var result = _world.Navigator.ShowAsync(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");

            ViewButtonOf("screen").GetComponent<Button>().onClick.Invoke();
            yield return WaitFor(() => !result.IsPending(), "the screen to close");

            Assert.That(result.ResultNow().Reason, Is.EqualTo(CloseReason.Dismissed));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnClickInvoke_BackActionOnPushedScreen_ClosesItWithBack()
        {
            _world = new StageWorld();
            _world.Row("first", _world.Template<RecordingView>("First"));
            _world.Row("second", _world.Template<RecordingView>("Second", WithViewButton(ViewButtonAction.Back)));
            _world.Navigator.Open(ViewKey.Create("first"), _world.Owner);
            yield return WaitSettled("first");
            var second = _world.Navigator.ShowAsync(ViewKey.Create("second"), _world.Owner);
            yield return WaitSettled("second");

            ViewButtonOf("second").GetComponent<Button>().onClick.Invoke();
            yield return WaitFor(() => !second.IsPending(), "the second screen to close");

            Assert.That(second.ResultNow().Reason, Is.EqualTo(CloseReason.Back));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnClickInvoke_OpenViewAction_OpensViewById()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithViewButton(ViewButtonAction.OpenView, "next")));
            _world.Row("next", _world.Template<RecordingView>("Next"), ViewLayers.System);
            _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");

            ViewButtonOf("screen").GetComponent<Button>().onClick.Invoke();

            Assert.That(_world.Navigator.IsOpen("next"), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        // Any request the click created would take an id between the two reservations.
        [UnityTest]
        public IEnumerator OnClickInvoke_OpenViewActionWithABlankViewId_LogsTheNoViewIdWarningAndOpensNothing(
            [Values("", " ")] string viewId)
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithViewButton(ViewButtonAction.OpenView, viewId)));
            _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");
            var button = ViewButtonOf("screen").GetComponent<Button>();
            var idBefore = _world.Navigator.ReserveRequestId();

            LogAssert.Expect(LogType.Warning, NoViewIdWarning);
            button.onClick.Invoke();

            var idAfter = _world.Navigator.ReserveRequestId();
            Assert.That(idAfter, Is.EqualTo(idBefore + 1), "next request id after the click");
            LogAssert.NoUnexpectedReceived();
        }

        // Reading the navigator here would initialise the root with defaults, before the bootstrap's Initialize.
        [Test]
        public void Click_BackActionUnderUninitializedRoot_LeavesRootUninitialized()
        {
            var root = CreateUninitializedRoot();
            var viewButton = CreateViewButton(root.transform, ViewButtonAction.Back);

            LogAssert.Expect(LogType.Warning, NotInitializedWarning);
            viewButton.Click();

            Assert.That(root.IsInitialized, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Click_OpenViewActionUnderUninitializedRoot_LeavesRootUninitialized()
        {
            var root = CreateUninitializedRoot();
            var viewButton = CreateViewButton(root.transform, ViewButtonAction.OpenView, "next");

            LogAssert.Expect(LogType.Warning, NotInitializedWarning);
            viewButton.Click();

            Assert.That(root.IsInitialized, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // The click before Initialize must not leave the cached root refused; the initialised root has nothing open.
        [Test]
        public void Click_BackActionAfterRootInitialized_NoLongerWarns()
        {
            CreateEventSystem();
            var root = CreateUninitializedRoot();
            var viewButton = CreateViewButton(root.transform, ViewButtonAction.Back);
            LogAssert.Expect(LogType.Warning, NotInitializedWarning);
            viewButton.Click();
            root.Initialize(new ViewRootSetup { Inject = _ => { } });

            viewButton.Click();

            Assert.That(root.IsInitialized, Is.True, "IsInitialized");
            LogAssert.NoUnexpectedReceived();
        }

        // ViewHost

        [UnityTest]
        public IEnumerator Close_WiredAsUnityEventListener_ClosesViewAsDismissed()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            var result = _world.Navigator.ShowAsync(ViewKey.Create("screen"), _world.Owner);
            yield return WaitSettled("screen");
            var unityEvent = new UnityEvent();
            unityEvent.AddListener(InstanceOf("screen").GetComponent<ViewHost>().Close);

            unityEvent.Invoke();
            yield return WaitFor(() => !result.IsPending(), "the screen to close");

            Assert.That(result.ResultNow().Reason, Is.EqualTo(CloseReason.Dismissed));
            LogAssert.NoUnexpectedReceived();
        }

        // SafeAreaFitter

        // Screen 1000 x 2000, safe area x 50..950, y 100..1800.
        [TestCase(true, true, true, true, 0.05f, 0.05f, 0.95f, 0.9f, TestName = "Fit_AllEdges_FitsEveryEdge")]
        [TestCase(true, false, false, false, 0f, 0f, 1f, 0.9f, TestName = "Fit_TopOnly_FitsTopEdge")]
        [TestCase(false, true, false, false, 0f, 0.05f, 1f, 1f, TestName = "Fit_BottomOnly_FitsBottomEdge")]
        [TestCase(false, false, true, false, 0.05f, 0f, 1f, 1f, TestName = "Fit_LeftOnly_FitsLeftEdge")]
        [TestCase(false, false, false, true, 0f, 0f, 0.95f, 1f, TestName = "Fit_RightOnly_FitsRightEdge")]
        [TestCase(false, false, false, false, 0f, 0f, 1f, 1f, TestName = "Fit_NoEdges_KeepsAnchorsAtScreenEdges")]
        public void Fit_EdgeFlags_SetsAnchorsFromSafeArea(
            bool top, bool bottom, bool left, bool right,
            float minX, float minY, float maxX, float maxY)
        {
            var fitter = CreateDetachedFitter(top, bottom, left, right);
            var rect = (RectTransform)fitter.transform;

            fitter.Fit(new Rect(50f, 100f, 900f, 1700f), new Vector2(1000f, 2000f));

            var comparer = new Vector2EqualityComparer(1e-5f);
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(minX, minY)).Using(comparer), "anchorMin");
            Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(maxX, maxY)).Using(comparer), "anchorMax");
        }

        [UnityTest]
        public IEnumerator OnEnable_UnderRoot_FitsToScreenSafeAreaAtOnce()
        {
            Assume.That(Screen.width, Is.GreaterThan(0), "screen width");
            Assume.That(Screen.height, Is.GreaterThan(0), "screen height");
            _world = new StageWorld();
            yield return null;

            var fitter = CreateFitterUnderRoot("Fitter");

            var rect = (RectTransform)fitter.transform;
            var comparer = new Vector2EqualityComparer(1e-5f);
            Assert.That(rect.anchorMin, Is.EqualTo(ExpectedAnchorMin()).Using(comparer), "anchorMin");
            Assert.That(rect.anchorMax, Is.EqualTo(ExpectedAnchorMax()).Using(comparer), "anchorMax");
            LogAssert.NoUnexpectedReceived();
        }

        // The root's first compare after a registration refits every registered fitter; a disabled one must not be among them.
        [UnityTest]
        public IEnumerator OnDisable_UnderRoot_UnregistersFromRoot()
        {
            Assume.That(Screen.width, Is.GreaterThan(0), "screen width");
            Assume.That(Screen.height, Is.GreaterThan(0), "screen height");
            _world = new StageWorld();
            yield return null;
            var kept = CreateFitterUnderRoot("Kept");
            var disabled = CreateFitterUnderRoot("Disabled");

            disabled.enabled = false;
            Scramble(kept);
            Scramble(disabled);
            yield return null;
            yield return null;

            var comparer = new Vector2EqualityComparer(1e-5f);
            Assume.That(((RectTransform)kept.transform).anchorMin, Is.EqualTo(ExpectedAnchorMin()).Using(comparer), "the root did not refit its registered fitter, so this test cannot tell");
            Assert.That(((RectTransform)disabled.transform).anchorMin, Is.EqualTo(ScrambledMin).Using(comparer), "anchorMin of the disabled fitter");
            Assert.That(((RectTransform)disabled.transform).anchorMax, Is.EqualTo(ScrambledMax).Using(comparer), "anchorMax of the disabled fitter");
            LogAssert.NoUnexpectedReceived();
        }

        // A registered fitter must cost nothing per frame, so the type declares no Update for Unity to call.
        [Test]
        public void SafeAreaFitterType_DeclaresNoUpdateMethod()
        {
            var methods = typeof(SafeAreaFitter).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(methods, Has.None.Matches<MethodInfo>(method => method.Name == "Update"));
        }

        [Test]
        public void OnEnable_WithoutRoot_FitsToScreenSafeAreaAtOnce()
        {
            Assume.That(Screen.width, Is.GreaterThan(0), "screen width");
            Assume.That(Screen.height, Is.GreaterThan(0), "screen height");

            var fitter = CreateActiveDetachedFitter("DetachedFitter");

            var rect = (RectTransform)fitter.transform;
            var comparer = new Vector2EqualityComparer(1e-5f);
            Assert.That(rect.anchorMin, Is.EqualTo(ExpectedAnchorMin()).Using(comparer), "anchorMin");
            Assert.That(rect.anchorMax, Is.EqualTo(ExpectedAnchorMax()).Using(comparer), "anchorMax");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnEnable_WithoutRootThenSafeAreaChanges_RefitsNextFrame()
        {
            Assume.That(Screen.width, Is.GreaterThan(0), "screen width");
            Assume.That(Screen.height, Is.GreaterThan(0), "screen height");
            var fitter = CreateActiveDetachedFitter("DetachedFitter");
            yield return null;

            SimulateSafeAreaChange(fitter);
            yield return null;
            yield return null;

            var rect = (RectTransform)fitter.transform;
            var comparer = new Vector2EqualityComparer(1e-5f);
            Assert.That(rect.anchorMin, Is.EqualTo(ExpectedAnchorMin()).Using(comparer), "anchorMin");
            Assert.That(rect.anchorMax, Is.EqualTo(ExpectedAnchorMax()).Using(comparer), "anchorMax");
            LogAssert.NoUnexpectedReceived();
        }

        // The kept fitter shows that a change in these frames is refitted at all.
        [UnityTest]
        public IEnumerator OnDisable_WithoutRoot_StopsRefitting()
        {
            Assume.That(Screen.width, Is.GreaterThan(0), "screen width");
            Assume.That(Screen.height, Is.GreaterThan(0), "screen height");
            var kept = CreateActiveDetachedFitter("Kept");
            var disabled = CreateActiveDetachedFitter("Disabled");
            yield return null;

            disabled.enabled = false;
            SimulateSafeAreaChange(kept);
            SimulateSafeAreaChange(disabled);
            yield return null;
            yield return null;

            var comparer = new Vector2EqualityComparer(1e-5f);
            Assume.That(((RectTransform)kept.transform).anchorMin, Is.EqualTo(ExpectedAnchorMin()).Using(comparer), "the enabled fitter was not refitted, so this test cannot tell");
            Assert.That(((RectTransform)disabled.transform).anchorMin, Is.EqualTo(ScrambledMin).Using(comparer), "anchorMin of the disabled fitter");
            Assert.That(((RectTransform)disabled.transform).anchorMax, Is.EqualTo(ScrambledMax).Using(comparer), "anchorMax of the disabled fitter");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnEnable_WithoutRootAfterDisableInSameFrame_KeepsRefitting()
        {
            Assume.That(Screen.width, Is.GreaterThan(0), "screen width");
            Assume.That(Screen.height, Is.GreaterThan(0), "screen height");
            var fitter = CreateActiveDetachedFitter("DetachedFitter");
            yield return null;

            fitter.enabled = false;
            fitter.enabled = true;
            SimulateSafeAreaChange(fitter);
            yield return null;
            yield return null;

            var rect = (RectTransform)fitter.transform;
            var comparer = new Vector2EqualityComparer(1e-5f);
            Assert.That(rect.anchorMin, Is.EqualTo(ExpectedAnchorMin()).Using(comparer), "anchorMin");
            Assert.That(rect.anchorMax, Is.EqualTo(ExpectedAnchorMax()).Using(comparer), "anchorMax");
            LogAssert.NoUnexpectedReceived();
        }

        // Two frames let the disabled fitter's player-loop item remove itself, so the enable must schedule a new one.
        [UnityTest]
        public IEnumerator OnEnable_WithoutRootAfterTheTickerRemovedItself_KeepsRefitting()
        {
            Assume.That(Screen.width, Is.GreaterThan(0), "screen width");
            Assume.That(Screen.height, Is.GreaterThan(0), "screen height");
            var fitter = CreateActiveDetachedFitter("DetachedFitter");
            yield return null;
            fitter.enabled = false;
            yield return null;
            yield return null;

            fitter.enabled = true;
            SimulateSafeAreaChange(fitter);
            yield return null;
            yield return null;

            var rect = (RectTransform)fitter.transform;
            var comparer = new Vector2EqualityComparer(1e-5f);
            Assert.That(rect.anchorMin, Is.EqualTo(ExpectedAnchorMin()).Using(comparer), "anchorMin");
            Assert.That(rect.anchorMax, Is.EqualTo(ExpectedAnchorMax()).Using(comparer), "anchorMax");
            LogAssert.NoUnexpectedReceived();
        }

        // Helpers

        // Keeps Initialize free of the missing EventSystem warning.
        private void CreateEventSystem()
        {
            var eventSystemObject = new GameObject("TestEventSystem");
            _created.Add(eventSystemObject);
            eventSystemObject.AddComponent<EventSystem>();
        }

        // Initialize On Awake is off and nothing initialises it.
        private ViewRoot CreateUninitializedRoot()
        {
            var rootObject = new GameObject("UninitializedRoot", typeof(RectTransform));
            rootObject.SetActive(false);
            _created.Add(rootObject);
            rootObject.AddComponent<Canvas>();
            var root = rootObject.AddComponent<ViewRoot>();
            StageWorld.SetField(root, "_initializeOnAwake", false);
            rootObject.SetActive(true);
            return root;
        }

        // A button directly under the root, not inside a view; it goes with the root.
        private static ViewButton CreateViewButton(Transform parent, ViewButtonAction action, string viewId = null)
        {
            var button = ProbeUi.NewButton(ButtonName, parent, new Vector2(0.4f, 0.4f), new Vector2(0.6f, 0.6f));
            var viewButton = button.gameObject.AddComponent<ViewButton>();
            StageWorld.SetField(viewButton, "_action", action);
            StageWorld.SetField(viewButton, "_viewId", viewId);
            return viewButton;
        }

        // No root above it; scrambled before activation, so the fit in OnEnable is visible.
        private SafeAreaFitter CreateActiveDetachedFitter(string name)
        {
            var fitterObject = new GameObject(name, typeof(RectTransform));
            fitterObject.SetActive(false);
            _created.Add(fitterObject);
            Scramble((RectTransform)fitterObject.transform);

            var fitter = fitterObject.AddComponent<SafeAreaFitter>();
            fitterObject.SetActive(true);
            return fitter;
        }

        // The fitter remembers an outdated safe area, so its next compare sees a change; scrambled anchors make the refit visible.
        private static void SimulateSafeAreaChange(SafeAreaFitter fitter)
        {
            StageWorld.SetField(fitter, "_lastSafeArea", OutdatedSafeArea);
            Scramble(fitter);
        }

        private static Action<GameObject> WithViewButton(ViewButtonAction action, string viewId = null)
        {
            return view =>
            {
                var button = ProbeUi.NewButton(ButtonName, view.transform, new Vector2(0.4f, 0.4f), new Vector2(0.6f, 0.6f));
                var viewButton = button.gameObject.AddComponent<ViewButton>();
                StageWorld.SetField(viewButton, "_action", action);
                StageWorld.SetField(viewButton, "_viewId", viewId);
            };
        }

        private SafeAreaFitter CreateDetachedFitter(bool top, bool bottom, bool left, bool right)
        {
            // Inactive, so OnEnable does not fit from the real screen first.
            var fitterObject = new GameObject("DetachedFitter", typeof(RectTransform));
            fitterObject.SetActive(false);
            _created.Add(fitterObject);

            var fitter = fitterObject.AddComponent<SafeAreaFitter>();
            StageWorld.SetField(fitter, "_top", top);
            StageWorld.SetField(fitter, "_bottom", bottom);
            StageWorld.SetField(fitter, "_left", left);
            StageWorld.SetField(fitter, "_right", right);
            return fitter;
        }

        // Scrambled anchors first, so a fit is visible; the object goes with the root.
        private SafeAreaFitter CreateFitterUnderRoot(string name)
        {
            var fitterObject = new GameObject(name, typeof(RectTransform));
            fitterObject.transform.SetParent(_world.Root.transform, false);
            Scramble((RectTransform)fitterObject.transform);
            return fitterObject.AddComponent<SafeAreaFitter>();
        }

        private static void Scramble(SafeAreaFitter fitter)
        {
            Scramble((RectTransform)fitter.transform);
        }

        private static void Scramble(RectTransform rect)
        {
            rect.anchorMin = ScrambledMin;
            rect.anchorMax = ScrambledMax;
        }

        private static Vector2 ExpectedAnchorMin()
        {
            var safeArea = Screen.safeArea;
            return new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
        }

        private static Vector2 ExpectedAnchorMax()
        {
            var safeArea = Screen.safeArea;
            return new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
        }

        private GameObject InstanceOf(string viewId)
        {
            ViewHandle handle;
            Assert.That(_world.Navigator.TryGetOpen(viewId, out handle), Is.True, viewId + " is open");
            var instance = StageWorld.InstanceOf(handle);
            Assert.That(instance, Is.Not.Null, "instance of " + viewId);
            return instance;
        }

        private ViewButton ViewButtonOf(string viewId)
        {
            var viewButton = InstanceOf(viewId).GetComponentInChildren<ViewButton>();
            Assert.That(viewButton, Is.Not.Null, "ViewButton in " + viewId);
            return viewButton;
        }

        // Open, nothing running and the shield down, so Back() is not swallowed by the shield.
        private IEnumerator WaitSettled(string viewId)
        {
            return WaitFor(
                () =>
                {
                    ViewHandle handle;
                    return _world.Navigator.TryGetOpen(viewId, out handle)
                           && handle.Phase == ViewPhase.Open
                           && !_world.Navigator.IsBusy
                           && !_world.Stage.Shield.raycastTarget;
                },
                viewId + " to open and the root to settle");
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
