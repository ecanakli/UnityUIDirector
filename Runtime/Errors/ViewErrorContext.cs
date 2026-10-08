namespace Ecanakli.UIDirector
{
    /// <summary>Where an error was raised: the kind of step, the view, the request and the object involved.</summary>
    public readonly struct ViewErrorContext
    {
        internal ViewErrorContext(ViewErrorSource source, string viewId, int requestId, UnityEngine.Object target)
        {
            Source = source;
            ViewId = viewId;
            RequestId = requestId;
            Target = target;
        }

        /// <summary>The kind of callback or step that failed.</summary>
        public ViewErrorSource Source { get; }

        /// <summary>The id of the view involved; null when none applies.</summary>
        public string ViewId { get; }

        /// <summary>The id of the request involved; zero when none applies.</summary>
        public int RequestId { get; }

        /// <summary>The Unity object involved, for example the view instance; null when none applies.</summary>
        public UnityEngine.Object Target { get; }
    }
}
