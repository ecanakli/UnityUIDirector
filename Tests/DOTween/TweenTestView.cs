using System;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests
{
    // The view on the TweenTransition test templates; it only counts opens.
    [AddComponentMenu("")]
    internal sealed class TweenTestView : MonoBehaviour, IView<NoArgs, NoResult>
    {
        [NonSerialized] public int OpenCount;

        public void OnOpen(NoArgs args, ViewContext<NoResult> context)
        {
            OpenCount++;
        }
    }
}
