using System;

namespace IDKEngine
{
    public record struct ReadOnlyArray<T>
    {
        public int Length => array.Length;

        private readonly T[] array;

        public ReadOnlyArray(T[] array)
        {
            this.array = array;
        }

        public ref readonly T this[int index]
        {
            get => ref array[index];
        }

        public static implicit operator ReadOnlySpan<T>(in ReadOnlyArray<T> arr)
        {
            return new ReadOnlySpan<T>(arr.array, 0, arr.Length);
        }
    }
}
