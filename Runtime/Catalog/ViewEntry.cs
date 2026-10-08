using System;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>One catalog row: an id, a prefab source and the view's policy.</summary>
    [Serializable]
    public sealed class ViewEntry
    {
        [SerializeField] private string _id = string.Empty;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private string _assetKey = string.Empty;
        [SerializeField] private string _layerId = "screens";
        [SerializeField] private ViewRetention _retention = ViewRetention.ReleaseOnClose;
        [SerializeField] private BackPolicy _back = BackPolicy.LayerDefault;
        [SerializeField] private bool _closeOnBackdrop;
        [SerializeField] private CoverPolicy _cover = CoverPolicy.LayerDefault;
        [SerializeField] private CoveredMode _whenCovered = CoveredMode.DisableCanvas;
        [SerializeField] private bool _allowMultiple;

        private ViewEntry()
        {
        }

        internal ViewEntry(
            string id,
            GameObject prefab,
            string assetKey,
            string layerId,
            ViewRetention retention,
            BackPolicy back,
            bool closeOnBackdrop,
            CoverPolicy cover,
            CoveredMode whenCovered,
            bool allowMultiple)
        {
            _id = id;
            _prefab = prefab;
            _assetKey = assetKey;
            _layerId = layerId;
            _retention = retention;
            _back = back;
            _closeOnBackdrop = closeOnBackdrop;
            _cover = cover;
            _whenCovered = whenCovered;
            _allowMultiple = allowMultiple;
        }

        /// <summary>The id code and remote data use to name this view.</summary>
        public string Id => _id;

        /// <summary>A direct prefab reference; resident while the catalog is loaded.</summary>
        public GameObject Prefab => _prefab;

        /// <summary>The asset key used when Prefab is null; resolved by the root's view provider.</summary>
        public string AssetKey => _assetKey;

        /// <summary>The id of the layer the view opens on.</summary>
        public string LayerId => _layerId;

        /// <summary>What happens to the instance when the view closes.</summary>
        public ViewRetention Retention => _retention;

        /// <summary>What Back() does when this view is the back target.</summary>
        public BackPolicy Back => _back;

        /// <summary>True when a tap on the backdrop closes this view.</summary>
        public bool CloseOnBackdrop => _closeOnBackdrop;

        /// <summary>Whether the view hides what is below it once it is fully open.</summary>
        public CoverPolicy Cover => _cover;

        /// <summary>How the view is hidden while another view covers it.</summary>
        public CoveredMode WhenCovered => _whenCovered;

        /// <summary>True when more than one instance of this id may exist at once.</summary>
        public bool AllowMultiple => _allowMultiple;
    }
}
