using System;
using System.Collections.Generic;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>Stand-in item list with three items, one of which cannot be renamed.</summary>
    public sealed class ItemLibrary : IItemLibrary
    {
        private const string ArchiveId = "archive";

        private readonly Dictionary<string, string> _titles = new Dictionary<string, string>
        {
            { "guide", "Getting started" },
            { "notes", "Release notes" },
            { ArchiveId, "Archive" }
        };

        public event Action<string> Renamed;

        public int Count
        {
            get { return _titles.Count; }
        }

        public string GetTitle(string itemId)
        {
            return _titles.TryGetValue(itemId, out var title) ? title : itemId;
        }

        public bool CanRename(string itemId)
        {
            return itemId != ArchiveId && _titles.ContainsKey(itemId);
        }

        public void Rename(string itemId, string title)
        {
            if (!CanRename(itemId))
                return;

            _titles[itemId] = title;
            Renamed?.Invoke(itemId);
        }
    }
}
