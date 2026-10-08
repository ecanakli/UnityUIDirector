using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ecanakli.UIDirector.Tests.Probes
{
    // Writes one "[PROBE <id>] key=value ..." line per observation.
    internal static class ProbeLog
    {
        public static void Write(string id, params object[] keyValues)
        {
            var builder = new StringBuilder();
            builder.Append("[PROBE ").Append(id).Append(']');
            for (var i = 0; i + 1 < keyValues.Length; i += 2)
                builder.Append(' ').Append(keyValues[i]).Append('=').Append(Format(keyValues[i + 1]));
            UnityEngine.Debug.Log(builder.ToString());
        }

        public static string Format(object value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case bool flag:
                    return flag ? "true" : "false";
                case float single:
                    return single.ToString("0.###", CultureInfo.InvariantCulture);
                case double number:
                    return number.ToString("0.###", CultureInfo.InvariantCulture);
                case string text:
                    return text.IndexOfAny(new[] { ' ', '=' }) >= 0 ? "\"" + text + "\"" : text;
                case IEnumerable sequence:
                {
                    var parts = new List<string>();
                    foreach (var item in sequence)
                        parts.Add(Format(item));
                    return parts.Count == 0 ? "-" : string.Join(",", parts);
                }
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
