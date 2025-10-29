using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IDKEngine;

[CollectionBuilder(typeof(NativeArrayBuilder), nameof(NativeArrayBuilder.Create))]
public unsafe struct NativeArray<T> : IDisposable
{
    public T* Elements { get; private set; }
    public int Length { get; private set; }

    public NativeArray(nuint count)
        : this((int)count)
    {
    }

    public NativeArray(int count)
    {
        Elements = (T*)NativeMemory.AllocZeroed((nuint)(sizeof(T) * count));
        Length = count;
    }

    public readonly ref T this[int index]
    {
        get
        {
            Debug.Assert(index >= 0 && index < Length, "Index out of bounds");

            return ref Elements[index];
        }
    }

    public readonly Span<T> AsSpan(int offset, int length)
    {
        return new Span<T>(Elements + offset, length);
    }

    public static implicit operator Span<T>(NativeArray<T> view)
    {
        return view.AsSpan(0, view.Length);
    }

    public static implicit operator ReadOnlySpan<T>(NativeArray<T> view)
    {
        return view.AsSpan(0, view.Length);
    }

    public readonly void Dispose()
    {
        NativeMemory.Free(Elements);
    }

    public readonly Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    public struct Enumerator
    {
        private readonly NativeArray<T> nativeArray;
        private int index;

        public Enumerator(NativeArray<T> nativeArray)
        {
            this.nativeArray = nativeArray;
            index = -1;
        }

        public readonly ref T Current => ref nativeArray[index];

        public bool MoveNext()
        {
            if (index + 1 < nativeArray.Length)
            {
                index++;
                return true;
            }
            return false;
        }
    }
}

internal static class NativeArrayBuilder
{
    internal static NativeArray<T> Create<T>(ReadOnlySpan<T> values)
    {
        NativeArray<T> nativeArray = new NativeArray<T>(values.Length);
        values.CopyTo(nativeArray);

        return nativeArray;
    }
}