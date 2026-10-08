using System;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    // The real seam: UniTask knows the main thread id and owns the player loop queue.
    internal sealed class UniTaskMainThread : IMainThread
    {
        public bool IsMainThread
        {
            get
            {
                // UniTask captures the id at load; before that nothing can be told apart.
                return PlayerLoopHelper.MainThreadId == 0 || PlayerLoopHelper.IsMainThread;
            }
        }

        public void Post(Action action)
        {
            UniTask.Post(action);
        }
    }
}
