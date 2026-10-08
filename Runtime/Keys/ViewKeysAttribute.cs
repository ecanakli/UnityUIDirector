using System;

namespace Ecanakli.UIDirector
{
    /// <summary>Marks a static class whose key fields the editor validator checks against the catalogs.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ViewKeysAttribute : Attribute
    {
    }
}
