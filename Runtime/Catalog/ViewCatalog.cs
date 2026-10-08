using System.Collections.Generic;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>A list of rows; several catalogs may be registered with one root.</summary>
    [CreateAssetMenu(menuName = "UIDirector/View Catalog")]
    public sealed class ViewCatalog : ScriptableObject
    {
        [SerializeField] private List<ViewEntry> _entries = new List<ViewEntry>();

        /// <summary>The rows of this catalog.</summary>
        public IReadOnlyList<ViewEntry> Entries => _entries;

        internal static ViewCatalog Create(IEnumerable<ViewEntry> entries)
        {
            var catalog = CreateInstance<ViewCatalog>();
            if (entries != null)
                catalog._entries.AddRange(entries);
            return catalog;
        }
    }
}
