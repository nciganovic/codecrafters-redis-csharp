using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace codecrafters_redis.src
{
    public class WaitableQueue<T>
    {
        private readonly Queue<object> _queue = new Queue<object>();
        private readonly object _lock = new object();
        private readonly ManualResetEvent _itemAddedEvent = new ManualResetEvent(false);

        public void Enqueue(object item)
        {
            lock (_lock)
            {
                _queue.Enqueue(item);
                _itemAddedEvent.Set(); // Signal that an item has been added
            }
        }

        public (T, Socket) WaitForItem(int timeoutMilliseconds, Socket socket)
        {
            DateTime timeoutTime = timeoutMilliseconds == Timeout.Infinite
            ? DateTime.MaxValue
            : DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);

            while (true)
            {
                lock (_lock)
                {
                    if (_queue.Count > 0)
                    {
                        T item = (T)_queue.Dequeue();
                        if (_queue.Count == 0)
                        {
                            _itemAddedEvent.Reset();
                        }
                        return (item, socket);
                    }
                }

                if (timeoutMilliseconds != Timeout.Infinite)
                {
                    TimeSpan remainingTime = timeoutTime - DateTime.UtcNow;
                    if (remainingTime <= TimeSpan.Zero)
                    {
                        return (default, socket)!;
                    }
                }

                if (timeoutMilliseconds == Timeout.Infinite)
                {
                    _itemAddedEvent.WaitOne(Timeout.Infinite);
                }
                else
                {
                    TimeSpan remainingTime = timeoutTime - DateTime.UtcNow;
                    if (remainingTime <= TimeSpan.Zero)
                    {
                        return (default, socket)!;
                    }

                    int waitMilliseconds = (int)Math.Min(remainingTime.TotalMilliseconds, int.MaxValue);
                    _itemAddedEvent.WaitOne(waitMilliseconds);
                }
            }
        }
    }
}
