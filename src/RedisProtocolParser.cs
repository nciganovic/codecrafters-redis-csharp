namespace codecrafters_redis.src
{
    public class RedisProtocolParser
    {
        private readonly byte[] _message;
        private readonly int _trueMessageLength;
        public List<RESPMessage> commandArray;
        private readonly string _messageAsASCII;
        private int _charactersConsumed;

        public RedisProtocolParser(byte[] message, int length)
        {
            _message = message;
            _trueMessageLength = length;
            commandArray = new List<RESPMessage>();
            _messageAsASCII = System.Text.Encoding.ASCII.GetString(_message, 0, _message.Length);
            _charactersConsumed = 0;
        }

        public void Parse()
        {
            // if the parser has read fewer charachters than we are told have been read from the socket, then there is work to do! 
            while (_charactersConsumed < _trueMessageLength)
            {
                RESPMessage nextMessage = DispatchBasedOnFirstByte(_messageAsASCII.Substring(_charactersConsumed));
                commandArray.Add(nextMessage);
            }
        }

        public RESPMessage DispatchBasedOnFirstByte(string messageString)
        {
            RESPMessage message = new RESPMessage();

            char firstCharConverted = messageString.ElementAt(0);
            int snapshotOfCharsConsumedPriorToMessageProcessing = _charactersConsumed;
            _charactersConsumed += 1;

            switch (firstCharConverted)
            {
                case Constants.PLUS_CHAR:
                    //simple string
                    string simpleString = GetSimpleString(messageString.Substring(1));
                    message.arguments.Add(simpleString);
                    message.bytes += _charactersConsumed - snapshotOfCharsConsumedPriorToMessageProcessing;
                    break;
                case Constants.DOLLAR_CHAR:
                    //bulk string
                    string bulkString = GetBulkString(messageString.Substring(1));
                    message.arguments.Add(bulkString);
                    message.bytes += _charactersConsumed - snapshotOfCharsConsumedPriorToMessageProcessing;
                    break;
                case Constants.ASTERISK_CHAR:
                    //array (of bulk strings)
                    List<string> bulkStringArray = GetArrayOfBulkStrings(messageString.Substring(1));
                    foreach (var item in bulkStringArray)
                    {
                        message.arguments.Add(item);
                    }
                    message.SetCommand();
                    message.bytes += _charactersConsumed - snapshotOfCharsConsumedPriorToMessageProcessing;
                    break;
                default:
                    Console.WriteLine("This crept in somehow: " + firstCharConverted);
                    Console.WriteLine("Everything after the dodge string:" + messageString.Substring(1));
                    break;
            }

            return message;
        }

        public string GetSimpleString(string message)
        {
            // 0 index of the first occurence of \r\n
            int firstReturnNewline = message.IndexOf("\r\n");

            // Read from beggining of string 0 until first first occurence of \r\n
            string messageBody = message.Substring(0, firstReturnNewline);

            int charsCounted = messageBody.Length + "\r\n".Length;
            _charactersConsumed += charsCounted;

            return messageBody;
        }

        public string GetBulkString(string message)
        {
            //get the 0 index of the first occurence of \r\n
            int firstSeparator = message.IndexOf("\r\n");

            // read the string from the start up until the first \r\n
            string size = message.Substring(0, firstSeparator);


            // read everything beyond the size declaration and firstSeparator, for as long as the 
            // size value dictates (note there is a \r\n on the end here)
            string messageBody = message.Substring(size.Length + "\r\n".Length, Int32.Parse(size));

            int charsCounted = size.Length + messageBody.Length + "\r\n".Length;
            _charactersConsumed += charsCounted;

            return messageBody;
        }

        public List<string> GetArrayOfBulkStrings(string message)
        {
            List<string> strings = new List<string>();

            // get the 0 index of the first occurence of  \r\n	   
            int firstSeparator = message.IndexOf("\r\n");

            // get the size of the array as a string
            string arraySize = message.Substring(0, firstSeparator);

            _charactersConsumed += (arraySize.Length + "\r\n".Length);

            // // read everything beyond the size declaration and first Separator
            string remainingMessage = message.Substring(arraySize.Length + "\r\n".Length);

            // get an integer to represent the size
            int arraySizeAsNumber = Int32.Parse(arraySize);

            int counter = 0;

            while (arraySizeAsNumber > 0)
            {
                // pop the first char from the remaining message (we are assuming bulk String here)
                string remainingMessageLessFirstChar = remainingMessage.Substring(1 + counter);
                /////// do we account for a char consumption here?


                int bulkStringFirstSeparator = remainingMessageLessFirstChar.IndexOf("\r\n");

                // store the number
                string bulkStringSize = remainingMessageLessFirstChar.Substring(0, bulkStringFirstSeparator);

                //read beyond the first char and separator for as long as the number
                string realRemainingMessage = remainingMessageLessFirstChar.Substring(bulkStringSize.Length + "\r\n".Length, Int32.Parse(bulkStringSize));

                strings.Add(realRemainingMessage);

                // chars consumed 1 (we pop the symbol) + bulkStringSize.Length + \r\n + \r\n + realRemainingMessage.Length;
                int charsCounted = 1 + bulkStringSize.Length + "\r\n".Length + "\r\n".Length + realRemainingMessage.Length;
                _charactersConsumed += charsCounted;

                counter += charsCounted;
                arraySizeAsNumber--;
            }

            return strings;
        }

        public class RESPMessage
        {
            public List<string> arguments = new List<string>();
            public string? command = string.Empty;
            public int bytes = 0;

            public void SetCommand()
            {
                command = arguments[0].ToUpper();
            }

            public string GetKey()
            {
                // if command is GET or SET the 1st index in the array will be the key to try
                return arguments[1];
            }

            public string GetValue()
            {
                // if command is SET the 2nd index in the array will be the value to set
                return arguments[2];
            }

            public string GetConfigParameter()
            {
                // if command is CONFIG (GET) the 2nd index in the array will be the config to get
                return arguments[2];
            }

            public string GetInfoParameter()
            {
                // if command is INFO the 1st index in the array will be a parameter
                return arguments[1];
            }

            public bool ExpiryExists()
            {
                return arguments.Exists(arg => arg == "px");
            }

            public Double? GetExpiry()
            {
                if (!ExpiryExists())
                    return null;
                
                var pxArgument = arguments.Where(arg => arg == "px").First();
                var indexOfPX = arguments.IndexOf(pxArgument);
                var expiryValue = arguments[indexOfPX + 1];
                return Double.Parse(expiryValue);
            }
        }
    }
}
