using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace IDKEngine;

public unsafe struct NativeMemoryView<T> where T : unmanaged
{
    public T* Ptr;
    public int Length;

    public NativeMemoryView(T* ptr, int length)
    {
        Ptr = ptr;
        Length = length;
    }

    public readonly ref T this[int index]
    {
        get
        {
            Debug.Assert(index >= 0 && index < Length);
            return ref Unsafe.AsRef<T>(Ptr + index);
        }
    }

    public readonly ref T this[nint index]
    {
        get
        {
            Debug.Assert(index >= 0 && index < Length);
            return ref Unsafe.AsRef<T>(Ptr + index);
        }
    }

    public readonly Span<T> AsSpan(int offset, int length)
    {
        return new Span<T>(Ptr + offset, length);
    }

    public static implicit operator Span<T>(NativeMemoryView<T> view)
    {
        return view.AsSpan(0, view.Length);
    }

    public static implicit operator ReadOnlySpan<T>(NativeMemoryView<T> view)
    {
        return view.AsSpan(0, view.Length);
    }
}
