using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace codecrafters_redis.src
{
    public class RespGenerator
    {
        public RespGenerator()
        {
            string input = Console.ReadLine().Trim() ?? string.Empty;

            string[] items = input.Split(' ');

            Console.WriteLine(ArrayResponse(items));
        }

        static string ArrayResponse(string[] elements)
        {
            string echo = string.Empty;
            echo += "*";
            echo += elements.Length;
            echo += "\\r\\n";
            foreach (var element in elements)
            {
                echo += BulkResponse(element);
            }
            return echo;
        }


        static string BulkResponse(string value)
        {
            string echo = string.Empty;
            echo += "\\$";
            echo += value.Length;
            echo += "\\r\\n";
            echo += value;
            echo += "\\r\\n";
            return echo;
        }
    }
}
