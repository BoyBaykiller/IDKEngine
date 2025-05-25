using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace IDKEngine
{
    [CollectionBuilder(typeof(ListBuilder), nameof(ListBuilder.Create))]
    public class List<T>
    {
        public const float GROWTH_FACTOR = 2.0f;
        public const int DefaultCapacity = 4;

        public int Capacity => buffer.Length;

        public int Count { get; private set; }
        private T[] buffer;

        public List(ReadOnlySpan<T> items)
        {
            buffer = items.ToArray();
            Count = buffer.Length;
        }

        public List(int capacity)
        {
            buffer = new T[capacity];
        }

        public List()
        {
            buffer = [];
        }

        public static List<T> FromArray(T[] array)
        {
            List<T> newList = new List<T>();
            newList.buffer = array;
            newList.Count = array.Length;

            return newList;
        }

        public static List<T> WithCount(int size)
        {
            return FromArray(new T[size]);
        }

        public ref T this[int index]
        {
            get
            {
                Debug.Assert(index >= 0 && index < Count);
                return ref buffer[index];
            }
        }

        public void Add(T item)
        {
            GrowIfNeeded(Count + 1);

            buffer[Count++] = item;
        }

        public void AddRange(ReadOnlySpan<T> items)
        {
            GrowIfNeeded(Count + items.Length);

            items.CopyTo(buffer.AsSpan(Count, buffer.Length - Count));
            Count += items.Length;
        }

        public void InsertRange(int index, ReadOnlySpan<T> items)
        {
            GrowIfNeeded(Count + items.Length);

            Array.Copy(buffer, index, buffer, index + items.Length, Count - index);

            items.CopyTo(buffer.AsSpan(index, buffer.Length - index));
            Count += items.Length;
        }

        public void GrowIfNeeded(int requiredSize)
        {
            int newCapacity = buffer.Length == 0 ? DefaultCapacity : buffer.Length;
            while (newCapacity < requiredSize)
            {
                newCapacity = (int)(newCapacity * GROWTH_FACTOR);
            }

            if (newCapacity > buffer.Length)
            {
                Array.Resize(ref buffer, newCapacity);
            }
        }

        public void Clear()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                // Clear the items so that the gc can reclaim the references
                Array.Clear(buffer);
            }

            Count = 0;
        }

        public T[] ToArray()
        {
            T[] arr = GC.AllocateUninitializedArray<T>(Count);
            buffer.AsSpan(0, Count).CopyTo(arr);

            return arr;
        }

        public Span<T> AsSpan()
        {
            return buffer.AsSpan(0, Count);
        }

        public Span<T> AsSpan(int start, int count)
        {
            return buffer.AsSpan(start, count);
        }

        public static implicit operator Span<T>(List<T> list)
        {
            return list.AsSpan(0, list.Count);
        }

        public static implicit operator ReadOnlySpan<T>(List<T> list)
        {
            return list.AsSpan(0, list.Count);
        }

        public static T[] DissolveToArray(ref List<T> list)
        {
            T[] arr = list.buffer;
            Array.Resize(ref arr, list.Count);

            list = null;

            return arr;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        public struct Enumerator
        {
            private readonly List<T> list;
            private int index;

            public Enumerator(List<T> list)
            {
                this.list = list;
                index = -1;
            }

            public readonly ref T Current => ref list[index];

            public bool MoveNext()
            {
                if (index + 1 < list.Count)
                {
                    index++;
                    return true;
                }
                return false;
            }
        }
    }

    internal static class ListBuilder
    {
        internal static List<T> Create<T>(ReadOnlySpan<T> values) => new List<T>(values);
    }
}
