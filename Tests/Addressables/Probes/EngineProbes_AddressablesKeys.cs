using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Which key forms InstantiateAsync resolves.
    [PrebuildSetup(typeof(ProbeContentSetup))]
    [PostBuildCleanup(typeof(ProbeContentSetup))]
    public sealed class EngineProbes_AddressablesKeys : AddressablesProbeFixture
    {
        [UnityTest]
        public IEnumerator P20_InstantiateAsync_KeyForms()
        {
            RequireEnvironment();

            string guid = ProbeContentLookup.ViewGuid();
            if (string.IsNullOrEmpty(guid))
                Assert.Inconclusive("The GUID of the temp prefab can only be read in the editor");

            var assetReference = new AssetReference(guid);
            var forms = new (string name, object key)[]
            {
                ("address", ProbeContent.ViewAddress),
                ("guid", guid),
                ("assetReference", assetReference),
            };

            var results = new List<KeyFormResult>();

            // a key that does not resolve logs an error through the exception handler, so errors are counted, not failed on
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                foreach (var form in forms)
                {
                    var result = new KeyFormResult { Name = form.name };
                    int failuresBefore = Logs.Failures.Count;

                    AsyncOperationHandle<GameObject> handle = Track(
                        Addressables.InstantiateAsync(form.key, Staging.transform, false, false));
                    result.CompletedSync = handle.IsDone;

                    var wait = new WaitResult();
                    yield return WaitDone(handle, wait);
                    result.TimedOut = wait.TimedOut;
                    result.Frames = wait.Frames;

                    if (handle.IsValid() && handle.IsDone)
                    {
                        result.Status = handle.Status.ToString();
                        result.Error = handle.OperationException == null
                            ? "none"
                            : handle.OperationException.GetType().Name;
                        result.InstanceFound = handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null;
                    }
                    else
                    {
                        result.Status = "NotDone";
                    }

                    if (handle.IsValid())
                    {
                        if (result.InstanceFound)
                            Addressables.ReleaseInstance(handle);
                        else
                            Addressables.Release(handle);
                    }

                    result.ErrorLogs = Logs.Failures.Count - failuresBefore;
                    results.Add(result);
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }

            KeyFormResult address = results[0];
            KeyFormResult guidForm = results[1];
            KeyFormResult reference = results[2];

            ProbeLog.Emit(
                "P20",
                "test", "InstantiateAsync_KeyForms",
                "addressablesVersion", Addressables.Version,
                "assetReferenceRuntimeKeyIsGuid", guid.Equals(assetReference.RuntimeKey),
                "assetReferenceRuntimeKeyIsValid", assetReference.RuntimeKeyIsValid(),
                "address", address.Status,
                "addressError", address.Error,
                "addressErrorLogs", address.ErrorLogs,
                "addressCompletedSync", address.CompletedSync,
                "guid", guidForm.Status,
                "guidError", guidForm.Error,
                "guidErrorLogs", guidForm.ErrorLogs,
                "guidCompletedSync", guidForm.CompletedSync,
                "assetReference", reference.Status,
                "assetReferenceError", reference.Error,
                "assetReferenceErrorLogs", reference.ErrorLogs,
                "assetReferenceCompletedSync", reference.CompletedSync);

            Assert.IsFalse(address.TimedOut, "The address form did not complete in time");
            Assert.AreEqual(nameof(AsyncOperationStatus.Succeeded), address.Status, "The address string did not resolve");
            Assert.IsTrue(address.InstanceFound, "The address form produced no instance");
            Assert.AreEqual(0, address.ErrorLogs, "The address form logged errors");
        }

        sealed class KeyFormResult
        {
            public string Name;
            public string Status = "unknown";
            public string Error = "none";
            public bool CompletedSync;
            public bool InstanceFound;
            public bool TimedOut;
            public int Frames;
            public int ErrorLogs;
        }
    }
}
