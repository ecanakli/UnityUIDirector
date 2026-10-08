using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Shared fixture: initializes Addressables once, owns an inactive staging parent and an active root.
    public abstract class AddressablesProbeFixture
    {
        // One InstantiateAsync request and what came out of it.
        public sealed class Spawn
        {
            public AsyncOperationHandle<GameObject> Handle;
            public GameObject Instance;
            public ProbeView View;
            public bool SyncDone;
            public WaitResult Wait = new WaitResult();
        }

        readonly List<AsyncOperationHandle> m_Handles = new List<AsyncOperationHandle>();
        string m_EnvironmentError;

        protected GameObject Staging { get; private set; }
        protected GameObject Root { get; private set; }
        protected ProbeLogCapture Logs { get; private set; }

        [UnitySetUp]
        public IEnumerator ProbeSetUp()
        {
            Logs = new ProbeLogCapture();
            Staging = new GameObject("ProbeStaging");
            Staging.SetActive(false);
            Root = new GameObject("ProbeRoot");

            m_EnvironmentError = CheckEnvironment();
            if (m_EnvironmentError != null)
                yield break;

            // auto-released handle; an invalid handle reports IsDone, so the wait ends in both cases
            var init = Addressables.InitializeAsync();
            var wait = new WaitResult();
            yield return ProbeSupport.WaitUntil(() => init.IsDone, wait, 30f);

            if (wait.TimedOut)
                m_EnvironmentError = "Addressables initialization did not complete in 30 s";
            else if (!Addressables.ResourceLocators.Any())
                m_EnvironmentError = "Addressables initialized without a resource locator";

            // let deferred completion callbacks run so counters start from a quiet state
            yield return null;
            yield return null;
        }

        [TearDown]
        public void ProbeTearDown()
        {
            for (int i = m_Handles.Count - 1; i >= 0; i--)
            {
                AsyncOperationHandle handle = m_Handles[i];
                if (!handle.IsValid())
                    continue;

                try
                {
                    Addressables.Release(handle);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Probe teardown could not release a handle: " + exception.Message);
                }
            }

            m_Handles.Clear();

            if (Root != null)
                Object.DestroyImmediate(Root);
            if (Staging != null)
                Object.DestroyImmediate(Staging);

            if (Logs != null)
            {
                Logs.Dispose();
                Logs = null;
            }
        }

        // Marks the test inconclusive when the temporary content or play mode script is not in place.
        protected void RequireEnvironment()
        {
            if (m_EnvironmentError != null)
                Assert.Inconclusive(m_EnvironmentError);
        }

        // Handles tracked here are released in teardown if the test did not release them.
        protected AsyncOperationHandle<T> Track<T>(AsyncOperationHandle<T> handle)
        {
            m_Handles.Add(handle);
            return handle;
        }

        protected IEnumerator WaitDone(AsyncOperationHandle handle, WaitResult wait, float timeoutSeconds = 10f)
        {
            yield return ProbeSupport.WaitUntil(() => handle.IsDone, wait, timeoutSeconds);
        }

        // Instantiates the view (or another address) with tracking off and waits for completion.
        protected IEnumerator SpawnView(Spawn spawn, Transform parent, string address = null, bool trackHandle = false)
        {
            spawn.Handle = Addressables.InstantiateAsync(
                address ?? ProbeContent.ViewAddress,
                parent: parent,
                instantiateInWorldSpace: false,
                trackHandle: trackHandle);
            Track(spawn.Handle);
            spawn.SyncDone = spawn.Handle.IsDone;

            yield return WaitDone(spawn.Handle, spawn.Wait);

            Assert.IsFalse(spawn.Wait.TimedOut, "InstantiateAsync did not complete in time");
            Assert.AreEqual(AsyncOperationStatus.Succeeded, spawn.Handle.Status, "InstantiateAsync failed");
            spawn.Instance = spawn.Handle.Result;
            spawn.View = spawn.Instance.GetComponent<ProbeView>();
        }

        // Parenting under the active root makes Awake and OnEnable run.
        protected void Activate(Spawn spawn)
        {
            spawn.Instance.transform.SetParent(Root.transform, false);
        }

        // Load delay the Asset Database mode adds to each load; -1 when it cannot be read.
        protected static float SimulatedLoadDelay()
        {
#if UNITY_EDITOR
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings != null ? settings.SimulatedLoadDelay : -1f;
#else
            return -1f;
#endif
        }

        static string CheckEnvironment()
        {
#if UNITY_EDITOR
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return "No Addressables settings exist; the probe prebuild setup did not run";
            if (!(settings.ActivePlayModeDataBuilder is BuildScriptFastMode))
                return "The active play mode script is not Use Asset Database";

            string guid = ProbeContentLookup.ViewGuid();
            if (string.IsNullOrEmpty(guid) || settings.FindAssetEntry(guid) == null)
                return "The probe view prefab is not an Addressables entry";
            return null;
#else
            return "The probes need the editor (Use Asset Database play mode script)";
#endif
        }
    }
}
