using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.Events;

namespace Ecanakli.UIDirector.Tests
{
    // ViewScope with a real navigator: the package ends the scopes of a view on every path.
    public sealed class ScopeOnNavigatorContractTests
    {
        private NavigatorHarness _harness;

        [SetUp]
        public void SetUp()
        {
            _harness = new NavigatorHarness();
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        [Test]
        public void OpenScopeBinding_IsLive_WhileTheViewIsOpen_AndRemovedWhenItCloses()
        {
            var source = new UnityEvent();
            var clicks = 0;
            var run = ContractShownRun.Start(_harness, view => view.BindOnOpen = scope => scope.Bind(source, () => clicks++));

            source.Invoke();
            Assert.That(clicks, Is.EqualTo(1), "A binding made in OnOpen must be live while the view is open.");

            run.View.Close();

            source.Invoke();
            Assert.That(clicks, Is.EqualTo(1), "The package must remove the binding when the open ends.");
        }

        [Test]
        public void VisibleScope_EndsBeforeTheOpenScope_AndBothEndBeforeTheInstanceIsReleased()
        {
            var timeline = new List<string>();
            var releasedAtEnd = new Dictionary<string, int>();
            var run = ContractShownRun.Start(_harness, view => view.BindOnOpen = scope => scope.OnEnd(() =>
            {
                timeline.Add("open scope");
                releasedAtEnd["open scope"] = _harness.Stage.LastInstanceOf(ContractShownRun.DefaultId).ReleaseCount;
            }));

            Assert.That(run.View.VisibleCount, Is.EqualTo(1), "The popup is visible, so its visible scope began.");
            run.View.LastVisible.OnEnd(() =>
            {
                timeline.Add("visible scope");
                releasedAtEnd["visible scope"] = _harness.Stage.LastInstanceOf(ContractShownRun.DefaultId).ReleaseCount;
            });

            run.View.Close();

            Assert.That(timeline, Is.EqualTo(new[] { "visible scope", "open scope" }), "The visible scope ends, then the open scope.");
            Assert.That(releasedAtEnd["visible scope"], Is.Zero, "Scopes end before the instance is released.");
            Assert.That(releasedAtEnd["open scope"], Is.Zero, "Scopes end before the instance is released.");
            Assert.That(_harness.Stage.LastInstanceOf(ContractShownRun.DefaultId).ReleaseCount, Is.EqualTo(1));
        }

        [TestCase(ContractClosePath.ViewComplete)]
        [TestCase(ContractClosePath.ViewClose)]
        [TestCase(ContractClosePath.HandleClose)]
        [TestCase(ContractClosePath.Back)]
        [TestCase(ContractClosePath.Backdrop)]
        [TestCase(ContractClosePath.CloseAll)]
        [TestCase(ContractClosePath.OwnerCancel)]
        [TestCase(ContractClosePath.InstanceDestroyed)]
        [TestCase(ContractClosePath.InstanceDeactivated)]
        [TestCase(ContractClosePath.Teardown)]
        public void OpenAndVisibleScopes_EndExactlyOnce_OnEveryClosePath(ContractClosePath path)
        {
            var ends = 0;
            ViewScope open = null;
            var run = ContractShownRun.Start(_harness, view => view.BindOnOpen = scope =>
            {
                open = scope;
                scope.OnEnd(() => ends++);
            });

            var visibleEnds = 0;
            run.View.LastVisible.OnEnd(() => visibleEnds++);

            run.End(path);

            Assert.That(ends, Is.EqualTo(1), "An open scope that began ends exactly once.");
            Assert.That(open.IsEnded, Is.True);
            Assert.That(visibleEnds, Is.EqualTo(1), "The visible scope ends exactly once.");
            Assert.That(run.View.VisibleCount, Is.EqualTo(1), "No second visible span began.");
            Assert.That(run.View.LastVisible.IsEnded, Is.True, "No visible scope stays open after the request ended.");
            Assert.That(run.View.OpenCount, Is.EqualTo(1), "OnOpen runs at most once per record.");
        }

        [Test]
        public void OpenScope_EndsExactlyOnce_WhenOnOpenThrows()
        {
            var ends = 0;
            var view = new ContractView
            {
                ThrowOnOpen = new InvalidOperationException("open failed"),
                BindOnOpen = scope => scope.OnEnd(() => ends++)
            };
            _harness.Row("p", ViewLayers.Popups).Map("p", () => view);

            var result = _harness.Navigator.Show("p", _harness.Owner).ResultNow();

            Assert.That(result.WasShown, Is.False);
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.Faulted));
            Assert.That(ends, Is.EqualTo(1), "The open scope ends when OnOpen threw.");
        }

