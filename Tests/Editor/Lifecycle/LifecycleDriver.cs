using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // An observer that hands every event to a delegate, for ordering checks against other state.
    internal sealed class LifecycleActionObserver : IViewObserver
    {
        private readonly Action<ViewEvent> _action;

        public LifecycleActionObserver(Action<ViewEvent> action)
        {
            _action = action;
        }

        public void OnViewEvent(in ViewEvent viewEvent)
        {
            _action(viewEvent);
        }
    }

    // Helpers shared by the lifecycle tests: rows, driving one request to a phase, call-order lookups.
    internal static class LifecycleDriver
    {
        public const string Target = "target";
        public const string Blocker = "blocker";

        public static ViewKey<NoArgs, bool> KeyOf(string id)
        {
            return ViewKey.Create<NoArgs, bool>(id);
        }

        // A plain layer: no history, no backdrop, no back, but it shields input.
        public static ViewLayer PlainLayer(string id = "plain")
        {
            return new ViewLayer(id, 0, ViewLayerRules.ShieldsInput);
        }

        // Rows whose views take no arguments and return a bool.
        public static void AddRows(NavigatorHarness h, ViewLayer layer, params string[] ids)
        {
            for (var i = 0; i < ids.Length; i++)
                h.Row(ids[i], layer).Map<NoArgs, bool>(ids[i]);
        }

        public static UniTask<ViewResult<bool>> Show(
            NavigatorHarness h,
            string id,
            CancellationToken owner,
            OpenOptions options = default)
        {
            return h.Navigator.ShowAsync(KeyOf(id), owner, options);
        }

        // The handle of the live request for the id; fails the test when there is none.
        public static ViewHandle HandleOf(NavigatorHarness h, string id)
        {
            ViewHandle handle;
            var found = h.Navigator.TryGetOpen(id, out handle);
            Assert.That(found, Is.True, "No live request for '" + id + "'.\n" + h.Describe());
            return handle;
        }

        public static FakeView<NoArgs, bool> ViewOf(NavigatorHarness h, string id)
        {
            return h.ViewOf<FakeView<NoArgs, bool>>(id);
        }

        // Drives one request to the phase and returns its show task. Waiting opens the blocker first (same layer).
        // Closing latches through the given action (default: Complete(true)) while the hide transition stays pending.
        public static UniTask<ViewResult<bool>> Reach(
            NavigatorHarness h,
            string id,
            ViewPhase phase,
            CancellationToken owner,
            string blocker = null,
            Action<FakeView<NoArgs, bool>> latch = null)
        {
            UniTask<ViewResult<bool>> task;

            switch (phase)
            {
                case ViewPhase.Waiting:
                    Assert.That(blocker, Is.Not.Null, "Reaching Waiting needs a blocker id.");
                    h.Stage.AcquireMode = StageMode.Manual;
                    h.Navigator.Open(KeyOf(blocker), h.Owner);
                    task = Show(h, id, owner);
                    break;

                case ViewPhase.Loading:
                    h.Stage.AcquireMode = StageMode.Manual;
                    task = Show(h, id, owner);
                    break;

                case ViewPhase.Opening:
                    h.Stage.PlayMode = StageMode.Manual;
                    task = Show(h, id, owner);
                    break;

                case ViewPhase.Open:
                    task = Show(h, id, owner);
                    break;

                case ViewPhase.Closing:
                    h.Stage.PlayMode = StageMode.Manual;
                    task = Show(h, id, owner);
                    h.Stage.CompletePlay();
                    var view = ViewOf(h, id);
                    if (latch != null)
                        latch(view);
                    else
                        view.Complete(true);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(phase));
            }

            var handle = HandleOf(h, id);
            Assert.That(handle.Phase, Is.EqualTo(phase), "The setup did not reach " + phase + ".\n" + h.Describe());
            return task;
        }

        // Index of the first recorded stage call of the kind on the instance, from startIndex on; -1 when none.
        public static int IndexOfCall(FakeStage stage, StageCallKind kind, FakeInstance instance, int startIndex = 0)
        {
            var calls = stage.Calls;
            for (var i = startIndex; i < calls.Count; i++)
            {
                if (calls[i].Kind == kind && ReferenceEquals(calls[i].Instance, instance))
                    return i;
            }

            return -1;
        }

        // Index of the first Play call on the instance with the given direction; -1 when none.
        public static int IndexOfPlay(FakeStage stage, FakeInstance instance, bool show)
        {
            var calls = stage.Calls;
            for (var i = 0; i < calls.Count; i++)
            {
                if (calls[i].Kind == StageCallKind.Play && ReferenceEquals(calls[i].Instance, instance) && calls[i].On == show)
                    return i;
            }

            return -1;
        }

        public static bool TryFindDiagnostic(RecordingDiagnostics diagnostics, string id, out DiagnosticEntry entry)
        {
            for (var i = 0; i < diagnostics.Entries.Count; i++)
            {
                if (string.Equals(diagnostics.Entries[i].Id, id, StringComparison.Ordinal))
                {
                    entry = diagnostics.Entries[i];
                    return true;
                }
            }

            entry = default(DiagnosticEntry);
            return false;
        }

        // The request ended without being shown, for the given reason.
        public static void AssertNotShown<T>(ViewResult<T> result, RejectReason rejected)
        {
            Assert.That(result.WasShown, Is.False, "WasShown");
            Assert.That(result.Reason, Is.EqualTo(CloseReason.NotShown), "Reason");
            Assert.That(result.Rejected, Is.EqualTo(rejected), "Rejected");
            Assert.That(result.HasValue, Is.False, "HasValue");
        }

        // Rejected at acceptance: no shield, no load, exactly one NotShown event, no Requested.
        public static void AssertRejectedAtAcceptance<T>(
            NavigatorHarness h,
            ViewResult<T> result,
            RejectReason expected,
            string viewId,
            int shieldBefore,
            int acquiresBefore)
        {
            AssertNotShown(result, expected);
            Assert.That(result.RequestId, Is.GreaterThan(0), "A rejected request still carries an id.");
            Assert.That(h.ShieldCount, Is.EqualTo(shieldBefore), "A rejection takes no shield.\n" + h.Describe());
            Assert.That(h.Stage.CountOf(StageCallKind.Acquire), Is.EqualTo(acquiresBefore), "A rejection starts no load.");

            var kinds = h.Observer.KindsFor(result.RequestId);
            Assert.That(kinds, Is.EqualTo(new[] { ViewEventKind.NotShown }), "Events of the rejected request.");

            var published = h.Observer.EventsFor(result.RequestId)[0];
            Assert.That(published.Reason, Is.EqualTo(CloseReason.NotShown), "Event reason");
            Assert.That(published.Rejected, Is.EqualTo(expected), "Event rejected");
            Assert.That(published.ViewId, Is.EqualTo(viewId), "Event view id");
        }

        // Nothing is left on the given layers: no request, no shield holder, shield down.
        public static void AssertClean(NavigatorHarness h, params ViewLayer[] layers)
        {
            for (var i = 0; i < layers.Length; i++)
                Assert.That(h.RequestCount(layers[i]), Is.Zero, "Requests left on " + layers[i].Id + ".\n" + h.Describe());

            Assert.That(h.ShieldCount, Is.Zero, "Shield holders left.\n" + h.Describe());
            Assert.That(h.Stage.ShieldBlocking, Is.False, "The shield is still up.");
        }
    }
}
