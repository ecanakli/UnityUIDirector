using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.UIDirector.Tests
{
    // Records every console line from construction until Dispose, so a test can count lines it does not LogAssert one by one.
    internal sealed class LogRecorder : IDisposable
    {
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly bool _changedIgnore;
        private readonly bool _previousIgnore;
        private bool _disposed;

        // With letFailingMessagesThrough, error lines do not fail the test until Dispose restores the previous setting.
        public LogRecorder(bool letFailingMessagesThrough = false)
        {
            if (letFailingMessagesThrough)
            {
                _previousIgnore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;
                _changedIgnore = true;
            }

            Application.logMessageReceived += OnLogMessage;
        }

        public IReadOnlyList<Entry> Entries
        {
            get { return _entries; }
        }

        // Lines of the given type whose text contains the fragment (ordinal).
        public int Count(LogType type, string fragment)
        {
            var count = 0;
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.Type == type && Contains(entry.Message, fragment))
                    count++;
            }

            return count;
        }

        // Lines of any type whose text contains the fragment (ordinal).
        public int Count(string fragment)
        {
            var count = 0;
            for (var i = 0; i < _entries.Count; i++)
            {
                if (Contains(_entries[i].Message, fragment))
                    count++;
            }

            return count;
        }

        // Every recorded line, one per row, for assertion messages.
        public string Describe()
        {
            if (_entries.Count == 0)
                return "(no log lines)";

            var text = new StringBuilder();
            for (var i = 0; i < _entries.Count; i++)
                text.Append(_entries[i].Type).Append(": ").Append(_entries[i].Message).Append('\n');

            return text.ToString();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Application.logMessageReceived -= OnLogMessage;
            if (_changedIgnore)
                LogAssert.ignoreFailingMessages = _previousIgnore;
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            _entries.Add(new Entry(type, condition));
        }

        private static bool Contains(string message, string fragment)
        {
            return message != null && fragment != null && message.IndexOf(fragment, StringComparison.Ordinal) >= 0;
        }

        public readonly struct Entry
        {
            public readonly LogType Type;
            public readonly string Message;

            public Entry(LogType type, string message)
            {
                Type = type;
                Message = message;
            }
        }
    }
}
