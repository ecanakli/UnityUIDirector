using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Legacy Dropdown with TMP_Dropdown's blocker parent: the real root canvas, not the nearest override-sorting canvas.
    public sealed class ProbeTmpLikeDropdown : Dropdown
    {
        protected override GameObject CreateBlocker(Canvas rootCanvas)
        {
            return base.CreateBlocker(rootCanvas.rootCanvas);
        }
    }
}
