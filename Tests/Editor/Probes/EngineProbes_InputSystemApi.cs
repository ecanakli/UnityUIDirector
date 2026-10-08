using System;
using System.Reflection;
using NUnit.Framework;
#if UNITY_6000_5_OR_NEWER
using UnityEngine.Assemblies;
#endif

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Input System API surface checked by reflection; this assembly has no Input System reference.
    public sealed class EngineProbes_InputSystemApi
    {
        [Test]
        public void P21_SingleUnifiedPointerExists()
        {
            var behaviorType = FindType("UnityEngine.InputSystem.UI.UIPointerBehavior");
            var moduleType = FindType("UnityEngine.InputSystem.UI.InputSystemUIInputModule");

            var behaviorNames = behaviorType != null && behaviorType.IsEnum ? Enum.GetNames(behaviorType) : new string[0];
            var property = moduleType?.GetProperty("pointerBehavior", BindingFlags.Public | BindingFlags.Instance);

            ProbeLog.Write("P21",
                "behaviorTypeFound", behaviorType != null,
                "moduleTypeFound", moduleType != null,
                "behaviorValues", behaviorNames,
                "pointerBehaviorPropertyFound", property != null,
                "pointerBehaviorPropertyType", property != null ? property.PropertyType.FullName : "n/a",
                "pointerBehaviorCanWrite", property != null && property.CanWrite);

            if (behaviorType == null && moduleType == null)
                Assert.Ignore("The Input System package is not installed in this project.");

            Assert.IsNotNull(behaviorType, "UnityEngine.InputSystem.UI.UIPointerBehavior was not found.");
            Assert.IsNotNull(moduleType, "UnityEngine.InputSystem.UI.InputSystemUIInputModule was not found.");
            CollectionAssert.Contains(behaviorNames, "SingleUnifiedPointer", "UIPointerBehavior must contain SingleUnifiedPointer.");
            Assert.IsNotNull(property, "InputSystemUIInputModule must have a pointerBehavior property.");
            Assert.AreEqual(behaviorType, property.PropertyType, "pointerBehavior must be of type UIPointerBehavior.");
        }

        static Type FindType(string fullName)
        {
#if UNITY_6000_5_OR_NEWER
            var assemblies = CurrentAssemblies.GetLoadedAssemblies();
#else
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
#endif
            foreach (var assembly in assemblies)
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
