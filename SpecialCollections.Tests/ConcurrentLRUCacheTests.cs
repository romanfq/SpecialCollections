using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpecialCollections.Tests
{
    [TestFixture]
    public class ConcurrentLRUCacheTests
    {
        public class TheCtor
        {
            [Test]
            [TestCase(0)]
            [TestCase(-1)]
            public void ShouldNotAcceptNonPositiveCacheLineSize(int size)
            {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new ConcurrentLRUCache<string, string>(size));
            }
        }
        
        public class TheAddMethod
        {
            [Test]
            public void IgnoresDuplicates()
            {
                var cache = new ConcurrentLRUCache<string, CustomObject>(3);

                cache.Add("k", 1);
                cache.Add("k", 1);

                VerifyCachedObjects(cache, "k", new[] { 1 });
            }

            [Test]
            public void EvictsOldestItemsWhenNoneUsed()
            {
                var cache = new ConcurrentLRUCache<string, CustomObject>(3);

                cache.Add("k", 1);
                cache.Add("k", 2);
                cache.Add("k", 3);

                // after the next Add, the "oldest" item should be evicted, in this case, 1
                cache.Add("k", 4);

                VerifyCachedObjects(cache, "k", new[] { 2, 3, 4 });
            }

            [Test]
            public void EvictsLRUItemWhenSomeAreUsed()
            {
                var cache = new ConcurrentLRUCache<string, CustomObject>(3);

                cache.Add("k", 1);
                cache.Add("k", 2);
                cache.Add("k", 3);

                // before adding another object, use 1
                cache.Use("k", 1);

                // after the next Add, the "oldest" item with least usage should be evicted, in this case, 2
                cache.Add("k", 4);

                VerifyCachedObjects(cache, "k", new[] { 1, 3, 4 });
            }

            private static void VerifyCachedObjects(ConcurrentLRUCache<string, CustomObject> cache, string key, IEnumerable<int> items)
            {
                var cachedObjects = from obj in cache.Get(key)
                                    orderby obj.Value
                                    select obj.Value;

                CollectionAssert.AreEquivalent(items, cachedObjects);
            }
        }

        public class TheUseMethod
        {
            [Test]
            public void ThrowsOnInvalidArgument()
            {
                var cache = new ConcurrentLRUCache<string, CustomObject>(3);
                cache.Add("k", 1);
                Assert.Throws<ArgumentException>(() => cache.Use("k", 2));
            }
        }

        class CustomObject : IEquatable<CustomObject>
        {
            public override int GetHashCode()
            {
                return Value;
            }

            public CustomObject(int value)
            {
                Value = value;
            }

            public int Value { get; private set; }

            public static implicit operator CustomObject(int x)
            {
                return new CustomObject(x);
            }

            public override string ToString()
            {
                return Value.ToString();
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((CustomObject)obj);
            }

            public bool Equals(CustomObject other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Value == other.Value;
            }
        }
    }
}
