using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    /// <summary>Shortcuts for keys without arguments and for typed keys.</summary>
    public static class NavigatorExtensions
    {
        /// <summary>Opens a view that takes no arguments and completes with its outcome.</summary>
        /// <param name="navigator">The navigator to open on.</param>
        /// <param name="key">The view to open.</param>
        /// <param name="owner">Ends the request when cancelled. Ignored when the call returns to a request already in a history layer's stack (a key without arguments): that request keeps its own owner, ShowAsync completes with its outcome, and Open returns its handle.</param>
        /// <param name="options">The source tag, priority and refusal options of this call.</param>
        /// <param name="member">Filled in by the compiler; do not pass it.</param>
        /// <param name="line">Filled in by the compiler; do not pass it.</param>
        /// <returns>The outcome of the request, completed once on the main thread.</returns>
        public static UniTask<ViewResult<TResult>> ShowAsync<TResult>(
            this INavigator navigator,
            ViewKey<NoArgs, TResult> key,
            CancellationToken owner,
            OpenOptions options = default,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0)
        {
            if (navigator == null)
                throw new ArgumentNullException(nameof(navigator));

            return navigator.ShowAsync(key, default(NoArgs), owner, options, member, line);
        }

        /// <summary>Opens a view that takes no arguments without waiting for it.</summary>
        /// <param name="navigator">The navigator to open on.</param>
        /// <param name="key">The view to open.</param>
        /// <param name="owner">Ends the request when cancelled. Ignored when the call returns to a request already in a history layer's stack (a key without arguments): that request keeps its own owner, ShowAsync completes with its outcome, and Open returns its handle.</param>
        /// <param name="options">The source tag, priority and refusal options of this call.</param>
        /// <param name="member">Filled in by the compiler; do not pass it.</param>
        /// <param name="line">Filled in by the compiler; do not pass it.</param>
        /// <returns>The handle of the request; an empty handle when the call was refused.</returns>
        public static ViewHandle Open<TResult>(
            this INavigator navigator,
            ViewKey<NoArgs, TResult> key,
            CancellationToken owner,
            OpenOptions options = default,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0)
        {
            if (navigator == null)
                throw new ArgumentNullException(nameof(navigator));

            return navigator.Open(key, default(NoArgs), owner, options, member, line);
        }

        /// <summary>Pops everything above the view named by the key; false when it is not in a history layer.</summary>
        public static bool CloseTo<TArgs, TResult>(this INavigator navigator, ViewKey<TArgs, TResult> key)
        {
            if (navigator == null)
                throw new ArgumentNullException(nameof(navigator));

            return navigator.CloseTo(key.Id);
        }

        /// <summary>True while a request for the view named by the key is open or in flight.</summary>
        public static bool IsOpen<TArgs, TResult>(this INavigator navigator, ViewKey<TArgs, TResult> key)
        {
            if (navigator == null)
                throw new ArgumentNullException(nameof(navigator));

            return navigator.IsOpen(key.Id);
        }
    }
}
