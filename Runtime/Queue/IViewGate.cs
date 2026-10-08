namespace Ecanakli.UIDirector
{
    /// <summary>A synchronous rule on a queue; asked at enqueue and again right before presentation.</summary>
    public interface IViewGate
    {
        /// <summary>Allows the request or denies it with a reason; a gate that throws counts as Deny.</summary>
        GateDecision Evaluate(in GateRequest request);
    }
}
