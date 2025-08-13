using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IDKEngine
{
    public unsafe struct UnmanagedArray<T> : IDisposable
    {
        public nint Length { get; private set; }
        public T* Elements { get; private set; }

        public UnmanagedArray(nuint count)
            : this((nint)count)
        {
        }

        public UnmanagedArray(nint count)
        {
            Elements = (T*)NativeMemory.AllocZeroed((nuint)(sizeof(T) * count));
            Length = count;
        }

        public readonly ref T this[nint index]
        {
            get
            {
                Debug.Assert(index >= 0 && index < Length, "Index out of bounds");

                return ref Elements[index];
            }
        }

        public readonly void Dispose()
        {
            NativeMemory.Free(Elements);
        }
    }
}
