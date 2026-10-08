namespace Ecanakli.UIDirector
{
    // Easing curves of the built-in transitions; serialized on each built-in, no public easing API.
    internal enum ViewEase
    {
        Linear,
        OutCubic,
        InCubic,
        InOutCubic,
        OutBack
    }

    internal static class Easing
    {
        private const float BackOvershoot = 1.70158f;

        // Clamps t to [0, 1]; every curve returns exactly 0 at 0 and 1 at 1.
        public static float Evaluate(ViewEase ease, float t)
        {
            // NaN and values below 0 map to the start.
            if (!(t > 0f))
                return 0f;
            if (t >= 1f)
                return 1f;

            switch (ease)
            {
                case ViewEase.OutCubic:
                {
                    var inverse = 1f - t;
                    return 1f - inverse * inverse * inverse;
                }
                case ViewEase.InCubic:
                    return t * t * t;
                case ViewEase.InOutCubic:
                {
                    if (t < 0.5f)
                        return 4f * t * t * t;

                    var tail = -2f * t + 2f;
                    return 1f - tail * tail * tail * 0.5f;
                }
                case ViewEase.OutBack:
                {
                    var shifted = t - 1f;
                    return 1f + (BackOvershoot + 1f) * shifted * shifted * shifted + BackOvershoot * shifted * shifted;
                }
                default:
                    return t;
            }
        }
    }
}
