using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // GateDecision values: default and Allow let a request through; Deny stops it and keeps its reason.
    public sealed class GateDecisionTests
    {
        [Test]
        public void Default_IsAllowed()
        {
            var decision = default(GateDecision);

            Assert.That(decision.IsAllowed, Is.True, "IsAllowed");
            Assert.That(decision.Reason, Is.Null, "Reason");
        }

        [Test]
        public void Allow_IsAllowed()
        {
            var decision = GateDecision.Allow;

            Assert.That(decision.IsAllowed, Is.True, "IsAllowed");
            Assert.That(decision.Reason, Is.Null, "Reason");
        }

        [TestCase("onboarding is running")]
        [TestCase((string)null)]
        public void Deny_IsNotAllowed(string reason)
        {
            var decision = GateDecision.Deny(reason);

            Assert.That(decision.IsAllowed, Is.False);
        }

        [Test]
        public void Deny_KeepsTheReason()
        {
            var decision = GateDecision.Deny("onboarding is running");

            Assert.That(decision.Reason, Is.EqualTo("onboarding is running"));
        }
    }
}
