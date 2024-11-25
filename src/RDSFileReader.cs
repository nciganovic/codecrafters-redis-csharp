using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace codecrafters_redis.src
{
    public class RDSFileReader
    {
        private const byte DB_START = 0xFE;
        private const byte DB_INDICATOR = 0xFB;
        private const byte EXPIRY_IN_SECONDS = 0xFD;
        private const byte EXPIRY_IN_MILISECONDS = 0xFC;
        private const int STRING_VALUE_TYPE = 0;

        private readonly string fileLocation;

        public Dictionary<string, ItemValue> rdsDatabse  { get; private set; } 

        public RDSFileReader(string fileLocation, bool loadItems = false)
        {
            this.fileLocation = fileLocation;
            rdsDatabse = new Dictionary<string, ItemValue>();

            if(loadItems)
                LoadItems();
        }

        public void LoadItems()
        {
            using (FileStream fileStream = new FileStream(fileLocation, FileMode.Open, FileAccess.Read))
            {
                using (BinaryReader reader = new BinaryReader(fileStream))
                {
                    var header = new string(reader.ReadChars(9));
                    if (header != "REDIS0011")
                        throw new Exception("Header not equal to REDIS0011");

                    while (true)
                    {
                        // find the database section
                        byte dbStart = reader.ReadByte();
                        if (dbStart == DB_START)
                        {
                            ReadDataBaseSection(reader);
                            break;
                        }
                    }
                }
            }
        }

        private void ReadDataBaseSection(BinaryReader reader)
        {
            uint dbNumber = reader.ReadByte();
            byte indicator = reader.ReadByte();

            if (indicator != DB_INDICATOR)
                throw new Exception("No database indicator");

            int hashTableSize = reader.ReadByte();
            int expireHashTableSize = reader.ReadByte();

            while (hashTableSize > 0)
            { 
                int nextByte = reader.ReadByte();

                if (nextByte == EXPIRY_IN_MILISECONDS)
                {
                    byte[] bytes = reader.ReadBytes(8);
                    long timestamp = BitConverter.ToInt64(bytes, 0);
                    long unixTime = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds() * 1000;

                    int valueType = reader.ReadByte();
                    if (valueType != STRING_VALUE_TYPE)
                        throw new Exception("Only string value type is supported");

                    int keySize = reader.ReadByte();
                    string key = new string(reader.ReadChars(keySize));

                    int valueSize = reader.ReadByte();
                    string value = new string(reader.ReadChars(valueSize));

                    double ttl = timestamp - unixTime;

                    rdsDatabse.Add(key, new ItemValue(value, ttl));
                }
                else if (nextByte == EXPIRY_IN_SECONDS)
                {
                    byte[] bytes = reader.ReadBytes(4);
                    long timestamp = BitConverter.ToInt64(bytes, 0);
                    long unixTime = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();

                    int valueType = reader.ReadByte();
                    if (valueType != STRING_VALUE_TYPE)
                        throw new Exception("Only string value type is supported");

                    int keySize = reader.ReadByte();
                    string key = new string(reader.ReadChars(keySize));

                    int valueSize = reader.ReadByte();
                    string value = new string(reader.ReadChars(valueSize));

                    double ttl = (timestamp - unixTime) * 1000; //Convert to miliseconds

                    rdsDatabse.Add(key, new ItemValue(value, ttl));
                }
                else if (nextByte == STRING_VALUE_TYPE)
                {
                    int keySize = reader.ReadByte();
                    string key = new string(reader.ReadChars(keySize));

                    int valueSize = reader.ReadByte();
                    string value = new string(reader.ReadChars(valueSize));

                    rdsDatabse.Add(key, new ItemValue(value));
                }

                hashTableSize--;
            }
        }
    }
}
