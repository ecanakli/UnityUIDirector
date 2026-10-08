using System;
using System.Collections;
using System.Threading;
using Ecanakli.UIDirector.Tests.Probes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // Hiding the whole UI on a real root: a root CanvasGroup at alpha 0 and interactable false, plus HoldInput.
    [TestFixture]
    public sealed class HideUiRecipeTests
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 1f;
        private const string ButtonName = "Button";
        private const string RemoteAssetKey = "remote-popup";
        private const string NextRemoteAssetKey = "remote-popup-next";

        private StageWorld _world;
        private RenderTexture _target;
        private Texture2D _readback;
        private ProbeRender.RenderRig _rig;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_world != null)
            {
                if (_world.Camera != null)
                    _world.Camera.targetTexture = null;

                _world.Dispose();
                _world = null;
            }

            if (_target != null)
                Object.Destroy(_target);
            if (_readback != null)
                Object.Destroy(_readback);

            _target = null;
            _readback = null;
            _rig = null;

            // Destroy is deferred; the next test must not see this world's objects.
            yield return null;
        }

        // Drawing

        [UnityTest]
        public IEnumerator RootGroupAlphaZero_DrawsNoViewAndNoBackdrop()
        {
            CreateDrawingWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithRedFill));
            _world.Row("popup", _world.Template<RecordingView>("Popup", WithGreenLeftHalf), ViewLayers.Popups);
            yield return OpenAndWait("screen");
            yield return OpenAndWait("popup");
            yield return WaitForBackdrop();
            var before = ProbeRender.ReadSample(_rig);
            Assume.That(ProbeRender.Classify(before.Left), Is.EqualTo("green"), "control: the popup draws at the left point, " + Describe(before));
            Assume.That(IsBlue(before.Right), Is.True, "control: the backdrop draws at the right point, " + Describe(before));
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;

            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);
            yield return null;
            var hidden = ProbeRender.ReadSample(_rig);

            Assert.That(hold.IsActive, Is.True, "control: the hold is up");
            Assert.That(ProbeRender.Classify(hidden.Left), Is.EqualTo("black"), "popup point, " + Describe(hidden));
            Assert.That(ProbeRender.Classify(hidden.Right), Is.EqualTo("black"), "backdrop point, " + Describe(hidden));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RootGroupAlphaOne_AfterHide_DrawsViewsAgain()
        {
            CreateDrawingWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithRedFill));
            yield return OpenAndWait("screen");
            yield return null;
            var before = ProbeRender.ReadSample(_rig);
            Assume.That(ProbeRender.Classify(before.Right), Is.EqualTo("red"), "control: the screen draws before the hide, " + Describe(before));
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;
            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);
            yield return null;
            Assume.That(IsBlack(ProbeRender.ReadSample(_rig)), Is.True, "control: the screen is hidden");

            hold.Remove();
            group.alpha = 1f;
            group.interactable = true;
            yield return null;
            var restored = ProbeRender.ReadSample(_rig);

            Assert.That(ProbeRender.Classify(restored.Left), Is.EqualTo("red"), "left point, " + Describe(restored));
            Assert.That(ProbeRender.Classify(restored.Right), Is.EqualTo("red"), "right point, " + Describe(restored));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ViewOpenedWhileHidden_StaysUndrawnAndDrawsAfterRestore()
        {
            CreateDrawingWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithRedFill));
            _world.Row("popup", _world.Template<RecordingView>("Popup", WithGreenLeftHalf), ViewLayers.Popups);
            yield return OpenAndWait("screen");
            yield return null;
            var before = ProbeRender.ReadSample(_rig);
            Assume.That(ProbeRender.Classify(before.Right), Is.EqualTo("red"), "control: the screen draws before the hide, " + Describe(before));
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;
            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);

            yield return OpenAndWait("popup");
            yield return WaitForBackdrop();
            yield return null;
            var hidden = ProbeRender.ReadSample(_rig);
            hold.Remove();
            group.alpha = 1f;
            group.interactable = true;
            yield return null;
            var restored = ProbeRender.ReadSample(_rig);

            Assert.That(ProbeRender.Classify(hidden.Left), Is.EqualTo("black"), "popup point while hidden, " + Describe(hidden));
            Assert.That(ProbeRender.Classify(hidden.Right), Is.EqualTo("black"), "backdrop point while hidden, " + Describe(hidden));
            Assert.That(ProbeRender.Classify(restored.Left), Is.EqualTo("green"), "popup point after the restore, " + Describe(restored));
            Assert.That(IsBlue(restored.Right), Is.True, "backdrop point after the restore, " + Describe(restored));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ViewClosedWhileHidden_EndsAndRestoreShowsOnlyTheScreen()
        {
            CreateDrawingWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithRedFill));
            _world.Row("popup", _world.Template<RecordingView>("Popup", WithGreenLeftHalf), ViewLayers.Popups);
            yield return OpenAndWait("screen");
            var popup = OpenView("popup");
            yield return WaitFor(() => popup.Phase == ViewPhase.Open, "the popup to open");
            yield return WaitForBackdrop();
            var before = ProbeRender.ReadSample(_rig);
            Assume.That(ProbeRender.Classify(before.Left), Is.EqualTo("green"), "control: the popup draws at the left point, " + Describe(before));
            Assume.That(IsBlue(before.Right), Is.True, "control: the backdrop draws at the right point, " + Describe(before));
            var backdrop = _world.Stage.BackdropOf(ViewLayers.Popups);
            var backdropCanvas = backdrop.GetComponent<Canvas>();
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;
            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);

            popup.Close();
            yield return WaitFor(() => popup.Phase == ViewPhase.Ended && !backdrop.IsFading && !backdropCanvas.enabled, "the popup to end and the backdrop to hide");
            var hidden = ProbeRender.ReadSample(_rig);
            hold.Remove();
            group.alpha = 1f;
            group.interactable = true;
            yield return WaitFor(() => !_world.Navigator.IsBusy, "the root to settle after the restore");
            var restored = ProbeRender.ReadSample(_rig);

            Assert.That(IsBlack(hidden), Is.True, "sample while hidden, " + Describe(hidden));
            Assert.That(ProbeRender.Classify(restored.Left), Is.EqualTo("red"), "former popup point after the restore, " + Describe(restored));
            Assert.That(ProbeRender.Classify(restored.Right), Is.EqualTo("red"), "former backdrop point after the restore, " + Describe(restored));
            Assert.That(_world.Core.ShieldCount, Is.Zero, "shield holders after the restore");
            LogAssert.NoUnexpectedReceived();
        }

        // Engine behaviour: a view CanvasGroup that ignores parent groups is not hidden by the root group.
        [UnityTest]
        public IEnumerator RootGroupAlphaZero_ViewGroupIgnoringParentGroups_StillDraws()
        {
            CreateDrawingWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithRedFillIgnoringParentGroups));
            yield return OpenAndWait("screen");
            yield return null;
            var before = ProbeRender.ReadSample(_rig);
            Assume.That(ProbeRender.Classify(before.Right), Is.EqualTo("red"), "control: the screen draws before the hide, " + Describe(before));
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;

            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);
            yield return null;
            var hidden = ProbeRender.ReadSample(_rig);

            Assert.That(hold.IsActive, Is.True, "control: the hold is up");
            Assert.That(ProbeRender.Classify(hidden.Right), Is.EqualTo("red"), "screen point with the root group at alpha 0, " + Describe(hidden));
            LogAssert.NoUnexpectedReceived();
        }

        // The load indicator sits under the root, so the root group hides it with the views.
        [UnityTest]
        public IEnumerator RootGroupAlphaZero_DrawsNoLoadIndicator()
        {
            CreateDrawingWorld(WithRedLoadIndicator);
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;
            var hold = navigator.HoldInput("cutscene", owner);
            _world.Stage.SetLoadIndicator(true);
            yield return null;
            var before = ProbeRender.ReadSample(_rig);
            Assume.That(ProbeRender.Classify(before.Right), Is.EqualTo("red"), "control: the load indicator draws before the hide, " + Describe(before));

            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            yield return null;
            var hidden = ProbeRender.ReadSample(_rig);

            Assert.That(hold.IsActive, Is.True, "control: the hold is up");
            Assert.That(ProbeRender.Classify(hidden.Right), Is.EqualTo("black"), "load indicator point with the root group at alpha 0, " + Describe(hidden));
            LogAssert.NoUnexpectedReceived();
        }

        // Engine behaviour the recipe avoids: a disabled root Canvas leaves its nested view canvases drawing.
        [UnityTest]
        public IEnumerator RootCanvasDisabled_NestedViewStillDraws()
        {
            CreateDrawingWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithRedFill));
            yield return OpenAndWait("screen");
            yield return null;
            var before = ProbeRender.ReadSample(_rig);
            Assume.That(ProbeRender.Classify(before.Right), Is.EqualTo("red"), "control: the screen draws, " + Describe(before));

            _world.RootCanvas.enabled = false;
            yield return null;
            var after = ProbeRender.ReadSample(_rig);

            Assert.That(ProbeRender.Classify(after.Right), Is.EqualTo("red"), "screen point with the root Canvas disabled, " + Describe(after));
            LogAssert.NoUnexpectedReceived();
        }

        // Input

        [UnityTest]
        public IEnumerator HoldInputWithRootHidden_FirstHitOnTheViewButtonPointIsTheShield()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithRedFillAndButton));
            _world.Row("popup", _world.Template<RecordingView>("Popup"), ViewLayers.Popups);
            var screen = OpenView("screen");
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");
            var popup = OpenView("popup");
            yield return WaitFor(() => popup.Phase == ViewPhase.Open && !_world.Navigator.IsBusy, "the popup to open and the root to settle");
            yield return SettleRender();
            var button = ButtonOf(screen);
            RequireRendered(button);
            var point = ProbeUi.ScreenPointOf((RectTransform)button.transform);
            var backdropObject = _world.Stage.BackdropOf(ViewLayers.Popups).gameObject;
            Assume.That(TopHit(point), Is.SameAs(backdropObject), "control: the backdrop is the topmost hit while the popup is open");
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;

            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);
            yield return null;
            var hits = ProbeUi.Raycast(point);

            Assert.That(hold.IsActive, Is.True, "control: the hold is up");
            Assert.That(hits, Is.Not.Empty, "nothing under the button point");
            Assert.That(hits[0].gameObject, Is.SameAs(_world.Stage.Shield.gameObject), "topmost hit: " + ProbeUi.Describe(hits));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RootGroupInteractableFalse_ViewButtonIsNotInteractable()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithRedFillAndButton));
            var screen = OpenView("screen");
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");
            var button = ButtonOf(screen);
            Assume.That(button.IsInteractable(), Is.True, "control: the button is interactable before the hide");
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;

            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);
            yield return null;
            var whileHidden = button.IsInteractable();
            hold.Remove();
            group.alpha = 1f;
            group.interactable = true;
            yield return null;
            var afterRestore = button.IsInteractable();

            Assert.That(whileHidden, Is.False, "IsInteractable while hidden");
            Assert.That(afterRestore, Is.True, "IsInteractable after the restore");
            LogAssert.NoUnexpectedReceived();
        }

        // The recipe as the documentation quotes it: the hide and the restore are each one contiguous run of lines.
        [UnityTest]
        public IEnumerator RestoreAfterHide_LeavesShieldAtZeroAndTheButtonHitAgain()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithRedFillAndButton));
            var screen = OpenView("screen");
            yield return WaitFor(() => screen.Phase == ViewPhase.Open && !_world.Navigator.IsBusy, "the screen to open and the root to settle");
            yield return SettleRender();
            var button = ButtonOf(screen);
            RequireRendered(button);
            var point = ProbeUi.ScreenPointOf((RectTransform)button.transform);
            Assume.That(TopHit(point), Is.SameAs(button.gameObject), "control: the button is the topmost hit before the hide");
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;

            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);

            yield return null;
            Assume.That(TopHit(point), Is.SameAs(_world.Stage.Shield.gameObject), "control: the shield is the topmost hit while hidden");

            hold.Remove();
            group.alpha = 1f;
            group.interactable = true;

            yield return SettleRender();

            Assert.That(_world.Core.ShieldCount, Is.Zero, "shield holders after the restore");
            Assert.That(_world.Navigator.IsBusy, Is.False, "navigator busy after the restore");
            Assert.That(TopHit(point), Is.SameAs(button.gameObject), "topmost hit after the restore: " + ProbeUi.Describe(ProbeUi.Raycast(point)));
            LogAssert.NoUnexpectedReceived();
        }

        // The group is kept and reused; each hide takes a new hold.
        [UnityTest]
        public IEnumerator HideRestoreTwice_SecondHideHidesAgainAndLeavesShieldAtZero()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithRedFillAndButton));
            var screen = OpenView("screen");
            yield return WaitFor(() => screen.Phase == ViewPhase.Open && !_world.Navigator.IsBusy, "the screen to open and the root to settle");
            yield return SettleRender();
            var button = ButtonOf(screen);
            RequireRendered(button);
            var point = ProbeUi.ScreenPointOf((RectTransform)button.transform);
            Assume.That(TopHit(point), Is.SameAs(button.gameObject), "control: the button is the topmost hit before the hide");
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;
            var shieldObject = _world.Stage.Shield.gameObject;

            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var firstHold = navigator.HoldInput("cutscene", owner);
            yield return null;
            Assume.That(TopHit(point), Is.SameAs(shieldObject), "control: the shield is the topmost hit during the first hide");
            firstHold.Remove();
            group.alpha = 1f;
            group.interactable = true;
            yield return SettleRender();
            Assume.That(TopHit(point), Is.SameAs(button.gameObject), "control: the button is the topmost hit after the first restore");

            group.alpha = 0f;
            group.interactable = false;
            var secondHold = navigator.HoldInput("cutscene", owner);
            yield return null;
            var secondHiddenHits = ProbeUi.Raycast(point);
            secondHold.Remove();
            group.alpha = 1f;
            group.interactable = true;
            yield return SettleRender();

            Assert.That(secondHiddenHits, Is.Not.Empty, "nothing under the button point during the second hide");
            Assert.That(secondHiddenHits[0].gameObject, Is.SameAs(shieldObject), "topmost hit during the second hide: " + ProbeUi.Describe(secondHiddenHits));
            Assert.That(_world.Core.ShieldCount, Is.Zero, "shield holders after the second restore");
            Assert.That(_world.Navigator.IsBusy, Is.False, "navigator busy after the second restore");
            Assert.That(TopHit(point), Is.SameAs(button.gameObject), "topmost hit after the second restore: " + ProbeUi.Describe(ProbeUi.Raycast(point)));
            LogAssert.NoUnexpectedReceived();
        }

        // Back

        [UnityTest]
        public IEnumerator BackWhileHidden_IsSwallowedByTheHold()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Row("popup", _world.Template<RecordingView>("Popup"), ViewLayers.Popups);
            var screen = OpenView("screen");
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");
            var popup = OpenView("popup");
            yield return WaitFor(() => popup.Phase == ViewPhase.Open && !_world.Navigator.IsBusy, "the popup to open and the root to settle");
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;
            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);

            var handled = navigator.Back();
            yield return null;
            yield return null;

            Assert.That(hold.IsActive, Is.True, "control: the hold is up");
            Assert.That(handled, Is.True, "Back() result");
            Assert.That(popup.Phase, Is.EqualTo(ViewPhase.Open), "popup phase after Back");
            Assert.That(screen.Phase, Is.EqualTo(ViewPhase.Open), "screen phase after Back");

            // Control: without the hold the same Back closes the popup, so the hold is what kept it open.
            hold.Remove();
            group.alpha = 1f;
            group.interactable = true;
            yield return WaitFor(() => !_world.Navigator.IsBusy, "the root to settle after the restore");
            var handledAfterRestore = navigator.Back();
            yield return WaitFor(() => popup.Phase == ViewPhase.Ended, "the popup to close on Back after the restore");

            Assert.That(handledAfterRestore, Is.True, "Back() result after the restore");
            LogAssert.NoUnexpectedReceived();
        }

        // Back withdraws a waiting or loading request before it checks the shield, so the hold does not protect it.
        [UnityTest]
        public IEnumerator BackWhileHiddenWithPendingOpen_WithdrawsThatRequest()
        {
            ScriptedProvider provider = null;
            _world = new StageWorld(root => provider = root.gameObject.AddComponent<ScriptedProvider>());
            var remoteTemplate = _world.Template<RecordingView>("Remote");
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Row("remote", null, ViewLayers.Popups, assetKey: RemoteAssetKey);
            var screen = OpenView("screen");
            yield return WaitFor(() => screen.Phase == ViewPhase.Open && !_world.Navigator.IsBusy, "the screen to open and the root to settle");
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;
            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);
            var remote = OpenView("remote");
            yield return WaitFor(() => provider.AcquireCount > 0, "the provider to be asked for the popup");
            Assume.That(remote.Phase, Is.EqualTo(ViewPhase.Loading), "control: the popup request waits on the provider");
            var closed = remote.WaitForCloseAsync(CancellationToken.None);
            Assume.That(_world.Core.ShieldCount, Is.GreaterThan(0), "control: the shield is up");

            var handled = navigator.Back();
            yield return WaitFor(() => !closed.IsPending(), "the popup request to end");
            var outcome = closed.ResultNow();

            // The late result is released, so nothing is left loading when the world goes.
            provider.LastLoad.CompleteWithInstanceOf(remoteTemplate);
            yield return WaitFor(() => provider.ReleaseCount > 0, "the late result to be released");

            Assert.That(hold.IsActive, Is.True, "control: the hold is up");
            Assert.That(handled, Is.True, "Back() result");
            Assert.That(outcome.WasShown, Is.False, "WasShown");
            Assert.That(outcome.Reason, Is.EqualTo(CloseReason.NotShown), "close reason");
            Assert.That(outcome.Rejected, Is.EqualTo(RejectReason.Cancelled), "reject reason");
            Assert.That(screen.Phase, Is.EqualTo(ViewPhase.Open), "screen phase after Back");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BackWhileHiddenWithWaitingOpen_WithdrawsTheNewestAndKeepsTheLoadingOne()
        {
            ScriptedProvider provider = null;
            _world = new StageWorld(root => provider = root.gameObject.AddComponent<ScriptedProvider>());
            var remoteTemplate = _world.Template<RecordingView>("Remote");
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Row("remote", null, ViewLayers.Popups, assetKey: RemoteAssetKey);
            _world.Row("remote-next", null, ViewLayers.Popups, assetKey: NextRemoteAssetKey);
            var screen = OpenView("screen");
            yield return WaitFor(() => screen.Phase == ViewPhase.Open && !_world.Navigator.IsBusy, "the screen to open and the root to settle");
            var rootCanvas = _world.RootCanvas;
            var navigator = _world.Navigator;
            var owner = _world.Owner;
            var group = rootCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            var hold = navigator.HoldInput("cutscene", owner);
            var loading = OpenView("remote");
            yield return WaitFor(() => provider.AcquireCount > 0, "the provider to be asked for the first popup");
            var waiting = OpenView("remote-next");
            yield return null;
            Assume.That(loading.Phase, Is.EqualTo(ViewPhase.Loading), "control: the first popup request waits on the provider");
            Assume.That(waiting.Phase, Is.EqualTo(ViewPhase.Waiting), "control: the second popup request waits behind the first");
            Assume.That(_world.Core.ShieldCount, Is.GreaterThan(0), "control: the shield is up");
            var closed = waiting.WaitForCloseAsync(CancellationToken.None);

            var handled = navigator.Back();
            yield return WaitFor(() => !closed.IsPending(), "the waiting popup request to end");
            var outcome = closed.ResultNow();
            var loadingPhaseAfterBack = loading.Phase;

            // The first load is finished, so nothing is left loading when the world goes.
            provider.LastLoad.CompleteWithInstanceOf(remoteTemplate);
            yield return WaitFor(() => loading.Phase == ViewPhase.Open, "the loading popup to open");

            Assert.That(hold.IsActive, Is.True, "control: the hold is up");
            Assert.That(handled, Is.True, "Back() result");
            Assert.That(outcome.WasShown, Is.False, "WasShown of the waiting request");
            Assert.That(outcome.Reason, Is.EqualTo(CloseReason.NotShown), "close reason of the waiting request");
            Assert.That(outcome.Rejected, Is.EqualTo(RejectReason.Cancelled), "reject reason of the waiting request");
            Assert.That(loadingPhaseAfterBack, Is.EqualTo(ViewPhase.Loading), "phase of the loading request after Back");
            Assert.That(provider.AcquireCount, Is.EqualTo(1), "loads the provider was asked for");
            LogAssert.NoUnexpectedReceived();
        }

        // Helpers

        // A camera-mode root drawing into a small RenderTexture over black, with an opaque blue backdrop so it is told apart from the views.
        private void CreateDrawingWorld(Action<ViewRoot> configure = null)
        {
            _world = new StageWorld(
                root =>
                {
                    StageWorld.SetField(root, "_backdropColor", Color.blue);
                    if (configure != null)
                        configure(root);
                },
                cameraMode: true);
            _target = new RenderTexture(ProbeRender.TargetSize, ProbeRender.TargetSize, 24, RenderTextureFormat.ARGB32);
            _readback = new Texture2D(ProbeRender.TargetSize, ProbeRender.TargetSize, TextureFormat.RGBA32, false);

            var camera = _world.Camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.targetTexture = _target;

            _rig = new ProbeRender.RenderRig { Camera = camera, Target = _target, Readback = _readback };
        }

        private static void WithRedFill(GameObject view)
        {
            ProbeUi.NewImage("Red", view.transform, Vector2.zero, Vector2.one).color = Color.red;
        }

        private static void WithGreenLeftHalf(GameObject view)
        {
            ProbeUi.NewImage("Green", view.transform, Vector2.zero, new Vector2(0.5f, 1f)).color = Color.green;
        }

        private static void WithRedFillIgnoringParentGroups(GameObject view)
        {
            view.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            WithRedFill(view);
        }

        // A full-area red indicator under the root, set before initialisation as a project would in the Inspector.
        private static void WithRedLoadIndicator(ViewRoot root)
        {
            var indicator = ProbeUi.NewObject("LoadIndicator", root.transform, active: false);
            ProbeUi.Stretch((RectTransform)indicator.transform);
            indicator.AddComponent<Image>().color = Color.red;
            StageWorld.SetField(root, "_loadIndicator", indicator);
        }

        private static void WithRedFillAndButton(GameObject view)
        {
            WithRedFill(view);
            ProbeUi.NewButton(ButtonName, view.transform, new Vector2(0.4f, 0.4f), new Vector2(0.6f, 0.6f));
        }

        private ViewHandle OpenView(string viewId)
        {
            return _world.Navigator.Open(ViewKey.Create(viewId), _world.Owner);
        }

        private IEnumerator OpenAndWait(string viewId)
        {
            var handle = OpenView(viewId);
            return WaitFor(() => handle.Phase == ViewPhase.Open, viewId + " to open");
        }

        // The backdrop fades in; drawing samples wait until it is fully shown.
        private IEnumerator WaitForBackdrop()
        {
            var backdrop = _world.Stage.BackdropOf(ViewLayers.Popups);
            return WaitFor(() => !backdrop.IsFading, "the backdrop fade to end");
        }

        private static Button ButtonOf(ViewHandle handle)
        {
            var instance = StageWorld.InstanceOf(handle);
            Assert.That(instance, Is.Not.Null, "instance of " + handle.ViewId);
            var child = instance.transform.Find(ButtonName);
            Assert.That(child, Is.Not.Null, ButtonName + " in " + handle.ViewId);
            return child.GetComponent<Button>();
        }

        // Batch runs may give no usable screen or render nothing; then raycast checks cannot run.
        private static void RequireRendered(Button button)
        {
            Assume.That(ProbeUi.ScreenIsUsable(), Is.True, "screen is " + Screen.width + "x" + Screen.height);
            Assume.That(button.targetGraphic.canvasRenderer.absoluteDepth, Is.GreaterThanOrEqualTo(0), "the button image has not rendered");
        }

        private static GameObject TopHit(Vector2 point)
        {
            var hits = ProbeUi.Raycast(point);
            return hits.Count == 0 ? null : hits[0].gameObject;
        }

        // Layout and a rendered frame, so graphics have a depth the raycaster accepts.
        private static IEnumerator SettleRender()
        {
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
        }

        private static bool IsBlack(ProbeRender.Sample sample)
        {
            return ProbeRender.Classify(sample.Left) == "black" && ProbeRender.Classify(sample.Right) == "black";
        }

        private static bool IsBlue(Color color)
        {
            return color.b > 0.6f && color.r < 0.3f && color.g < 0.3f;
        }

        private static string Describe(ProbeRender.Sample sample)
        {
            return "left " + ProbeRender.Describe(sample.Left) + ", right " + ProbeRender.Describe(sample.Right);
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
    }
}
