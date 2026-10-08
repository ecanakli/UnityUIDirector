using UnityEngine;

namespace Ecanakli.UIDirector.Tests
{
    // A test view with int arguments and no result for the editor validator tests; components cannot live in an editor-only assembly.
    public sealed class ValidationArgsView : MonoBehaviour, IView<int, NoResult>
    {
        public void OnOpen(int args, ViewContext<NoResult> context)
        {
        }
    }
}
