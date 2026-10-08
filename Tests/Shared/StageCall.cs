using System.Threading;

namespace Ecanakli.UIDirector.Tests
{
    // The stage members the fake records; SetEvents and TryGetGameObject are not recorded.
    internal enum StageCallKind
    {
        Acquire,
        ResolveView,
        Inject,
        Place,
        Relink,
        Apply,
        Snap,
        Play,
        ApplyBackdrop,
        SetShield,
        SetLoadIndicator,
        Stash,
        Release,
        Preload
    }

    // One recorded call to the fake stage, in call order.
    internal sealed class StageCall
    {
        public StageCall(
            StageCallKind kind,
            FakeInstance instance = null,
            string viewId = null,
            bool on = false,
            int order = 0,
            ViewLayer layer = null,
            ViewVisualState visual = default,
            CancellationToken token = default)
        {
            Kind = kind;
            Instance = instance;
            ViewId = viewId ?? (instance != null ? instance.ViewId : null);
            On = on;
            Order = order;
            Layer = layer;
            Visual = visual;
            Token = token;
        }

        public StageCallKind Kind { get; }

        // The instance the call targeted; null for stage-wide calls and for Acquire and Preload.
        public FakeInstance Instance { get; }

        // The row id for Acquire and Preload, the instance's row id otherwise.
        public string ViewId { get; }

        // Snap: shown. Play: show. SetShield: blocking. ApplyBackdrop: active. SetLoadIndicator: visible.
        public bool On { get; }

        // The sorting order passed to ApplyBackdrop and SetShield.
        public int Order { get; }

        // The layer passed to Place and ApplyBackdrop.
        public ViewLayer Layer { get; }

        // The state passed to Apply.
        public ViewVisualState Visual { get; }

        // The token passed to Acquire, Play and Preload.
        public CancellationToken Token { get; }

        public override string ToString()
        {
            var layerId = Layer != null ? Layer.Id : "?";
            switch (Kind)
            {
                case StageCallKind.Acquire:
                case StageCallKind.Preload:
                    return Kind + " " + ViewId;
                case StageCallKind.Apply:
                    return "Apply " + Instance + " hidden=" + Visual.Hidden + " interactive=" + Visual.Interactive
                        + " order=" + Visual.SortingOrder + " covered=" + Visual.CoveredMode;
                case StageCallKind.Snap:
                    return "Snap " + Instance + " shown=" + On;
                case StageCallKind.Play:
                    return "Play " + Instance + (On ? " show" : " hide");
                case StageCallKind.Place:
                    return "Place " + Instance + " on " + layerId;
                case StageCallKind.ApplyBackdrop:
                    return "ApplyBackdrop " + layerId + " active=" + On + " order=" + Order;
                case StageCallKind.SetShield:
                    return "SetShield blocking=" + On + " order=" + Order;
                case StageCallKind.SetLoadIndicator:
                    return "SetLoadIndicator visible=" + On;
                default:
                    return Kind + " " + Instance;
            }
        }
    }
}
