using System.Collections.Generic;

namespace IceCoast.Simulation.Core
{
    /// <summary>
    /// Minimal binary min-heap keyed by float priority. .NET Standard 2.1 (Unity's API level)
    /// has no PriorityQueue, so we bring our own. Duplicates are allowed; callers skip stale entries.
    /// </summary>
    public sealed class MinHeap<T>
    {
        private readonly List<(float priority, T item)> _items = new List<(float, T)>();

        public int Count => _items.Count;

        public void Clear() => _items.Clear();

        public void Push(T item, float priority)
        {
            _items.Add((priority, item));
            int i = _items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_items[parent].priority <= _items[i].priority) break;
                (_items[parent], _items[i]) = (_items[i], _items[parent]);
                i = parent;
            }
        }

        public T Pop(out float priority)
        {
            var root = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);

            int i = 0;
            int n = _items.Count;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, smallest = i;
                if (l < n && _items[l].priority < _items[smallest].priority) smallest = l;
                if (r < n && _items[r].priority < _items[smallest].priority) smallest = r;
                if (smallest == i) break;
                (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                i = smallest;
            }

            priority = root.priority;
            return root.item;
        }
    }
}
