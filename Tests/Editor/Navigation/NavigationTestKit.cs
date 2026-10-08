using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Helpers shared by the navigation fixtures; they only read what the fakes recorded.
    internal static class NavigationTestKit
    {
        // A plain layer above the three defaults: no rules, so it shields nothing and takes no back.
        public static readonly ViewLayer Hud = new ViewLayer("hud", 3, ViewLayerRules.None);

        // A harness whose root also has the plain hud layer.
        public static NavigatorHarness NewHarnessWithHud(int sortingBase = 0)
        {
            var layers = new[] { ViewLayers.Screens, ViewLayers.Popups, ViewLayers.System, Hud };
            return new NavigatorHarness(layers: layers, sortingBase: sortingBase);
        }

        // Opens a view without arguments and without a result.
        public static ViewHandle Open(NavigatorHarness harness, string id)
        {
            return harness.Navigator.Open(ViewKey.Create(id), harness.Owner);
        }

        // The state the stage last received for the newest instance of the id.
        public static ViewVisualState VisualOf(NavigatorHarness harness, string id)
        {
            var instance = harness.Stage.LastInstanceOf(id);
            Assert.That(instance, Is.Not.Null, "No instance was acquired for '" + id + "'.\n" + harness.Stage.DescribeCalls());
            Assert.That(instance.HasVisual, Is.True, "The stage never received a visual state for '" + id + "'.\n" + harness.Stage.DescribeCalls());
            return instance.Visual;
        }

        // Events

        public static ViewEvent ClosedOf(RecordingObserver observer, string viewId)
        {
            return EventOf(observer, ViewEventKind.Closed, viewId);
        }

        public static ViewEvent NotShownOf(RecordingObserver observer, string viewId)
        {
            return EventOf(observer, ViewEventKind.NotShown, viewId);
        }

        // The first event of the kind for the view id; fails the test when there is none.
        public static ViewEvent EventOf(RecordingObserver observer, ViewEventKind kind, string viewId)
        {
            var index = IndexOfEvent(observer, kind, viewId);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "No " + kind + " event was recorded for '" + viewId + "'.");
            return observer.Events[index];
        }

        public static bool HasEvent(RecordingObserver observer, ViewEventKind kind, string viewId)
        {
            return IndexOfEvent(observer, kind, viewId) >= 0;
        }

        // Position of the first event of the kind for the view id; -1 when there is none.
        public static int IndexOfEvent(RecordingObserver observer, ViewEventKind kind, string viewId)
        {
            for (var i = 0; i < observer.Events.Count; i++)
            {
                var viewEvent = observer.Events[i];
                if (viewEvent.Kind == kind && string.Equals(viewEvent.ViewId, viewId, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        // Stage calls

        // Position of the first call of the kind for the view id; on narrows Snap, Play and the like by their flag.
        public static int IndexOfCall(IReadOnlyList<StageCall> calls, StageCallKind kind, string viewId, bool? on = null)
        {
            for (var i = 0; i < calls.Count; i++)
            {
                var call = calls[i];
                if (call.Kind != kind || !string.Equals(call.ViewId, viewId, StringComparison.Ordinal))
                    continue;

                if (on.HasValue && call.On != on.Value)
                    continue;

                return i;
            }

            return -1;
        }

        // Position of the first Apply for the view id whose state has the given hidden flag; -1 when there is none.
        public static int IndexOfApply(IReadOnlyList<StageCall> calls, string viewId, bool hidden)
        {
            for (var i = 0; i < calls.Count; i++)
            {
                var call = calls[i];
                if (call.Kind != StageCallKind.Apply || !string.Equals(call.ViewId, viewId, StringComparison.Ordinal))
                    continue;

                if (call.Visual.Hidden == hidden)
                    return i;
            }

            return -1;
        }

        public static int CountCalls(IReadOnlyList<StageCall> calls, StageCallKind kind, string viewId)
        {
            var count = 0;
            for (var i = 0; i < calls.Count; i++)
            {
                if (calls[i].Kind == kind && string.Equals(calls[i].ViewId, viewId, StringComparison.Ordinal))
                    count++;
            }

            return count;
        }

        // How many instances the stage ever acquired for the row id.
        public static int InstanceCount(FakeStage stage, string viewId)
        {
            var count = 0;
            for (var i = 0; i < stage.Instances.Count; i++)
            {
                if (string.Equals(stage.Instances[i].ViewId, viewId, StringComparison.Ordinal))
                    count++;
            }

            return count;
        }
    }
}
