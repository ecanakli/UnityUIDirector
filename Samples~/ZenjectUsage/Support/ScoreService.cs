namespace Ecanakli.UIDirector.Samples.ZenjectUsage
{
    /// <summary>Stand-in game state: a score the views read and change through the container.</summary>
    public sealed class ScoreService
    {
        public int Points { get; private set; }

        public void Add(int points)
        {
            Points += points;
        }

        public void Reset()
        {
            Points = 0;
        }
    }
}
