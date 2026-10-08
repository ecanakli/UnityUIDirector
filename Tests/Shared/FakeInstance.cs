using System;

namespace Ecanakli.UIDirector.Tests
{
    // A plain stand-in for a view instance: an id, the view object and what the stage did to it.
    internal sealed class FakeInstance
    {
        public FakeInstance(int id, string viewId, object view)
        {
            Id = id;
            ViewId = viewId;
            View = view;
        }

        public int Id { get; }

        // The catalog row id the instance was acquired for.
        public string ViewId { get; }

        // The view object ResolveView hands to the logic; null means no view.
        public object View { get; }

        // False makes ResolveView answer MissingHost.
        public bool HasHost { get; set; } = true;

        // Everything below is written by FakeStage as it applies calls.
        public bool Injected { get; set; }
        public bool Placed { get; set; }
        public ViewLayer Layer { get; set; }
        public ViewHandle Handle { get; set; }
        public bool Stashed { get; set; }

        // The last state passed to Apply; meaningful only when HasVisual is true.
        public bool HasVisual { get; set; }
        public ViewVisualState Visual { get; set; }

        // The last Snap argument; null when the instance was never snapped.
        public bool? LastSnapShown { get; set; }

        public int ApplyCount { get; set; }
        public int SnapCount { get; set; }
        public int PlayCount { get; set; }
        public int StashCount { get; set; }
        public int ReleaseCount { get; set; }

        // Raised through OnInstanceLost; Destroyed tells destroyed from deactivated.
        public bool Lost { get; set; }
        public bool Destroyed { get; set; }

        // True when the stage released it itself because its acquire was cancelled before it finished.
        public bool ReleasedLate { get; set; }

        public bool IsReleased
        {
            get { return ReleaseCount > 0; }
        }

        // Neither released nor stashed: somebody still has to release it.
        public bool IsLive
        {
            get { return ReleaseCount == 0 && !Stashed; }
        }

        public override string ToString()
        {
            return "#" + Id + " " + ViewId;
        }
    }

    // A preload lease the fake stage handed out; disposing it ends the "load".
    internal sealed class FakeLease : IDisposable
    {
        public FakeLease(string viewId)
        {
            ViewId = viewId;
        }

        public string ViewId { get; }

        public int DisposeCount { get; private set; }

        // When set, Dispose still counts and then throws this.
        public Exception ThrowOnDispose { get; set; }

        public bool IsDisposed
        {
            get { return DisposeCount > 0; }
        }

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose != null)
                throw ThrowOnDispose;
        }
    }
}
