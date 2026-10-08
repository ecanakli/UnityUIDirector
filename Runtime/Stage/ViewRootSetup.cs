using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    /// <summary>Code-only settings for ViewRoot.Initialize; numbers and references are Inspector fields on ViewRoot.</summary>
    public sealed class ViewRootSetup
    {
        /// <summary>Runs on each new instance while it is inactive, before Awake; null skips injection.</summary>
        public Action<GameObject> Inject;

        /// <summary>Resolves rows that carry an asset key; null uses an IViewProvider component on the root, if any.</summary>
        public IViewProvider AssetProvider;

        /// <summary>Layers declared in code, added to the three default layers.</summary>
        public IReadOnlyList<ViewLayer> ExtraLayers;

        /// <summary>Receives exceptions from user code and providers; null logs them with Debug.LogException.</summary>
        public ViewErrorHandler ErrorHandler;
    }
}
