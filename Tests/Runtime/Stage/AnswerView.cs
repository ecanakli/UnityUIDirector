namespace Ecanakli.UIDirector.Tests
{
    // A view with an int argument and a bool result, for PlayMode tests of typed opens and results.
    internal sealed class AnswerView : ViewBehaviour<int, bool>
    {
        public int OpenCount;
        public int LastArgs;

        public void Answer(bool value)
        {
            Complete(value);
        }

        protected override void OnOpen(int args, ViewScope open)
        {
            OpenCount++;
            LastArgs = args;
        }
    }
}
