using System;
using System.IO;
using System.Diagnostics;
using BBLogger;
using IDKEngine.Utils;

namespace IDKEngine;

class StateRecorder<T> where T : unmanaged
{
    private int _replayStateIndex;
    public int ReplayStateIndex
    {
        get => _replayStateIndex;
        
        set
        {
            if (Count == 0)
            {
                _replayStateIndex = 0;
                return;
            }
            _replayStateIndex = value % Count;
        }
    }

    public int Count => recordedStates.Count;

    private readonly List<T> recordedStates;
    public StateRecorder()
    {
        recordedStates = new List<T>();
    }

    public StateRecorder(ReadOnlySpan<T> values)
        : this()
    {
        recordedStates.AddRange(values);
    }

    public static StateRecorder<T> Load(string path)
    {
        if (!File.Exists(path))
        {
            Logger.Log(Logger.LogLevel.Error, $"File \"{path}\" does not exist");
            return new StateRecorder<T>();
        }

        if (!Helper.TryReadFromFile(path, out T[] recordedStates))
        {
            Logger.Log(Logger.LogLevel.Error, $"Error loading \"{path}\"");
            return new StateRecorder<T>();
        }
        return new StateRecorder<T>(recordedStates);
    }

    public T this[int index]
    {
        get
        {
            Debug.Assert(index < Count);
            return recordedStates[index];
        }
    }

    public void Record(in T state)
    {
        recordedStates.Add(state);
    }

    public T Replay()
    {
        return recordedStates[ReplayStateIndex++];
    }

    public void Clear()
    {
        ReplayStateIndex = 0;
        recordedStates.Clear();
    }

    public void SaveToFile(string path)
    {
        if (recordedStates == null || recordedStates.Count == 0)
        {
            return;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        Helper.WriteToFile<T>(path, recordedStates);
    }
}
