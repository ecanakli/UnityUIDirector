namespace Ecanakli.UIDirector
{
    /// <summary>The outcome of one request; HasValue is true only after Complete(value). default(ViewResult&lt;T&gt;) is not an outcome the navigator returns.</summary>
    public readonly struct ViewResult<TResult>
    {
        private readonly TResult _value;

        internal ViewResult(
            int requestId,
            bool wasShown,
            CloseReason reason,
            RejectReason rejected,
            string detail,
            bool hasValue,
            TResult value)
        {
            RequestId = requestId;
            WasShown = wasShown;
            Reason = reason;
            Rejected = rejected;
            Detail = detail;
            HasValue = hasValue;
            _value = value;
        }

        /// <summary>The id of the request that produced this outcome.</summary>
        public int RequestId { get; }

        /// <summary>True when the view began to show (its show transition started) before the request ended.</summary>
        public bool WasShown { get; }

        /// <summary>How the view closed; NotShown when it was never shown.</summary>
        public CloseReason Reason { get; }

        /// <summary>Why the request ended without being shown; None when it was shown.</summary>
        public RejectReason Rejected { get; }

        /// <summary>Optional text with more detail about the outcome.</summary>
        public string Detail { get; }

        /// <summary>True only when the view closed with Complete(value).</summary>
        public bool HasValue { get; }

        /// <summary>The value the view completed with; the default value when HasValue is false.</summary>
        public TResult Value => _value;

        /// <summary>Returns HasValue and writes the value to the out parameter.</summary>
        public bool TryGetValue(out TResult value)
        {
            value = _value;
            return HasValue;
        }
    }

    /// <summary>Factories for game-side fakes and tests.</summary>
    public static class ViewResult
    {
        /// <summary>An outcome of a view that closed with Complete(value).</summary>
        public static ViewResult<TResult> Completed<TResult>(TResult value)
        {
            return new ViewResult<TResult>(0, true, CloseReason.Completed, RejectReason.None, null, true, value);
        }

        /// <summary>
        /// An outcome of a view that was shown and closed without a value.
        /// Completed without a value is what a ShowAsync joined with other argument or result types returns;
        /// use Completed(value) for a view that completed with a value.
        /// </summary>
        /// <exception cref="System.ArgumentException">The reason is CloseReason.NotShown.</exception>
        public static ViewResult<TResult> Closed<TResult>(CloseReason reason)
        {
            if (reason == CloseReason.NotShown)
            {
                throw new System.ArgumentException(
                    "A closed result was shown; use ViewResult.NotShown<TResult>(rejectReason) for a request that was not shown.",
                    nameof(reason));
            }

            return new ViewResult<TResult>(0, true, reason, RejectReason.None, null, false, default(TResult));
        }

        /// <summary>An outcome of a request that ended without being shown.</summary>
        /// <exception cref="System.ArgumentException">The reason is RejectReason.None.</exception>
        public static ViewResult<TResult> NotShown<TResult>(RejectReason reason, string detail = null)
        {
            if (reason == RejectReason.None)
            {
                throw new System.ArgumentException(
                    "A request that was not shown always has a reject reason; RejectReason.None belongs to shown views.",
                    nameof(reason));
            }

            return new ViewResult<TResult>(0, false, CloseReason.NotShown, reason, detail, false, default(TResult));
        }
    }
}
