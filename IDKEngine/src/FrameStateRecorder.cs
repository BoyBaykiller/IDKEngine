using System;
using System.IO;
using System.Diagnostics;

namespace IDKEngine
{
    class FrameStateRecorder<T> where T : unmanaged
    {
        public ref readonly T this[int index]
        {
            get
            {
                Debug.Assert(index < FrameCount);
                return ref recordedFrames[index];
            }
        }

        public bool IsFramesLoaded => FrameCount > 0;

        public int FrameCount { get; private set; }

        private int _replayFrame;
        public int ReplayFrameIndex
        {
            get => _replayFrame;
            set
            {
                if (FrameCount == 0)
                {
                    _replayFrame = 0;
                    return;
                }
                _replayFrame = value % FrameCount;
            }
        }

        private T[] recordedFrames;
        public FrameStateRecorder()
        {
            
        }

        public void Record(T state)
        {
            if (recordedFrames == null)
            {
                recordedFrames = new T[240];
                recordedFrames[FrameCount++] = state;
                return;
            }

            if (FrameCount >= recordedFrames.Length)
            {
                Array.Resize(ref recordedFrames, (int)(recordedFrames.Length * 1.5f));
            }
            recordedFrames[FrameCount++] = state;
        }

        public T Replay()
        {
            if (FrameCount == 0)
            {
                Console.WriteLine("Error: Can't replay state. Nothing is loaded");
                return new T();
            }
            return recordedFrames[ReplayFrameIndex++];
        }

        public void Clear()
        {
            ReplayFrameIndex = 0;
            FrameCount = 0;
        }

        public unsafe void Load(string path)
        {
            try
            {
                using FileStream fileStream = File.OpenRead(path);
                recordedFrames = new T[fileStream.Length / sizeof(T)];
                fixed (void* ptr = recordedFrames)
                {
                    Span<byte> data = new Span<byte>(ptr, recordedFrames.Length * sizeof(T));
                    fileStream.Read(data);
                }
                FrameCount = recordedFrames.Length;
                ReplayFrameIndex = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public unsafe void SaveToFile(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            using FileStream file = File.OpenWrite(path);
            fixed (void* ptr = recordedFrames)
            {
                ReadOnlySpan<byte> data = new ReadOnlySpan<byte>(ptr, FrameCount * sizeof(RecordableState));
                file.Write(data);
            }
        }
    }
}
