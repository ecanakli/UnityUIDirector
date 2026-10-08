using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // The public ViewResult factories build only outcomes the navigator can return.
    public sealed class ViewResultTests
    {
        [Test]
        public void Completed_AnyValue_ReturnsAShownCompletedResultCarryingTheValue()
        {
            var result = ViewResult.Completed(42);

            var gotValue = result.TryGetValue(out var value);

            Assert.That(result.WasShown, Is.True, "WasShown");
            Assert.That(result.Reason, Is.EqualTo(CloseReason.Completed), "Reason");
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.None), "Rejected");
            Assert.That(result.HasValue, Is.True, "HasValue");
            Assert.That(result.Value, Is.EqualTo(42), "Value");
            Assert.That(gotValue, Is.True, "TryGetValue return");
            Assert.That(value, Is.EqualTo(42), "TryGetValue out value");
            Assert.That(result.RequestId, Is.EqualTo(0), "RequestId");
        }

        [TestCase(CloseReason.Completed)]
        [TestCase(CloseReason.Dismissed)]
        [TestCase(CloseReason.Back)]
        [TestCase(CloseReason.Backdrop)]
        [TestCase(CloseReason.Code)]
        [TestCase(CloseReason.Navigated)]
        [TestCase(CloseReason.OwnerEnded)]
        [TestCase(CloseReason.Destroyed)]
        public void Closed_AShownReason_ReturnsAShownResultWithThatReasonAndNoValue(CloseReason reason)
        {
            var result = ViewResult.Closed<string>(reason);

            var gotValue = result.TryGetValue(out var value);

            Assert.That(result.WasShown, Is.True, "WasShown");
            Assert.That(result.Reason, Is.EqualTo(reason), "Reason");
            Assert.That(result.Rejected, Is.EqualTo(RejectReason.None), "Rejected");
            Assert.That(result.HasValue, Is.False, "HasValue");
            Assert.That(gotValue, Is.False, "TryGetValue return");
            Assert.That(value, Is.Null, "TryGetValue out value is the default");
        }

        [Test]
        public void Closed_NotShown_ThrowsArgumentException()
        {
            Assert.That(
                () => ViewResult.Closed<string>(CloseReason.NotShown),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("reason"));
        }

        [Test]
        public void NotShown_ARejectReason_ReturnsANotShownResultWithThatReasonAndDetail(
            [ValueSource(nameof(RejectReasonsExceptNone))] RejectReason reason,
            [Values(null, "text")] string detail)
        {
            var result = ViewResult.NotShown<string>(reason, detail);

            Assert.That(result.WasShown, Is.False, "WasShown");
            Assert.That(result.Reason, Is.EqualTo(CloseReason.NotShown), "Reason");
            Assert.That(result.Rejected, Is.EqualTo(reason), "Rejected");
            Assert.That(result.Detail, Is.EqualTo(detail), "Detail");
            Assert.That(result.HasValue, Is.False, "HasValue");
        }

        [Test]
        public void NotShown_None_ThrowsArgumentException()
        {
            Assert.That(
                () => ViewResult.NotShown<string>(RejectReason.None),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("reason"));
        }

        private static IEnumerable<RejectReason> RejectReasonsExceptNone()
        {
            foreach (RejectReason reason in Enum.GetValues(typeof(RejectReason)))
            {
                if (reason != RejectReason.None)
                    yield return reason;
            }
        }
    }
}
