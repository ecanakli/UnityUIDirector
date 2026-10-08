using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Prefab Mode's UI Environment setting exists. Looked up by reflection so a missing member fails the test, not the compile.
    public sealed class EngineProbes_EditorSettings
    {
        [Test]
        public void P16_PrefabUiEnvironmentSettingExists()
        {
            var property = typeof(EditorSettings).GetProperty("prefabUIEnvironment", BindingFlags.Public | BindingFlags.Static);

            var currentValue = "n/a";
            if (property != null && property.CanRead)
            {
                var value = property.GetValue(null) as UnityEngine.Object;
                currentValue = value != null ? value.name : "none";
            }

            ProbeLog.Write("P16",
                "propertyFound", property != null,
                "propertyType", property != null ? property.PropertyType.FullName : "n/a",
                "canRead", property != null && property.CanRead,
                "canWrite", property != null && property.CanWrite,
                "currentValue", currentValue);

            Assert.IsNotNull(property, "UnityEditor.EditorSettings.prefabUIEnvironment does not exist.");
            Assert.IsTrue(property.CanRead, "UnityEditor.EditorSettings.prefabUIEnvironment must be readable.");
        }
    }
}
