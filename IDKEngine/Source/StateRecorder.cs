using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BBLogger;
using IDKEngine.Utils;

namespace IDKEngine
{
    class StateRecorder<T> where T : unmanaged
    {
        public ref readonly T this[int index]
        {
            get
            {
                Debug.Assert(index < StatesCount);
                return ref recordedStates[index];
            }
        }

        private int _replayStateIndex;
        public int ReplayStateIndex
        {
            get => _replayStateIndex;
            set
            {
                if (StatesCount == 0)
                {
                    _replayStateIndex = 0;
                    return;
                }
                _replayStateIndex = value % StatesCount;
            }
        }

        public bool AreStatesLoaded => StatesCount > 0;

        public int StatesCount { get; private set; }

        private T[] recordedStates;
        public StateRecorder()
        {
            
        }

        public void Record(T state)
        {
            if (recordedStates == null)
            {
                recordedStates = new T[240];
                recordedStates[StatesCount++] = state;
                return;
            }

            if (StatesCount >= recordedStates.Length)
            {
                Array.Resize(ref recordedStates, (int)(recordedStates.Length * 1.5f));
            }
            recordedStates[StatesCount++] = state;
        }

        public T Replay()
        {
            if (StatesCount == 0)
            {
                Logger.Log(Logger.LogLevel.Warn, "Cannot replay anything, because there is no state data loaded");
                return new T();
            }
            return recordedStates[ReplayStateIndex++];
        }

        public void Clear()
        {
            ReplayStateIndex = 0;
            StatesCount = 0;
        }

        public unsafe void Load(string path)
        {
            if (!File.Exists(path))
            {
                Logger.Log(Logger.LogLevel.Error, $"File \"{path}\" does not exist");
                return;
            }

            if (!Helper.TryReadFromFile(path, out recordedStates))
            {
                Logger.Log(Logger.LogLevel.Error, $"Error loading \"{path}\"");
                return;
            }

            StatesCount = recordedStates.Length;
            ReplayStateIndex = 0;
        }

        public void SaveToFile(string path)
        {
            if (recordedStates == null || recordedStates.Length == 0)
            {
                return;
            }
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            Helper.WriteToFile(path, new ReadOnlySpan<T>(recordedStates, 0, StatesCount));
        }
    }
}
