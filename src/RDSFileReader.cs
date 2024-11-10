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
        private const byte DB_INDICATOR = 0xFB;
        private const int STRING_VALUE_TYPE = 0;

        private readonly string fileLocation;

        public Dictionary<string, ItemValue> rdsDatabse  { get; private set; } 

        public RDSFileReader(string fileLocation, bool loadItems = false)
        {
            //TODO maybe add check for --dir and --dbfilename separetly

            //if (!File.Exists(fileLocation))
            //{
            //    throw new Exception("FILE NOT FOUND!!!");
                //File.Create(fileLocation);
            //}

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
                int valueType = reader.ReadByte();
                if(valueType != STRING_VALUE_TYPE)
                    throw new Exception("Only string encodings are supported.");

                int keySize = reader.ReadByte();
                string key = new string(reader.ReadChars(keySize));

                int valueSize = reader.ReadByte();
                string value = new string(reader.ReadChars(valueSize));

                rdsDatabse.Add(key, new ItemValue(value));

                hashTableSize--;
            }
        }
    }
}
