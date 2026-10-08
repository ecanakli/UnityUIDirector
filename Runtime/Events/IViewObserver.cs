namespace Ecanakli.UIDirector
{
    /// <summary>Receives every view event of a navigator; add it with an owner token.</summary>
    public interface IViewObserver
    {
        /// <summary>Called once per event, on the main thread; an exception thrown here is routed and does not stop other observers.</summary>
        void OnViewEvent(in ViewEvent viewEvent);
    }
}
