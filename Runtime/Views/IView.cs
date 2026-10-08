namespace Ecanakli.UIDirector
{
    /// <summary>The contract a view component implements; called once per open, before the first visible frame.</summary>
    public interface IView<TArgs, TResult>
    {
        /// <summary>Receives the arguments and the context of this open; set up the view and bind on the open scope.</summary>
        void OnOpen(TArgs args, ViewContext<TResult> context);
    }
}
