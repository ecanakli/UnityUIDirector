using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // A DiContainer can inject an instance that is staged under an inactive parent.
    public sealed class EngineProbes_Injection
    {
        readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (var i = m_Created.Count - 1; i >= 0; i--)
            {
                if (m_Created[i] != null)
                    Object.DestroyImmediate(m_Created[i]);
            }

            m_Created.Clear();
        }

        [UnityTest]
        public IEnumerator P2_InjectGameObject_ReachesInactiveInstance_BeforeAwake()
        {
            var container = new DiContainer();
            var value = new ProbeInjectedValue { Name = "probe-value" };
            container.BindInstance(value);

            // The template lives under an inactive holder, so its own Awake never runs.
            var holder = Track(new GameObject("PrefabHolder"));
            holder.SetActive(false);
            var template = new GameObject("Template");
            template.transform.SetParent(holder.transform, false);
            template.AddComponent<ProbeInjectTarget>();

            var inactiveParent = Track(new GameObject("InactiveParent"));
            inactiveParent.SetActive(false);
            var activeParent = Track(new GameObject("ActiveParent"));

            var instance = Object.Instantiate(template, inactiveParent.transform);
            var target = instance.GetComponent<ProbeInjectTarget>();
            var eventsBeforeInject = Describe(target);

            container.InjectGameObject(instance);
            var eventsAfterInject = Describe(target);
            var injectedAfterInject = target.Injected;

            instance.transform.SetParent(activeParent.transform, false);
            var eventsAfterReparent = Describe(target);

            foreach (var step in Frames(2))
                yield return step;

            ProbeLog.Write("P2",
                "case", "zenjectInjectInactive",
                "instanceActiveInHierarchyWhenInjected", false,
                "eventsBeforeInject", eventsBeforeInject,
                "eventsAfterInject", eventsAfterInject,
                "eventsAfterReparent", eventsAfterReparent,
                "awakeSawInjectedValue", target.InjectedSeenByAwake != null && target.InjectedSeenByAwake.Name == value.Name);

            Assert.AreEqual("-", eventsBeforeInject, "Precondition: nothing ran before injection.");
            Assert.AreEqual("Inject", eventsAfterInject, "InjectGameObject must run the inject method and not Awake on an inactive instance.");
            Assert.AreSame(value, injectedAfterInject, "The bound instance must be injected.");
            Assert.AreEqual("Inject|Awake", eventsAfterReparent, "Awake must run after reparenting under an active parent, after the injection.");
            Assert.AreSame(value, target.InjectedSeenByAwake, "Awake must see the injected value.");
        }

        T Track<T>(T obj) where T : Object
        {
            m_Created.Add(obj);
            return obj;
        }

        static string Describe(ProbeInjectTarget target)
        {
            return target.Events.Count == 0 ? "-" : string.Join("|", target.Events);
        }

        static IEnumerable Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }
    }
}
