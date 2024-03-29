﻿using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

        public bool IsFramesLoaded => FrameCount > 0;

        public int FrameCount { get; private set; }

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
                Logger.Log(Logger.LogLevel.Warn, "Cannot replay anything, because there is no frame data loaded");
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
            if (!File.Exists(path))
            {
                Logger.Log(Logger.LogLevel.Error, $"File \"{path}\" does not exist");
                return;
            }

            using FileStream fileStream = File.OpenRead(path);
            if (fileStream.Length % sizeof(T) != 0)
            {
                Logger.Log(Logger.LogLevel.Error, $"Cannot load \"{path}\", because file size is not a multiple of {sizeof(T)} bytes");
                return;
            }

            if (fileStream.Length == 0)
            {
                Logger.Log(Logger.LogLevel.Info, $"\"{path}\" is an empty file");
                return;
            }

            recordedFrames = new T[fileStream.Length / sizeof(T)];
            
            Span<byte> data = MemoryMarshal.AsBytes<T>(recordedFrames);
            fileStream.Read(data);

            FrameCount = recordedFrames.Length;
            ReplayFrameIndex = 0;
        }

        public void SaveToFile(string path)
        {
            if (recordedFrames == null || recordedFrames.Length == 0)
            {
                return;
            }
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            using FileStream file = File.OpenWrite(path);
            ReadOnlySpan<byte> data = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(recordedFrames, 0, FrameCount));
            file.Write(data);
        }
    }
}
