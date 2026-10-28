using System;
using System.Collections.Generic;
using System.Threading;

namespace InputPolish
{
    public class Worker : IDisposable
    {
        private readonly Queue<Action> _queue = new Queue<Action>();
        private readonly object _gate = new object();
        private Thread _thread;
        private bool _stop;

        public Worker(string name)
        {
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = name;
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public void Post(Action action)
        {
            if (action == null) return;
            lock (_gate)
            {
                _queue.Enqueue(action);
                Monitor.Pulse(_gate);
            }
        }

        private void Loop()
        {
            while (true)
            {
                Action action = null;
                lock (_gate)
                {
                    while (!_stop && _queue.Count == 0) Monitor.Wait(_gate);
                    if (_stop) return;
                    action = _queue.Dequeue();
                }
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log.Write("worker error: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _stop = true;
                Monitor.Pulse(_gate);
            }
        }
    }
}
