using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // The ways a shown popup can end.
    public enum ContractClosePath
    {
        ViewComplete,
        ViewClose,
        HandleClose,
        Back,
        Backdrop,
        CloseAll,
        OwnerCancel,
        InstanceDestroyed,
        InstanceDeactivated,
        Teardown
    }

    // The ways a popup request can end without being shown.
    public enum ContractNotShownPath
    {
        UnknownKey,
        OwnerAlreadyCancelled,
        OnOpenThrows,
        OnOpenDeclines,
        LoadFails,
        OwnerEndsWhileLoading,
        HandleClosedWhileLoading,
        TeardownWhileLoading,
        TeardownWhileWaiting,
        ShowAfterTeardown
    }

    // Opens one popup and ends it by a chosen path; the popup row is created once per harness.
    internal sealed class ContractShownRun
    {
        public const string DefaultId = "n";

        private readonly NavigatorHarness _harness;
        private readonly string _id;

        private ContractShownRun(
            NavigatorHarness harness,
            string id,
            ContractView view,
            CancellationTokenSource owner,
            UniTask<ViewResult<NoResult>> result)
        {
            _harness = harness;
            _id = id;
            View = view;
            Owner = owner;
            Result = result;
        }

        public ContractView View { get; }

        public CancellationTokenSource Owner { get; }

        // The ShowAsync task of the request; pending until the run ends.
        public UniTask<ViewResult<NoResult>> Result { get; }

        // Opens the popup and checks it waits for its close; configure runs before the view opens.
        public static ContractShownRun Start(NavigatorHarness harness, Action<ContractView> configure = null, string id = DefaultId)
        {
            var view = new ContractView();
            if (configure != null)
                configure(view);

            if (!harness.Stage.HasView(id))
                harness.Row(id, ViewLayers.Popups, closeOnBackdrop: true);
            harness.Map(id, () => view);

            var owner = harness.NewOwner();
            var result = harness.Navigator.Show(id, owner.Token);
            Assert.That(result.IsPending(), Is.True, "The popup '" + id + "' should be open and waiting for its close.");

            return new ContractShownRun(harness, id, view, owner, result);
        }

        public void End(ContractClosePath path)
        {
            var navigator = _harness.Navigator;
            var stage = _harness.Stage;

            switch (path)
            {
                case ContractClosePath.ViewComplete:
                    View.Complete(default(NoResult));
                    break;

                case ContractClosePath.ViewClose:
                    View.Close();
                    break;

                case ContractClosePath.HandleClose:
                {
                    ViewHandle handle;
                    Assert.That(navigator.TryGetOpen(_id, out handle), Is.True, "The request should be open.");
                    handle.Close();
                    break;
                }

                case ContractClosePath.Back:
                    Assert.That(navigator.Back(), Is.True, "Back should close the popup.");
                    break;

                case ContractClosePath.Backdrop:
                    stage.RaiseBackdropTapped(ViewLayers.Popups);
                    break;

                case ContractClosePath.CloseAll:
                    navigator.CloseAll(ViewLayers.Popups);
                    break;

                case ContractClosePath.OwnerCancel:
                    Owner.Cancel();
                    break;

                case ContractClosePath.InstanceDestroyed:
                    stage.RaiseInstanceLost(stage.LastInstanceOf(_id), true);
                    break;

                case ContractClosePath.InstanceDeactivated:
                    stage.RaiseInstanceLost(stage.LastInstanceOf(_id), false);
                    break;

                case ContractClosePath.Teardown:
                    navigator.Teardown();
                    break;
            }
        }
    }

    // Starts one popup request that ends without being shown; Finish performs the ending step.
    internal sealed class ContractNotShownRun
    {
        public const string DefaultId = "n";
        public const string CompanionId = "n-first";

        private readonly NavigatorHarness _harness;
        private readonly ContractNotShownPath _path;
        private readonly CancellationTokenSource _owner;

        private ContractNotShownRun(NavigatorHarness harness, ContractNotShownPath path)
        {
            _harness = harness;
            _path = path;
            _owner = harness.NewOwner();
        }

        // The ShowAsync task of the request under test.
        public UniTask<ViewResult<NoResult>> Result { get; private set; }

        // The loading request ahead of the waiting one; only for TeardownWhileWaiting.
        public UniTask<ViewResult<NoResult>> Companion { get; private set; }

        public static ContractNotShownRun Start(NavigatorHarness harness, ContractNotShownPath path)
        {
            var run = new ContractNotShownRun(harness, path);
            run.Begin();
            return run;
        }

        // Ends the request the way the path says, then lets a cancelled load answer late.
        public void Finish()
        {
            var navigator = _harness.Navigator;
            var stage = _harness.Stage;

            switch (_path)
            {
                case ContractNotShownPath.LoadFails:
                    stage.FailAcquire();
                    break;

                case ContractNotShownPath.OwnerEndsWhileLoading:
                    _owner.Cancel();
                    break;

                case ContractNotShownPath.HandleClosedWhileLoading:
                {
                    ViewHandle handle;
                    Assert.That(navigator.TryGetOpen(DefaultId, out handle), Is.True, "The request should be in flight.");
                    handle.Close();
                    break;
                }

                case ContractNotShownPath.TeardownWhileLoading:
                case ContractNotShownPath.TeardownWhileWaiting:
                    navigator.Teardown();
                    break;
            }

            while (stage.PendingAcquireCount > 0)
                stage.CompleteAcquire();

            stage.AcquireMode = StageMode.Auto;
        }

        private void Begin()
        {
            var navigator = _harness.Navigator;
            var stage = _harness.Stage;

            switch (_path)
            {
                case ContractNotShownPath.UnknownKey:
                    Result = navigator.Show("missing", _owner.Token);
                    break;

                case ContractNotShownPath.OwnerAlreadyCancelled:
                    Prepare(DefaultId);
                    _owner.Cancel();
                    Result = navigator.Show(DefaultId, _owner.Token);
                    break;

                case ContractNotShownPath.OnOpenThrows:
                    Prepare(DefaultId).ThrowOnOpen = new InvalidOperationException("The view failed.");
                    Result = navigator.Show(DefaultId, _owner.Token);
                    break;

                case ContractNotShownPath.OnOpenDeclines:
                    Prepare(DefaultId).RunOnOpen = context => context.Decline("declined here");
                    Result = navigator.Show(DefaultId, _owner.Token);
                    break;

                case ContractNotShownPath.LoadFails:
                case ContractNotShownPath.OwnerEndsWhileLoading:
                case ContractNotShownPath.HandleClosedWhileLoading:
                case ContractNotShownPath.TeardownWhileLoading:
                    Prepare(DefaultId);
                    stage.AcquireMode = StageMode.Manual;
                    Result = navigator.Show(DefaultId, _owner.Token);
                    break;

                case ContractNotShownPath.TeardownWhileWaiting:
                    Prepare(CompanionId);
                    Prepare(DefaultId);
                    stage.AcquireMode = StageMode.Manual;
                    Companion = navigator.Show(CompanionId, _harness.Owner);
                    Result = navigator.Show(DefaultId, _owner.Token);
                    break;

                case ContractNotShownPath.ShowAfterTeardown:
                    Prepare(DefaultId);
                    navigator.Teardown();
                    Result = navigator.Show(DefaultId, _owner.Token);
                    break;
            }
        }

        // Registers the row once and maps a fresh view for the next load.
        private ContractView Prepare(string id)
        {
            var view = new ContractView();

            if (!_harness.Stage.HasView(id))
                _harness.Row(id, ViewLayers.Popups);
            _harness.Map(id, () => view);

            return view;
        }
    }
}
