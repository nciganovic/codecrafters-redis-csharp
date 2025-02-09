using System.Collections.Concurrent;

namespace codecrafters_redis.src
{
    public class RedisDatabase
    {
        private ConcurrentDictionary<string, ItemValue> _data = new();
        public void Set(string key, ItemValue value) => _data[key] = value;
        public ItemValue Get(string key) => _data.TryGetValue(key, out var value) ? value : ItemValue.InvalidItem;
        public ItemValue Remove(string key) => _data.TryRemove(key, out var value) ? value : ItemValue.InvalidItem;
        public ICollection<string> Keys => _data.Keys;
    }
}
