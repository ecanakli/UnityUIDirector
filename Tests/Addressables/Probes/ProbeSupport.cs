using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // One-line probe output: [PROBE id] key=value key=value
    public static class ProbeLog
    {
        public static void Emit(string probe, params object[] keyValues)
        {
            var builder = new StringBuilder();
            builder.Append("[PROBE ").Append(probe).Append(']');
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
                builder.Append(' ').Append(keyValues[i]).Append('=').Append(Format(keyValues[i + 1]));
            Debug.Log(builder.ToString());
        }

        static string Format(object value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case bool flag:
                    return flag ? "true" : "false";
                case float single:
                    return single.ToString("F3", CultureInfo.InvariantCulture);
                case double number:
                    return number.ToString("F3", CultureInfo.InvariantCulture);
                case string text:
                    text = text.Replace("\r", " ").Replace("\n", " ");
                    if (text.Length == 0)
                        return "\"\"";
                    return text.IndexOf(' ') >= 0 ? "\"" + text + "\"" : text;
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }

    // Records warning and failing log messages while a test runs.
    public sealed class ProbeLogCapture : IDisposable
    {
        readonly List<string> m_Failures = new List<string>();
        readonly List<string> m_Warnings = new List<string>();

        public ProbeLogCapture()
        {
            Application.logMessageReceived += OnMessage;
        }

        public IReadOnlyList<string> Failures => m_Failures;
        public IReadOnlyList<string> Warnings => m_Warnings;
        public string FailuresText => string.Join(" | ", m_Failures);
        public string WarningsText => string.Join(" | ", m_Warnings);

        public void Dispose()
        {
            Application.logMessageReceived -= OnMessage;
        }

        void OnMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                m_Warnings.Add(condition);
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_Failures.Add(type + ": " + condition);
        }
    }

    // How long a wait took, in frames and seconds.
    public sealed class WaitResult
    {
        public int Frames;
        public float Seconds;
        public bool TimedOut;
    }

    // Result of a call that may throw.
    public readonly struct CallOutcome
    {
        public readonly bool Threw;
        public readonly bool Value;
        public readonly string Error;

        public CallOutcome(bool value)
        {
            Threw = false;
            Value = value;
            Error = null;
        }

        public CallOutcome(Exception exception)
        {
            Threw = true;
            Value = false;
            Error = exception.GetType().Name + ": " + exception.Message;
        }
    }

    public static class ProbeSupport
    {
        // Polls once per frame; a frame count of 0 means the condition held at once.
        public static IEnumerator WaitUntil(Func<bool> condition, WaitResult result, float timeoutSeconds)
        {
            int startFrame = Time.frameCount;
            float startTime = Time.realtimeSinceStartup;
            result.TimedOut = false;

            while (!condition())
            {
                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                {
                    result.TimedOut = true;
                    break;
                }

                yield return null;
            }

            result.Frames = Time.frameCount - startFrame;
            result.Seconds = Time.realtimeSinceStartup - startTime;
        }

        public static CallOutcome Call(Func<bool> call)
        {
            try
            {
                return new CallOutcome(call());
            }
            catch (Exception exception)
            {
                return new CallOutcome(exception);
            }
        }

        public static CallOutcome Run(Action call)
        {
            try
            {
                call();
                return new CallOutcome(true);
            }
            catch (Exception exception)
            {
                return new CallOutcome(exception);
            }
        }
    }

    // Internal Addressables counters, read by reflection (4.0.1 has no public observable for them).
    public readonly struct OpSnapshot
    {
        public readonly int? OperationCache;
        public readonly int? InstanceOperations;
        public readonly int? TrackedHandles;

        OpSnapshot(int? operationCache, int? instanceOperations, int? trackedHandles)
        {
            OperationCache = operationCache;
            InstanceOperations = instanceOperations;
            TrackedHandles = trackedHandles;
        }

        public bool AnyAvailable => OperationCache.HasValue || InstanceOperations.HasValue || TrackedHandles.HasValue;

        public static OpSnapshot Take()
        {
            ResourceManager resourceManager = Addressables.ResourceManager;
            object impl = typeof(Addressables)
                .GetProperty("Instance", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null);

            return new OpSnapshot(
                ReadInt(resourceManager, "OperationCacheCount"),
                ReadInt(resourceManager, "InstanceOperationCount"),
                ReadInt(impl, "TrackedHandleCount"));
        }

        // Empty when every counter that is available in both snapshots is equal.
        public string Diff(OpSnapshot other)
        {
            var builder = new StringBuilder();
            AppendDiff(builder, "operationCache", OperationCache, other.OperationCache);
            AppendDiff(builder, "instanceOperations", InstanceOperations, other.InstanceOperations);
            AppendDiff(builder, "trackedHandles", TrackedHandles, other.TrackedHandles);
            return builder.ToString();
        }

        public bool Moved(OpSnapshot other)
        {
            return OperationCache != other.OperationCache
                || InstanceOperations != other.InstanceOperations
                || TrackedHandles != other.TrackedHandles;
        }

        public override string ToString()
        {
            return "cache=" + Text(OperationCache) + ",instances=" + Text(InstanceOperations) + ",tracked=" + Text(TrackedHandles);
        }

        static void AppendDiff(StringBuilder builder, string name, int? left, int? right)
        {
            if (left.HasValue && right.HasValue && left.Value != right.Value)
                builder.Append(name).Append(' ').Append(left.Value).Append("->").Append(right.Value).Append(';');
        }

        static string Text(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "n/a";
        }

        static int? ReadInt(object target, string propertyName)
        {
            if (target == null)
                return null;

            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
                return null;

            try
            {
                return property.GetValue(target) as int?;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
