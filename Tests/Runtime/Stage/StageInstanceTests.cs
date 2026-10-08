using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests
{
    // Instances on a real stage: staging and injection, placement, release, relink on reopen and instances lost outside the package.
    [TestFixture]
    public sealed class StageInstanceTests
    {
        private const int MaxFrames = 300;
        private const float MinWaitSeconds = 1f;

        // A loss is forwarded on the next frame; this bounds "within a couple of frames".
        private const int LossFrames = 3;

        private StageWorld _world;
        private LogRecorder _log;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // A test that marks the application as quitting must not leak the flag to later tests.
            QuitSignal.Reset();

            if (_world != null)
            {
                _world.Dispose();
                _world = null;

                // Destroy is deferred; the next test must not see this world's objects.
                yield return null;
            }

            // Last, so lines the teardown writes are still let through and the log setting is restored for the next test.
            if (_log != null)
            {
                _log.Dispose();
                _log = null;
            }
        }

        [UnityTest]
        public IEnumerator Inject_NewInstance_SeesInactiveStagedObjectBeforeAwake()
        {
            var injectCount = 0;
            var activeAtInject = true;
            var awakeCountAtInject = -1;
            Transform parentAtInject = null;
            var setup = new ViewRootSetup
            {
                Inject = instance =>
                {
                    injectCount++;
                    activeAtInject = instance.activeInHierarchy;
                    awakeCountAtInject = instance.GetComponent<RecordingView>().AwakeCount;
                    parentAtInject = instance.transform.parent;
                }
            };
            _world = new StageWorld(setup: setup);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");

            Assert.That(injectCount, Is.EqualTo(1), "inject calls");
            Assert.That(activeAtInject, Is.False, "active in hierarchy at inject");
            Assert.That(awakeCountAtInject, Is.EqualTo(0), "Awake calls before inject");
            Assert.That(parentAtInject, Is.SameAs(_world.Stage.Staging), "parent at inject");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnOpen_PlacedInstance_RunsOnActiveObjectAfterAwakeWithTheCanvasAsAuthored()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");

            var view = StageWorld.InstanceOf(handle).GetComponent<RecordingView>();
            Assert.That(view.OpenCount, Is.EqualTo(1), "OnOpen calls");
            Assert.That(view.ActiveDuringOpen, Is.True, "active during OnOpen");
            Assert.That(view.CanvasEnabledDuringOpen, Is.True, "canvas as authored during OnOpen: nothing is switched off before the first projection, which runs in the same step");
            Assert.That(view.AwakeCountAtOpen, Is.EqualTo(1), "Awake calls before OnOpen");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Release_ReleaseOnCloseViewClosed_DestroysInstance()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");
            var instance = StageWorld.InstanceOf(handle);
            handle.Close();
            yield return WaitFor(() => handle.Phase == ViewPhase.Ended, "the screen to close");

            yield return WaitFor(() => instance == null, "the released instance to be destroyed");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Relink_ReopenInPlaceWithArguments_LinksViewHostToNewRequest()
        {
            _world = new StageWorld();
            _world.Row("answer", _world.Template<AnswerView>("Answer"));
            var key = ViewKey.Create<int, bool>("answer");

            var first = _world.Navigator.Open(key, 1, _world.Owner);
            yield return WaitFor(() => first.Phase == ViewPhase.Open, "the first request to open");
            var instance = StageWorld.InstanceOf(first);
            var firstClosed = first.WaitForCloseAsync(CancellationToken.None);
            var second = _world.Navigator.Open(key, 2, _world.Owner);
            yield return WaitFor(() => second.Phase == ViewPhase.Open, "the reopened request to open");

            var host = instance.GetComponent<ViewHost>();
            Assert.That(StageWorld.InstanceOf(second), Is.SameAs(instance), "same instance");
            Assert.That(host.Handle.RequestId, Is.EqualTo(second.RequestId), "ViewHost.Handle request id");
            Assert.That(host.IsOpen, Is.True, "ViewHost.IsOpen");
            Assert.That(instance.GetComponent<AnswerView>().LastArgs, Is.EqualTo(2), "arguments of the last OnOpen");
            Assert.That(firstClosed.ResultNow().Reason, Is.EqualTo(CloseReason.Navigated), "first request close reason");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnHostLost_OpenViewDestroyedOutside_EndsRequestAsDestroyed()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");
            var closed = handle.WaitForCloseAsync(CancellationToken.None);
            LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR107"));
            Object.Destroy(StageWorld.InstanceOf(handle));
            yield return WithinFrames(() => !closed.IsPending(), "the request to end", LossFrames);

            Assert.That(closed.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnHostLost_OpenViewDeactivatedOutside_EndsRequestAsDestroyed()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");
            var closed = handle.WaitForCloseAsync(CancellationToken.None);
            LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR107"));
            StageWorld.InstanceOf(handle).SetActive(false);
            yield return WithinFrames(() => !closed.IsPending(), "the request to end", LossFrames);

            Assert.That(closed.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnHostLost_OpenViewDeactivatedWhileTheApplicationIsQuitting_LeavesTheRequestOpen()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");
            QuitSignal.MarkQuittingForTests();
            StageWorld.InstanceOf(handle).SetActive(false);

            // Proving that nothing happens needs a fixed window; this one outlasts the frame a loss report is forwarded on.
            for (var frame = 0; frame < LossFrames; frame++)
                yield return null;

            Assert.That(handle.IsAlive, Is.True, "the request is still alive");
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open), "request phase");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnHostLost_OpenViewDestroyedWhileTheApplicationIsQuitting_LeavesTheRequestOpen()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");
            QuitSignal.MarkQuittingForTests();
            Object.Destroy(StageWorld.InstanceOf(handle));

            // Proving that nothing happens needs a fixed window; this one outlasts the frame a loss report is forwarded on.
            for (var frame = 0; frame < LossFrames; frame++)
                yield return null;

            Assert.That(handle.IsAlive, Is.True, "the request is still alive");
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open), "request phase");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Snap_TransitionSnapThrows_RoutesTransitionErrorWithViewAndRequestId()
        {
            var errors = new ErrorHandlerSpy();
            _world = new StageWorld(setup: new ViewRootSetup { ErrorHandler = errors.Handle });
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithThrowingSnapTransition));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");

            Assert.That(errors.Received.Count, Is.EqualTo(1), "routed errors: " + errors.Describe());
            var received = errors.Received[0];
            Assert.That(received.Exception, Is.InstanceOf<InvalidOperationException>(), "exception type");
            Assert.That(received.Exception.Message, Is.EqualTo(ThrowingSnapTransition.FailureMessage), "exception message");
            Assert.That(received.Context.Source, Is.EqualTo(ViewErrorSource.Transition), "error source");
            Assert.That(received.Context.ViewId, Is.EqualTo("screen"), "view id");
            Assert.That(received.Context.RequestId, Is.EqualTo(handle.RequestId), "request id");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Snap_TransitionSnapThrowsWhenOwnerEndsOpenView_RoutesTransitionErrorWithViewAndRequestId()
        {
            var errors = new ErrorHandlerSpy();
            _world = new StageWorld(setup: new ViewRootSetup { ErrorHandler = errors.Handle });
            _world.Row("screen", _world.Template<RecordingView>("Screen", WithThrowingSnapTransition));

            using (var owner = new CancellationTokenSource())
            {
                var handle = _world.Navigator.Open(ViewKey.Create("screen"), owner.Token);
                yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");
                var requestId = handle.RequestId;
                var before = errors.Received.Count;

                owner.Cancel();
                yield return WaitFor(() => handle.Phase == ViewPhase.Ended, "the owner to end the request");

                Assert.That(errors.Received.Count - before, Is.EqualTo(1), "errors routed when the owner ends the view: " + errors.Describe());
                var received = errors.Received[before];
                Assert.That(received.Exception, Is.InstanceOf<InvalidOperationException>(), "exception type");
                Assert.That(received.Exception.Message, Is.EqualTo(ThrowingSnapTransition.FailureMessage), "exception message");
                Assert.That(received.Context.Source, Is.EqualTo(ViewErrorSource.Transition), "error source");
                Assert.That(received.Context.ViewId, Is.EqualTo("screen"), "view id");
                Assert.That(received.Context.RequestId, Is.EqualTo(requestId), "request id");
                LogAssert.NoUnexpectedReceived();
            }
        }

        [UnityTest]
        public IEnumerator OnHostLost_OpenViewDestroyedOutside_NextOpenOfSameIdShowsNewInstance()
        {
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var first = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => first.Phase == ViewPhase.Open, "the first request to open");
            var lost = StageWorld.InstanceOf(first);
            LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR107"));
            Object.Destroy(lost);
            yield return WithinFrames(() => first.Phase == ViewPhase.Ended, "the lost request to end", LossFrames);
            var second = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => second.Phase == ViewPhase.Open, "the next request to open");

            var instance = StageWorld.InstanceOf(second);
            Assert.That(instance != null, Is.True, "new instance alive");
            Assert.That(ReferenceEquals(instance, lost), Is.False, "a new instance, not the lost one");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnHostLost_KeptViewDestroyedWhileOpen_NextOpenCreatesNewInstance()
        {
            _world = new StageWorld();
            _world.Row("kept", _world.Template<RecordingView>("Kept"), retention: ViewRetention.KeepInstance);

            var first = _world.Navigator.Open(ViewKey.Create("kept"), _world.Owner);
            yield return WaitFor(() => first.Phase == ViewPhase.Open, "the first request to open");
            var lost = StageWorld.InstanceOf(first);
            LogAssert.Expect(LogType.Warning, new Regex("UIDIRECTOR107"));
            Object.Destroy(lost);
            yield return WithinFrames(() => first.Phase == ViewPhase.Ended, "the lost request to end", LossFrames);
            var second = _world.Navigator.Open(ViewKey.Create("kept"), _world.Owner);
            yield return WaitFor(() => second.Phase == ViewPhase.Open, "the next request to open");

            var instance = StageWorld.InstanceOf(second);
            Assert.That(instance != null, Is.True, "new instance alive");
            Assert.That(ReferenceEquals(instance, lost), Is.False, "a new instance, not the destroyed one");
            Assert.That(instance.GetComponent<RecordingView>().OpenCount, Is.EqualTo(1), "OnOpen calls on the new instance");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnHostLost_KeptIdleInstanceDestroyedOutside_NextOpenCreatesNewInstance()
        {
            _world = new StageWorld();
            _world.Row("kept", _world.Template<RecordingView>("Kept"), retention: ViewRetention.KeepInstance);

            var first = _world.Navigator.Open(ViewKey.Create("kept"), _world.Owner);
            yield return WaitFor(() => first.Phase == ViewPhase.Open, "the first request to open");
            var kept = StageWorld.InstanceOf(first);
            first.Close();
            yield return WaitFor(() => first.Phase == ViewPhase.Ended, "the first request to close");
            Assert.That(kept.transform.parent, Is.SameAs(_world.Stage.Staging), "the kept instance waits under staging");
            Object.Destroy(kept);

            // The loss is forwarded on the next frame; a kept instance is dropped without a diagnostic.
            yield return null;
            yield return null;
            var second = _world.Navigator.Open(ViewKey.Create("kept"), _world.Owner);
            yield return WaitFor(() => second.Phase == ViewPhase.Open, "the next request to open");

            var instance = StageWorld.InstanceOf(second);
            Assert.That(instance != null, Is.True, "new instance alive");
            Assert.That(ReferenceEquals(instance, kept), Is.False, "a new instance, not the destroyed one");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnHostLost_DeactivatedAndActivatedAgainInTheSameFrame_EndsNothingAndLogsNothing()
        {
            var log = StartLogRecorder(letFailingMessagesThrough: false);
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => handle.Phase == ViewPhase.Open, "the screen to open");
            var instance = StageWorld.InstanceOf(handle);
            instance.SetActive(false);
            instance.SetActive(true);

            // Proving that nothing happens needs a fixed window; this one outlasts the frame a loss report is forwarded on.
            for (var frame = 0; frame < LossFrames; frame++)
                yield return null;

            Assert.That(handle.IsAlive, Is.True, "the request is still alive");
            Assert.That(handle.Phase, Is.EqualTo(ViewPhase.Open), "request phase");
            Assert.That(instance.activeSelf, Is.True, "instance active");
            Assert.That(log.Count(DiagnosticIds.ViewLostOutsidePackage), Is.EqualTo(0), "UIDIRECTOR107 lines:\n" + log.Describe());
            LogAssert.NoUnexpectedReceived();
        }

        // The routed error's source is not asserted; only the outcome and the diagnostics are part of the contract.
        [UnityTest]
        public IEnumerator Inject_DestroysTheStagedInstance_EndsNotShownLoadFailedWith104AndNever107()
        {
            var log = StartLogRecorder(letFailingMessagesThrough: true);
            var errors = new ErrorHandlerSpy();
            var setup = new ViewRootSetup
            {
                Inject = instance => Object.DestroyImmediate(instance),
                ErrorHandler = errors.Handle
            };
            _world = new StageWorld(setup: setup);
            _world.Row("screen", _world.Template<RecordingView>("Screen"));

            var handle = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            var closed = handle.WaitForCloseAsync(CancellationToken.None);
            yield return WaitFor(() => !closed.IsPending(), "the request to end");
            for (var frame = 0; frame < LossFrames; frame++)
                yield return null;

            var result = closed.ResultNow();
            Assert.That(result.Reason, Is.EqualTo(CloseReason.NotShown), "close reason");
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.LoadFailed), "reject reason");
            Assert.That(log.Count(LogType.Error, DiagnosticIds.LoadFailed), Is.EqualTo(1), "UIDIRECTOR104 error lines:\n" + log.Describe());
            Assert.That(log.Count(DiagnosticIds.ViewLostOutsidePackage), Is.EqualTo(0), "UIDIRECTOR107 lines:\n" + log.Describe());

            // An empty fragment matches every line of the type, so no other error or exception slips through.
            Assert.That(log.Count(LogType.Error, ""), Is.EqualTo(1), "error lines, only the UIDIRECTOR104 one expected:\n" + log.Describe());
            Assert.That(log.Count(LogType.Exception, ""), Is.EqualTo(0), "exception lines:\n" + log.Describe());
        }

        [UnityTest]
        public IEnumerator OnHostLost_LayerObjectDeactivated_EndsEveryViewUnderItWithOne107PerView()
        {
            var log = StartLogRecorder(letFailingMessagesThrough: false);
            _world = new StageWorld();
            _world.Row("lower", _world.Template<RecordingView>("Lower"));
            _world.Row("upper", _world.Template<RecordingView>("Upper"));

            var lower = _world.Navigator.Open(ViewKey.Create("lower"), _world.Owner);
            yield return WaitFor(() => lower.Phase == ViewPhase.Open, "the lower screen to open");
            var upper = _world.Navigator.Open(ViewKey.Create("upper"), _world.Owner);
            yield return WaitFor(() => upper.Phase == ViewPhase.Open, "the upper screen to open");
            var lowerClosed = lower.WaitForCloseAsync(CancellationToken.None);
            var upperClosed = upper.WaitForCloseAsync(CancellationToken.None);
            _world.Stage.LayerRect(ViewLayers.Screens).gameObject.SetActive(false);

            for (var frame = 0; frame < LossFrames; frame++)
                yield return null;

            Assert.That(lowerClosed.IsPending(), Is.False, "lower request ended");
            Assert.That(upperClosed.IsPending(), Is.False, "upper request ended");
            Assert.That(lowerClosed.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed), "lower close reason");
            Assert.That(upperClosed.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed), "upper close reason");
            Assert.That(log.Count(LogType.Warning, DiagnosticIds.ViewLostOutsidePackage), Is.EqualTo(2), "UIDIRECTOR107 warnings:\n" + log.Describe());
        }

        [UnityTest]
        public IEnumerator OnHostLost_RootObjectDeactivated_EndsEveryViewUnderIt()
        {
            var log = StartLogRecorder(letFailingMessagesThrough: false);
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Row("popup", _world.Template<RecordingView>("Popup"), ViewLayers.Popups);

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");
            var popup = _world.Navigator.Open(ViewKey.Create("popup"), _world.Owner);
            yield return WaitFor(() => popup.Phase == ViewPhase.Open, "the popup to open");
            var screenClosed = screen.WaitForCloseAsync(CancellationToken.None);
            var popupClosed = popup.WaitForCloseAsync(CancellationToken.None);
            _world.Root.gameObject.SetActive(false);

            for (var frame = 0; frame < LossFrames; frame++)
                yield return null;

            Assert.That(screenClosed.IsPending(), Is.False, "screen request ended");
            Assert.That(popupClosed.IsPending(), Is.False, "popup request ended");
            Assert.That(screenClosed.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed), "screen close reason");
            Assert.That(popupClosed.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed), "popup close reason");
            Assert.That(log.Count(LogType.Warning, DiagnosticIds.ViewLostOutsidePackage), Is.EqualTo(2), "UIDIRECTOR107 warnings:\n" + log.Describe());
        }

        // The close reason is not asserted; only that every request ends and no loss is reported.
        [UnityTest]
        public IEnumerator OnHostLost_RootObjectDestroyed_EndsOpenViewsWithoutLoggingUIDIRECTOR107()
        {
            var log = StartLogRecorder(letFailingMessagesThrough: false);
            _world = new StageWorld();
            _world.Row("screen", _world.Template<RecordingView>("Screen"));
            _world.Row("popup", _world.Template<RecordingView>("Popup"), ViewLayers.Popups);

            var screen = _world.Navigator.Open(ViewKey.Create("screen"), _world.Owner);
            yield return WaitFor(() => screen.Phase == ViewPhase.Open, "the screen to open");
            var popup = _world.Navigator.Open(ViewKey.Create("popup"), _world.Owner);
            yield return WaitFor(() => popup.Phase == ViewPhase.Open, "the popup to open");
            var screenClosed = screen.WaitForCloseAsync(CancellationToken.None);
            var popupClosed = popup.WaitForCloseAsync(CancellationToken.None);
            Object.Destroy(_world.Root.gameObject);

            for (var frame = 0; frame < LossFrames; frame++)
                yield return null;

            Assert.That(screenClosed.IsPending(), Is.False, "screen request ended");
            Assert.That(popupClosed.IsPending(), Is.False, "popup request ended");
            Assert.That(log.Count(DiagnosticIds.ViewLostOutsidePackage), Is.EqualTo(0), "UIDIRECTOR107 lines:\n" + log.Describe());
        }

        [UnityTest]
        public IEnumerator OnHostLost_KeepInstanceViewDeactivatedByTheProject_IsNotKeptAndTheNextOpenMakesANewInstance()
        {
            var log = StartLogRecorder(letFailingMessagesThrough: false);
            _world = new StageWorld();
            _world.Row("kept", _world.Template<RecordingView>("Kept"), retention: ViewRetention.KeepInstance);

            var first = _world.Navigator.Open(ViewKey.Create("kept"), _world.Owner);
            yield return WaitFor(() => first.Phase == ViewPhase.Open, "the first request to open");
            var lost = StageWorld.InstanceOf(first);
            var firstClosed = first.WaitForCloseAsync(CancellationToken.None);
            lost.SetActive(false);
            for (var frame = 0; frame < LossFrames; frame++)
                yield return null;
            Assume.That(firstClosed.IsPending(), Is.False, "control: the deactivated request ended");
            var second = _world.Navigator.Open(ViewKey.Create("kept"), _world.Owner);
            yield return WaitFor(() => second.Phase == ViewPhase.Open, "the next request to open");

            var instance = StageWorld.InstanceOf(second);
            Assert.That(firstClosed.ResultNow().Reason, Is.EqualTo(CloseReason.Destroyed), "first close reason");
            Assert.That(instance != null, Is.True, "new instance alive");
            Assert.That(ReferenceEquals(instance, lost), Is.False, "a new instance, not the deactivated one");
            Assert.That((bool)lost, Is.False, "the deactivated instance was released, not kept");
            Assert.That(log.Count(LogType.Warning, DiagnosticIds.ViewLostOutsidePackage), Is.EqualTo(1), "UIDIRECTOR107 warnings:\n" + log.Describe());
        }

        private LogRecorder StartLogRecorder(bool letFailingMessagesThrough)
        {
            _log = new LogRecorder(letFailingMessagesThrough);
            return _log;
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

        // Strictly frame-bounded, for steps the package ties to a frame boundary.
        private static IEnumerator WithinFrames(Func<bool> condition, string what, int frames)
        {
            for (var frame = 0; !condition(); frame++)
            {
                if (frame >= frames)
                    Assert.Fail("Not done within " + frames + " frames: " + what + ".");

                yield return null;
            }
        }

        // Template build step: a transition whose Snap throws, referenced by the ViewHost.
        private static void WithThrowingSnapTransition(GameObject template)
        {
            var transition = template.AddComponent<ThrowingSnapTransition>();
            StageWorld.SetField(template.GetComponent<ViewHost>(), "_transition", transition);
        }

        // Plays instantly; Snap always throws.
        private sealed class ThrowingSnapTransition : ViewTransition
        {
            public const string FailureMessage = "Snap failed on purpose.";

            public override UniTask PlayAsync(bool show, CancellationToken ct)
            {
                return UniTask.CompletedTask;
            }

            public override void Snap(bool shown)
            {
                throw new InvalidOperationException(FailureMessage);
            }
        }

        // Records every exception the root routes to the game's handler, with its context.
        private sealed class ErrorHandlerSpy
        {
            public readonly List<RoutedError> Received = new List<RoutedError>();

            public void Handle(Exception exception, in ViewErrorContext context)
            {
                Received.Add(new RoutedError(exception, context));
            }

            public string Describe()
            {
                var parts = new List<string>(Received.Count);
                foreach (var error in Received)
                    parts.Add(error.Context.Source + " '" + error.Context.ViewId + "' #" + error.Context.RequestId + " " + error.Exception.GetType().Name + ": " + error.Exception.Message);
                return Received.Count + " [" + string.Join(" | ", parts) + "]";
            }
        }

        private readonly struct RoutedError
        {
            public RoutedError(Exception exception, ViewErrorContext context)
            {
                Exception = exception;
                Context = context;
            }

            public Exception Exception { get; }
            public ViewErrorContext Context { get; }
        }
    }
}
