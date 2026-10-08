using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.Events;

namespace Ecanakli.UIDirector.Tests
{
    // ViewScope rules, driven on a bare scope without a navigator.
    public sealed class ScopeContractTests
    {
        private ContractScopeRig _rig;
        private int _calls;

        [SetUp]
        public void SetUp()
        {
            _rig = new ContractScopeRig();
            _calls = 0;
        }

        // Bind and removal

        [Test]
        public void Bind_UnityEvent_AddsTheListenerNow_AndRemovesItWhenTheScopeEnds()
        {
            var scope = _rig.NewScope();
            var source = new UnityEvent();
            var calls = 0;

            scope.Bind(source, () => calls++);

            source.Invoke();
            Assert.That(calls, Is.EqualTo(1), "The listener must be live right after Bind.");

            scope.End();

            source.Invoke();
            Assert.That(calls, Is.EqualTo(1), "The scope end must remove the listener.");
        }

        [Test]
        public void Bind_TypedUnityEvent_AddsTheListenerNow_AndRemovesItWhenTheScopeEnds()
        {
            var scope = _rig.NewScope();
            var source = new UnityEvent<int>();
            var seen = new List<int>();

            scope.Bind(source, (UnityAction<int>)(value => seen.Add(value)));

            source.Invoke(5);
            scope.End();
            source.Invoke(6);

            Assert.That(seen, Is.EqualTo(new[] { 5 }));
        }

        [Test]
        public void Subscribe_CallsAddNow_AndRunsRemoveWhenTheScopeEnds()
        {
            var scope = _rig.NewScope();
            Action published = null;
            var calls = 0;
            Action handler = () => calls++;

            scope.Subscribe(h => published += h, h => published -= h, handler);

            Assert.That(published, Is.Not.Null, "Subscribe must call the add delegate now.");
            published();
            Assert.That(calls, Is.EqualTo(1));

            scope.End();

            Assert.That(published, Is.Null, "The scope end must call the remove delegate with the same handler.");
        }

        [Test]
        public void Subscribe_Typed_CallsAddNow_AndRunsRemoveWhenTheScopeEnds()
        {
            var scope = _rig.NewScope();
            Action<int> published = null;
            var seen = new List<int>();
            Action<int> handler = value => seen.Add(value);

            scope.Subscribe<int>(h => published += h, h => published -= h, handler);

            published(3);
            scope.End();

            Assert.That(seen, Is.EqualTo(new[] { 3 }));
            Assert.That(published, Is.Null);
        }

        // Duplicate Bind

        [Test]
        public void Bind_TheSameEventAndHandlerTwice_IsIgnored_TheListenerRunsOnce()
        {
            var scope = _rig.NewScope();
            var source = new UnityEvent();
            var calls = 0;
            UnityAction handler = () => calls++;

            scope.Bind(source, handler);
            scope.Bind(source, handler);

            source.Invoke();
            Assert.That(calls, Is.EqualTo(1), "A duplicate Bind of the same event and handler must be ignored.");

            scope.End();

            source.Invoke();
            Assert.That(calls, Is.EqualTo(1), "The single listener must be removed at the end.");
        }

        [Test]
        public void Bind_TheSameMethodGroupTwice_IsIgnored_TheListenerRunsOnce()
        {
            var scope = _rig.NewScope();
            var source = new UnityEvent();

            scope.Bind(source, CountCall);
            scope.Bind(source, CountCall);

            source.Invoke();
            Assert.That(_calls, Is.EqualTo(1));
        }

