using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector.Tests
{
    internal enum InvariantStepKind
    {
        OpenScreen,
        OpenPopup,
        OpenSystem,
        OpenHud,
        ShowAsync,
        WaitForClose,
        ViewComplete,
        ViewClose,
        HandleClose,
        Back,
        CloseTo,
        CloseAll,
        OwnerCancel,
        InstanceLost,
        LoadCompletes,
        LoadFails,
        ClockAdvance,
        TransitionCompletes,
        HoldAdd,
        HoldRemove,
        Enqueue,
        QueueHoldAdd,
        QueueHoldRemove,
        GateToggle,
        QueueTick,
        QueueClear,
        QueueSettle
    }

    // What one seeded run found; the step log has one entry per step, so a step number is an index into it.
    internal sealed class InvariantRunResult
    {
        public int Seed;
        public int StepsRun;
        public bool Aborted;
        public readonly List<string> StepLog = new List<string>();
        public IReadOnlyList<InvariantViolation> Violations = new List<InvariantViolation>();
        public int FirstViolationStep = -1;
        public string StateAtFirstViolation = string.Empty;
        public int Requested;
        public int Shown;
        public int Closed;
        public int NotShown;

        // Queue activity, so a passing run shows the queue steps actually ran.
        public int Enqueued;
        public int QueuePresented;
        public int QueueNotShown;
        public int QueueDenied;
        public int QueueCancelled;
        public int QueueRootGone;
        public int QueueOwnerEnded;

        // Waiting items the root's end ended as RootGone while the queue's lifetime was a source of its own.
        public int QueueRootGoneOwnLifetime;

        // Waiting items the lifetime's end ended as Cancelled while the root was alive.
        public int QueueCancelledRootAlive;

        public bool Passed
        {
            get { return Violations.Count == 0; }
        }

        // The violations, the steps up to the given one (default: the first violation) and, for the default, the state at that moment.
        public string Describe(int upToStep = -1)
        {
            var text = new StringBuilder();
            text.Append("Seed ").Append(Seed).Append(": ").Append(Violations.Count)
                .Append(" invariant violation(s) in ").Append(StepsRun).Append(" steps.\n");

            for (var i = 0; i < Violations.Count; i++)
                text.Append("  ").Append(Violations[i]).Append('\n');

            var target = upToStep >= 0 ? upToStep : FirstViolationStep;
            var last = target >= 0 && target < StepLog.Count ? target : StepLog.Count - 1;
            text.Append("Steps 0 to ").Append(last).Append(" (replay with seed ").Append(Seed).Append("):\n");
            for (var i = 0; i <= last; i++)
                text.Append("  ").Append(i).Append(": ").Append(StepLog[i]).Append('\n');

            if (upToStep < 0 && StateAtFirstViolation.Length > 0)
                text.Append("State at the first violation:\n").Append(StateAtFirstViolation);

            return text.ToString();
        }
    }

    // One seeded sequence: random steps against a manual stage, every per-step invariant after each step,
    // then drain, destroy the root and the end-of-run invariants.
    internal static class InvariantRun
    {
        public static InvariantRunResult Execute(int seed, int stepCount = 200, InvariantOptions options = null)
        {
            var result = new InvariantRunResult { Seed = seed };

            var world = new InvariantWorld(seed);
            var checker = new InvariantChecker(world, options);
            var driver = new InvariantDriver(world, checker, new Random(seed), result);

            try
            {
                for (var i = 0; i < stepCount && !checker.HasModelFailure; i++)
                    driver.RunStep();

                if (!checker.HasModelFailure)
                    driver.Finish();
            }
            catch (Exception exception)
            {
                result.Aborted = true;
                var failedStep = result.StepLog.Count - 1;
                world.CurrentStep = failedStep;
                checker.Report("X.exception", "Step " + failedStep + " threw " + exception);
                driver.Note();
            }

            result.StepsRun = result.StepLog.Count;
            result.Requested = world.Harness.Observer.CountOf(ViewEventKind.Requested);
            result.Shown = world.Harness.Observer.CountOf(ViewEventKind.Shown);
            result.Closed = world.Harness.Observer.CountOf(ViewEventKind.Closed);
            result.NotShown = world.Harness.Observer.CountOf(ViewEventKind.NotShown);
            foreach (var pair in world.QueueItems)
            {
                result.Enqueued++;
                if (pair.Value.ReachedNavigator)
                    result.QueuePresented++;
                else if (pair.Value.TerminalFromQueue)
                {
                    result.QueueNotShown++;
                    if (pair.Value.TerminalRejected == RejectReason.Denied)
                        result.QueueDenied++;
                    else if (pair.Value.TerminalRejected == RejectReason.Cancelled)
                        result.QueueCancelled++;
                    else if (pair.Value.TerminalRejected == RejectReason.RootGone)
                        result.QueueRootGone++;
                    else if (pair.Value.TerminalRejected == RejectReason.OwnerEnded)
                        result.QueueOwnerEnded++;
                }
            }

            try
            {
                world.Dispose();
            }
            catch (Exception exception)
            {
                checker.Report("X.dispose", "Disposing the harness threw " + exception);
            }

            result.Violations = checker.Violations;
            return result;
        }
    }

    // Draws and executes the steps; it never decides what is right, the checker does.
    internal sealed class InvariantDriver
    {
        private static readonly InvariantStepKind[] StepKinds =
        {
            InvariantStepKind.OpenScreen, InvariantStepKind.OpenPopup, InvariantStepKind.OpenSystem, InvariantStepKind.OpenHud,
            InvariantStepKind.ShowAsync, InvariantStepKind.WaitForClose, InvariantStepKind.ViewComplete, InvariantStepKind.ViewClose,
            InvariantStepKind.HandleClose, InvariantStepKind.Back, InvariantStepKind.CloseTo, InvariantStepKind.CloseAll,
            InvariantStepKind.OwnerCancel, InvariantStepKind.InstanceLost, InvariantStepKind.LoadCompletes, InvariantStepKind.LoadFails,
            InvariantStepKind.ClockAdvance, InvariantStepKind.TransitionCompletes, InvariantStepKind.HoldAdd, InvariantStepKind.HoldRemove,
            InvariantStepKind.Enqueue, InvariantStepKind.QueueHoldAdd, InvariantStepKind.QueueHoldRemove, InvariantStepKind.GateToggle,
            InvariantStepKind.QueueTick, InvariantStepKind.QueueClear, InvariantStepKind.QueueSettle
        };

        // The queue steps take about a sixth of the draws, so the navigator-only coverage stays close to what it was.
        // QueueSettle gives the queue moments with no load or transition pending, or it would rarely find its layer clear.
        private static readonly int[] StepWeights = { 8, 8, 3, 3, 8, 3, 6, 4, 5, 6, 3, 2, 3, 4, 12, 3, 4, 12, 3, 3, 6, 2, 2, 2, 6, 1, 2 };

        private static readonly int TotalWeight = SumWeights();

        // Ties are common on purpose, so enqueue order decides often.
        private static readonly int[] Priorities = { -1, 0, 0, 0, 1, 1, 5, 100 };

        private const int MaxHandles = 24;
        private const int MaxLoggedEvents = 6;

        private readonly InvariantWorld _world;
        private readonly InvariantChecker _checker;
        private readonly Random _rng;
        private readonly InvariantRunResult _result;
        private readonly List<string> _log;
        private readonly List<CancellationTokenSource> _waiters = new List<CancellationTokenSource>();
        private int _awaiterNumber;
        private int _holdNumber;
        private int _enqueueNumber;
        private int _queueHoldNumber;
        private int _gateNumber;

        // True once the finish ended the queue's lifetime while the root was alive.
        private bool _lifetimeEndedEarly;

        public InvariantDriver(InvariantWorld world, InvariantChecker checker, Random rng, InvariantRunResult result)
        {
            _world = world;
            _checker = checker;
            _rng = rng;
            _result = result;
            _log = result.StepLog;
        }

        private Navigator Nav
        {
            get { return _world.Navigator; }
        }

        private FakeStage Stage
        {
            get { return _world.Stage; }
        }

        // Remembers the state at the first violation, for the report.
        public void Note()
        {
            if (_result.FirstViolationStep >= 0 || !_checker.HasViolations)
                return;

            _result.FirstViolationStep = _world.CurrentStep;
            _result.StateAtFirstViolation = _checker.DescribeForReport();
        }

        public void RunStep()
        {
            _world.CurrentStep = _log.Count;
            _checker.SyncStage();

            // The entry exists before the step runs, so a step that throws is still in the list.
            var kind = DrawKind();
            var index = _log.Count;
            _log.Add(kind + " (did not finish)");

            var text = Execute(kind);
            _log[index] = kind + " " + text;

            _checker.CheckStep();
            Note();
        }

        // Drains, closes everything, destroys the root, and checks what must hold at the end.
        public void Finish()
        {
            var drain = _rng.NextDouble() < 0.5;

            // Own-lifetime seeds, plain or linked, sometimes end the lifetime while the root is alive, so a presented view outlives it.
            var endLifetimeEarly = !_world.QueueOnRootToken && _rng.NextDouble() < 0.5;

            if (drain)
            {
                FinishStep("finish: drain every load, transition, hold and queue tick", () =>
                {
                    Drain(true);
                    return string.Empty;
                });
                _checker.CheckStep();
                _checker.CheckProgressFinal();
                Note();

                if (endLifetimeEarly)
                    EndLifetimeWhileRootAlive();

                FinishStep("finish: the navigator is settled at rest", () =>
                {
                    if (Nav.IsBusy)
                        _checker.Report("X.drain", "The navigator is still busy after every load and transition completed and every hold ended.");
                    return string.Empty;
                });

                // The queue is not ticked here, so it cannot present into the layers this phase empties.
                FinishStep("finish: CloseAll on every layer, then drain", () =>
                {
                    for (var i = 0; i < _world.Layers.Count; i++)
                        Nav.CloseAll(_world.Layers[i]);

                    Drain(false);
                    return string.Empty;
                });
                _checker.CheckStep();
                Note();

                FinishStep("finish: every layer is empty after CloseAll", () =>
                {
                    for (var i = 0; i < _world.Layers.Count; i++)
                    {
                        var count = Nav.RequestCount(_world.Layers[i]);
                        if (count != 0)
                            _checker.Report("X.closeall", "Layer '" + _world.Layers[i].Id + "' still holds " + count + " request(s) after CloseAll and a full drain.");
                    }

                    return string.Empty;
                });
            }
            else if (endLifetimeEarly)
            {
                EndLifetimeWhileRootAlive();
            }

            // Destroying the root ends the queue whatever its lifetime; after an earlier lifetime end nothing waits any more.
            var rootMark = _world.MarkQueue();
            FinishStep(drain ? "finish: destroy the root" : "finish: destroy the root with work in flight", () =>
            {
                Nav.Teardown();
                return string.Empty;
            });
            _checker.CheckLifetimeEnded(rootMark, false, RejectReason.RootGone);
            if (!_world.QueueOnRootToken)
                _result.QueueRootGoneOwnLifetime += CountEndedBy(rootMark, RejectReason.RootGone);
            Note();

            // Q5: once every hold owner has ended the queue holds nothing; the tick shows nothing is presented without a root.
            FinishStep("finish: every queue hold owner ends, then the queue ticks", () =>
            {
                var cancelled = 0;
                for (var i = 0; i < _world.QueueHolds.Count; i++)
                {
                    var owner = _world.QueueHolds[i].Owner;
                    if (owner != null && !owner.IsCancellationRequested)
                    {
                        owner.Cancel();
                        cancelled++;
                    }
                }

                _world.TickUntilIdle();
                return " (" + cancelled + " owner(s) cancelled, queue lifetime " + _world.QueueLifetimeKind + ")";
            });
            _checker.CheckQueueHoldsFinal();
            Note();

            // Queue lifetime: the root's end already ended every waiting item, so this end finds nothing waiting.
            var lifetimeMark = _world.MarkQueue();
            FinishStep("finish: the queue's lifetime ends", () =>
            {
                var waiting = _world.Queue.Count;
                _world.EndQueueLifetime();
                _world.TickUntilIdle();
                _world.Harness.MainThread.Flush();
                return " (" + waiting + " item(s) were waiting)";
            });
            _checker.CheckLifetimeEnded(lifetimeMark, false, RejectReason.Cancelled);
            _checker.CheckAfterTeardown();
            Note();

            FinishStep("finish: calls after root destruction", () =>
            {
                ProbeAfterTeardown();
                return string.Empty;
            });

            FinishStep("finish: late results of cancelled loads and transitions", () =>
            {
                CompleteStale();
                return string.Empty;
            });
            _checker.CheckInstancesFinal();
            Note();
        }

        private void FinishStep(string label, Func<string> action)
        {
            _world.CurrentStep = _log.Count;
            _checker.SyncStage();

            var index = _log.Count;
            _log.Add(label + " (did not finish)");

            var text = action();
            _log[index] = label + text;
        }

        // Queue lifetime: waiting items end as NotShown / Cancelled; a presented view stays until its own owner or a close ends it.
        private void EndLifetimeWhileRootAlive()
        {
            var mark = _world.MarkQueue();
            FinishStep("finish: the queue's lifetime ends while the root is alive", () =>
            {
                var waiting = _world.Queue.Count;
                _world.EndQueueLifetime();
                _world.TickUntilIdle();
                _world.Harness.MainThread.Flush();
                return " (" + waiting + " item(s) were waiting, " + mark.Presented.Count + " presented)";
            });
            _lifetimeEndedEarly = true;
            _checker.CheckLifetimeEnded(mark, true, RejectReason.Cancelled);
            _result.QueueCancelledRootAlive += CountEndedBy(mark, RejectReason.Cancelled);
            _checker.CheckStep();
            Note();
        }

        // Waiting items of the mark that a queue NotShown with the reason ended after the mark.
        private static int CountEndedBy(InvariantQueueMark mark, RejectReason reason)
        {
            var count = 0;
            for (var i = 0; i < mark.Waiting.Count; i++)
            {
                var item = mark.Waiting[i];
                if (item.TerminalIndex >= mark.EventIndex && item.TerminalFromQueue
                    && item.TerminalKind == ViewEventKind.NotShown && item.TerminalRejected == reason)
                    count++;
            }

            return count;
        }

        // Steps

        private string Execute(InvariantStepKind kind)
        {
            switch (kind)
            {
                case InvariantStepKind.OpenScreen:
                    return OpenOn(ViewLayers.Screens);
                case InvariantStepKind.OpenPopup:
                    return OpenOn(ViewLayers.Popups);
                case InvariantStepKind.OpenSystem:
                    return OpenOn(ViewLayers.System);
                case InvariantStepKind.OpenHud:
                    return OpenOn(InvariantWorld.Hud);
                case InvariantStepKind.ShowAsync:
                    return ShowAsyncStep();
                case InvariantStepKind.WaitForClose:
                    return WaitForCloseStep();
                case InvariantStepKind.ViewComplete:
                    return ViewStep(true);
                case InvariantStepKind.ViewClose:
                    return ViewStep(false);
                case InvariantStepKind.HandleClose:
                    return HandleCloseStep();
                case InvariantStepKind.Back:
                    return "-> " + Nav.Back();
                case InvariantStepKind.CloseTo:
                    return CloseToStep();
                case InvariantStepKind.CloseAll:
                    return CloseAllStep();
                case InvariantStepKind.OwnerCancel:
                    return OwnerCancelStep();
                case InvariantStepKind.InstanceLost:
                    return InstanceLostStep();
                case InvariantStepKind.LoadCompletes:
                    return LoadStep(false);
                case InvariantStepKind.LoadFails:
                    return LoadStep(true);
                case InvariantStepKind.ClockAdvance:
                    return ClockStep();
                case InvariantStepKind.TransitionCompletes:
                    return TransitionStep();
                case InvariantStepKind.HoldAdd:
                    return HoldAddStep();
                case InvariantStepKind.HoldRemove:
                    return HoldRemoveStep();
                case InvariantStepKind.Enqueue:
                    return EnqueueStep();
                case InvariantStepKind.QueueHoldAdd:
                    return QueueHoldAddStep();
                case InvariantStepKind.QueueHoldRemove:
                    return QueueHoldRemoveStep();
                case InvariantStepKind.GateToggle:
                    return GateToggleStep();
                case InvariantStepKind.QueueTick:
                    return QueueTickStep();
                case InvariantStepKind.QueueClear:
                    return QueueClearStep();
                case InvariantStepKind.QueueSettle:
                    return QueueSettleStep();
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown step kind.");
            }
        }

        private string OpenOn(ViewLayer layer)
        {
            var row = PickRow(layer);
            var slot = PickOwnerSlot();
            var options = PickOptions();
            var token = _world.OwnerToken(slot);

            ViewHandle handle;
            var argument = _rng.Next(3);
            if (row.TakesArgs)
                handle = Nav.Open(ViewKey.Create<int>(row.Id), argument, token, options);
            else
                handle = Nav.Open(ViewKey.Create(row.Id), default(NoArgs), token, options);

            if (handle.RequestId != 0)
                AddHandle(handle);

            return "Open " + row + (row.TakesArgs ? "(" + argument + ")" : string.Empty) + Describe(slot, options) + " -> #" + handle.RequestId;
        }

        private string ShowAsyncStep()
        {
            var row = _world.Rows[_rng.Next(_world.Rows.Count)];
            var slot = PickOwnerSlot();
            var options = PickOptions();
            var token = _world.OwnerToken(slot);
            var argument = _rng.Next(3);
            var before = _world.Harness.Observer.Count;

            UniTask<ViewResult<NoResult>> task;
            if (row.TakesArgs)
                task = Nav.ShowAsync(ViewKey.Create<int>(row.Id), argument, token, options);
            else
                task = Nav.ShowAsync(ViewKey.Create(row.Id), default(NoArgs), token, options);

            var awaiter = new InvariantAwaiter(++_awaiterNumber, row.Id, false, null);
            awaiter.Show = task;
            awaiter.RequestId = BindRequest(row.Id, before, task.Status);
            _world.Awaiters.Add(awaiter);

            // The request is also reachable through a handle, so handle steps cover requests that came from ShowAsync.
            ViewHandle opened;
            if (awaiter.RequestId != 0 && Nav.TryGetOpen(row.Id, out opened) && opened.RequestId == awaiter.RequestId)
                AddHandle(opened);

            return "ShowAsync " + row + (row.TakesArgs ? "(" + argument + ")" : string.Empty) + Describe(slot, options) + " -> " + awaiter + " " + task.Status;
        }

        private void AddHandle(ViewHandle handle)
        {
            _world.Handles.Add(handle);
            if (_world.Handles.Count > MaxHandles)
                _world.Handles.RemoveAt(0);
        }

        // The request a new ShowAsync belongs to: the Requested event it caused, or the request it joined.
        private int BindRequest(string viewId, int eventsBefore, UniTaskStatus status)
        {
            var events = _world.Harness.Observer.Events;
            for (var i = eventsBefore; i < events.Count; i++)
            {
                if (events[i].Kind == ViewEventKind.Requested && string.Equals(events[i].ViewId, viewId, StringComparison.Ordinal))
                    return events[i].RequestId;
            }

            if (status != UniTaskStatus.Pending)
                return 0;

            ViewHandle handle;
            return Nav.TryGetOpen(viewId, out handle) ? handle.RequestId : 0;
        }

        private string WaitForCloseStep()
        {
            if (_world.Handles.Count == 0)
                return "(no handle yet)";

            var handle = _world.Handles[_rng.Next(_world.Handles.Count)];
            CancellationTokenSource waiter = null;
            var token = CancellationToken.None;
            if (_rng.NextDouble() < 0.5)
            {
                waiter = _world.Harness.NewOwner();
                _waiters.Add(waiter);
                token = waiter.Token;
            }

            var awaiter = new InvariantAwaiter(++_awaiterNumber, handle.ViewId, true, waiter);
            awaiter.Wait = handle.WaitForCloseAsync(token);
            awaiter.RequestId = handle.RequestId;
            _world.Awaiters.Add(awaiter);

            return "WaitForClose #" + handle.RequestId + " (" + handle.Phase + ")" + (waiter != null ? " with a waiter token" : string.Empty)
                + " -> " + awaiter.Wait.Status;
        }

        private string ViewStep(bool complete)
        {
            var view = PickView();
            if (view == null)
                return "(no view has opened yet)";

            var handle = view.Context.Handle;
            var description = view.ViewId + " #" + handle.RequestId + " (" + handle.Phase + ")";
            if (complete)
                view.Context.Complete(default(NoResult));
            else
                view.Context.Close();

            return (complete ? "Complete " : "Close ") + description;
        }

        private string HandleCloseStep()
        {
            if (_world.Handles.Count == 0)
                return "(no handle yet)";

            var handle = _world.Handles[_rng.Next(_world.Handles.Count)];
            var description = handle.ViewId + " #" + handle.RequestId + " (" + handle.Phase + ")";
            handle.Close();
            return description;
        }

        private string CloseToStep()
        {
            string id;
            if (_rng.NextDouble() < 0.75)
            {
                var row = PickRow(ViewLayers.Screens);
                id = row.Id;
            }
            else
            {
                id = _world.Rows[_rng.Next(_world.Rows.Count)].Id;
            }

            return id + " -> " + Nav.CloseTo(id);
        }

        private string CloseAllStep()
        {
            var layer = _world.Layers[_rng.Next(_world.Layers.Count)];
            Nav.CloseAll(layer);
            return layer.Id;
        }

        private string OwnerCancelStep()
        {
            if (_waiters.Count > 0 && _rng.NextDouble() < 0.2)
            {
                var index = _rng.Next(_waiters.Count);
                _waiters[index].Cancel();
                return "waiter token " + index;
            }

            var slot = 1 + _rng.Next(_world.Owners.Count - 1);
            var name = _world.OwnerName(slot);
            var owner = _world.Owners[slot];
            var mark = _world.MarkQueue(owner);
            owner.Cancel();
            _checker.CheckOwnerEnded(mark);

            var replaced = _rng.NextDouble() < 0.7;
            if (replaced)
                _world.Owners[slot] = _world.Harness.NewOwner();

            return name + (replaced ? ", replaced by a fresh owner" : ", kept cancelled");
        }

        private string InstanceLostStep()
        {
            var candidates = new List<FakeInstance>();
            candidates.AddRange(Stage.LiveInstances);
            var live = candidates.Count;
            candidates.AddRange(Stage.StashedInstances);

            if (candidates.Count == 0)
                return "(no instance)";

            var index = _rng.Next(candidates.Count);
            var instance = candidates[index];
            var destroyed = index >= live || _rng.NextDouble() < 0.5;
            Stage.RaiseInstanceLost(instance, destroyed);
            return (destroyed ? "destroy " : "deactivate ") + instance + (index >= live ? " (kept)" : string.Empty);
        }

        private string LoadStep(bool fail)
        {
            var pending = _checker.PendingAcquires;
            if (pending.Count == 0)
                return "(no load pending)";

            var index = PickPending(pending);
            var call = pending[index];
            var cancelled = call.Token.IsCancellationRequested;

            if (fail)
                Stage.FailAcquire(new InvariantInjectedException("load of " + call.ViewId), index);
            else
                Stage.CompleteAcquire(index);

            _checker.AcquireTaken(index);
            return call.ViewId + (cancelled ? " (already cancelled)" : string.Empty);
        }

        private string TransitionStep()
        {
            var pending = _checker.PendingPlays;
            if (pending.Count == 0)
                return "(no transition pending)";

            var index = PickPending(pending);
            var call = pending[index];
            var cancelled = call.Token.IsCancellationRequested;

            Stage.CompletePlay(index);
            _checker.PlayTaken(index);
            return call.Instance + (call.On ? " show" : " hide") + (cancelled ? " (already cancelled)" : string.Empty);
        }

        private string ClockStep()
        {
            var roll = _rng.NextDouble();
            float seconds;
            if (roll < 0.30)
                seconds = 0.2f;
            else if (roll < 0.50)
                seconds = 0.5f;
            else if (roll < 0.75)
                seconds = 3.2f;
            else if (roll < 0.90)
                seconds = 15.5f;
            else
                seconds = 31f;

            _world.Harness.Advance(seconds);

            // The queue's clock moves with the navigator's, so the long-hold timer runs too.
            _world.Ticker.Advance(seconds);
            return seconds.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + " s";
        }

        private string HoldAddStep()
        {
            var slot = PickOwnerSlot();
            var number = ++_holdNumber;
            var registration = Nav.HoldInput("hold" + number, _world.OwnerToken(slot));
            _world.Holds.Add(new InvariantHold(number, registration, _world.Owners[slot], false));
            return "hold" + number + " owner=" + _world.OwnerName(slot) + " active=" + registration.IsActive;
        }

        private string HoldRemoveStep()
        {
            if (_world.Holds.Count == 0)
                return "(no hold yet)";

            var hold = _world.Holds[_rng.Next(_world.Holds.Count)];
            var wasLive = hold.ModelLive;
            hold.Registration.Remove();
            hold.Removed = true;
            return "hold" + hold.Number + (wasLive ? string.Empty : " (already over)");
        }

        // Queue steps

        private string EnqueueStep()
        {
            var row = PickRow(ViewLayers.Popups);
            var slot = PickOwnerSlot();
            var token = _world.OwnerToken(slot);
            var priority = Priorities[_rng.Next(Priorities.Length)];
            var isAsync = _rng.NextDouble() < 0.5;
            var number = ++_enqueueNumber;
            var options = new QueueOptions { Priority = priority, Source = "q" + number };
            var key = ViewKey.Create(row.Id);
            var before = _world.Events.Count;

            var call = new InvariantEnqueueCall(number, row.Id, priority, isAsync);
            call.Source = options.Source;
            call.Owner = _world.Owners[slot];
            call.OwnerCancelledAtCall = token.IsCancellationRequested;
            _world.EnqueueCalls.Add(call);

            // The probe binds the first queue event for a new id during the call to this call: Enqueued, or NotShown when rejected.
            var task = default(UniTask<ViewResult<NoResult>>);
            _world.PendingEnqueue = call;
            try
            {
                if (isAsync)
                    task = _world.Queue.EnqueueAsync(key, default(NoArgs), token, options);
                else
                    call.Returned = _world.Queue.Enqueue(key, default(NoArgs), token, options);
            }
            finally
            {
                _world.PendingEnqueue = null;
            }

            var first = call.RequestId != 0 ? FirstQueueEventFor(call.RequestId, before) : -1;
            string outcome;
            if (first < 0)
            {
                call.RequestId = call.Returned;
                outcome = "no queue event";
                _world.ReportFromView("X.enqueue", (isAsync ? "EnqueueAsync" : "Enqueue") + " of '" + row.Id + "' published neither Enqueued nor NotShown.");
            }
            else
            {
                var e = _world.Events[first].Event;
                outcome = e.Kind + (e.Kind == ViewEventKind.NotShown ? "(" + e.Rejected + ")" : string.Empty);
                if (!isAsync && call.Returned != e.RequestId)
                    _world.ReportFromView("X.enqueue", "Enqueue returned #" + call.Returned + " but its first queue event carries #" + e.RequestId + ".");
            }

            var text = (isAsync ? "EnqueueAsync " : "Enqueue ") + row + " priority=" + priority + " owner=" + _world.OwnerName(slot)
                + " -> #" + call.RequestId + " " + outcome;

            if (isAsync)
            {
                var awaiter = new InvariantAwaiter(++_awaiterNumber, row.Id, false, null);
                awaiter.IsEnqueue = true;
                awaiter.Show = task;
                awaiter.RequestId = call.RequestId;
                _world.Awaiters.Add(awaiter);
                call.Awaiter = awaiter;
                text += " " + awaiter + " " + task.Status;
            }

            _checker.CheckEnqueueCall(call, before);
            return text + QueueEventsSince(before);
        }

        // Half the holds share an owner slot that OwnerCancel ends; the others get an owner of their own.
        private string QueueHoldAddStep()
        {
            var number = ++_queueHoldNumber;
            CancellationTokenSource owner;
            string ownerName;
            var dedicated = _rng.NextDouble() < 0.5;
            if (dedicated)
            {
                owner = _world.Harness.NewOwner();
                ownerName = "own";
            }
            else
            {
                var slot = 1 + _rng.Next(_world.Owners.Count - 1);
                owner = _world.Owners[slot];
                ownerName = _world.OwnerName(slot);
            }

            var registration = _world.Queue.Hold("qhold" + number, owner.Token);
            var hold = new InvariantHold(number, registration, owner, false);
            hold.Dedicated = dedicated;
            _world.QueueHolds.Add(hold);

            return "qhold" + number + " owner=" + ownerName + " active=" + registration.IsActive + " held=" + _world.Queue.IsHeld;
        }

        private string QueueHoldRemoveStep()
        {
            if (_world.QueueHolds.Count == 0)
                return "(no queue hold yet)";

            var hold = _world.QueueHolds[_rng.Next(_world.QueueHolds.Count)];
            var wasLive = _world.IsQueueHoldLive(hold);
            var text = "qhold" + hold.Number + (wasLive ? string.Empty : " (already over)");

            if (hold.Dedicated && !hold.Owner.IsCancellationRequested && _rng.NextDouble() < 0.4)
            {
                hold.Owner.Cancel();
                text += " by cancelling its owner";
            }
            else
            {
                hold.Registration.Remove();
                hold.Removed = true;
            }

            return text + " held=" + _world.Queue.IsHeld;
        }

        private string GateToggleStep()
        {
            var gates = _world.Gates;
            if (gates.Count > 0 && _rng.NextDouble() < 0.5)
            {
                var index = _rng.Next(gates.Count);
                var gate = gates[index];
                var wasActive = gate.Registration.IsActive;
                gate.Registration.Remove();
                gate.Removed = true;
                gates.RemoveAt(index);
                return "remove " + gate + (wasActive ? string.Empty : " (already ended)");
            }

            var denied = new List<string>();
            for (var i = 0; i < _world.Rows.Count; i++)
            {
                var row = _world.Rows[i];
                if (ReferenceEquals(row.Layer, ViewLayers.Popups) && _rng.NextDouble() < 0.35)
                    denied.Add(row.Id);
            }

            var mode = _rng.Next(3);
            var throws = _rng.NextDouble() < 0.2;
            var order = _rng.Next(4);
            var slot = PickOwnerSlot();

            var added = new InvariantToggleGate(_world, ++_gateNumber, denied, mode != 2, mode != 1, throws, order, _world.Owners[slot]);
            added.Registration = _world.Queue.AddGate(added, _world.OwnerToken(slot), order);
            gates.Add(added);
            _world.AllGates.Add(added);

            return "add " + added + " order=" + order + " owner=" + _world.OwnerName(slot) + " active=" + added.Registration.IsActive;
        }

        private string QueueTickStep()
        {
            var pending = _world.Ticker.PendingTicks;
            var waiting = _world.Queue.Count;
            var held = _world.Queue.IsHeld;
            var before = _world.Events.Count;

            _world.Tick();

            return pending + " pending, waiting " + waiting + " -> " + _world.Queue.Count + (held ? " (held)" : string.Empty) + QueueEventsSince(before);
        }

        // Finishes every pending load and transition and ticks the queue; input holds and open views stay as they are.
        private string QueueSettleStep()
        {
            var waiting = _world.Queue.Count;
            var before = _world.Events.Count;

            Drain(true, false);

            return "waiting " + waiting + " -> " + _world.Queue.Count + QueueEventsSince(before);
        }

        private string QueueClearStep()
        {
            var waiting = _world.Queue.Count;
            var before = _world.Events.Count;
            var mark = _world.MarkQueue();

            _world.Queue.Clear();
            _checker.CheckCleared(mark);

            return "waiting " + waiting + " -> " + _world.Queue.Count + QueueEventsSince(before);
        }

        private int FirstQueueEventFor(int requestId, int index)
        {
            var events = _world.Events;
            for (var i = index; i < events.Count; i++)
            {
                if (events[i].FromQueue && events[i].Event.RequestId == requestId)
                    return i;
            }

            return -1;
        }

        // The queue's events and the navigator's events for queued ids, for the step log.
        private string QueueEventsSince(int index)
        {
            var events = _world.Events;
            var text = new StringBuilder();
            var shown = 0;
            for (var i = index; i < events.Count; i++)
            {
                var e = events[i].Event;
                if (!events[i].FromQueue && (e.Kind == ViewEventKind.Settled || !_world.QueueItems.ContainsKey(e.RequestId)))
                    continue;

                if (shown == MaxLoggedEvents)
                {
                    text.Append(" ...");
                    break;
                }

                text.Append(shown == 0 ? " [" : ", ").Append(events[i].FromQueue ? "queue " : string.Empty).Append(e.Kind)
                    .Append(" #").Append(e.RequestId);
                if (e.Kind == ViewEventKind.NotShown)
                    text.Append('(').Append(e.Rejected).Append(')');
                shown++;
            }

            if (shown > 0)
                text.Append(']');

            return text.ToString();
        }

        // Finish

        // Completes every pending load and transition, oldest first, until the stage holds none; input holds end first.
        // With tickQueue the queue also ticks between them, so it presents what it may; queue holds stay.
        private void Drain(bool tickQueue, bool removeHolds = true)
        {
            for (var i = 0; removeHolds && i < _world.Holds.Count; i++)
            {
                _world.Holds[i].Registration.Remove();
                _world.Holds[i].Removed = true;
            }

            // Work posted to the main thread may schedule more, so the loop runs again until a flush finds nothing.
            for (var round = 0; round < 10; round++)
            {
                for (var guard = 0; guard < 2000; guard++)
                {
                    _checker.SyncStage();

                    if (_checker.PendingAcquires.Count > 0)
                    {
                        Stage.CompleteAcquire(0);
                        _checker.AcquireTaken(0);
                        continue;
                    }

                    if (_checker.PendingPlays.Count > 0)
                    {
                        Stage.CompletePlay(0);
                        _checker.PlayTaken(0);
                        continue;
                    }

                    if (tickQueue && _world.Ticker.PendingTicks > 0)
                    {
                        _world.Tick();
                        continue;
                    }

                    break;
                }

                if (_world.Harness.MainThread.Flush() == 0)
                    break;
            }

            _checker.SyncStage();
        }

        // Late results of loads and transitions that were cancelled: the stage releases a late instance itself.
        private void CompleteStale()
        {
            for (var guard = 0; guard < 2000; guard++)
            {
                _checker.SyncStage();

                if (_checker.PendingAcquires.Count > 0)
                {
                    Stage.CompleteAcquire(0);
                    _checker.AcquireTaken(0);
                    continue;
                }

                if (_checker.PendingPlays.Count > 0)
                {
                    Stage.CompletePlay(0);
                    _checker.PlayTaken(0);
                    continue;
                }

                break;
            }

            _world.Harness.MainThread.Flush();
            _checker.SyncStage();
        }

        // With the root gone every open is refused and Back finds nothing.
        private void ProbeAfterTeardown()
        {
            var token = _world.Harness.Owner;

            var task = Nav.ShowAsync(ViewKey.Create("home"), default(NoArgs), token);
            if (task.Status != UniTaskStatus.Succeeded)
            {
                _checker.Report("I11", "ShowAsync after root destruction did not complete at once (" + task.Status + ").");
            }
            else
            {
                var result = task.GetAwaiter().GetResult();
                if (result.WasShown || result.Reason != CloseReason.NotShown || result.Rejected != RejectReason.RootGone)
                    _checker.Report("I11", "ShowAsync after root destruction gave (shown " + result.WasShown + ", " + result.Reason + ", " + result.Rejected + "), not NotShown / RootGone.");
            }

            var handle = Nav.Open(ViewKey.Create("home"), default(NoArgs), token);
            if (handle.IsAlive)
                _checker.Report("I11", "Open after root destruction returned a live handle.");

            if (Nav.Back())
                _checker.Report("I11", "Back() after root destruction returned true.");

            if (Nav.CloseTo("home"))
                _checker.Report("I11", "CloseTo after root destruction returned true.");

            Nav.CloseAll(ViewLayers.Screens);

            if (Nav.HoldInput("late", token).IsActive)
                _checker.Report("I11", "HoldInput after root destruction returned an active registration.");

            if (Nav.ShieldCount != 0)
                _checker.Report("I11", "The shield count is " + Nav.ShieldCount + " after calls on a destroyed root.");

            if (Nav.RequestCount(ViewLayers.Screens) != 0 || Nav.RequestCount(ViewLayers.Popups) != 0)
                _checker.Report("I11", "A call on a destroyed root left a request in a layer list.");

            ProbeEndedQueue(token);
        }

        // An ended queue is inert: an enqueue completes at once with the first end's reason, registrations come back inactive.
        private void ProbeEndedQueue(CancellationToken token)
        {
            var queue = _world.Queue;

            // The root ended first unless the finish ended the lifetime while the root was alive; that order's reason is not checked.
            var expected = _lifetimeEndedEarly ? RejectReason.None : RejectReason.RootGone;

            var task = queue.EnqueueAsync(ViewKey.Create("confirm"), default(NoArgs), token);
            if (task.Status != UniTaskStatus.Succeeded)
            {
                _checker.Report("X.inert", "EnqueueAsync on the ended queue did not complete at once (" + task.Status + ").");
            }
            else
            {
                var result = task.GetAwaiter().GetResult();
                var reasonWrong = expected != RejectReason.None
                    ? result.Rejected != expected
                    : result.Rejected != RejectReason.Cancelled && result.Rejected != RejectReason.RootGone;

                if (result.WasShown || result.Reason != CloseReason.NotShown || reasonWrong)
                {
                    _checker.Report("X.inert", "EnqueueAsync on the ended queue gave (shown " + result.WasShown + ", " + result.Reason + ", " + result.Rejected + "), not NotShown / "
                        + (expected != RejectReason.None ? expected.ToString() : "Cancelled or RootGone") + ".");
                }
            }

            if (queue.Count != 0)
                _checker.Report("X.inert", "The ended queue holds " + queue.Count + " waiting item(s) after a late enqueue.");

            if (queue.Hold("late", token).IsActive)
                _checker.Report("X.inert", "Hold on the ended queue returned an active registration.");

            if (queue.IsHeld)
                _checker.Report("X.inert", "The ended queue is held after a late Hold.");

            if (queue.AddGate(new InvariantRecordingGate(_world), token).IsActive)
                _checker.Report("X.inert", "AddGate on the ended queue returned an active registration.");

            if (queue.AddObserver(new InvariantQueueProbe(_world, true), token).IsActive)
                _checker.Report("X.inert", "AddObserver on the ended queue returned an active registration.");
        }

        // Drawing

        private InvariantStepKind DrawKind()
        {
            var roll = _rng.Next(TotalWeight);
            for (var i = 0; i < StepWeights.Length; i++)
            {
                roll -= StepWeights[i];
                if (roll < 0)
                    return StepKinds[i];
            }

            return StepKinds[StepKinds.Length - 1];
        }

        private static int SumWeights()
        {
            var total = 0;
            for (var i = 0; i < StepWeights.Length; i++)
                total += StepWeights[i];
            return total;
        }

        private InvariantRow PickRow(ViewLayer layer)
        {
            var candidates = new List<InvariantRow>();
            for (var i = 0; i < _world.Rows.Count; i++)
            {
                if (ReferenceEquals(_world.Rows[i].Layer, layer))
                    candidates.Add(_world.Rows[i]);
            }

            return candidates[_rng.Next(candidates.Count)];
        }

        private int PickOwnerSlot()
        {
            var roll = _rng.NextDouble();
            if (roll < 0.4)
                return 0;

            return 1 + _rng.Next(_world.Owners.Count - 1);
        }

        private OpenOptions PickOptions()
        {
            var options = default(OpenOptions);
            options.RejectWhenBusy = _rng.NextDouble() < 0.1;
            return options;
        }

        private InvariantViewBase PickView()
        {
            var any = new List<InvariantViewBase>();
            var live = new List<InvariantViewBase>();
            for (var i = 0; i < _world.Views.Count; i++)
            {
                var view = _world.Views[i];
                if (view.Context == null)
                    continue;

                any.Add(view);
                if (view.Context.Handle.IsAlive)
                    live.Add(view);
            }

            if (any.Count == 0)
                return null;

            if (live.Count > 0 && _rng.NextDouble() < 0.85)
                return live[_rng.Next(live.Count)];

            return any[_rng.Next(any.Count)];
        }

        // A live operation most of the time; sometimes a stale one whose token is already cancelled.
        private int PickPending(IReadOnlyList<StageCall> pending)
        {
            var live = new List<int>();
            for (var i = 0; i < pending.Count; i++)
            {
                if (!pending[i].Token.IsCancellationRequested)
                    live.Add(i);
            }

            if (live.Count > 0 && _rng.NextDouble() < 0.8)
                return live[_rng.Next(live.Count)];

            return _rng.Next(pending.Count);
        }

        private string Describe(int slot, OpenOptions options)
        {
            return " owner=" + _world.OwnerName(slot) + (options.RejectWhenBusy ? " rejectWhenBusy" : string.Empty);
        }
    }
}
