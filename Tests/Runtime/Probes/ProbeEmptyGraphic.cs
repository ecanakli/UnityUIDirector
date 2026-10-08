using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // A graphic whose mesh is always empty. Graphic itself does not require a CanvasRenderer.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ProbeEmptyGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }
    }
}
