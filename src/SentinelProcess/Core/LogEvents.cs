using Microsoft.Extensions.Logging;

namespace SentinelProcess.Core;

public static class LogEvents
{
    public static readonly EventId ProcessStarting = new(1000, "ProcessStarting");
    public static readonly EventId ProcessStarted = new(1001, "ProcessStarted");
    public static readonly EventId ProcessStopping = new(1002, "ProcessStopping");
    public static readonly EventId ProcessStopped = new(1003, "ProcessStopped");
    public static readonly EventId ProcessFailed = new(1004, "ProcessFailed");
    public static readonly EventId ProcessOutput = new(1005, "ProcessOutput");
    public static readonly EventId ProcessError = new(1006, "ProcessError");
    public static readonly EventId MonitoringStarted = new(2000, "MonitoringStarted");
    public static readonly EventId MonitoringStopped = new(2001, "MonitoringStopped");
    public static readonly EventId ParentProcessLost = new(2002, "ParentProcessLost");
    public static readonly EventId ResourceCleanup = new(3000, "ResourceCleanup");
}