        [Test]
        public void OpenScope_EndsExactlyOnce_WhenOnOpenDeclines()
        {
            var ends = 0;
            var view = new ContractView
            {
                RunOnOpen = context => context.Decline("not now"),
                BindOnOpen = scope => scope.OnEnd(() => ends++)
            };
            _harness.Row("p", ViewLayers.Popups).Map("p", () => view);

            var result = _harness.Navigator.Show("p", _harness.Owner).ResultNow();

            Assert.That(result.Rejected, Is.EqualTo(RejectReason.Declined));
            Assert.That(ends, Is.EqualTo(1), "The open scope ends when OnOpen declined.");
        }

        [Test]
        public void OpenScopeRun_IsCancelledWhenTheViewCloses_AndNoErrorIsRouted()
        {
            var gate = new UniTaskCompletionSource();
            var cancelled = false;
            var run = ContractShownRun.Start(_harness, view => view.BindOnOpen = scope => scope.Run(ct =>
            {
                ct.Register(() =>
                {
                    cancelled = true;
                    gate.TrySetCanceled(ct);
                });
                return gate.Task;
            }));

            Assert.That(cancelled, Is.False);

            run.View.Close();

            Assert.That(cancelled, Is.True, "Awaits inside the view must see the open scope token cancelled.");
            Assert.That(gate.Task.Status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.That(_harness.Errors, Is.Empty, "The cancellation of scope work is swallowed.");
        }

        [Test]
        public void VisibleScopeRun_IsCancelledWhenTheViewCloses_AndNoErrorIsRouted()
        {
            var gate = new UniTaskCompletionSource();
            var run = ContractShownRun.Start(_harness);

            run.View.LastVisible.Run(ct =>
            {
                ct.Register(() => gate.TrySetCanceled(ct));
                return gate.Task;
            });

            run.View.Close();

            Assert.That(gate.Task.Status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.That(_harness.Errors, Is.Empty);
        }

        [Test]
        public void ScopeReferenceKeptAfterTheViewClosed_IsInert()
        {
            ViewScope kept = null;
            var run = ContractShownRun.Start(_harness, view => view.BindOnOpen = scope => kept = scope);
            run.View.Close();

            var source = new UnityEvent();
            var calls = 0;
            var endRuns = 0;
            var disposable = new ContractDisposable();

            kept.Bind(source, () => calls++);
            kept.OnEnd(() => endRuns++);
            kept.Add(disposable);

            source.Invoke();
            Assert.That(kept.IsEnded, Is.True);
            Assert.That(calls, Is.Zero, "A stale scope must not add listeners.");
            Assert.That(endRuns, Is.EqualTo(1), "A stale scope runs an end action at once.");
            Assert.That(disposable.DisposeCount, Is.EqualTo(1), "A stale scope disposes at once.");
            Assert.That(kept.Token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void ContextOpen_AfterTheRequestEnded_IsAnEndedScope_NotNull()
        {
            var run = ContractShownRun.Start(_harness);
            run.View.Close();

            // A view that kept its context reaches the scope through it; the stale reference must stay inert.
            Assert.That(run.View.Context.Open, Is.Not.Null, "A late call on the context must not hit a null scope.");
            Assert.That(run.View.Context.Open.IsEnded, Is.True);
        }

        [Test]
        public void ContextClose_AfterTheRequestEnded_DoesNothing_AndDoesNotThrow()
        {
            var run = ContractShownRun.Start(_harness);
            run.View.Close();
            var result = run.Result.ResultNow();

            Assert.DoesNotThrow(() => run.View.Close());
            Assert.DoesNotThrow(() => run.View.Complete(default(NoResult)));
            Assert.DoesNotThrow(() => run.View.Context.Decline("too late"));

            Assert.That(result.Reason, Is.EqualTo(CloseReason.Dismissed), "The first close cause stays the outcome.");
            Assert.That(_harness.Errors, Is.Empty);
        }
    }
}
