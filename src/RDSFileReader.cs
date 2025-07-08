namespace codecrafters_redis.src
{
    public class RDSFileReader
    {
        private string _fileLocation;
        private string? _header;
        public RedisDatabaseStored rdsDatabase;

        public RDSFileReader(string pathToFile)
        {
            _fileLocation = pathToFile;
            rdsDatabase = new RedisDatabaseStored();
        }

        public void ReadRDSFile()
        {

            if (File.Exists(_fileLocation))
            {
                using (FileStream fileStream = new FileStream(_fileLocation, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader reader = new BinaryReader(fileStream))
                    {
                        // Header (confirm RDS file format)
                        var header = new string(reader.ReadChars(9));
                        if (header != "REDIS0011")
                        {
                            throw new Exception("Header not equal to REDIS0011");
                        }
                        while (true)
                        {
                            // find the database section
                            byte dbStart = reader.ReadByte();
                            if (dbStart == 0xFE)
                            {
                                ReadDataBaseSection(reader);
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                Console.Out.WriteLine("The file asked for doesn't exist");
            }
        }

        private void ReadDataBaseSection(BinaryReader reader)
        {
            uint dbNumber = reader.ReadByte();
            byte dbIndicator = reader.ReadByte();
            if (dbIndicator == 0xFB)
            {
                // reading the hash table size information
                int hashTableSize = reader.ReadByte();
                int expireHashTableSize = reader.ReadByte();

                while (hashTableSize > 0)
                {
                    hashTableSize--;
                    int firstChar = reader.ReadByte();
                    // this could be 0 string (meaning we are about to read a K-V pair w/o expiry
                    // OR it could be 0xFC or 0xFD indicating the K-V pair we are about to read DOES have expiry
                    if (firstChar == 252 || firstChar == 253)
                    {
                        if (firstChar == 252)
                        {
                            byte[] expiryInMillisecondsByteArray = reader.ReadBytes(8);
                            var expiryString = ParseRDSExpiry(expiryInMillisecondsByteArray, firstChar);

                            // this should be a string.
                            int _ = reader.ReadByte();
                            PersistKVPair(reader, expiryString);
                        }
                        else if (firstChar == 253)
                        {
                            byte[] expiryInMillisecondsByteArray = reader.ReadBytes(8);
                            var expiryString = ParseRDSExpiry(expiryInMillisecondsByteArray, firstChar);

                            // this should be a string.
                            int _ = reader.ReadByte();
                            PersistKVPair(reader, expiryString);
                        }

                    }
                    else if (firstChar == 0)
                    {
                        // k-v pair with no expiry
                        PersistKVPair(reader, null);
                    }
                    else throw new Exception("Only strings right now!");
                }
            }
            else
            {
                throw new Exception("No Database section");
            }
        }

        private string ParseRDSExpiry(byte[] expiryBytes, int unit)
        {
            string dateAsString;

            if (unit == 252)
            {
                var expiryInMilliseconds = BitConverter.ToInt64(expiryBytes, 0);
                dateAsString = DateTimeOffset.FromUnixTimeMilliseconds(expiryInMilliseconds).UtcDateTime.ToString();

            }
            else
            { // will be 253

                var expiryInSeconds = BitConverter.ToUInt32(expiryBytes, 0);
                dateAsString = DateTimeOffset.FromUnixTimeSeconds(expiryInSeconds).UtcDateTime.ToString();
            }
            return dateAsString;
        }

        private void PersistKVPair(BinaryReader reader, string? expiryString)
        {
            int keyLen = reader.ReadByte();
            string key = new string(reader.ReadChars(keyLen));
            int valueLen = reader.ReadByte();
            string value = new string(reader.ReadChars(valueLen));
            rdsDatabase.Set(key, new StoredValue(value, expiryString));
        }

        public RedisDatabaseStored GetCurrentDBState()
        {
            return rdsDatabase;
        }
    }
}
