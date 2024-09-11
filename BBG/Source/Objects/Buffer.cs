using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public unsafe class Buffer : IDisposable
        {
            public enum BufferTarget : uint
            {
                PixelUnpack = OpenTK.Graphics.OpenGL.BufferTarget.PixelUnpackBuffer,
                DispatchIndirect = OpenTK.Graphics.OpenGL.BufferTarget.DispatchIndirectBuffer,
            }

            public enum BufferBackedBlockTarget : uint
            {
                ShaderStorage = OpenTK.Graphics.OpenGL.BufferTarget.ShaderStorageBuffer,
                Uniform = OpenTK.Graphics.OpenGL.BufferTarget.UniformBuffer,
            }

            public enum MemLocation : uint
            {
                /// <summary>
                /// The buffer resides in DEVICE memory
                /// </summary>
                DeviceLocal = 0,

                /// <summary>
                /// The buffer resides in HOST memory
                /// </summary>
                HostLocal = BufferStorageMask.ClientStorageBit,
            }

            public enum MemAccess : uint
            {
                /// <summary>
                /// The buffer must be written or read to by using the mapped memory pointer or read by using the Download functions.
                /// Writes by the HOST only become visible to the DEVICE after a call to glFlushMappedBufferRange.
                /// Writes by the DEVICE only become visible to the HOST after a call to glMemoryBarrier(CLIENT_MAPPED_BUFFER_BARRIER_BIT) followed by a wait for glFenceSync(SYNC_GPU_COMMANDS_COMPLETE, 0)
                /// Note: AMD driver places this in HOST memory!
                /// </summary>
                MappedIncoherent = BufferStorageMask.MapPersistentBit | BufferStorageMask.MapReadBit | BufferStorageMask.MapWriteBit /* MapFlushExplicitBit, MapUnsynchronizedBit*/,

                /// <summary>
                /// The buffer must be written or read to by using the mapped memory pointer or read by using the Download functions.
                /// Writes by the DEVICE only become visible to the HOST after waiting for glFenceSync(SYNC_GPU_COMMANDS_COMPLETE, 0).
                /// Note: AMD driver places this in HOST memory!
                /// </summary>
                MappedCoherent = BufferStorageMask.MapPersistentBit | BufferStorageMask.MapCoherentBit | BufferStorageMask.MapReadBit | BufferStorageMask.MapWriteBit,

                /// <summary>
                /// Same as <see cref="MappedIncoherent"/> except that it's write-only AND leverages ReBAR/SAM on AMD drivers. <br/>
                /// https://gist.github.com/BoyBaykiller/6334c26912e6d2cf0da5c7a76d341a41 <br/>
                /// <![CDATA[https://vanguard.amd.com/project/feedback/view.html?cap=00de81e7400a4f968b2b7c7ed88e35b8&f={662A7A4D-8782-46AA-BB66-31734E56D968}&uf={01316738-8958-4B49-8CFE-1B68A831626C}]]>
                /// </summary>
                MappedIncoherentWriteOnlyReBAR = BufferStorageMask.MapPersistentBit | BufferStorageMask.MapWriteBit,

                /// <summary>
                /// The buffer must be written or read by to using the Upload/Download functions.
                /// Synchronization is taken care of by OpenGL.
                /// </summary>
                AutoSync = BufferStorageMask.DynamicStorageBit,
            }

            public readonly int ID;
            public nint Size { get; private set; }
            public void* MappedMemory { get; private set; }

            public BufferBackedBlockTarget BackingBlock { get; private set; }
            public int BlockIndex { get; private set; }

            public Buffer()
            {
                GL.CreateBuffers(1, ref ID);
            }

            public void BindToBufferBackedBlock(BufferBackedBlockTarget target, int index)
            {
                GL.BindBufferBase((OpenTK.Graphics.OpenGL.BufferTarget)target, (uint)index, ID);
                BackingBlock = target;
                BlockIndex = index;
            }

            public void Allocate(MemLocation memLocation, MemAccess memAccess, nint size, void* data = null)
            {
                GL.NamedBufferStorage(ID, size, data, (BufferStorageMask)memLocation | (BufferStorageMask)memAccess);
                Size = size;

                MappedMemory = null;

                if (memAccess == MemAccess.MappedCoherent)
                {
                    MappedMemory = GL.MapNamedBufferRange(ID, 0, size,(MapBufferAccessMask)memAccess);
                }
                if (memAccess == MemAccess.MappedIncoherent || memAccess == MemAccess.MappedIncoherentWriteOnlyReBAR)
                {
                    MappedMemory = GL.MapNamedBufferRange(ID, 0, size, (MapBufferAccessMask)memAccess | MapBufferAccessMask.MapFlushExplicitBit);
                }
            }

            public void UploadData(nint offset, nint size, void* data)
            {
                GL.NamedBufferSubData(ID, offset, size, data);
            }

            public void UploadData<T>(nint offset, nint size, in T data) where T : unmanaged
            {
                fixed (void* ptr = &data)
                {
                    UploadData(offset, size, ptr);
                }
            }

            public void DownloadData(nint offset, nint size, void* data)
            {
                GL.GetNamedBufferSubData(ID, offset, size, data);
            }

            public void CopyTo(Buffer buffer, nint readOffset, nint writeOffset, nint size)
            {
                GL.CopyNamedBufferSubData(ID, buffer.ID, readOffset, writeOffset, size);
            }

            public void Clear(nint offset, nint size, in uint data)
            {
                fixed (void* ptr = &data)
                {
                    Clear(Texture.InternalFormat.R32Uint, Texture.PixelFormat.RInteger, Texture.PixelType.Int, offset, size, ptr);
                }
            }
            
            public void Clear(nint offset, nint size, in float data)
            {
                fixed (void* ptr = &data)
                {
                    Clear(Texture.InternalFormat.R32Float, Texture.PixelFormat.R, Texture.PixelType.Float, offset, size, ptr);
                }
            }
            
            public void Clear(Texture.InternalFormat internalFormat, Texture.PixelFormat pixelFormat, Texture.PixelType pixelType, nint offset, nint size, void* data)
            {
                GL.ClearNamedBufferSubData(ID, (SizedInternalFormat)internalFormat, offset, size, (PixelFormat)pixelFormat, (PixelType)pixelType, data);
            }

            public void FlushMemory(nint offset, nint size)
            {
                GL.FlushMappedNamedBufferRange(ID, offset, size);
            }

            public void Dispose()
            {
                if (MappedMemory != null)
                {
                    GL.UnmapNamedBuffer(ID);
                }
                GL.DeleteBuffer(ID);
            }

            public static void Recreate(ref Buffer buffer, MemLocation memLocation, MemAccess memAccess, nint size, void* data = null)
            {
                Buffer newBuffer = new Buffer();
                newBuffer.Allocate(memLocation, memAccess, size, data);
                if (buffer.BackingBlock != 0)
                {
                    newBuffer.BindToBufferBackedBlock(buffer.BackingBlock, buffer.BlockIndex);
                }
                buffer.Dispose();

                buffer = newBuffer;
            }

            public static void Recreate<T>(ref TypedBuffer<T> buffer, MemLocation memLocation, MemAccess memAccess, Span<T> newValues) where T : unmanaged
            {
                Recreate(ref buffer, memLocation, memAccess, newValues.Length, MemoryMarshal.GetReference(newValues));
            }

            public static void Recreate<T>(ref TypedBuffer<T> buffer, MemLocation memLocation, MemAccess memAccess, nint count, in T data) where T : unmanaged
            {
                fixed (void* ptr = &data)
                {
                    Recreate(ref buffer, memLocation, memAccess, count, ptr);
                }
            }

            public static void Recreate<T>(ref TypedBuffer<T> buffer, MemLocation memLocation, MemAccess memAccess, nint count, void* data = null) where T : unmanaged
            {
                if (count == 0) return;
                ref Buffer baseBuffer = ref Unsafe.As<TypedBuffer<T>, Buffer>(ref buffer);
                Recreate(ref baseBuffer, memLocation, memAccess, sizeof(T) * count, data);
            }
        }

        public unsafe class TypedBuffer<T> : Buffer where T : unmanaged
        {
            public new T* MappedMemory => (T*)base.MappedMemory;
            public int NumElements => (int)(Size / sizeof(T));

            public TypedBuffer()
                : base()
            {

            }

            public void AllocateElements(MemLocation memLocation, MemAccess memAccess, ReadOnlySpan<T> data)
            {
                AllocateElements(memLocation, memAccess, data.Length, MemoryMarshal.GetReference(data));
            }
            public void AllocateElements(MemLocation memLocation, MemAccess memAccess, nint count, in T data)
            {
                fixed (void* ptr = &data)
                {
                    AllocateElements(memLocation, memAccess, count, ptr);
                }
            }
            public void AllocateElements(MemLocation memLocation, MemAccess memAccess, nint count, void* data = null)
            {
                if (count == 0) return;
                Allocate(memLocation, memAccess, sizeof(T) * count, data);
            }

            public void UploadElements(ReadOnlySpan<T> data, nint startIndex = 0)
            {
                UploadElements(startIndex, data.Length, MemoryMarshal.GetReference(data));
            }
            public void UploadElements(in T data, nint startIndex = 0)
            {
                UploadElements(startIndex, 1, data);
            }
            public void UploadElements(nint startIndex, nint count, in T data)
            {
                if (count == 0) return;
                fixed (void* ptr = &data)
                {
                    UploadData(startIndex * sizeof(T), count * sizeof(T), ptr);
                }
            }

            public void DownloadElements(out T[] values)
            {
                values = new T[NumElements];
                DownloadElements(values, 0);
            }
            public void DownloadElements(Span<T> data, nint startIndex = 0)
            {
                DownloadElements(startIndex, data.Length, ref MemoryMarshal.GetReference(data));
            }
            public void DownloadElements(nint startIndex, nint count, ref T data)
            {
                fixed (void* ptr = &data)
                {
                    DownloadData(startIndex * sizeof(T), count * sizeof(T), ptr);
                }
            }

            public void CopyElementsTo(TypedBuffer<T> buffer, nint readElementOffset, nint writeElementOffset, nint count)
            {
                CopyTo(buffer, readElementOffset * sizeof(T), writeElementOffset * sizeof(T), count * sizeof(T));
            }

            public void CopyElementsTo(TypedBuffer<T> buffer, nint readElementOffset = 0, nint writeElementOffset = 0)
            {
                CopyElementsTo(buffer, readElementOffset, writeElementOffset, NumElements);
            }
        }
    }
}
