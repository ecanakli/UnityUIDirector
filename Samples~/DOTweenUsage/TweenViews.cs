using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.DOTweenUsage
{
    /// <summary>The keys of this sample: one screen and two popups animated with DOTween.</summary>
    [ViewKeys]
    public static class TweenViews
    {
        public static readonly ViewKey<NoArgs, NoResult> Welcome = ViewKey.Create("welcome");
        public static readonly ViewKey<HintArgs, NoResult> Hint = ViewKey.Create<HintArgs>("hint");
        public static readonly ViewKey<HintArgs, NoResult> Card = ViewKey.Create<HintArgs>("card");
    }

    /// <summary>The text a hint or a card shows.</summary>
    public readonly struct HintArgs
    {
        public readonly string Text;

        public HintArgs(string text)
        {
            Text = text;
        }
    }
}
