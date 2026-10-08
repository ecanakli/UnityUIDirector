namespace Ecanakli.UIDirector
{
    /// <summary>Optional: called each time the view comes on screen, with the scope of that span.</summary>
    public interface IViewVisible
    {
        /// <summary>The view is on screen; work started on the scope ends when the view is covered or closes.</summary>
        void OnVisible(ViewScope visible);
    }
}
