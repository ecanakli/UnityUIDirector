namespace Ecanakli.UIDirector
{
    /// <summary>Optional: asked when Back() targets a view whose policy is HandledByView.</summary>
    public interface IViewBack
    {
        /// <summary>Returns true when the view handled back itself; false applies the layer's rule, as BackPolicy.LayerDefault does.</summary>
        bool OnBack();
    }
}
