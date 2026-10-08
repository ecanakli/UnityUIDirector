using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>The key of the news feature; a feature brings its own keys and its own catalog, apart from the app's.</summary>
    [ViewKeys]
    public static class NewsViews
    {
        public static readonly ViewKey<NoticeArgs, NoResult> News = ViewKey.Create<NoticeArgs>("news");
    }
}
