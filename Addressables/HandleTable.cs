using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Ecanakli.UIDirector
{
    // Instances and their asset handles by instance id; the id stays readable after the object is destroyed.
    internal sealed class HandleTable
    {
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        public int Count
        {
            get { return _entries.Count; }
        }

        public void Add(GameObject instance, AsyncOperationHandle<GameObject> asset)
        {
            if (ReferenceEquals(instance, null))
                throw new ArgumentNullException(nameof(instance));

            _entries[instance.GetInstanceID()] = new Entry(instance, asset);
        }

        // False for a C# null and for an instance this table does not hold.
        public bool TryTake(GameObject instance, out Entry entry)
        {
            entry = default;
            if (ReferenceEquals(instance, null))
                return false;

            int id = instance.GetInstanceID();
            if (!_entries.TryGetValue(id, out entry))
                return false;

            _entries.Remove(id);
            return true;
        }

        // Appends every entry to the list and empties the table.
        public void TakeAll(List<Entry> into)
        {
            foreach (KeyValuePair<int, Entry> pair in _entries)
                into.Add(pair.Value);

            _entries.Clear();
        }

        // One provider instance and the asset handle it was instantiated from.
        public readonly struct Entry
        {
            public readonly GameObject Instance;
            public readonly AsyncOperationHandle<GameObject> Asset;

            public Entry(GameObject instance, AsyncOperationHandle<GameObject> asset)
            {
                Instance = instance;
                Asset = asset;
            }
        }
    }
}
