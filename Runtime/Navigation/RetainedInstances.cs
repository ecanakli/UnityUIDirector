using System.Collections.Generic;
using System.Text;

namespace Ecanakli.UIDirector
{
    // One idle kept instance per view id: stash, take, drop, trim. It never knows why a view closed.
    internal sealed class RetainedInstances
    {
        private readonly Dictionary<string, object> _byId = new Dictionary<string, object>();

        public int Count
        {
            get { return _byId.Count; }
        }

        public bool Contains(string viewId)
        {
            return viewId != null && _byId.ContainsKey(viewId);
        }

        // Keeps the instance for the id; false when the slot of that id is taken, so the caller releases it instead.
        public bool TryStash(string viewId, object instance)
        {
            if (viewId == null || instance == null || _byId.ContainsKey(viewId))
                return false;

            _byId.Add(viewId, instance);
            return true;
        }

        // Hands the kept instance of the id to a new open and forgets it.
        public bool TryTake(string viewId, out object instance)
        {
            if (viewId != null && _byId.TryGetValue(viewId, out instance))
            {
                _byId.Remove(viewId);
                return true;
            }

            instance = null;
            return false;
        }

        // Forgets a kept instance that was lost outside the package; false when it is not kept.
        public bool TryDrop(object instance, out string viewId)
        {
            viewId = null;
            if (instance == null)
                return false;

            foreach (var pair in _byId)
            {
                if (ReferenceEquals(pair.Value, instance))
                {
                    viewId = pair.Key;
                    break;
                }
            }

            return viewId != null && _byId.Remove(viewId);
        }

        // Moves every kept instance into output and forgets them; the caller releases them.
        public void DrainTo(List<object> output)
        {
            foreach (var pair in _byId)
                output.Add(pair.Value);

            _byId.Clear();
        }

        // Moves the kept instance of the id into output and forgets it; used when its row is removed.
        public bool DrainTo(string viewId, List<object> output)
        {
            object instance;
            if (!TryTake(viewId, out instance))
                return false;

            output.Add(instance);
            return true;
        }

        // Clears into and copies the ids of the kept instances; only reads.
        internal void CaptureViewIds(List<string> into)
        {
            into.Clear();
            foreach (var pair in _byId)
                into.Add(pair.Key);
        }

        public void Describe(StringBuilder output)
        {
            output.Append("kept instances: ").Append(_byId.Count);
            foreach (var pair in _byId)
                output.Append(' ').Append(pair.Key);
            output.Append('\n');
        }
    }
}
