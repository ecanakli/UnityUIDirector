using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests
{
    [TestFixture]
    public sealed class BuiltInTransitionTests
    {
        public enum BuiltInKind
        {
            Fade,
            Scale,
            Slide
        }

        private const float PlaySeconds = 0.2f;

        private readonly List<CancellationTokenSource> _owners = new List<CancellationTokenSource>();
        private StageWorld _world;
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _timeScale;

            if (_world != null)
            {
                _world.Dispose();
                _world = null;
            }

            for (var i = 0; i < _owners.Count; i++)
                _owners[i].Dispose();
            _owners.Clear();

            yield return null;
        }

        // Built-ins

        [UnityTest]
        public IEnumerator Conformance_BuiltInTransition_MeetsTheTransitionContract([Values] BuiltInKind kind)
        {
            var world = CreateWorld();
            var view = CreateBenchView(world);
            var transition = AddBuiltIn(view, kind, PlaySeconds);

            yield return TransitionConformance.Run(transition, PoseReader(transition, kind));

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Open_BuiltInTransitionAtZeroTimeScale_OpensAndClosesWithoutTheWatchdog([Values] BuiltInKind kind)
        {
            // The watchdog is pushed past the wait deadline, so only the transition itself can finish the steps.
            var world = CreateWorld(root => StageWorld.SetField(root, "_transitionTimeoutSeconds", 10f));
            var template = world.Template<RecordingView>("TimeScaleView", t => UseTransition(t, AddBuiltIn(t, kind, PlaySeconds)));
            world.Row("timescale-view", template);
            Time.timeScale = 0f;

            var handle = world.Navigator.Open(ViewKey.Create("timescale-view"), world.Owner);
            var closed = CloseWatch.Start(handle);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open at timeScale 0");
            var transition = StageWorld.InstanceOf(handle).GetComponent<ViewTransition>();
            var poseAtOpen = PoseReader(transition, kind)();
            handle.Close();
            yield return TransitionConformance.WaitFor(() => closed.IsDone, "the view to close at timeScale 0");

            TransitionConformance.AssertPose(poseAtOpen, ExpectedShownPose(kind), "pose when the view reached Open");
            Assert.That(closed.Result.Reason, Is.EqualTo(CloseReason.Code), "close reason");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Snap_SlideOnScaledCanvas_OffsetsByLayerRectWidthNotScreenWidth()
        {
            var world = CreateWorld(root => root.GetComponent<CanvasScaler>().scaleFactor = 2f);
            var template = world.Template<RecordingView>("SlideView", t =>
            {
                var slide = AddBuiltIn(t, BuiltInKind.Slide, PlaySeconds);
                StageWorld.SetField(slide, "_edge", SlideEdge.Right);
                UseTransition(t, slide);
            });
            world.Row("slide-view", template);
            // The scaler applies on the next update; the layer rect is sized by the canvas after it.
            yield return null;
            Canvas.ForceUpdateCanvases();
            var layerRect = world.Stage.LayerRect(ViewLayers.Screens);
            Assume.That(Mathf.Abs(layerRect.rect.width - Screen.width), Is.GreaterThan(1f), "the scaled canvas must make the layer narrower than the screen");

            var handle = world.Navigator.Open(ViewKey.Create("slide-view"), world.Owner);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the slide view to open");
            var slideView = StageWorld.InstanceOf(handle).GetComponent<SlideTransition>();
            var rect = (RectTransform)slideView.transform;
            var shown = rect.anchoredPosition;
            slideView.Snap(false);
            var hidden = rect.anchoredPosition;
            slideView.Snap(true);

            Assert.That(hidden.x - shown.x, Is.EqualTo(layerRect.rect.width).Within(0.01f), "horizontal offset in canvas units; screen width is " + Screen.width);
            Assert.That(hidden.y - shown.y, Is.EqualTo(0f).Within(0.01f), "vertical offset");
            LogAssert.NoUnexpectedReceived();
        }

        // Sequence

        [UnityTest]
        public IEnumerator Open_CustomTransition_SnapsHiddenThenPlaysShowThenPlaysHideOnClose()
        {
            var world = CreateWorld();
            var template = world.Template<RecordingView>("SequenceView", t => UseTransition(t, t.AddComponent<ScaledTimeTransition>()));
            world.Row("sequence-view", template);

            var handle = world.Navigator.Open(ViewKey.Create("sequence-view"), world.Owner);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the view to open");
            var transition = StageWorld.InstanceOf(handle).GetComponent<ScaledTimeTransition>();
            var callsAtOpen = transition.Calls.ToArray();
            var canvasAtOpen = transition.CanvasEnabledAtCall.ToArray();
            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the view to close");

            Assert.That(callsAtOpen, Is.EqualTo(new[] { ScaledTimeTransition.SnapHidden, ScaledTimeTransition.PlayShow }), "calls up to Open");
            Assert.That(canvasAtOpen, Is.EqualTo(new[] { true, true }), "view canvas enabled at each call: the hidden snap runs before the first projection in the same step, so nothing is drawn");
            Assert.That(transition.Calls.ToArray(), Is.EqualTo(new[] { ScaledTimeTransition.SnapHidden, ScaledTimeTransition.PlayShow, ScaledTimeTransition.PlayHide }), "calls after the close");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OwnerCancelled_DuringShowTransition_SnapsToAnEndPoseAndEndsTheRequest()
        {
            // KeepInstance, so the instance survives the terminate and its pose can be read; a cut transition snaps only an instance that survives.
            var world = CreateWorld();
            var template = world.Template<RecordingView>("TerminateView", t => UseTransition(t, AddBuiltIn(t, BuiltInKind.Fade, 2f)));
            world.Row("terminate-view", template, retention: ViewRetention.KeepInstance);
            var owner = CreateOwner();

            var handle = world.Navigator.Open(ViewKey.Create("terminate-view"), owner.Token);
            var closed = CloseWatch.Start(handle);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Opening && AlphaOf(handle) > 0f, "the show fade to be under way");
            var group = StageWorld.InstanceOf(handle).GetComponent<CanvasGroup>();
            var alphaBeforeCancel = group.alpha;
            owner.Cancel();
            yield return TransitionConformance.WaitFor(() => closed.IsDone, "the request to end");
            var alphaAtEnd = group.alpha;
            yield return null;
            yield return null;
            yield return null;

            Assert.That(alphaBeforeCancel, Is.GreaterThan(0f).And.LessThan(1f), "the fade was mid-way when the owner ended");
            Assert.That(closed.Result.Reason, Is.EqualTo(CloseReason.OwnerEnded), "close reason");
            Assert.That(closed.Result.WasShown, Is.True, "a terminate during Opening ends as shown");
            Assert.That(alphaAtEnd, Is.EqualTo(0f).Or.EqualTo(1f), "the cut transition is snapped to an end pose");
            Assert.That(group.alpha, Is.EqualTo(alphaAtEnd), "the cut transition stopped writing");
            LogAssert.NoUnexpectedReceived();
        }

        // Watchdog

        [UnityTest]
        public IEnumerator Open_ScaledTimeTransitionAtZeroTimeScale_WatchdogSnapsLogs106AndOpens()
        {
            var world = CreateWorld(root => StageWorld.SetField(root, "_transitionTimeoutSeconds", 0.3f));
            var template = world.Template<RecordingView>("WatchdogView", t => UseTransition(t, t.AddComponent<ScaledTimeTransition>()));
            world.Row("watchdog-view", template);
            Time.timeScale = 0f;
            LogAssert.Expect(LogType.Warning, new Regex(DiagnosticIds.TransitionTimedOut + ".*watchdog-view"));

            var handle = world.Navigator.Open(ViewKey.Create("watchdog-view"), world.Owner);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open, "the watchdog to end the show");
            var transition = StageWorld.InstanceOf(handle).GetComponent<ScaledTimeTransition>();
            yield return TransitionConformance.WaitFor(() => transition.PlayStoppedByToken, "the cancelled play to return", 1f);

            Assert.That(transition.Progress, Is.EqualTo(1f), "snapped to the shown pose");
            Assert.That(transition.Calls.ToArray(), Is.EqualTo(new[] { ScaledTimeTransition.SnapHidden, ScaledTimeTransition.PlayShow, ScaledTimeTransition.SnapShown }), "calls");
            LogAssert.NoUnexpectedReceived();
        }

        // Backdrop

        [UnityTest]
        public IEnumerator Open_FirstModalPopup_EnablesBackdropAndFadesItIn()
        {
            var world = CreateWorld();
            world.Row("popup-a", world.Template<RecordingView>("PopupA"), ViewLayers.Popups);
            var backdrop = world.Stage.BackdropOf(ViewLayers.Popups);
            var canvas = backdrop.GetComponent<Canvas>();
            var raycaster = backdrop.GetComponent<GraphicRaycaster>();
            var group = backdrop.GetComponent<CanvasGroup>();

            world.Navigator.Open(ViewKey.Create("popup-a"), world.Owner);
            yield return TransitionConformance.WaitFor(() => canvas.enabled, "the backdrop canvas to turn on");
            var alphaWhenEnabled = group.alpha;
            var raycasterWhenEnabled = raycaster.enabled;
            yield return TransitionConformance.WaitFor(() => group.alpha >= 1f, "the backdrop to finish fading in", 2f);

            Assert.That(alphaWhenEnabled, Is.LessThan(1f), "alpha when the canvas turned on");
            Assert.That(raycasterWhenEnabled, Is.True, "raycaster when the canvas turned on");
            Assert.That(canvas.enabled, Is.True, "canvas after the fade");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Close_LastModalPopup_StopsBackdropRaycastsAtOnceAndTurnsCanvasOffAfterFade()
        {
            var world = CreateWorld();
            world.Row("popup-a", world.Template<RecordingView>("PopupA"), ViewLayers.Popups);
            var backdrop = world.Stage.BackdropOf(ViewLayers.Popups);
            var canvas = backdrop.GetComponent<Canvas>();
            var raycaster = backdrop.GetComponent<GraphicRaycaster>();
            var group = backdrop.GetComponent<CanvasGroup>();
            var handle = world.Navigator.Open(ViewKey.Create("popup-a"), world.Owner);
            yield return TransitionConformance.WaitFor(() => handle.Phase == ViewPhase.Open && group.alpha >= 1f, "the popup to open with its backdrop faded in");

            handle.Close();
            yield return TransitionConformance.WaitFor(() => !handle.IsAlive, "the popup to close");
            var raycasterAtClose = raycaster.enabled;
            var canvasAtClose = canvas.enabled;
            yield return TransitionConformance.WaitFor(() => !canvas.enabled, "the backdrop canvas to turn off", 2f);

            Assert.That(raycasterAtClose, Is.False, "raycaster when the last modal view ended");
            Assert.That(canvasAtClose, Is.True, "canvas while fading out");
            Assert.That(group.alpha, Is.EqualTo(0f), "alpha after the fade");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator StackedPopups_OpenAndCloseSecond_BackdropJumpsWithoutFading()
        {
            var world = CreateWorld();
            world.Row("popup-a", world.Template<RecordingView>("PopupA"), ViewLayers.Popups);
            world.Row("popup-b", world.Template<RecordingView>("PopupB"), ViewLayers.Popups);
            var backdrop = world.Stage.BackdropOf(ViewLayers.Popups);
            var canvas = backdrop.GetComponent<Canvas>();
            var group = backdrop.GetComponent<CanvasGroup>();
            var first = world.Navigator.Open(ViewKey.Create("popup-a"), world.Owner);
            yield return TransitionConformance.WaitFor(() => first.Phase == ViewPhase.Open && group.alpha >= 1f, "the first popup to open with its backdrop faded in");
            var firstHost = StageWorld.InstanceOf(first).GetComponent<ViewHost>();
            var lowestAlpha = group.alpha;
            Func<bool> sample = () =>
            {
                lowestAlpha = Mathf.Min(lowestAlpha, group.alpha);
                return true;
            };

            var second = world.Navigator.Open(ViewKey.Create("popup-b"), world.Owner);
            yield return TransitionConformance.WaitFor(() => sample() && second.Phase == ViewPhase.Open, "the second popup to open");
            yield return SampleFrames(3, sample);
            var secondHost = StageWorld.InstanceOf(second).GetComponent<ViewHost>();
            var orderWithSecond = canvas.sortingOrder;
            var lowestWithSecond = lowestAlpha;
            second.Close();
            yield return TransitionConformance.WaitFor(() => sample() && !second.IsAlive, "the second popup to close");
            yield return SampleFrames(3, sample);

            Assert.That(orderWithSecond, Is.EqualTo(secondHost.SortingOrder - 5), "backdrop order under the second popup");
            Assert.That(lowestWithSecond, Is.EqualTo(1f), "lowest alpha while the second popup opened");
            Assert.That(canvas.sortingOrder, Is.EqualTo(firstHost.SortingOrder - 5), "backdrop order after the second popup closed");
            Assert.That(lowestAlpha, Is.EqualTo(1f), "lowest alpha while the second popup closed");
            Assert.That(canvas.enabled, Is.True, "canvas with the first popup still open");
            LogAssert.NoUnexpectedReceived();
        }

        // Helpers

        private StageWorld CreateWorld(Action<ViewRoot> configure = null)
        {
            _world = new StageWorld(configure);
            return _world;
        }

        private CancellationTokenSource CreateOwner()
        {
            var owner = new CancellationTokenSource();
            _owners.Add(owner);
            return owner;
        }

        // A view object under a fixed 400 x 300 parent inside the root, so slide offsets do not depend on the screen.
        private static GameObject CreateBenchView(StageWorld world)
        {
            var bench = new GameObject("Bench", typeof(RectTransform));
            var benchRect = (RectTransform)bench.transform;
            benchRect.SetParent(world.Stage.LayerRect(ViewLayers.Screens), false);
            benchRect.anchorMin = new Vector2(0.5f, 0.5f);
            benchRect.anchorMax = new Vector2(0.5f, 0.5f);
            benchRect.sizeDelta = new Vector2(400f, 300f);

            var view = new GameObject("BenchView", typeof(RectTransform));
            var viewRect = (RectTransform)view.transform;
            viewRect.SetParent(benchRect, false);
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero;
            viewRect.offsetMax = Vector2.zero;
            return view;
        }

        private static ViewTransition AddBuiltIn(GameObject view, BuiltInKind kind, float duration)
        {
            ViewTransition transition;
            switch (kind)
            {
                case BuiltInKind.Fade:
                    transition = view.AddComponent<FadeTransition>();
                    break;
                case BuiltInKind.Scale:
                    transition = view.AddComponent<ScaleTransition>();
                    break;
                default:
                    transition = view.AddComponent<SlideTransition>();
                    break;
            }

            StageWorld.SetField(transition, "_duration", duration);
            return transition;
        }

        private static void UseTransition(GameObject template, ViewTransition transition)
        {
            StageWorld.SetField(template.GetComponent<ViewHost>(), "_transition", transition);
        }

        private static Func<Vector4> PoseReader(ViewTransition transition, BuiltInKind kind)
        {
            switch (kind)
            {
                case BuiltInKind.Fade:
                {
                    var group = transition.GetComponent<CanvasGroup>();
                    return () => new Vector4(group.alpha, 0f, 0f, 0f);
                }
                case BuiltInKind.Scale:
                {
                    var target = transition.transform;
                    return () => new Vector4(target.localScale.x, target.localScale.y, target.localScale.z, 0f);
                }
                default:
                {
                    var target = (RectTransform)transition.transform;
                    return () => new Vector4(target.anchoredPosition.x, target.anchoredPosition.y, 0f, 0f);
                }
            }
        }

        // The stretched template has alpha 1, scale 1 and anchored position zero when shown.
        private static Vector4 ExpectedShownPose(BuiltInKind kind)
        {
            switch (kind)
            {
                case BuiltInKind.Fade:
                    return new Vector4(1f, 0f, 0f, 0f);
                case BuiltInKind.Scale:
                    return new Vector4(1f, 1f, 1f, 0f);
                default:
                    return Vector4.zero;
            }
        }

        private static float AlphaOf(ViewHandle handle)
        {
            GameObject instance;
            if (!handle.TryGetInstance(out instance))
                return 0f;

            var group = instance.GetComponent<CanvasGroup>();
            return group == null ? 0f : group.alpha;
        }

        private static IEnumerator SampleFrames(int frames, Func<bool> sample)
        {
            for (var i = 0; i < frames; i++)
            {
                sample();
                yield return null;
            }

            sample();
        }

        // Records the outcome of one request without blocking the test.
        private sealed class CloseWatch
        {
            public bool IsDone { get; private set; }
            public ViewClosed Result { get; private set; }

            public static CloseWatch Start(ViewHandle handle)
            {
                var watch = new CloseWatch();

                // Fire-and-forget: the test polls IsDone frame by frame.
                watch.RunAsync(handle).Forget();
                return watch;
            }

            private async UniTaskVoid RunAsync(ViewHandle handle)
            {
                Result = await handle.WaitForCloseAsync(CancellationToken.None);
                IsDone = true;
            }
        }
    }
}
