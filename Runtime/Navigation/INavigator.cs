using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.UIDirector
{
    /// <summary>Opens, closes and queries views on one root.</summary>
    public interface INavigator
    {
        /// <summary>Opens a view and completes with its outcome; never throws for anything that happens to the view.</summary>
        /// <param name="key">The view to open.</param>
        /// <param name="args">The arguments handed to the view's OnOpen.</param>
        /// <param name="owner">Ends the request when cancelled. Ignored when the call returns to a request already in a history layer's stack (a key without arguments): that request keeps its own owner, ShowAsync completes with its outcome, and Open returns its handle.</param>
        /// <param name="options">The source tag, priority and refusal options of this call.</param>
        /// <param name="member">Filled in by the compiler; do not pass it.</param>
        /// <param name="line">Filled in by the compiler; do not pass it.</param>
        /// <returns>The outcome of the request, completed once on the main thread.</returns>
        /// <remarks>
        /// To stop waiting when a token of your own ends without ending the request it joined, wrap the call:
        /// <c>var (stopped, result) = await navigator.ShowAsync(key, args, ct).AttachExternalCancellation(ct).SuppressCancellationThrow();</c>
        /// <c>stopped</c> is true whenever your token ended; when the call started a new request owned by that token, the view also ended.
        /// </remarks>
        UniTask<ViewResult<TResult>> ShowAsync<TArgs, TResult>(
            ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            OpenOptions options = default,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0);

        /// <summary>Opens a view without waiting for it.</summary>
        /// <param name="key">The view to open.</param>
        /// <param name="args">The arguments handed to the view's OnOpen.</param>
        /// <param name="owner">Ends the request when cancelled. Ignored when the call returns to a request already in a history layer's stack (a key without arguments): that request keeps its own owner, ShowAsync completes with its outcome, and Open returns its handle.</param>
        /// <param name="options">The source tag, priority and refusal options of this call.</param>
        /// <param name="member">Filled in by the compiler; do not pass it.</param>
        /// <param name="line">Filled in by the compiler; do not pass it.</param>
        /// <returns>The handle of the request; an empty handle when the call was refused.</returns>
        ViewHandle Open<TArgs, TResult>(
            ViewKey<TArgs, TResult> key,
            TArgs args,
            CancellationToken owner,
            OpenOptions options = default,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0);

        /// <summary>The single back rule; false means nothing handled it and the game decides.</summary>
        bool Back();

        /// <summary>Pops everything above the given screen; false when it is not in a history layer.</summary>
        bool CloseTo(string viewId);

        /// <summary>Closes every view and withdraws every pending request on the layer.</summary>
        void CloseAll(ViewLayer layer);

        /// <summary>True while a request for the view id is open or in flight.</summary>
        bool IsOpen(string viewId);

        /// <summary>Gets the handle of the request for the view id when it is open or in flight.</summary>
        bool TryGetOpen(string viewId, out ViewHandle handle);

        /// <summary>Gets the handle of the top request of the layer when it has one.</summary>
        bool TryGetTop(ViewLayer layer, out ViewHandle handle);

        /// <summary>True while an operation runs or an input hold exists.</summary>
        bool IsBusy { get; }

        /// <summary>True when the layer has no request, nothing modal is above it and the navigator is not busy.</summary>
        bool IsLayerClear(ViewLayer layer);

        /// <summary>Cancelled when the root is destroyed; the explicit "as long as the UI exists" owner.</summary>
        CancellationToken RootToken { get; }

        /// <summary>Keeps the shield up until the owner ends or the registration is removed.</summary>
        ViewRegistration HoldInput(
            string reason,
            CancellationToken owner,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0);

        /// <summary>Delivers every view event to the observer until the owner ends or the registration is removed.</summary>
        ViewRegistration AddObserver(
            IViewObserver observer,
            CancellationToken owner,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0);

        /// <summary>Registers the catalog rows until the owner ends or the registration is removed.</summary>
        ViewRegistration AddCatalog(
            ViewCatalog catalog,
            CancellationToken owner,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0);

        /// <summary>Keeps the view's asset loaded until the owner ends; false when the id is unknown or the load failed.</summary>
        UniTask<bool> PreloadAsync(
            string viewId,
            CancellationToken owner,
            [CallerMemberName] string member = null,
            [CallerLineNumber] int line = 0);

        /// <summary>Releases every idle kept instance.</summary>
        void Trim();

        /// <summary>Reserves an id so that a caller can publish events for a request before it reaches the navigator.</summary>
        int ReserveRequestId();

        /// <summary>Writes the layers, requests, shield holders and kept instances as text.</summary>
        void Describe(StringBuilder output);
    }
}
