using System.Threading;
using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.QueueUsage
{
    /// <summary>A feature that adds its own catalog at run time and takes it away again.</summary>
    public sealed class NewsFeature
    {
        private readonly INavigator _navigator;
        private readonly ViewCatalog _catalog;
        private readonly ViewQueue _queue;

        // The feature's owner: it ends with the root, or earlier on Remove.
        private CancellationTokenSource _owner;

        public NewsFeature(INavigator navigator, ViewCatalog catalog, ViewQueue queue)
        {
            _navigator = navigator;
            _catalog = catalog;
            _queue = queue;
        }

        public bool IsAdded
        {
            get { return _owner != null && !_owner.IsCancellationRequested; }
        }

        public void Add()
        {
            if (IsAdded)
                return;

            _owner = CancellationTokenSource.CreateLinkedTokenSource(_navigator.RootToken);
            _navigator.AddCatalog(_catalog, _owner.Token);

            // The same owner: if the feature goes away while the notice waits, the notice ends with it.
            _queue.Enqueue(
                NewsViews.News,
                new NoticeArgs("News", "A news feature was added at run time."),
                _owner.Token,
                new QueueOptions { Priority = 50, Source = "news.added" });
        }

        public void Remove()
        {
            if (_owner == null)
                return;

            // Ending the owner removes the catalog and releases its kept instances; the news popup ends too, because it was queued with this owner.
            _owner.Cancel();
            _owner.Dispose();
            _owner = null;
        }
    }
}
