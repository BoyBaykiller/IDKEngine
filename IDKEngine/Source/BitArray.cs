using System;
using System.Numerics;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using IDKEngine.Utils;

namespace IDKEngine;

public record struct BitArray
{
    public const int BITS_PER_ELEMENT = sizeof(uint) * 8;

    private readonly uint[] data;
#if DEBUG
    private readonly int bitCount;
#endif
    public BitArray(int count, bool value)
    {
        data = new uint[MyMath.DivUp(count, BITS_PER_ELEMENT)];
        if (value)
        {
            Array.Fill(data, (byte)0xFF);
        }

#if DEBUG
        bitCount = count;
#endif
    }

    public readonly bool this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
#if DEBUG
            Debug.Assert(index >= 0 && index < bitCount);
#endif
            uint arrIndex = (uint)index / BITS_PER_ELEMENT;
            uint bitIndex = (uint)index % BITS_PER_ELEMENT;
            uint mask = 1u << (int)bitIndex;

            return (data[arrIndex] & mask) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
#if DEBUG
            Debug.Assert(index >= 0 && index < bitCount);
#endif

            uint arrIndex = (uint)index / BITS_PER_ELEMENT;
            uint bitIndex = (uint)index % BITS_PER_ELEMENT;
            uint mask = 1u << (int)bitIndex;

            if (value)
            {
                data[arrIndex] |= mask;
            }
            else
            {
                // Optimization discovered by clang/gcc: https://godbolt.org/z/3dGa4MYPj
                uint rol = BitOperations.RotateLeft(uint.MaxValue - 1, index);
                data[arrIndex] &= rol;
            }
        }
    }
}
