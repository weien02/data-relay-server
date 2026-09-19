using System.Collections;
using System.Collections.Generic;

namespace DedicatedServer.Framework
{
    public class DictionaryList<TKey, TValue> : IEnumerable
    {
        private Dictionary<TKey, TValue> _dictionary;
        private List<TValue> _list;

        public int Count => this._list.Count;

        public DictionaryList()
        {
            this._dictionary = new Dictionary<TKey, TValue>();
            this._list = new List<TValue>();
        }

        public void Add(TKey key, TValue val)
        {
            this._dictionary.Add(key, val);
            this._list.Add(val);
        }

        public void Remove(TKey key)
        {
            TValue val = this._dictionary[key];
            this._dictionary.Remove(key);
            this._list.Remove(val);
        }

        public TValue Get(TKey key)
        {
            TValue result;
            if (!this._dictionary.TryGetValue(key, out result))
            {
                return default(TValue);
            }
            return result;
        }

        public TValue GetItemAt(int index)
        {
            return this._list[index];
        }

        public bool ContainsKey(TKey key)
        {
            return this._dictionary.ContainsKey(key);
        }

        public void Clear()
        {
            this._dictionary.Clear();
            this._list.Clear();
        }

        public bool TryGetValue(TKey key, out TValue val)
        {
            return this._dictionary.TryGetValue(key, out val);
        }

        public IEnumerator GetEnumerator()
        {
            return this._list.GetEnumerator();
        }

        public IEnumerator<TValue> GetGenericEnumerator()
        {
            return this._list.GetEnumerator();
        }
    }
}
