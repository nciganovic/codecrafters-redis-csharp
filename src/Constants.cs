using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace codecrafters_redis.src
{
    public static class Constants
    {
        public const string PING_REQUEST_COMMAND = "*1\r\n$4\r\nPING\r\n";

        public const string PING_RESPOSNSE = "+PONG\r\n";
    }
}