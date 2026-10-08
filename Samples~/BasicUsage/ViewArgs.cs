namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>What the confirm popup shows.</summary>
    public readonly struct ConfirmArgs
    {
        public readonly string Title;
        public readonly string Message;

        public ConfirmArgs(string title, string message)
        {
            Title = title;
            Message = message;
        }
    }

    /// <summary>Which item the detail screen shows.</summary>
    public readonly struct ItemArgs
    {
        public readonly string ItemId;

        public ItemArgs(string itemId)
        {
            ItemId = itemId;
        }
    }

    /// <summary>The item to rename and its current title.</summary>
    public readonly struct RenameArgs
    {
        public readonly string ItemId;
        public readonly string CurrentTitle;

        public RenameArgs(string itemId, string currentTitle)
        {
            ItemId = itemId;
            CurrentTitle = currentTitle;
        }
    }

    /// <summary>The one line a toast shows.</summary>
    public readonly struct ToastArgs
    {
        public readonly string Message;

        public ToastArgs(string message)
        {
            Message = message;
        }
    }
}
