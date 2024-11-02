using SentinelProcess.Core;
using System.Diagnostics;

namespace SentinelProcess.Events;

public class ProcessOutputEventArgs : EventArgs
{
    public string Data { get; }
    public DateTime Timestamp { get; }
    public ProcessOutputEventArgs(string data)
    {
        Data = data;
        Timestamp = DateTime.UtcNow;
    }
}

public class ProcessStateChangedEventArgs : EventArgs
{
    public Process Process { get; }
    public ProcessState PreviousState { get; }
    public ProcessState CurrentState { get; }
    public DateTime Timestamp { get; }

    public ProcessStateChangedEventArgs(Process process, ProcessState previousState, ProcessState currentState)
    {
        Process = process;
        PreviousState = previousState;
        CurrentState = currentState;
        Timestamp = DateTime.UtcNow;
    }
}