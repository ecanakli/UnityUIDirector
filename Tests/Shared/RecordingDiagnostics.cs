using System.Collections.Generic;
using System.Text;

namespace Ecanakli.UIDirector.Tests
{
    // A Diagnostics with a hook that keeps every entry and counts them by id.
    internal sealed class RecordingDiagnostics
    {
        private readonly List<DiagnosticEntry> _entries = new List<DiagnosticEntry>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        private int _faults;

        public RecordingDiagnostics()
        {
            Diagnostics = new Diagnostics();
            Diagnostics.Hook = Record;
        }

        // Pass this to the navigator, the clock and any component that reports.
        public Diagnostics Diagnostics { get; }

        public IReadOnlyList<DiagnosticEntry> Entries
        {
            get { return _entries; }
        }

        public int Total
        {
            get { return _entries.Count; }
        }

        // Internal faults carry no id.
        public int FaultCount
        {
            get { return _faults; }
        }

        // How many entries carried the id; a null id counts internal faults.
        public int Count(string id)
        {
            if (id == null)
                return _faults;

            int count;
            return _counts.TryGetValue(id, out count) ? count : 0;
        }

        public bool Has(string id)
        {
            return Count(id) > 0;
        }

        public void Clear()
        {
            _entries.Clear();
            _counts.Clear();
            _faults = 0;
        }

        public string Describe()
        {
            var text = new StringBuilder("diagnostics: ").Append(_entries.Count);
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                text.Append("\n  ").Append(entry.Id ?? "(fault)").Append(entry.IsError ? " error " : " warning ").Append(entry.Message);
            }

            return text.ToString();
        }

        private void Record(DiagnosticEntry entry)
        {
            _entries.Add(entry);

            if (entry.Id == null)
            {
                _faults++;
                return;
            }

            int count;
            _counts.TryGetValue(entry.Id, out count);
            _counts[entry.Id] = count + 1;
        }
    }
}
