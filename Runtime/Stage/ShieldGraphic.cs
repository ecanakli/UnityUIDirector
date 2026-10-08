using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector
{
    // The input shield: always active and rendered with an empty mesh; the stage only toggles raycastTarget.
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class ShieldGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }
    }
}
