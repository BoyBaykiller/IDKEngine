using System.Runtime.InteropServices;

namespace IDKEngine.Utils
{
    public static class Memory
    {
        public static unsafe T* Malloc<T>(nint count = 1) where T : unmanaged
        {
            return (T*)NativeMemory.Alloc((nuint)(sizeof(T) * count));
        }

        public static unsafe T* Malloc<T>(int count = 1) where T : unmanaged
        {
            return (T*)NativeMemory.Alloc((nuint)(sizeof(T) * count));
        }

        public static unsafe void Free(void* ptr)
        {
            NativeMemory.Free(ptr);
        }

        public static unsafe void Fill(void* ptr, byte value, nint byteCount)
        {
            NativeMemory.Fill(ptr, (nuint)byteCount, value);
        }

        public static unsafe void Copy<T1, T2>(in T1 src, ref T2 dest, nint byteCount)
            where T1 : unmanaged
            where T2 : unmanaged
        {
            fixed (void* srcPtr = &src, destPtr = &dest)
            {
                Copy(srcPtr, destPtr, byteCount);
            }
        }

        public static unsafe void Copy<T1>(in T1 src, T1* dest, nint byteCount)
            where T1 : unmanaged
        {
            fixed (void* srcPtr = &src)
            {
                Copy(srcPtr, dest, byteCount);
            }
        }

        public static unsafe void Copy(void* src, void* dest, nint byteCount)
        {
            NativeMemory.Copy(src, dest, (nuint)byteCount);
        }
    }
}
