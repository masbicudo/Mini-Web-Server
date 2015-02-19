using System.Collections.Generic;
using System.Linq;

namespace HttpFileServer
{
    public class ListDictionary<TKey, TValue>
    {
        struct Item
        {
            public int index;
            public TKey key;
            public TValue value;
        }

        private readonly List<Item> list = new List<Item>();
        private readonly Dictionary<TKey, Item> dictionary = new Dictionary<TKey, Item>();

        public void Add(TKey key, TValue value)
        {
            Item item;
            item.index = this.list.Count;
            item.key = key;
            item.value = value;
            this.list.Add(item);
            this.dictionary.Add(key, item);
        }

        public void Add(TValue value)
        {
            Item item;
            item.index = this.list.Count;
            item.key = default(TKey);
            item.value = value;
            this.list.Add(item);
        }

        public void Remove(TKey key)
        {
            Item item;
            if (this.dictionary.TryGetValue(key, out item))
            {
                this.dictionary.Remove(key);
                this.list.RemoveAt(item.index);
            }
        }

        public TValue[] ToArray()
        {
            return this.list.Where(i => i.index >= 0).Select(i => i.value).ToArray();
        }
    }
}