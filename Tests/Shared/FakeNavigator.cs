using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector.Tests
{
    // One recorded ShowAsync call; the test decides when and how it ends.
    internal abstract class FakeShowCall
    {
        protected FakeShowCall(string viewId, object args, CancellationToken owner, OpenOptions options, string member, int line)
        {
            ViewId = viewId;
            Args = args;
            Owner = owner;
            Options = options;
            Member = member;
            Line = line;
        }

        public string ViewId { get; }

        public object Args { get; }

        public CancellationToken Owner { get; }

        // Includes the internal Priority a queue sets.
        public OpenOptions Options { get; }

        public string Member { get; }

        public int Line { get; }

        public int RequestId
        {
            get { return Options.RequestId; }
        }

        public abstract Type ResultType { get; }

        public abstract bool IsCompleted { get; }

        // Shown, then closed with the reason and no value.
        public abstract void Close(CloseReason reason);

        // Ended without being shown.
        public abstract void NotShown(RejectReason reason, string detail = null);

        // The ShowAsync task faults, as a broken custom navigator's would.
        public abstract void Fault(Exception exception);

        // Shown, then closed with Complete(value); the value type must match the key's result type.
        public void Complete<TResult>(TResult value)
        {
            var typed = this as FakeShowCall<TResult>;
            if (typed == null)
                throw new InvalidOperationException("The call for '" + ViewId + "' has result type " + ResultType.Name + ", not " + typeof(TResult).Name + ".");

            typed.SetResult(new ViewResult<TResult>(RequestId, true, CloseReason.Completed, RejectReason.None, null, true, value));
        }
    }

    internal sealed class FakeShowCall<TResult> : FakeShowCall
    {
        private readonly UniTaskCompletionSource<ViewResult<TResult>> _completion = new UniTaskCompletionSource<ViewResult<TResult>>();
        private bool _completed;

        public FakeShowCall(string viewId, object args, CancellationToken owner, OpenOptions options, string member, int line)
            : base(viewId, args, owner, options, member, line)
        {
        }

        public UniTask<ViewResult<TResult>> Task
        {
            get { return _completion.Task; }
        }

        public override Type ResultType
        {
            get { return typeof(TResult); }
        }

        public override bool IsCompleted
        {
            get { return _completed; }
        }

        public override void Close(CloseReason reason)
        {
            SetResult(new ViewResult<TResult>(RequestId, reason != CloseReason.NotShown, reason, RejectReason.None, null, false, default(TResult)));
        }

        public override void NotShown(RejectReason reason, string detail = null)
        {
            SetResult(new ViewResult<TResult>(RequestId, false, CloseReason.NotShown, reason, detail, false, default(TResult)));
        }

        public override void Fault(Exception exception)
        {
            if (_completed)
                return;

            _completed = true;
            _completion.TrySetException(exception);
        }

        // A second end is ignored, like a real request that ends once.
        public void SetResult(ViewResult<TResult> result)
        {
            if (_completed)
                return;

            _completed = true;
            _completion.TrySetResult(result);
        }
    }

    // An INavigator that only records: ShowAsync calls wait for the test, layer state is set by the test.
    internal sealed class FakeNavigator : INavigator
    {
        private readonly List<FakeShowCall> _calls = new List<FakeShowCall>();
        private readonly List<ObserverSlot> _observers = new List<ObserverSlot>();
        private readonly Dictionary<ViewLayer, bool> _layerClear = new Dictionary<ViewLayer, bool>();
        private readonly CancellationTokenSource _root = new CancellationTokenSource();
        private readonly IMainThread _mainThread;
        private readonly Diagnostics _diagnostics;
        private int _lastRequestId;

        // The navigator's own main thread; registrations it hands out use it.
        public FakeNavigator(IMainThread mainThread = null)
        {
            _mainThread = mainThread ?? new FakeMainThread();
            _diagnostics = new Diagnostics();
        }

        public IReadOnlyList<FakeShowCall> Calls
        {
            get { return _calls; }
        }

        public FakeShowCall LastCall
        {
            get { return _calls.Count == 0 ? null : _calls[_calls.Count - 1]; }
        }

        // Ids handed out by ReserveRequestId so far.
        public int ReservedCount
        {
            get { return _lastRequestId; }
        }

        public int LastReservedId
        {
            get { return _lastRequestId; }
        }

        public int OpenCount { get; private set; }

        public int CloseAllCount { get; private set; }

        public int IsLayerClearCount { get; private set; }

        public int HoldInputCount { get; private set; }

        // When set, the next ShowAsync throws it synchronously without recording a call, then the property clears.
        public Exception ThrowOnNextShowAsync { get; set; }

        // Observers still registered.
        public int ObserverCount
        {
            get { return _observers.Count; }
        }

        public bool IsBusy { get; set; }

        public CancellationToken RootToken
        {
            get { return _root.Token; }
        }

        // A layer is clear unless set otherwise, and never while the navigator is busy.
        public void SetLayerClear(ViewLayer layer, bool clear)
        {
            _layerClear[layer] = clear;
        }

        public bool IsLayerClear(ViewLayer layer)
        {
            IsLayerClearCount++;
            if (IsBusy || layer == null)
                return false;

            bool clear;
            return !_layerClear.TryGetValue(layer, out clear) || clear;
        }

        public UniTask<ViewResult<TResult>> ShowAsync<TArgs, TResult>(
            ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            OpenOptions options = default,
            string member = null,
            int line = 0)
        {
            var toThrow = ThrowOnNextShowAsync;
            if (toThrow != null)
            {
                ThrowOnNextShowAsync = null;
                throw toThrow;
            }

            var call = new FakeShowCall<TResult>(key.Id, args, owner, options, member, line);
            _calls.Add(call);
            return call.Task;
        }

        public ViewHandle Open<TArgs, TResult>(
            ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            OpenOptions options = default,
            string member = null,
            int line = 0)
        {
            OpenCount++;
            return default(ViewHandle);
        }

        public bool Back()
        {
            return false;
        }

        public bool CloseTo(string viewId)
        {
            return false;
        }

        public void CloseAll(ViewLayer layer)
        {
            CloseAllCount++;
        }

        public bool IsOpen(string viewId)
        {
            return false;
        }

        public bool TryGetOpen(string viewId, out ViewHandle handle)
        {
            handle = default(ViewHandle);
            return false;
        }

        public bool TryGetTop(ViewLayer layer, out ViewHandle handle)
        {
            handle = default(ViewHandle);
            return false;
        }

        public ViewRegistration HoldInput(string reason, CancellationToken owner, string member = null, int line = 0)
        {
            HoldInputCount++;
            return default(ViewRegistration);
        }

        public ViewRegistration AddObserver(IViewObserver observer, CancellationToken owner, string member = null, int line = 0)
        {
            if (observer == null || owner.IsCancellationRequested)
                return default(ViewRegistration);

            var slot = new ObserverSlot(observer);
            _observers.Add(slot);

            var entry = new RegistrationEntry(_mainThread, _diagnostics, RemoveObserver, slot);
            entry.BindOwner(owner);
            return new ViewRegistration(entry);
        }

        public ViewRegistration AddCatalog(ViewCatalog catalog, CancellationToken owner, string member = null, int line = 0)
        {
            return default(ViewRegistration);
        }

        public UniTask<bool> PreloadAsync(string viewId, CancellationToken owner, string member = null, int line = 0)
        {
            return UniTask.FromResult(false);
        }

        public void Trim()
        {
        }

        public int ReserveRequestId()
        {
            return ++_lastRequestId;
        }

        public void Describe(StringBuilder output)
        {
            if (output == null)
                return;

            output.Append("fake navigator: ").Append(_calls.Count).Append(" calls, ")
                .Append(_observers.Count).Append(" observers").Append(IsBusy ? ", busy" : string.Empty).Append('\n');
        }

        // Delivers the event to every observer registered before this call; an observer that throws does not stop the rest.
        public void Publish(in ViewEvent viewEvent)
        {
            var snapshot = _observers.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                if (!snapshot[i].Active)
                    continue;

                try
                {
                    snapshot[i].Observer.OnViewEvent(in viewEvent);
                }
                catch (Exception)
                {
                    // The fake isolates observers like the real navigator; the queue's own observer never throws.
                }
            }
        }

        public void PublishSettled()
        {
            var settled = new ViewEvent(ViewEventKind.Settled, 0, null, null, null, 0, default(CloseReason), RejectReason.None, null, 0f, 0f);
            Publish(in settled);
        }

        // Ends the root token, as a destroyed root would.
        public void CancelRoot()
        {
            _root.Cancel();
        }

        private void RemoveObserver(object state)
        {
            var slot = (ObserverSlot)state;
            slot.Active = false;
            _observers.Remove(slot);
        }

        private sealed class ObserverSlot
        {
            public readonly IViewObserver Observer;
            public bool Active = true;

            public ObserverSlot(IViewObserver observer)
            {
                Observer = observer;
            }
        }
    }
}
