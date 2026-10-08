using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Application.lowMemory can be raised and its subscribers read in a test, through engine internals.
    public sealed class EngineProbes_LowMemory
    {
        int m_Calls;
        Application.LowMemoryCallback m_Callback;

        [SetUp]
        public void SetUp()
        {
            m_Calls = 0;
            m_Callback = OnLowMemory;
            Application.lowMemory += m_Callback;
        }

        [TearDown]
        public void TearDown()
        {
            Application.lowMemory -= m_Callback;
        }

        [Test]
        public void P12_ApplicationLowMemory_CanBeRaisedThroughTheEngineDispatcher()
        {
            EngineLowMemory.Raise();

            ProbeLog.Write("P12", "dispatcher", EngineLowMemory.DescribeDispatcher(), "calls", m_Calls);
            Assert.AreEqual(1, m_Calls, "The subscriber must be called once when the engine dispatcher runs.");
        }

        [Test]
        public void P12_ApplicationLowMemory_SubscribersCanBeCountedPerTarget()
        {
            var count = EngineLowMemory.CountSubscribers(this);

            ProbeLog.Write("P12", "subscribersOfProbe", count);
            Assert.AreEqual(1, count, "The probe's one subscription must be found in the engine's event storage.");
        }

        void OnLowMemory()
        {
            m_Calls++;
        }
    }

    // Raises Application.lowMemory and counts its subscribers; fails loudly when the engine internals change.
    internal static class EngineLowMemory
    {
        const string DispatcherName = "CallLowMemory";
        const string CriticalUsage = "Critical";

        public static void Raise()
        {
            var dispatcher = FindDispatcher();
            Assert.That(dispatcher, Is.Not.Null,
                "UnityEngine.Application has no static " + DispatcherName + " taking no argument or one memory-usage enum with a Critical value; the engine's low-memory dispatcher changed.");

            dispatcher.Invoke(null, ArgumentsFor(dispatcher));
        }

        // Subscriptions to Application.lowMemory whose target is the given object.
        public static int CountSubscribers(object target)
        {
            var field = FindEventField();
            Assert.That(field, Is.Not.Null,
                "UnityEngine.Application has no static field of type LowMemoryCallback; the engine's lowMemory event storage changed.");

            var handlers = field.GetValue(null) as Delegate;
            if (handlers == null)
                return 0;

            var count = 0;
            var list = handlers.GetInvocationList();
            for (var i = 0; i < list.Length; i++)
            {
                if (ReferenceEquals(list[i].Target, target))
                    count++;
            }

            return count;
        }

        public static string DescribeDispatcher()
        {
            var dispatcher = FindDispatcher();
            if (dispatcher == null)
                return "missing";

            var parameters = dispatcher.GetParameters();
            return parameters.Length == 0 ? DispatcherName + "()" : DispatcherName + "(" + parameters[0].ParameterType.Name + ")";
        }

        static MethodInfo FindDispatcher()
        {
            var methods = typeof(Application).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (var i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                if (method.Name != DispatcherName)
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length == 0)
                    return method;

                if (parameters.Length == 1 && parameters[0].ParameterType.IsEnum && Enum.IsDefined(parameters[0].ParameterType, CriticalUsage))
                    return method;
            }

            return null;
        }

        static object[] ArgumentsFor(MethodInfo dispatcher)
        {
            var parameters = dispatcher.GetParameters();
            if (parameters.Length == 0)
                return null;

            return new[] { Enum.Parse(parameters[0].ParameterType, CriticalUsage) };
        }

        static FieldInfo FindEventField()
        {
            var fields = typeof(Application).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (var i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType == typeof(Application.LowMemoryCallback))
                    return fields[i];
            }

            return null;
        }
    }
}
