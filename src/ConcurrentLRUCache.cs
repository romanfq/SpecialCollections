using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace SpecialCollections
{
    /// <summary>
    /// Implements a concurrent cache that uses LRU as the cache replacement policy
    /// </summary>
    public class ConcurrentLRUCache<TKey, TValue>
        where TKey : IEquatable<TKey>
        where TValue : class, IEquatable<TValue>
    {
        private readonly int _cacheLineSize;

        private readonly ConcurrentDictionary<TKey, LRUList> _entries
            = new ConcurrentDictionary<TKey, LRUList>();

        public ConcurrentLRUCache(int cacheLineSize)
        {
            if (0 >= cacheLineSize)
            {
                throw new ArgumentOutOfRangeException("cacheLineSize", cacheLineSize, "Must be > 0");
            }
            _cacheLineSize = cacheLineSize;
        }

        /// <summary>
        /// Adds value to the cache line associated with key. Keeps the invariant of the cache line size
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            _entries
                .GetOrAdd(key, k => new LRUList(_cacheLineSize))
                .Add(value);
        }

        /// <summary>
        /// Gets the whole cache line associated with a given key 
        /// </summary>
        public TValue[] Get(TKey key)
        {
            LRUList list;
            if (!_entries.TryGetValue(key, out list))
            {
                return null;
            }

            var array = new TValue[list.Size];
            list.CopyTo(array, 0);
            return array;
        }

        /// <summary>
        /// Increments usage of a set of items in the cache line associated with the key
        /// </summary>
        public void Use(TKey key, params TValue[] values)
        {
            if (values == null)
                throw new ArgumentNullException("values");

            LRUList list;
            if (!_entries.TryGetValue(key, out list))
            {
                throw new ArgumentException("There is are no values cached for that key", "key");
            }

            list.Use(values);
        }

        class LRUList
        {
            private readonly int _maxSize;
            private int _size;

            private readonly LRUEntry _head;
            private readonly LRUEntry _tail;

            private readonly ReaderWriterLockSlim _syncLock = new ReaderWriterLockSlim();

            public LRUList(int maxSize)
            {
                if (maxSize <= 0)
                {
                    throw new ArgumentOutOfRangeException("maxSize", "Must be > 0");
                }
                _maxSize = maxSize;
                _head = new LRUEntry("h")
                {
                    Next = _tail = new LRUEntry("t")
                };
            }

            public int Size
            {
                get { return _size; }
            }

            public void Add(TValue value)
            {
                if (value == null)
                    throw new ArgumentNullException("value");

                try
                {
                    _syncLock.EnterWriteLock();

                    if (Find(value) != null)
                    {
                        return;
                    }

                    // insert the value, keep invariant _size <= maxSize
                    {
                        var newEntry = new LRUEntry(value);
                        if (_size == _maxSize)
                        {
                            Debug.Assert(_tail.Previous != _head);
                            _tail.Previous.Previous.Next = _tail;
                        }
                        else
                        {
                            _size++;
                        }
                        // h -> a1 -> a2 -> ... -> a(N-1) -> aN = tA
                        newEntry.Next = _head.Next;
                        _head.Next = newEntry;
                    }
                }
                finally
                {
                    _syncLock.ExitWriteLock();
                }
            }

            public void CopyTo(TValue[] array, int index)
            {
                try
                {
                    _syncLock.EnterWriteLock();

                    var cursor = _head.Next;
                    var pos = 0;
                    while (cursor != _tail)
                    {
                        array[index + pos] = cursor.Value;
                        cursor = cursor.Next;
                        pos++;
                    }
                }
                finally
                {
                    _syncLock.ExitWriteLock();
                }
            }

            public void Use(TValue[] values)
            {
                try
                {
                    _syncLock.EnterWriteLock();
                    for (int i = values.Length - 1; i >= 0; i--)
                    {
                        Use(values[i]);
                    }
                }
                finally
                {
                    _syncLock.ExitWriteLock();
                }
            }

            private void Use(TValue value)
            {
                // seek the value, raise error if not found
                var cursor = Find(value);
                if (cursor == null)
                {
                    throw new ArgumentException("Specified value is not present in the list", "value");
                }

                // adjust the entries around the cursor
                var nextByUsage = cursor.Next;
                cursor.Previous.Next = nextByUsage;

                // move the cursor to the start of the list
                if (cursor != _head.Next)
                {
                    var mostRU = _head.Next;
                    _head.Next = cursor;
                    cursor.Next = mostRU;
                }
            }

            private LRUEntry Find(TValue value)
            {
                LRUEntry frontCursor = _head, backCursor = _tail;
                while (frontCursor != backCursor && backCursor.Next != frontCursor)
                {
                    if (Equals(value, frontCursor.Value))
                    {
                        return frontCursor;
                    }
                    if (Equals(value, backCursor.Value))
                    {
                        return backCursor;
                    }
                    frontCursor = frontCursor.Next;
                    backCursor = backCursor.Previous;
                }

                if (frontCursor == backCursor && Equals(value, frontCursor.Value))
                {
                    return frontCursor;
                }
                return null;
            }

            private class LRUEntry
            {
                private readonly string _name;
                private LRUEntry _next;

                public LRUEntry(TValue value)
                {
                    Value = value;
                }

                internal LRUEntry(string name)
                {
                    _name = name;
                }

                public TValue Value { get; private set; }
                public LRUEntry Next
                {
                    get { return _next; }
                    set
                    {
                        _next = value;
                        if (value != null)
                        {
                            value.Previous = this;
                        }
                    }
                }

                public LRUEntry Previous { get; private set; }

                public override string ToString()
                {
                    return !string.IsNullOrWhiteSpace(_name)
                            ? _name
                            : Value == null
                                ? "<NULL>"
                                : Value.ToString();
                }
            }
        }
    }
}
