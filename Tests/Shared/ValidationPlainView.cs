using UnityEngine;

namespace Ecanakli.UIDirector.Tests
{
    // A test view with no arguments and no result for the editor validator tests; components cannot live in an editor-only assembly.
    public sealed class ValidationPlainView : MonoBehaviour, IView<NoArgs, NoResult>
    {
        public void OnOpen(NoArgs args, ViewContext<NoResult> context)
        {
        }
    }
}
