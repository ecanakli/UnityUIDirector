namespace Ecanakli.UIDirector
{
    /// <summary>A gate's answer: allow the request, or deny it with a reason; default(GateDecision) is Allow.</summary>
    public readonly struct GateDecision
    {
        private readonly bool _denied;

        private GateDecision(bool denied, string reason)
        {
            _denied = denied;
            Reason = reason;
        }

        /// <summary>Lets the request pass this gate; the same as default(GateDecision).</summary>
        public static GateDecision Allow
        {
            get { return default(GateDecision); }
        }

        /// <summary>Ends the request as not shown, with the reason as its detail.</summary>
        public static GateDecision Deny(string reason)
        {
            return new GateDecision(true, reason);
        }

        /// <summary>True for Allow.</summary>
        public bool IsAllowed => !_denied;

        /// <summary>The reason given to Deny; null for Allow.</summary>
        public string Reason { get; }
    }
}
