using System;
using System.Collections.Concurrent;
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

        private readonly ConcurrentDictionary<TKey, LRUList<TValue>> _entries
            = new ConcurrentDictionary<TKey, LRUList<TValue>>();

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
                .GetOrAdd(key, k => new LRUList<TValue>(_cacheLineSize))
                .Add(value);
        }

        /// <summary>
        /// Gets the whole cache line associated with a given key 
        /// </summary>
        public TValue[] Get(TKey key)
        {
            LRUList<TValue> list;
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
        public void Use(TKey key, params TValue[] usedValues)
        {
            if (usedValues == null)
                throw new ArgumentNullException("usedValues");

            LRUList<TValue> list;
            if (!_entries.TryGetValue(key, out list))
            {
                throw new ArgumentException("There is are no values cached for that key", "key");
            }

            for (var i = usedValues.Length - 1; i >= 0; i--)
            {
                var value = usedValues[i];
                list.Use(value);
            }
        }

        class LRUList<T> where T : class, IEquatable<T>
        {
            private readonly int _maxSize;
            private int _size;

            private readonly LRUEntry _head;
            private readonly ReaderWriterLockSlim _syncLock = new ReaderWriterLockSlim();

            public LRUList(int maxSize)
            {
                _maxSize = maxSize;
                _head = new LRUEntry("h");
            }

            public int Size
            {
                get { return _size; }
            }

            public void Add(T value)
            {
                if (value == null)
                    throw new ArgumentNullException("value");

                try
                {
                    // invariant: no more than _maxSize elements
                    _syncLock.EnterWriteLock();

                    // seek the value, bail out if found
                    {
                        var cursor = _head;
                        while (cursor != null)
                        {
                            if (value.Equals(cursor.Value))
                            {
                                return;
                            }
                            cursor = cursor.Next;
                        }
                    }

                    // insert the value, keep invariant _size <= maxSize
                    {
                        var newEntry = new LRUEntry(value);
                        if (_size == _maxSize)
                        {
                            var cursor = _head;
                            while (cursor.Next != null)
                            {
                                cursor = cursor.Next;
                            }

                            cursor.Previous.Next = null;
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

            public void Use(T value)
            {
                try
                {
                    _syncLock.EnterWriteLock();

                    // seek the value, bail out if not found
                    var cursor = _head;
                    while (cursor != null)
                    {
                        if (value.Equals(cursor.Value))
                        {
                            break;
                        }

                        cursor = cursor.Next;
                    }

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
                        var moreRecentlyUsed = _head.Next;
                        _head.Next = cursor;
                        cursor.Next = moreRecentlyUsed;
                    }
                }
                finally
                {
                    _syncLock.ExitWriteLock();
                }
            }

            public void CopyTo(T[] array, int index)
            {
                try
                {
                    _syncLock.EnterWriteLock();

                    var cursor = _head.Next;
                    int pos = 0;
                    while (cursor != null)
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

            class LRUEntry
            {
                private readonly string _name;
                private LRUEntry _next;
                public T Value { get; private set; }

                public LRUEntry(T value, LRUEntry previous = null)
                {
                    Value = value;
                    Previous = previous;
                }

                public LRUEntry(string name)
                {
                    _name = name;
                }

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
