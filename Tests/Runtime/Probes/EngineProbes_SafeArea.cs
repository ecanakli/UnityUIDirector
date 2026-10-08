using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using UnityEngine.Assemblies;
#endif

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Screen.safeArea is readable and uGUI ships no SafeArea component.
    public sealed class EngineProbes_SafeArea
    {
        [Test]
        public void P19_SafeArea()
        {
            var safeArea = Screen.safeArea;
            var scanned = new List<string>();
            var offenders = new List<string>();

#if UNITY_6000_5_OR_NEWER
            var assemblies = CurrentAssemblies.GetLoadedAssemblies();
#else
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
#endif
            foreach (var assembly in assemblies)
            {
                var name = assembly.GetName().Name;
                if (!name.StartsWith("UnityEngine.UI", StringComparison.Ordinal) && !name.StartsWith("Unity.TextMeshPro", StringComparison.Ordinal))
                    continue;

                scanned.Add(name);
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = Array.FindAll(exception.Types, type => type != null);
                }

                foreach (var type in types)
                {
                    if (type.Name == "SafeArea")
                        offenders.Add(type.FullName + "@" + name);
                }
            }

            ProbeLog.Write("P19",
                "screen", Screen.width + "x" + Screen.height,
                "safeAreaX", safeArea.x,
                "safeAreaY", safeArea.y,
                "safeAreaWidth", safeArea.width,
                "safeAreaHeight", safeArea.height,
                "scannedAssemblies", scanned,
                "safeAreaTypes", offenders);

            if (scanned.Count == 0)
                Assert.Inconclusive("Neither UnityEngine.UI nor Unity.TextMeshPro is loaded, so there is nothing to scan.");

            Assert.IsFalse(float.IsNaN(safeArea.width) || float.IsNaN(safeArea.height), "Screen.safeArea must be a readable rect.");
            Assert.That(offenders, Is.Empty, "A SafeArea type exists in uGUI or TextMeshPro: " + string.Join(", ", offenders));
        }
    }
}
