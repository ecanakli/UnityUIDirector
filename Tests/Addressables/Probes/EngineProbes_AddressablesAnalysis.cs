#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.AnalyzeRules;
using UnityEngine.AddressableAssets;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // What the package offers in place of the "Check Duplicate Bundle Dependencies" analyze rule. Logs only.
    public sealed class EngineProbes_AddressablesAnalysis
    {
        [Test]
        public void P20_DuplicateDependencyAnalysis_Name()
        {
            // the rule list is internal; a registered rule appears in the Analyze window
            var registered = new List<string>();
            PropertyInfo rulesProperty = typeof(AnalyzeSystem).GetProperty("Rules", BindingFlags.NonPublic | BindingFlags.Static);
            if (rulesProperty != null && rulesProperty.GetValue(null) is IEnumerable list)
            {
                foreach (object entry in list)
                {
                    if (entry is AnalyzeRule registeredRule)
                        registered.Add(registeredRule.ruleName);
                }
            }

            AnalyzeRule[] rules =
            {
                new CheckBundleDupeDependencies(),
                new CheckResourcesDupeDependencies(),
                new CheckSceneDupeDependencies(),
            };

            foreach (AnalyzeRule rule in rules)
            {
                Type type = rule.GetType();
                ProbeLog.Emit(
                    "P20",
                    "test", "DuplicateDependencyAnalysis_Name",
                    "addressablesVersion", Addressables.Version,
                    "ruleName", rule.ruleName,
                    "type", type.FullName,
                    "canFix", rule.CanFix,
                    "obsolete", Attribute.IsDefined(type, typeof(ObsoleteAttribute)),
                    "registeredInAnalyzeSystem", registered.Contains(rule.ruleName),
                    "analyzeSystemRulesListRead", rulesProperty != null);
            }

            Assembly editorAssembly = typeof(AnalyzeSystem).Assembly;
            ProbeLog.Emit(
                "P20",
                "test", "DuplicateDependencyAnalysis_Name",
                "analyzeWindowType", editorAssembly.GetType("UnityEditor.AddressableAssets.GUI.AnalyzeWindow") != null,
                "analyzeMenuFromSource", "Window/Asset Management/Addressables/Analyze",
                "buildReportWindowType", editorAssembly.GetType("UnityEditor.AddressableAssets.BuildReportVisualizer.BuildReportWindow") != null,
                "duplicatedAssetsViewType", editorAssembly.GetType("UnityEditor.AddressableAssets.BuildReportVisualizer.DuplicatedAssetsContentView") != null,
                "buildReportMenuFromSource", "Window/Asset Management/Addressables/Addressables Report",
                "registeredRuleCount", registered.Count);
        }
    }
}
#endif
