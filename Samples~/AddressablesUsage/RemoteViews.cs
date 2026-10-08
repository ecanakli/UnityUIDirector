using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.AddressablesUsage
{
    /// <summary>The keys of this sample. Their catalog rows have no prefab, only an Addressables address.</summary>
    [ViewKeys]
    public static class RemoteViews
    {
        public static readonly ViewKey<NoArgs, NoResult> Gallery = ViewKey.Create("gallery");
        public static readonly ViewKey<PhotoArgs, NoResult> Photo = ViewKey.Create<PhotoArgs>("photo");
    }

    /// <summary>Which photo the popup shows.</summary>
    public readonly struct PhotoArgs
    {
        public readonly string Title;

        public PhotoArgs(string title)
        {
            Title = title;
        }
    }
}
