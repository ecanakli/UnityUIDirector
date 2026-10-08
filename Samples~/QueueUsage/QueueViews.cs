using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>The key of every view in this sample's catalog. The news feature brings its own keys and catalog.</summary>
    [ViewKeys]
    public static class QueueViews
    {
        public static readonly ViewKey<NoArgs, NoResult> Lobby = ViewKey.Create("lobby");
        public static readonly ViewKey<NoticeArgs, NoResult> Notice = ViewKey.Create<NoticeArgs>("notice");
        public static readonly ViewKey<AskArgs, bool> Ask = ViewKey.Create<AskArgs, bool>("ask");
        public static readonly ViewKey<TipArgs, NoResult> TutorialTip = ViewKey.Create<TipArgs>("tutorial-tip");
    }

    /// <summary>A notice's title and body.</summary>
    public readonly struct NoticeArgs
    {
        public readonly string Title;
        public readonly string Body;

        public NoticeArgs(string title, string body)
        {
            Title = title;
            Body = body;
        }
    }

    /// <summary>A yes or no question.</summary>
    public readonly struct AskArgs
    {
        public readonly string Question;

        public AskArgs(string question)
        {
            Question = question;
        }
    }

    /// <summary>One step of the tutorial.</summary>
    public readonly struct TipArgs
    {
        public readonly string Text;
        public readonly int Step;
        public readonly int StepCount;

        public TipArgs(string text, int step, int stepCount)
        {
            Text = text;
            Step = step;
            StepCount = stepCount;
        }
    }
}
