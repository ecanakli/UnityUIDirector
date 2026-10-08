using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests
{
    // The process-wide quit flag: a play session started without a domain reload must not inherit it.
    [TestFixture]
    public sealed class QuitSignalTests
    {
        // A Play Mode run leaves the flag set in the Editor until the next play session.
        [SetUp]
        public void SetUp()
        {
            QuitSignal.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            QuitSignal.Reset();
        }

        [Test]
        public void Reset_AfterQuitting_ClearsTheFlag()
        {
            QuitSignal.MarkQuittingForTests();
            Assert.That(QuitSignal.IsQuitting, Is.True, "Control: the flag is set before the reset.");

            QuitSignal.Reset();

            Assert.That(QuitSignal.IsQuitting, Is.False);
        }

        [Test]
        public void Reset_CalledAgain_KeepsOneQuittingSubscription()
        {
            QuitSignal.Reset();

            Assert.That(QuitSignalHandlers().Count, Is.EqualTo(1), "QuitSignal handlers on Application.quitting after a second reset.");
        }

        [Test]
        public void ApplicationQuitting_HandlerAddedByReset_SetsTheFlag()
        {
            var handlers = QuitSignalHandlers();
            Assert.That(handlers.Count, Is.EqualTo(1), "Control: Reset subscribed QuitSignal to Application.quitting.");

            handlers[0].DynamicInvoke();

            Assert.That(QuitSignal.IsQuitting, Is.True);
        }

        // Raising Application.quitting would run the Editor's own handlers too, so the tests read and call only QuitSignal's.
        private static List<Delegate> QuitSignalHandlers()
        {
            var field = typeof(Application).GetField("quitting", BindingFlags.Static | BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, "Application.quitting has no backing field named 'quitting' in this Unity version.");

            var result = new List<Delegate>();
            var quitting = field.GetValue(null) as Delegate;
            if (quitting == null)
                return result;

            foreach (var handler in quitting.GetInvocationList())
            {
                if (handler.Method.DeclaringType == typeof(QuitSignal))
                    result.Add(handler);
            }

            return result;
        }
    }
}
