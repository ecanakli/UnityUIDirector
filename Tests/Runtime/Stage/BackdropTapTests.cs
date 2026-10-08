using System;
using System.Collections;
using System.Threading;
using Ecanakli.UIDirector.Tests.Probes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests
{
    // Taps on a real stage's backdrop through EventSystem.RaycastAll and ExecuteEvents: close on backdrop, swallowing, shield, stacking and hiding.
    [TestFixture]
    public sealed class BackdropTapTests
    {
        private const string ScreenId = "screen";
        private const string PopupId = "popup";
        private const string FirstPopupId = "first";
        private const string SecondPopupId = "second";
        private const string OverlayId = "overlay";
        private const string CornerButtonName = "CornerButton";
        private const string ContentButtonName = "ContentButton";

        private const int MaxFrames = 300;

        // Well above the fades, so fast batch-mode frames do not time out a running open.
        private const float MinWaitSeconds = 5f;

        // Long enough that the open is still in flight for several frames (one frame advances at most 1/30 s).
        private const float SlowFadeSeconds = 0.5f;

        // Frames a tap that should change nothing gets to show an effect before the state is read.
        private const int SettleFrames = 3;

        private StageWorld _world;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_world != null)
            {
                _world.Dispose();
                _world = null;
            }

            // Destroy is deferred; the next test must not see this world's objects.
            yield return null;
        }

        [UnityTest]
        public IEnumerator Tap_OpenPopupWithCloseOnBackdrop_ClosesWithBackdrop()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(PopupId, _world.Template<RecordingView>("Popup"), layer: ViewLayers.Popups, closeOnBackdrop: true);
            var popup = Open(PopupId);
            yield return WaitSettled(popup, "the popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            var backdrop = BackdropGraphic(ViewLayers.Popups);
            RequireRendered(backdrop);
            var closed = popup.WaitForCloseAsync(CancellationToken.None);

            var hit = UiTap.At(CornerPoint());
            Assert.That(hit, Is.SameAs(backdrop.gameObject), "first hit: " + NameOf(hit));
            yield return WaitFor(() => !closed.IsPending(), "the popup to close after the tap");

            Assert.That(closed.ResultNow().Reason, Is.EqualTo(CloseReason.Backdrop), "close reason");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Tap_PopupWithoutCloseOnBackdrop_StaysOpenAndNothingBehindReceivesIt()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(ScreenId, _world.Template<RecordingView>("Screen", WithCornerButton));
            _world.Row(PopupId, _world.Template<RecordingView>("Popup"), layer: ViewLayers.Popups);
            var screen = Open(ScreenId);
            yield return WaitSettled(screen, "the screen to open");
            yield return SettleRender();
            var button = CornerButtonOf(screen);
            RequireRendered(button.targetGraphic);
            var clicks = 0;
            button.onClick.AddListener(() => clicks++);
            var point = ProbeUi.ScreenPointOf((RectTransform)button.transform);
            Assume.That(TopHit(point), Is.SameAs(button.gameObject), "control: the screen's button is the topmost hit before the popup opens");
            var popup = Open(PopupId);
            yield return WaitSettled(popup, "the popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            var backdrop = BackdropGraphic(ViewLayers.Popups);
            RequireRendered(backdrop);

            var hit = UiTap.At(point);
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            Assert.That(hit, Is.SameAs(backdrop.gameObject), "first hit: " + NameOf(hit));
            Assert.That(clicks, Is.Zero, "clicks on the screen's button");
            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Open), "popup phase after the tap");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Tap_WhileTheSecondPopupIsStillOpening_ShieldUp_ClosesNothing()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(FirstPopupId, _world.Template<RecordingView>("First"), layer: ViewLayers.Popups, closeOnBackdrop: true);
            _world.Row(SecondPopupId, _world.Template<RecordingView>("Second", WithSlowFade), layer: ViewLayers.Popups, closeOnBackdrop: true);
            var first = Open(FirstPopupId);
            yield return WaitSettled(first, "the first popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            RequireRendered(BackdropGraphic(ViewLayers.Popups));

            var second = Open(SecondPopupId);
            yield return null;
            var phaseAtTap = second.Phase;
            var shieldUpAtTap = _world.Stage.Shield.raycastTarget;
            var hit = UiTap.At(CornerPoint());
            yield return WaitSettled(second, "the second popup to open");
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            Assume.That(phaseAtTap, Is.Not.EqualTo(ViewPhase.Open), "the second popup was already open at the tap; the fade is too short");
            Assume.That(shieldUpAtTap, Is.True, "control: the shield was up at the tap");
            Assert.That(hit, Is.SameAs(_world.Stage.Shield.gameObject), "first hit: " + NameOf(hit));
            Assert.That(first.Phase, Is.EqualTo(ViewPhase.Open), "first popup phase");
            Assert.That(second.Phase, Is.EqualTo(ViewPhase.Open), "second popup phase");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Tap_WhileInputIsHeld_ShieldTakesTheTapAndTheOpenPopupStaysOpen()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(PopupId, _world.Template<RecordingView>("Popup"), layer: ViewLayers.Popups, closeOnBackdrop: true);
            var popup = Open(PopupId);
            yield return WaitSettled(popup, "the popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            RequireRendered(BackdropGraphic(ViewLayers.Popups));
            _world.Navigator.HoldInput("backdrop tap test", _world.Owner);
            Assume.That(_world.Stage.Shield.raycastTarget, Is.True, "control: the hold raised the shield");

            var hit = UiTap.At(CornerPoint());
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            Assert.That(hit, Is.SameAs(_world.Stage.Shield.gameObject), "first hit: " + NameOf(hit));
            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Open), "popup phase after the tap");
            LogAssert.NoUnexpectedReceived();
        }

        // A pointer that raycast the backdrop before the shield rose still delivers its click there.
        [UnityTest]
        public IEnumerator Tap_ShieldRaisedBetweenRaycastAndClick_ClosesNothing()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(PopupId, _world.Template<RecordingView>("Popup"), layer: ViewLayers.Popups, closeOnBackdrop: true);
            var popup = Open(PopupId);
            yield return WaitSettled(popup, "the popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            var backdrop = BackdropGraphic(ViewLayers.Popups);
            RequireRendered(backdrop);
            var raycast = UiTap.Raycast(CornerPoint());
            Assume.That(raycast.gameObject, Is.SameAs(backdrop.gameObject), "control: the backdrop is the topmost hit before the hold");

            _world.Navigator.HoldInput("backdrop tap test", _world.Owner);
            var hit = UiTap.Click(raycast);
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            Assert.That(hit, Is.SameAs(backdrop.gameObject), "clicked object: " + NameOf(hit));
            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Open), "popup phase after the click");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Tap_StackedPopups_ClosesOnlyTheTop()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(FirstPopupId, _world.Template<RecordingView>("First"), layer: ViewLayers.Popups, closeOnBackdrop: true);
            _world.Row(SecondPopupId, _world.Template<RecordingView>("Second"), layer: ViewLayers.Popups, closeOnBackdrop: true);
            var first = Open(FirstPopupId);
            yield return WaitSettled(first, "the first popup to open");
            var second = Open(SecondPopupId);
            yield return WaitSettled(second, "the second popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            var backdrop = BackdropGraphic(ViewLayers.Popups);
            RequireRendered(backdrop);
            var secondClosed = second.WaitForCloseAsync(CancellationToken.None);

            var hit = UiTap.At(CornerPoint());
            Assert.That(hit, Is.SameAs(backdrop.gameObject), "first hit: " + NameOf(hit));
            yield return WaitFor(() => !secondClosed.IsPending(), "the top popup to close after the tap");
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            Assert.That(secondClosed.ResultNow().Reason, Is.EqualTo(CloseReason.Backdrop), "close reason of the top popup");
            Assert.That(first.Phase, Is.EqualTo(ViewPhase.Open), "phase of the popup below");
            LogAssert.NoUnexpectedReceived();
        }

        // Without a hide transition the top ends at once, and a second tap rightly closes the next popup.
        [UnityTest]
        public IEnumerator Tap_TwiceInTheSameFrameWhileTheTopPopupHides_ClosesOnlyTheTop()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(FirstPopupId, _world.Template<RecordingView>("First"), layer: ViewLayers.Popups, closeOnBackdrop: true);
            _world.Row(SecondPopupId, _world.Template<RecordingView>("Second", WithSlowFade), layer: ViewLayers.Popups, closeOnBackdrop: true);
            var first = Open(FirstPopupId);
            yield return WaitSettled(first, "the first popup to open");
            var second = Open(SecondPopupId);
            yield return WaitSettled(second, "the second popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            var backdrop = BackdropGraphic(ViewLayers.Popups);
            RequireRendered(backdrop);
            var secondClosed = second.WaitForCloseAsync(CancellationToken.None);

            var firstHit = UiTap.At(CornerPoint());
            Assume.That(second.Phase, Is.EqualTo(ViewPhase.Closing), "control: the top popup is still hiding at the second tap");
            var secondHit = UiTap.At(CornerPoint());
            Assert.That(firstHit, Is.SameAs(backdrop.gameObject), "first tap's hit: " + NameOf(firstHit));
            yield return WaitFor(() => !secondClosed.IsPending(), "the top popup to close after the taps");
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            Assert.That(secondClosed.ResultNow().Reason, Is.EqualTo(CloseReason.Backdrop), "close reason of the top popup");
            Assert.That(first.Phase, Is.EqualTo(ViewPhase.Open), "phase of the popup below; the second tap hit " + NameOf(secondHit));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Tap_TwiceOnOnePopup_ClosesOnceWithBackdrop()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(PopupId, _world.Template<RecordingView>("Popup"), layer: ViewLayers.Popups, closeOnBackdrop: true);
            var popup = Open(PopupId);
            yield return WaitSettled(popup, "the popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            var backdrop = BackdropGraphic(ViewLayers.Popups);
            RequireRendered(backdrop);
            var closed = popup.WaitForCloseAsync(CancellationToken.None);

            var firstHit = UiTap.At(CornerPoint());
            UiTap.At(CornerPoint());
            Assert.That(firstHit, Is.SameAs(backdrop.gameObject), "first tap's hit: " + NameOf(firstHit));
            yield return WaitFor(() => !closed.IsPending(), "the popup to close after the taps");
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            Assert.That(closed.ResultNow().Reason, Is.EqualTo(CloseReason.Backdrop), "close reason");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Tap_OnPopupContent_ClosesNothingAndReachesTheContent()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(PopupId, _world.Template<RecordingView>("Popup", WithContentButton), layer: ViewLayers.Popups, closeOnBackdrop: true);
            var popup = Open(PopupId);
            yield return WaitSettled(popup, "the popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            RequireRendered(BackdropGraphic(ViewLayers.Popups));
            var button = ButtonOf(popup, ContentButtonName);
            RequireRendered(button.targetGraphic);
            var clicks = 0;
            button.onClick.AddListener(() => clicks++);

            var hit = UiTap.At(ProbeUi.ScreenPointOf((RectTransform)button.transform));
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            Assert.That(hit, Is.SameAs(button.gameObject), "first hit: " + NameOf(hit));
            Assert.That(clicks, Is.EqualTo(1), "clicks on the popup's button");
            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Open), "popup phase after the tap");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Tap_AfterTheBackdropHid_ReachesTheScreenBelow()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(ScreenId, _world.Template<RecordingView>("Screen", WithCornerButton));
            _world.Row(PopupId, _world.Template<RecordingView>("Popup"), layer: ViewLayers.Popups);
            var screen = Open(ScreenId);
            yield return WaitSettled(screen, "the screen to open");
            yield return SettleRender();
            var button = CornerButtonOf(screen);
            RequireRendered(button.targetGraphic);
            var clicks = 0;
            button.onClick.AddListener(() => clicks++);
            var point = ProbeUi.ScreenPointOf((RectTransform)button.transform);
            var popup = Open(PopupId);
            yield return WaitSettled(popup, "the popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            var backdrop = BackdropGraphic(ViewLayers.Popups);
            RequireRendered(backdrop);
            Assume.That(TopHit(point), Is.SameAs(backdrop.gameObject), "control: the backdrop is the topmost hit while the popup is open");

            popup.Close();
            yield return WaitFor(
                () => !popup.IsAlive && !_world.Navigator.IsBusy && !_world.Stage.Shield.raycastTarget,
                "the popup to end and the root to settle");
            var hiding = _world.Stage.BackdropOf(ViewLayers.Popups);
            Assume.That(hiding.IsFading, Is.True, "control: the backdrop is still fading out at the tap");
            Assume.That(hiding.GetComponent<Canvas>().enabled, Is.True, "control: the backdrop's canvas is still on at the tap");
            var hit = UiTap.At(point);

            Assert.That(hit, Is.SameAs(button.gameObject), "first hit: " + NameOf(hit));
            Assert.That(clicks, Is.EqualTo(1), "clicks on the screen's button");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Tap_AfterReopeningWhileTheBackdropFadesOut_ClosesWithBackdropAgain()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(PopupId, _world.Template<RecordingView>("Popup"), layer: ViewLayers.Popups, closeOnBackdrop: true);
            var popup = Open(PopupId);
            yield return WaitSettled(popup, "the popup to open");
            yield return SettleBackdrop(ViewLayers.Popups);
            var backdrop = BackdropGraphic(ViewLayers.Popups);
            RequireRendered(backdrop);
            var closed = popup.WaitForCloseAsync(CancellationToken.None);
            var firstHit = UiTap.At(CornerPoint());
            Assume.That(firstHit, Is.SameAs(backdrop.gameObject), "control: the first tap hit the backdrop");
            yield return WaitFor(
                () => !closed.IsPending() && !popup.IsAlive && !_world.Navigator.IsBusy && !_world.Stage.Shield.raycastTarget,
                "the first popup to end and the root to settle");
            Assume.That(_world.Stage.BackdropOf(ViewLayers.Popups).IsFading, Is.True, "control: the backdrop is still fading out at the reopen");

            var reopened = Open(PopupId);
            yield return WaitSettled(reopened, "the popup to open again");
            yield return SettleBackdrop(ViewLayers.Popups);
            RequireRendered(backdrop);
            var reclosed = reopened.WaitForCloseAsync(CancellationToken.None);

            var hit = UiTap.At(CornerPoint());
            Assert.That(hit, Is.SameAs(backdrop.gameObject), "second tap's hit: " + NameOf(hit));
            yield return WaitFor(() => !reclosed.IsPending(), "the reopened popup to close after the tap");

            Assert.That(reclosed.ResultNow().Reason, Is.EqualTo(CloseReason.Backdrop), "close reason of the reopened popup");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Tap_SystemLayerRowWithCloseOnBackdrop_ClosesWithBackdrop()
        {
            RequireUsableScreen();
            _world = new StageWorld();
            _world.Row(OverlayId, _world.Template<RecordingView>("Overlay"), layer: ViewLayers.System, closeOnBackdrop: true);
            var overlay = Open(OverlayId);
            yield return WaitSettled(overlay, "the overlay to open");
            yield return SettleBackdrop(ViewLayers.System);
            var backdrop = BackdropGraphic(ViewLayers.System);
            RequireRendered(backdrop);
            var closed = overlay.WaitForCloseAsync(CancellationToken.None);

            var hit = UiTap.At(CornerPoint());
            Assert.That(hit, Is.SameAs(backdrop.gameObject), "first hit: " + NameOf(hit));
            yield return WaitFor(() => !closed.IsPending(), "the overlay to close after the tap");

            Assert.That(closed.ResultNow().Reason, Is.EqualTo(CloseReason.Backdrop), "close reason");
            LogAssert.NoUnexpectedReceived();
        }

        // Helpers

        private static void WithSlowFade(GameObject view)
        {
            var fade = view.AddComponent<FadeTransition>();
            StageWorld.SetField(fade, "_duration", SlowFadeSeconds);
            StageWorld.SetField(view.GetComponent<ViewHost>(), "_transition", fade);
        }

        // A button over the lower-left corner, under the tap point the popup tests use.
        private static void WithCornerButton(GameObject view)
        {
            ProbeUi.NewButton(CornerButtonName, view.transform, Vector2.zero, new Vector2(0.2f, 0.2f));
        }

        // A button over the centre, clear of the corner tap point.
        private static void WithContentButton(GameObject view)
        {
            ProbeUi.NewButton(ContentButtonName, view.transform, new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.7f));
        }

        // Away from the centre, where a popup's own content would sit.
        private static Vector2 CornerPoint()
        {
            return new Vector2(Screen.width * 0.1f, Screen.height * 0.1f);
        }

        // Batch runs may give no usable screen; then no raycast can run.
        private static void RequireUsableScreen()
        {
            Assume.That(ProbeUi.ScreenIsUsable(), Is.True, "screen is " + Screen.width + "x" + Screen.height);
        }

        // A graphic keeps depth -1 until it renders, and the raycaster skips it until then.
        private static void RequireRendered(Graphic graphic)
        {
            Assume.That(graphic.canvasRenderer.absoluteDepth, Is.GreaterThanOrEqualTo(0), graphic.name + " has not rendered");
        }

        private static GameObject TopHit(Vector2 point)
        {
            var hits = ProbeUi.Raycast(point);
            return hits.Count == 0 ? null : hits[0].gameObject;
        }

        private static string NameOf(GameObject hit)
        {
            return hit != null ? hit.name : "nothing";
        }

        // Layout and a rendered frame, so graphics have a depth the raycaster accepts.
        private static IEnumerator SettleRender()
        {
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
        }

        private ViewHandle Open(string viewId)
        {
            return _world.Navigator.Open(ViewKey.Create(viewId), _world.Owner);
        }

        private IEnumerator WaitSettled(ViewHandle handle, string what)
        {
            return WaitFor(
                () => handle.Phase == ViewPhase.Open && !_world.Navigator.IsBusy && !_world.Stage.Shield.raycastTarget,
                what);
        }

        // The backdrop fade has ended and the backdrop has rendered.
        private IEnumerator SettleBackdrop(ViewLayer layer)
        {
            var backdrop = _world.Stage.BackdropOf(layer);
            Assert.That(backdrop, Is.Not.Null, "backdrop of layer " + layer.Id);
            yield return WaitFor(() => !backdrop.IsFading, "the backdrop of layer " + layer.Id + " to finish its fade");
            yield return SettleRender();
        }

        private Graphic BackdropGraphic(ViewLayer layer)
        {
            var backdrop = _world.Stage.BackdropOf(layer);
            Assert.That(backdrop, Is.Not.Null, "backdrop of layer " + layer.Id);
            var graphic = backdrop.GetComponent<Graphic>();
            Assert.That(graphic, Is.Not.Null, "graphic on the backdrop of layer " + layer.Id);
            return graphic;
        }

        private Button CornerButtonOf(ViewHandle handle)
        {
            return ButtonOf(handle, CornerButtonName);
        }

        private Button ButtonOf(ViewHandle handle, string name)
        {
            var instance = StageWorld.InstanceOf(handle);
            Assert.That(instance, Is.Not.Null, "instance of " + handle.ViewId);
            var child = instance.transform.Find(name);
            Assert.That(child, Is.Not.Null, name + " in " + handle.ViewId);
            return child.GetComponent<Button>();
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
