using Ecanakli.UIDirector;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>The key of every view in this sample. Each id matches a row of the sample's catalog.</summary>
    [ViewKeys]
    public static class AppViews
    {
        public static readonly ViewKey<NoArgs, NoResult> Home = ViewKey.Create("home");
        public static readonly ViewKey<NoArgs, NoResult> Library = ViewKey.Create("library");
        public static readonly ViewKey<ItemArgs, NoResult> ItemDetail = ViewKey.Create<ItemArgs>("item-detail");
        public static readonly ViewKey<NoArgs, NoResult> Settings = ViewKey.Create("settings");
        public static readonly ViewKey<ConfirmArgs, bool> Confirm = ViewKey.Create<ConfirmArgs, bool>("confirm");
        public static readonly ViewKey<RenameArgs, string> Rename = ViewKey.Create<RenameArgs, string>("rename");
        public static readonly ViewKey<NoArgs, NoResult> Saving = ViewKey.Create("saving");
        public static readonly ViewKey<ToastArgs, NoResult> Toast = ViewKey.Create<ToastArgs>("toast");
    }
}
