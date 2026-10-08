using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    // Waiting -> Presenting -> Done, or Waiting -> Done.
    internal enum QueueItemState
    {
        Waiting,
        Presenting,
        Done
    }

    // One queued request as the queue walks it; the typed part lives in QueueItem<TArgs, TResult>.
    internal abstract class QueueItem
    {
        // Passed as the call site of the navigator request.
        protected const string CallSite = "ViewQueue";

        // The item's owner callback while it waits; disposed when it is presented or ends.
        public CancellationTokenRegistration OwnerRegistration;

        // Set when the navigator published Closed or NotShown for this item; the queue then publishes no end of its own.
        public bool NavigatorEnded;

        protected QueueItem(
            int requestId,
            string viewId,
            int priority,
            string source,
            long sequence,
            double enqueuedAt,
            CancellationToken owner)
        {
            RequestId = requestId;
            ViewId = viewId;
            Priority = priority;
            Source = source;
            Sequence = sequence;
            EnqueuedAt = enqueuedAt;
            Owner = owner;
            State = QueueItemState.Waiting;
        }

        public int RequestId { get; }

        public string ViewId { get; }

        public int Priority { get; }

        public string Source { get; }

        // Enqueue order; ties of priority keep it.
        public long Sequence { get; }

        // Scheduler time at enqueue, for Describe.
        public double EnqueuedAt { get; }

        public CancellationToken Owner { get; }

        public QueueItemState State { get; set; }

        // Calls ShowAsync with the reserved id and completes the awaiter with the navigator's result.
        public abstract UniTask PresentAsync(INavigator navigator, ViewLayer layer);

        // Completes the awaiter with a result that was not shown; at most once.
        public abstract void EndNotShown(RejectReason reason, string detail);
    }

    internal sealed class QueueItem<TArgs, TResult> : QueueItem
    {
        private readonly ViewKey<TArgs, TResult> _key;
        private readonly TArgs _args;
        private readonly UniTaskCompletionSource<ViewResult<TResult>> _completion;
        private bool _completed;

        // The completion source exists only for EnqueueAsync.
        public QueueItem(
            int requestId,
            ViewKey<TArgs, TResult> key,
            TArgs args,
            int priority,
            string source,
            long sequence,
            double enqueuedAt,
            CancellationToken owner,
            bool awaitable)
            : base(requestId, key.Id, priority, source, sequence, enqueuedAt, owner)
        {
            _key = key;
            _args = args;
            if (awaitable)
                _completion = new UniTaskCompletionSource<ViewResult<TResult>>();
        }

        // Only valid for an item created as awaitable.
        public UniTask<ViewResult<TResult>> Completion
        {
            get { return _completion.Task; }
        }

        public override UniTask PresentAsync(INavigator navigator, ViewLayer layer)
        {
            var options = new OpenOptions
            {
                RequestId = RequestId,
                Source = Source,
                Priority = Priority,
                ExpectedLayerId = layer.Id
            };

            // The request is accepted synchronously here; only its outcome is awaited.
            var show = navigator.ShowAsync(_key, _args, Owner, options, CallSite, 0);
            return AwaitResultAsync(show);
        }

        public override void EndNotShown(RejectReason reason, string detail)
        {
            Complete(new ViewResult<TResult>(
                RequestId,
                false,
                CloseReason.NotShown,
                reason,
                detail,
                false,
                default(TResult)));
        }

        private async UniTask AwaitResultAsync(UniTask<ViewResult<TResult>> show)
        {
            var result = await show;
            Complete(result);
        }

        private void Complete(ViewResult<TResult> result)
        {
            if (_completed)
                return;

            _completed = true;
            if (_completion != null)
                _completion.TrySetResult(result);
        }
    }
}
