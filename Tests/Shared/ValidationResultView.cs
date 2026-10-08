using UnityEngine;

namespace Ecanakli.UIDirector.Tests
{
    // A test view with string arguments and a bool result for the editor validator tests; components cannot live in an editor-only assembly.
    public sealed class ValidationResultView : MonoBehaviour, IView<string, bool>
    {
        public void OnOpen(string args, ViewContext<bool> context)
        {
        }
    }
}
