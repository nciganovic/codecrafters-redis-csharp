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

        public RedisStream GetOrCreateStream(string streamName)
        {
            RedisStream? redisStream = GetStream(streamName);
           
            if (redisStream == null)
            {
                redisStream = new RedisStream(streamName);
                AddStream(redisStream);
            }

            return redisStream;
        }

        public void AddEntryToStream(string streamName, RedisStreamEntry entry)
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

        public RedisStreamEntry GenerateValidEntry(RedisStream stream, string entryId, Dictionary<string, string> entries)
        {
            if(entryId == "*")
            {
                long currentTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds();
                int sameTimeStamps = stream.Entries.Where(x => x.CreatedAt == currentTimestamp).Count();
                
                entryId = $"{currentTimestamp}-{sameTimeStamps}";
            }
            else if (entryId.Split("-")[1] == "*")
            { 
                long timestamp = Convert.ToInt64(entryId.Split("-")[0]);
                int sameTimeStamps = stream.Entries.Where(x => x.CreatedAt == timestamp).Count();

                if (timestamp == 0 && sameTimeStamps == 0)
                    sameTimeStamps = 1;

                entryId = $"{timestamp}-{sameTimeStamps}";
            }
            else
            {
                long timestamp = Convert.ToInt64(entryId.Split("-")[0]);
                int sequence = Convert.ToInt32(entryId.Split("-")[1]);

                if (timestamp == 0 && sequence == 0)
                    sequence = 1;

                entryId = $"{timestamp}-{sequence}";
            }

            return new RedisStreamEntry(entryId, entries);
        }
    }

    public record RedisStream (string Name)
    {
        public string Name { get; private set; } = Name;

        public List<RedisStreamEntry> Entries = new List<RedisStreamEntry>();

        public List<RedisStreamEntry> GetEntriesInRange(string startStreamId, string endStreamId)
        {
            string[] startStreamInfo = startStreamId.Split('-');
            string[] endStreamInfo = endStreamId.Split('-');

            long startTimestamp = Convert.ToInt64(startStreamInfo[0]);
            long endTimestamp = Convert.ToInt64(endStreamInfo[0]);
            
            int startSequence = startStreamInfo.Length > 1 ? Convert.ToInt32(startStreamInfo[1]) : 0;
            int endSequence = endStreamInfo.Length > 1 ? Convert.ToInt32(endStreamInfo[1]) : int.MaxValue;

            return Entries.Where(entry =>
                entry.CreatedAt >= startTimestamp &&
                entry.CreatedAt <= endTimestamp &&
                entry.Sequence >= startSequence &&
                entry.Sequence <= endSequence)
                .ToList();
        }
    }

    public record RedisStreamEntry (string Id, Dictionary<string, string> Values)
    {
        public string Id { get; private set; } = Id;
        public long CreatedAt => Convert.ToInt64(Id.Split('-')[0]);
        public int Sequence => Convert.ToInt32(Id.Split('-')[1]);
        public Dictionary<string, string> Values { get; private set; } = Values;
    }
}
