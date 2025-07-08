using System.Collections.Concurrent;
using System.Reflection.Metadata.Ecma335;

namespace codecrafters_redis.src
{
    public class RedisDatabaseStored
    {
        private ConcurrentDictionary<string, StoredValue> _data = new();
        public void Set(string key, StoredValue value) => _data[key] = value;
        public StoredValue? Get(string key) => _data.TryGetValue(key, out var value) ? value : null;
        public StoredValue? Remove(string key) => _data.TryRemove(key, out var value) ? value : null;
        public ICollection<string> Keys => _data.Keys;
    }

    public class RedisStreamStorage
    { 
        private List<RedisStream> RedisStreams = new List<RedisStream>();

        public void AddStream(RedisStream stream)
        {
            RedisStreams.Add(stream);
        }

        public RedisStream? GetStream(string name)
        {
            return RedisStreams.FirstOrDefault(s => s.Name == name);
        }

        public bool StreamExists(string streamName)
        {
            return RedisStreams.Any(s => s.Name == streamName);
        }   

        public void AddEntryToStream(string streamName, RedisStreamEntries entry)
        {
            var stream = GetStream(streamName);
            if (stream != null)
            {
                stream.Entries.Add(entry);
            }
            else
            {
                throw new Exception($"Stream {streamName} does not exist.");
            }
        }
    }

    public record RedisStream (string Name)
    {
        public string Name { get; private set; } = Name;

        public List<RedisStreamEntries> Entries = new List<RedisStreamEntries>();
    }

    public record RedisStreamEntries (string Id, Dictionary<string, string> Values)
    {
        public string Id { get; private set; } = Id;
        public Dictionary<string, string> Values { get; private set; } = Values;
    }
}
