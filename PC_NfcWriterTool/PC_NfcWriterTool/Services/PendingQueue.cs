using System;
using System.Collections.Generic;

namespace PC_NfcWriterTool.Services
{
    public sealed class PendingQueue
    {
        private readonly List<Data.QueueItem> _items = new List<Data.QueueItem>();
        private readonly object _sync = new object();

        public event EventHandler Changed;

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _items.Count;
                }
            }
        }

        public Data.QueueItem Peek()
        {
            lock (_sync)
            {
                return _items.Count == 0 ? null : _items[0];
            }
        }

        public IList<Data.QueueItem> Snapshot()
        {
            lock (_sync)
            {
                return _items.ToArray();
            }
        }

        public void Enqueue(Data.QueueItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            lock (_sync)
            {
                _items.Add(item);
            }

            RaiseChanged();
        }

        public void EnqueueFront(Data.QueueItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            lock (_sync)
            {
                _items.Insert(0, item);
            }

            RaiseChanged();
        }

        public Data.QueueItem Dequeue()
        {
            Data.QueueItem item = null;
            lock (_sync)
            {
                if (_items.Count > 0)
                {
                    item = _items[0];
                    _items.RemoveAt(0);
                }
            }

            if (item != null)
            {
                RaiseChanged();
            }

            return item;
        }

        /// <summary>
        /// Skip current item: keep it in the queue by default (move to end).
        /// </summary>
        public void SkipCurrentKeepInQueue()
        {
            lock (_sync)
            {
                if (_items.Count < 2)
                {
                    return;
                }

                Data.QueueItem current = _items[0];
                _items.RemoveAt(0);
                _items.Add(current);
            }

            RaiseChanged();
        }

        public int IndexOfCurrent
        {
            get { return Count == 0 ? 0 : 1; }
        }

        public int OriginalTotal { get; set; }

        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
