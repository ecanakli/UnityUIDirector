using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // Status checks for tasks the tests never await: nothing here waits for a player-loop tick.
    internal static class TestTaskExtensions
    {
        public static bool IsPending(this UniTask task)
        {
            return task.Status == UniTaskStatus.Pending;
        }

        public static bool IsPending<T>(this UniTask<T> task)
        {
            return task.Status == UniTaskStatus.Pending;
        }

        // Fails unless the task already succeeded.
        public static void AssertCompleted(this UniTask task)
        {
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded), "The task has not succeeded.");
        }

        // Fails unless the task already succeeded, then returns its result.
        public static T ResultNow<T>(this UniTask<T> task)
        {
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded), "The task has not succeeded.");
            return task.GetAwaiter().GetResult();
        }
    }
}
