using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Unity message order for destroy, deactivate and destroy-from-OnDisable.
    public sealed class EngineProbes_MessageOrder
    {
        sealed class Subject
        {
            public GameObject Parent;
            public GameObject Child;
            public ProbeMessageRecorder Recorder;
        }

        sealed class Case
        {
            public readonly string Name;
            public readonly bool IsDestroy;
            public readonly Action<Subject> Act;

            public Case(string name, bool isDestroy, Action<Subject> act)
            {
                Name = name;
                IsDestroy = isDestroy;
                Act = act;
            }
        }

        static readonly string[] s_SnapshotKeys =
        {
            "activeInHierarchy", "activeSelf", "enabled", "isActiveAndEnabled", "sceneIsLoaded", "sceneIsValid", "destroyTokenCancelled",
        };

        readonly ProbeCleanup m_Cleanup = new ProbeCleanup();

        [TearDown]
        public void TearDown()
        {
            m_Cleanup.DestroyAll();
        }

        [UnityTest]
        public IEnumerator P6_Destroy_CallsOnDisableThenOnDestroy()
        {
            var sink = new List<string>();
            var subject = BuildSubject();
            subject.Recorder.Sink = sink;

            Object.Destroy(subject.Child);
            var immediately = new List<string>(sink);

            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            // Observation only: the immediate variant.
            var immediateSink = new List<string>();
            var immediateSubject = BuildSubject();
            immediateSubject.Recorder.Sink = immediateSink;
            Object.DestroyImmediate(immediateSubject.Child);

            ProbeLog.Write("P6", "case", "destroy",
                "eventsRightAfterDestroy", immediately,
                "eventsAfterTwoFrames", sink,
                "destroyImmediateEvents", immediateSink);

            CollectionAssert.AreEqual(new[] { "OnDisable", "OnDestroy" }, sink, "Destroy must call OnDisable and then OnDestroy.");
        }

        [UnityTest]
        public IEnumerator P6_ParentDeactivated_CallsChildOnDisable_NotOnDestroy()
        {
            var sink = new List<string>();
            var subject = BuildSubject();
            subject.Recorder.Sink = sink;

            subject.Parent.SetActive(false);
            var immediately = new List<string>(sink);

            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            ProbeLog.Write("P6", "case", "parentDeactivated",
                "eventsRightAfterSetActive", immediately,
                "eventsAfterTwoFrames", sink,
                "childAlive", subject.Child != null);

            CollectionAssert.AreEqual(new[] { "OnDisable" }, sink, "Deactivating the parent must call the child's OnDisable and not OnDestroy.");
            Assert.IsTrue(subject.Child != null, "The child must still exist.");
        }

        [UnityTest]
        public IEnumerator P6_DestroyFromInsideOnDisable_IsSafe()
        {
            var sink = new List<string>();
            var subject = BuildSubject();
            subject.Recorder.Sink = sink;
            subject.Recorder.DestroyGameObjectOnDisable = true;

            subject.Parent.SetActive(false);
            foreach (var step in ProbeUi.Frames(2))
                yield return step;

            var childGone = subject.Child == null;
            ProbeLog.Write("P6", "case", "destroyInOnDisable",
                "events", sink,
                "childGoneAfterTwoFrames", childGone);

            Assert.IsTrue(childGone, "The child destroyed from inside OnDisable must be gone after a frame.");
            Assert.AreEqual(1, sink.FindAll(entry => entry == "OnDisable").Count, "OnDisable must run once.");
        }

        [UnityTest]
        public IEnumerator P6_OnDisable_DistinguishesDestroyFromDeactivate()
        {
            var cases = new List<Case>
            {
                new Case("SetActiveFalse", false, subject => subject.Child.SetActive(false)),
                new Case("ParentSetActiveFalse", false, subject => subject.Parent.SetActive(false)),
                new Case("ComponentDisabled", false, subject => subject.Recorder.enabled = false),
                new Case("DestroyGameObject", true, subject => Object.Destroy(subject.Child)),
                new Case("DestroyComponent", true, subject => Object.Destroy(subject.Recorder)),
                new Case("DestroyImmediateGameObject", true, subject => Object.DestroyImmediate(subject.Child)),
                new Case("DestroyParent", true, subject => Object.Destroy(subject.Parent)),
            };

            var deactivateValues = new Dictionary<string, HashSet<string>>();
            var destroyValues = new Dictionary<string, HashSet<string>>();
            foreach (var key in s_SnapshotKeys)
            {
                deactivateValues[key] = new HashSet<string>();
                destroyValues[key] = new HashSet<string>();
            }

            foreach (var testCase in cases)
            {
                var subject = BuildSubject();
                var recorder = subject.Recorder;
                testCase.Act(subject);

                foreach (var step in ProbeUi.Frames(2))
                    yield return step;

                var fields = new List<object> { "case", "onDisableState", "scenario", testCase.Name, "kind", testCase.IsDestroy ? "destroy" : "deactivate" };
                if (recorder.DisableSnapshots.Count == 0)
                {
                    fields.Add("onDisable");
                    fields.Add("notCalled");
                }
                else
                {
                    var snapshot = recorder.DisableSnapshots[0];
                    var target = testCase.IsDestroy ? destroyValues : deactivateValues;
                    foreach (var key in s_SnapshotKeys)
                    {
                        fields.Add(key);
                        fields.Add(snapshot[key]);
                        target[key].Add(snapshot[key]);
                    }
                }

                ProbeLog.Write("P6", fields.ToArray());
            }

            var separating = new List<string>();
            foreach (var key in s_SnapshotKeys)
            {
                if (deactivateValues[key].Count > 0 && destroyValues[key].Count > 0 && !deactivateValues[key].Overlaps(destroyValues[key]))
                    separating.Add(key);
            }

            ProbeLog.Write("P6", "case", "distinguishSummary", "separatingKeys", separating);
        }

        Subject BuildSubject()
        {
            var parent = m_Cleanup.Track(new GameObject("Parent"));
            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform, false);
            var recorder = child.AddComponent<ProbeMessageRecorder>();
            return new Subject { Parent = parent, Child = child, Recorder = recorder };
        }
    }
}
