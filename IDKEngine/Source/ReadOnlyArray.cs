using System;

namespace IDKEngine;

public record struct ReadOnlyArray<T>
{
    public readonly int Length => array.Length;

    private readonly T[] array;

    public ReadOnlyArray(T[] array)
    {
        this.array = array;
    }

    public readonly ref readonly T this[int index]
    {
        get => ref array[index];
    }

    public static implicit operator ReadOnlySpan<T>(in ReadOnlyArray<T> arr)
    {
        return new ReadOnlySpan<T>(arr.array, 0, arr.Length);
    }
}
