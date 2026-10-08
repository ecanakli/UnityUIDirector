using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Instances staged under an inactive parent run no Awake or OnEnable until they are reparented.
    public sealed class EngineProbes_Staging
    {
        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();

        [TearDown]
        public void TearDown()
        {
            m_Cleanup.DestroyAll();
        }

        [UnityTest]
        public IEnumerator P2_InstantiateUnderInactiveParent_RunsNoAwakeOrOnEnable()
        {
            var template = BuildTemplate();
            var staging = BuildInactiveParent("Staging");

            var clone = Object.Instantiate(template, staging.transform);
            var counter = clone.GetComponent<ProbeLifecycleCounter>();
            var immediately = counter.Describe();

            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            ProbeLog.Write("P2", "case", "instantiateInactive",
                "cloneActiveSelf", clone.activeSelf,
                "cloneActiveInHierarchy", clone.activeInHierarchy,
                "eventsImmediately", immediately,
                "eventsAfterTwoFrames", counter.Describe());

            Assert.IsTrue(clone.activeSelf, "Precondition: the clone is active itself, like a prefab root.");
            Assert.IsFalse(clone.activeInHierarchy, "Precondition: the staging parent is inactive.");
            Assert.AreEqual(0, counter.Count("Awake"), "Awake must not run under an inactive parent.");
            Assert.AreEqual(0, counter.Count("OnEnable"), "OnEnable must not run under an inactive parent.");
            Assert.AreEqual(0, counter.Count("Start"), "Start must not run under an inactive parent.");
        }

        [UnityTest]
        public IEnumerator P2_ReparentUnderActiveParent_RunsAwakeThenOnEnable()
        {
            var template = BuildTemplate();
            var staging = BuildInactiveParent("Staging");
            var layer = m_Cleanup.Track(new GameObject("ActiveLayer"));

            var clone = Object.Instantiate(template, staging.transform);
            var counter = clone.GetComponent<ProbeLifecycleCounter>();
            var beforeReparent = counter.Describe();

            clone.transform.SetParent(layer.transform, false);
            var insideSetParent = counter.Describe();

            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            var afterFrames = counter.Describe();
            ProbeLog.Write("P2", "case", "reparentActive",
                "eventsBeforeReparent", beforeReparent,
                "eventsRightAfterSetParent", insideSetParent,
                "awakeAndEnableSynchronousInSetParent", insideSetParent == "Awake|OnEnable",
                "eventsAfterTwoFrames", afterFrames);

            Assert.AreEqual("-", beforeReparent, "Precondition: nothing ran while staged.");
            Assert.AreEqual(1, counter.Count("Awake"), "Awake must run once after reparenting under an active parent.");
            Assert.AreEqual(1, counter.Count("OnEnable"), "OnEnable must run once after reparenting under an active parent.");
            Assert.Less(counter.Events.IndexOf("Awake"), counter.Events.IndexOf("OnEnable"), "Awake must run before OnEnable.");
        }

        [UnityTest]
        public IEnumerator P2_ObjectInstantiateAsync_UnderInactiveParent_RunsNoAwake()
        {
            var template = BuildTemplate();
            var staging = BuildInactiveParent("Staging");

            var operation = Object.InstantiateAsync(template, staging.transform);
            var startFrame = Time.frameCount;
            while (!operation.isDone && Time.frameCount - startFrame < 600)
                yield return null;

            if (!operation.isDone)
                Assert.Inconclusive("InstantiateAsync did not finish within 600 frames.");

            var results = operation.Result;
            Assert.AreEqual(1, results.Length, "InstantiateAsync of one object must return one result.");
            var clone = m_Cleanup.Track(results[0]);
            var counter = clone.GetComponent<ProbeLifecycleCounter>();
            var atCompletion = counter.Describe();

            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            var afterFrames = counter.Describe();
            var parentIsStaging = clone.transform.parent == staging.transform;

            // Observation only: what happens once the async instance is reparented under an active parent.
            var layer = m_Cleanup.Track(new GameObject("ActiveLayer"));
            clone.transform.SetParent(layer.transform, false);
            var afterReparent = counter.Describe();

            ProbeLog.Write("P2", "case", "instantiateAsyncInactive",
                "framesToComplete", Time.frameCount - startFrame,
                "cloneActiveSelf", clone.activeSelf,
                "cloneActiveInHierarchy", clone.activeInHierarchy,
                "parentIsStaging", parentIsStaging,
                "eventsAtCompletion", atCompletion,
                "eventsAfterTwoFrames", afterFrames,
                "eventsRightAfterReparent", afterReparent);

            Assert.IsTrue(parentIsStaging, "Precondition: the async instance is parented under the staging object.");
            Assert.AreEqual("-", atCompletion, "Awake and OnEnable must not run at completion under an inactive parent.");
            Assert.AreEqual("-", afterFrames, "Awake and OnEnable must not run in later frames under an inactive parent.");
        }

        // The template lives under an inactive holder, so its own components never wake up.
        GameObject BuildTemplate()
        {
            var holder = BuildInactiveParent("PrefabHolder");
            var template = new GameObject("Template");
            template.transform.SetParent(holder.transform, false);
            template.AddComponent<ProbeLifecycleCounter>();
            return template;
        }

        GameObject BuildInactiveParent(string name)
        {
            var parent = m_Cleanup.Track(new GameObject(name));
            parent.SetActive(false);
            return parent;
        }
    }
}
