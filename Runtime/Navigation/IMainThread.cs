using System;

namespace Ecanakli.UIDirector
{
    // The one place the logic asks "am I on the main thread" and "run this there".
    internal interface IMainThread
    {
        bool IsMainThread { get; }

        // Runs the action later on the main thread; may be called from any thread.
        void Post(Action action);
    }
}