        [Test]
        public void Bind_TypedEventAndHandlerTwice_IsIgnored_TheListenerRunsOnce()
        {
            var scope = _rig.NewScope();
            var source = new UnityEvent<int>();
            var seen = new List<int>();
            UnityAction<int> handler = value => seen.Add(value);

            scope.Bind(source, handler);
            scope.Bind(source, handler);

            source.Invoke(1);
            Assert.That(seen, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Bind_TwoDifferentHandlersOnOneEvent_IsNotADuplicate()
        {
            var scope = _rig.NewScope();
            var source = new UnityEvent();
            var first = 0;
            var second = 0;

            scope.Bind(source, () => first++);
            scope.Bind(source, () => second++);

            source.Invoke();
            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(1));

            scope.End();

            source.Invoke();
            Assert.That(first, Is.EqualTo(1), "Both listeners must be removed.");
            Assert.That(second, Is.EqualTo(1), "Both listeners must be removed.");
        }

        [Test]
        public void Bind_TheSameHandlerOnTwoEvents_IsNotADuplicate()
        {
            var scope = _rig.NewScope();
            var left = new UnityEvent();
            var right = new UnityEvent();
            var calls = 0;
            UnityAction handler = () => calls++;

            scope.Bind(left, handler);
            scope.Bind(right, handler);

            left.Invoke();
            right.Invoke();
            Assert.That(calls, Is.EqualTo(2));

            scope.End();

            left.Invoke();
            right.Invoke();
            Assert.That(calls, Is.EqualTo(2), "Both events must lose the listener.");
        }

        // Registration on an ended scope

        [Test]
        public void Bind_OnAnEndedScope_DoesNotAddTheListener()
        {
            var scope = _rig.NewScope();
            scope.End();
            var source = new UnityEvent();
            var calls = 0;

            scope.Bind(source, () => calls++);

            source.Invoke();
            Assert.That(calls, Is.Zero, "A registration on an ended scope must not add the listener.");
        }

        [Test]
        public void Bind_TypedOnAnEndedScope_DoesNotAddTheListener()
        {
            var scope = _rig.NewScope();
            scope.End();
            var source = new UnityEvent<int>();
            var calls = 0;

            scope.Bind(source, (UnityAction<int>)(value => calls++));

            source.Invoke(1);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void Subscribe_OnAnEndedScope_DoesNotCallAdd()
        {
            var scope = _rig.NewScope();
            scope.End();
            var adds = 0;
            var removes = 0;

            scope.Subscribe(h => adds++, h => removes++, () => { });

            Assert.That(adds, Is.Zero, "An ended scope must not subscribe.");
            Assert.That(removes, Is.Zero);
        }

        [Test]
        public void Add_OnAnEndedScope_DisposesTheObjectAtOnce()
        {
            var scope = _rig.NewScope();
            scope.End();
            var disposable = new ContractDisposable();

            scope.Add(disposable);

            Assert.That(disposable.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void OnEnd_OnAnEndedScope_RunsTheActionAtOnce()
        {
            var scope = _rig.NewScope();
            scope.End();
            var runs = 0;

            scope.OnEnd(() => runs++);

            Assert.That(runs, Is.EqualTo(1));
        }

        [Test]
        public void OnEnd_WithState_OnAnEndedScope_RunsTheActionAtOnceWithTheState()
        {
            var scope = _rig.NewScope();
            scope.End();
            var seen = new List<string>();

            scope.OnEnd("state", value => seen.Add(value));

            Assert.That(seen, Is.EqualTo(new[] { "state" }));
        }

        [Test]
        public void Run_OnAnEndedScope_DoesNotStartTheWork()
        {
            var scope = _rig.NewScope();
            scope.End();
            var started = 0;

            scope.Run(ct =>
            {
                started++;
                return UniTask.CompletedTask;
            });

            Assert.That(started, Is.Zero, "A stale scope reference is inert.");
            Assert.That(_rig.Errors, Is.Empty);
        }

        [Test]
        public void End_CalledAgainOnAnEndedScope_DoesNotRerunActionsRegisteredAfterTheEnd()
        {
            var scope = _rig.NewScope();
            scope.End();
            var runs = 0;
            scope.OnEnd(() => runs++);

            scope.End();

            Assert.That(runs, Is.EqualTo(1), "A scope is single-use: a second End must not rerun anything.");
        }

        // Ending

        [Test]
        public void End_RunsEveryCleanupExactlyOnce_WhenCalledTwice()
        {
            var scope = _rig.NewScope();
            var runs = 0;
            var disposable = new ContractDisposable();
            scope.OnEnd(() => runs++);
            scope.Add(disposable);

            scope.End();
            scope.End();

            Assert.That(runs, Is.EqualTo(1));
            Assert.That(disposable.DisposeCount, Is.EqualTo(1));
            Assert.That(scope.IsEnded, Is.True);
        }

        [Test]
        public void Token_IsNotCancelledBeforeTheEnd_AndIsCancelledAfterIt()
        {
            var scope = _rig.NewScope();
            var token = scope.Token;

            Assert.That(token.IsCancellationRequested, Is.False);
            Assert.That(scope.IsEnded, Is.False);

            scope.End();

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(scope.IsEnded, Is.True);
        }

        [Test]
        public void Token_ReadForTheFirstTimeAfterTheEnd_IsAlreadyCancelled()
        {
            var scope = _rig.NewScope();
            scope.End();

            Assert.That(scope.Token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void End_CancelsTheTokenFirst_ThenRunsTheEntriesNewestFirst()
        {
            var scope = _rig.NewScope();
            var order = new List<string>();

            scope.Token.Register(() => order.Add("token"));
            scope.OnEnd(() => order.Add("action"));
            scope.Add(new ContractDisposable(() => order.Add("disposable")));
            scope.Subscribe(h => { }, h => order.Add("subscription"), () => { });
            scope.OnEnd("s", value => order.Add("state action"));

            scope.End();

            Assert.That(order, Is.EqualTo(new[] { "token", "state action", "subscription", "disposable", "action" }));
        }

        [Test]
        public void End_EntriesAlreadySeeTheCancelledToken()
        {
            var scope = _rig.NewScope();
            var seen = new List<bool>();
            scope.OnEnd(() => seen.Add(scope.Token.IsCancellationRequested));
            scope.OnEnd(() => seen.Add(scope.IsEnded));

            scope.End();

            Assert.That(seen, Is.EqualTo(new[] { true, true }));
        }

        // A throwing cleanup

        [Test]
        public void End_WhenACleanupThrows_RunsTheRemainingCleanups_AndRoutesTheError()
        {
            var scope = _rig.NewScope("shop", 42);
            var order = new List<string>();
            var failure = new InvalidOperationException("cleanup failed");
            scope.OnEnd(() => order.Add("oldest"));
            scope.OnEnd(() =>
            {
                order.Add("middle");
                throw failure;
            });
            scope.OnEnd(() => order.Add("newest"));

            Assert.DoesNotThrow(() => scope.End(), "A scope end never throws.");

            Assert.That(order, Is.EqualTo(new[] { "newest", "middle", "oldest" }), "The cleanups after the failing one must still run.");
            Assert.That(_rig.Errors.Count, Is.EqualTo(1));
            Assert.That(_rig.Errors[0].Exception, Is.SameAs(failure));
            Assert.That(_rig.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.ScopeCleanup));
            Assert.That(_rig.Errors[0].Context.ViewId, Is.EqualTo("shop"));
            Assert.That(_rig.Errors[0].Context.RequestId, Is.EqualTo(42));
        }

        [Test]
        public void End_WhenEveryKindOfCleanupThrows_EachIsRouted_AndAllOfThemRun()
        {
            var scope = _rig.NewScope();
            var order = new List<string>();
            scope.OnEnd(() =>
            {
                order.Add("action");
                throw new InvalidOperationException("action");
            });
            scope.Add(new ContractDisposable(() =>
            {
                order.Add("disposable");
                throw new InvalidOperationException("disposable");
            }));
            scope.Subscribe(
                h => { },
                h =>
                {
                    order.Add("subscription");
                    throw new InvalidOperationException("subscription");
                },
                () => { });
            scope.OnEnd("s", value =>
            {
                order.Add("state action");
                throw new InvalidOperationException("state action");
            });

            Assert.DoesNotThrow(() => scope.End());

            Assert.That(order, Is.EqualTo(new[] { "state action", "subscription", "disposable", "action" }));
            Assert.That(_rig.Errors.Count, Is.EqualTo(4));
        }

        [Test]
        public void End_WhenATokenCallbackThrows_StillRunsEveryEntry_AndRoutesTheError()
        {
            var scope = _rig.NewScope();
            var runs = 0;
            scope.Token.Register(() => throw new InvalidOperationException("token callback"));
            scope.OnEnd(() => runs++);

            Assert.DoesNotThrow(() => scope.End());

            Assert.That(runs, Is.EqualTo(1), "A failing token callback must not stop the entries.");
            Assert.That(_rig.Errors.Count, Is.EqualTo(1));
            Assert.That(_rig.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.ScopeCleanup));
        }

        [Test]
        public void End_WhenTheErrorHandlerItselfThrows_DoesNotThrow_AndTheRestStillRuns()
        {
            var rig = new ContractScopeRig(true);
            var scope = rig.NewScope();
            var runs = 0;
            scope.OnEnd(() => runs++);
            scope.OnEnd(() => throw new InvalidOperationException("cleanup failed"));

            Assert.DoesNotThrow(() => scope.End());

            Assert.That(runs, Is.EqualTo(1));
            Assert.That(rig.Diagnostics.FaultCount, Is.EqualTo(1), "A failing handler is reported as an internal fault.");
        }

        // Run

        [Test]
        public void Run_StartsTheWorkNow_WithTheScopeToken()
        {
            var scope = _rig.NewScope();
            var gate = new UniTaskCompletionSource();
            var started = 0;
            var seen = default(CancellationToken);

            scope.Run(ct =>
            {
                started++;
                seen = ct;
                return gate.Task;
            });

            Assert.That(started, Is.EqualTo(1));
            Assert.That(seen, Is.EqualTo(scope.Token));
            Assert.That(seen.IsCancellationRequested, Is.False);

            gate.TrySetResult();
            scope.End();
        }

        [Test]
        public void Run_WithState_PassesTheStateAndTheScopeToken()
        {
            var scope = _rig.NewScope();
            var gate = new UniTaskCompletionSource();
            var seenState = string.Empty;
            var seenToken = default(CancellationToken);

            scope.Run("payload", (state, ct) =>
            {
                seenState = state;
                seenToken = ct;
                return gate.Task;
            });

            Assert.That(seenState, Is.EqualTo("payload"));
            Assert.That(seenToken, Is.EqualTo(scope.Token));

            gate.TrySetResult();
            scope.End();
        }

        [Test]
        public void Run_WorkCancelledByTheScopeEnd_IsSwallowed_AndNothingIsRouted()
        {
            var scope = _rig.NewScope();
            var gate = new UniTaskCompletionSource();
            var seen = default(CancellationToken);

            scope.Run(ct =>
            {
                seen = ct;
                ct.Register(() => gate.TrySetCanceled(ct));
                return gate.Task;
            });

            scope.End();

            Assert.That(seen.IsCancellationRequested, Is.True, "The scope end must cancel the token the work received.");
            Assert.That(gate.Task.Status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.That(_rig.Errors, Is.Empty, "A cancellation is the normal end of scope work.");
            Assert.That(_rig.Diagnostics.Total, Is.Zero);
        }

        [Test]
        public void Run_WorkThatThrowsOperationCanceled_IsSwallowed_AndNothingIsRouted()
        {
            var scope = _rig.NewScope();

            scope.Run(ct => throw new OperationCanceledException());

            Assert.That(_rig.Errors, Is.Empty);
            Assert.That(_rig.Diagnostics.Total, Is.Zero);
        }

        [Test]
        public void Run_WorkThatThrowsSynchronously_IsRoutedAsAScopeTaskError()
        {
            var scope = _rig.NewScope("lobby", 9);
            var failure = new InvalidOperationException("work failed");

            Assert.DoesNotThrow(() => scope.Run(ct => throw failure), "Run itself never throws for work errors.");

            Assert.That(_rig.Errors.Count, Is.EqualTo(1));
            Assert.That(_rig.Errors[0].Exception, Is.SameAs(failure));
            Assert.That(_rig.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.ScopeTask));
            Assert.That(_rig.Errors[0].Context.ViewId, Is.EqualTo("lobby"));
            Assert.That(_rig.Errors[0].Context.RequestId, Is.EqualTo(9));
        }

        [Test]
        public void Run_WorkThatFailsAfterAnAwait_IsRoutedOnce()
        {
            var scope = _rig.NewScope();
            var gate = new UniTaskCompletionSource();
            var failure = new InvalidOperationException("late failure");

            scope.Run(async ct =>
            {
                await gate.Task;
                throw failure;
            });

            Assert.That(_rig.Errors, Is.Empty, "Nothing failed yet.");

            gate.TrySetResult();

            Assert.That(_rig.Errors.Count, Is.EqualTo(1));
            Assert.That(_rig.Errors[0].Exception, Is.SameAs(failure));
            Assert.That(_rig.Errors[0].Context.Source, Is.EqualTo(ViewErrorSource.ScopeTask));
        }

        [Test]
        public void Run_WorkThatFailsWhileTheScopeEnds_IsRoutedBecauseItIsNotACancellation()
        {
            var scope = _rig.NewScope();
            var gate = new UniTaskCompletionSource();
            var failure = new InvalidOperationException("cleanup in work failed");

            scope.Run(ct =>
            {
                ct.Register(() => gate.TrySetException(failure));
                return gate.Task;
            });

            scope.End();

            Assert.That(_rig.Errors.Count, Is.EqualTo(1), "Only cancellation is swallowed; other errors are routed.");
            Assert.That(_rig.Errors[0].Exception, Is.SameAs(failure));
        }

        [Test]
        public void Run_WorkStillRunningAtTheEnd_ObservesACancelledToken_InTheGenericForm()
        {
            var scope = _rig.NewScope();
            var gate = new UniTaskCompletionSource();
            var cancelledSeen = false;

            scope.Run(5, (value, ct) =>
            {
                ct.Register(() =>
                {
                    cancelledSeen = true;
                    gate.TrySetCanceled(ct);
                });
                return gate.Task;
            });

            scope.End();

            Assert.That(cancelledSeen, Is.True);
            Assert.That(_rig.Errors, Is.Empty);
        }

        // Null arguments throw synchronously

        [Test]
        public void NullArguments_Throw_ArgumentNullException_AtTheCall()
        {
            var scope = _rig.NewScope();

            Assert.Throws<ArgumentNullException>(() => scope.Bind((UnityEvent)null, () => { }));
            Assert.Throws<ArgumentNullException>(() => scope.Bind(new UnityEvent(), (UnityAction)null));
            Assert.Throws<ArgumentNullException>(() => scope.Bind((UnityEvent<int>)null, (UnityAction<int>)(v => { })));
            Assert.Throws<ArgumentNullException>(() => scope.Bind(new UnityEvent<int>(), (UnityAction<int>)null));
            Assert.Throws<ArgumentNullException>(() => scope.Subscribe((Action<Action>)null, h => { }, () => { }));
            Assert.Throws<ArgumentNullException>(() => scope.Subscribe(h => { }, (Action<Action>)null, () => { }));
            Assert.Throws<ArgumentNullException>(() => scope.Subscribe(h => { }, h => { }, (Action)null));
            Assert.Throws<ArgumentNullException>(() => scope.Add(null));
            Assert.Throws<ArgumentNullException>(() => scope.OnEnd((Action)null));
            Assert.Throws<ArgumentNullException>(() => scope.OnEnd<int>(0, null));
            Assert.Throws<ArgumentNullException>(() => scope.Run((Func<CancellationToken, UniTask>)null));
            Assert.Throws<ArgumentNullException>(() => scope.Run<int>(0, null));
        }

        private void CountCall()
        {
            _calls++;
        }
    }
}
