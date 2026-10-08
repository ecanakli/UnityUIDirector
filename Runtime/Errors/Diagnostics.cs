using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    // One diagnostic as seen by a test hook; Id is null for an internal fault.
    internal readonly struct DiagnosticEntry
    {
        public readonly string Id;
        public readonly bool IsError;
        public readonly string Message;

        public DiagnosticEntry(string id, bool isError, string message)
        {
            Id = id;
            IsError = isError;
            Message = message;
        }
    }

    // Logs the stable diagnostics; an installed hook replaces the console so tests can count them.
    internal sealed class Diagnostics
    {
        private static readonly Dictionary<string, string> Suffixes = new Dictionary<string, string>(StringComparer.Ordinal);

        public Action<DiagnosticEntry> Hook { get; set; }

        public void Warn(string id, string message)
        {
            Emit(new DiagnosticEntry(id, false, message));
        }

        public void Error(string id, string message)
        {
            Emit(new DiagnosticEntry(id, true, message));
        }

        // For a fault inside the package itself; never throws.
        public void ReportFault(string location, Exception exception)
        {
            try
            {
                var hook = Hook;
                if (hook != null)
                {
                    hook(new DiagnosticEntry(null, true, location + ": " + exception));
                    return;
                }

                Debug.LogException(exception);
            }
            catch (Exception)
            {
                // Nothing left to report to.
            }
        }

        private void Emit(DiagnosticEntry entry)
        {
            try
            {
                var hook = Hook;
                if (hook != null)
                {
                    hook(entry);
                    return;
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var text = string.Concat(entry.Id, ": ", entry.Message, AnchorSuffix(entry.Id));
                if (entry.IsError)
                    Debug.LogError(text);
                else
                    Debug.LogWarning(text);
#endif
            }
            catch (Exception)
            {
                // A failing hook or logger must not break the caller.
            }
        }

        // " (see Troubleshooting#uidirector1xx)", built once per id so a console line costs one string as before.
        private static string AnchorSuffix(string id)
        {
            if (string.IsNullOrEmpty(id))
                return string.Empty;

            lock (Suffixes)
            {
                string suffix;
                if (!Suffixes.TryGetValue(id, out suffix))
                {
                    suffix = " (see Troubleshooting#" + id.ToLowerInvariant() + ")";
                    Suffixes.Add(id, suffix);
                }

                return suffix;
            }
        }
    }
}
