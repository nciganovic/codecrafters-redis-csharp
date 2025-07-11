using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace codecrafters_redis.src
{
    public static class ResponseHandler
    {
        public static string ErrorResponse(string message)
        {
            return $"-ERR {message}{Constants.SPACE_SING}";
        }

        public static string SimpleResponse(string value)
        {
            string echo = string.Empty;
            echo += Constants.PLUS_CHAR;
            echo += value;
            echo += Constants.SPACE_SING;
            return echo;
        }

        public static string ArrayResponse(string[] elements)
        {
            string echo = string.Empty;
            echo += Constants.ASTERISK_CHAR;
            echo += elements.Length;
            echo += Constants.SPACE_SING;
            foreach (var element in elements)
            {
                echo += BulkResponse(element);
            }
            return echo;
        }

        public static string BulkResponse(string value)
        {
            string echo = string.Empty;
            echo += Constants.DOLLAR_CHAR;
            echo += value.Length;
            echo += Constants.SPACE_SING;
            echo += value;
            echo += Constants.SPACE_SING;
            return echo;
        }

        public static string NullResponse()
        {
            string response = string.Empty;
            response += Constants.DOLLAR_CHAR;
            response += Constants.NULL_RESPONSE;
            response += Constants.SPACE_SING;
            return response;
        }

        public static string IntigerResponse(int value)
        {
            string response = string.Empty;
            response += Constants.COLON_CHAR;
            response += value.ToString();
            response += Constants.SPACE_SING;
            return response;
        }
    }
}
