using Microsoft.Extensions.Logging;
using SentinelProcess.Events;
using System.Diagnostics;

namespace SentinelProcess.Core;

public class ProcessEventHandler
{
    private readonly ILogger? _logger;

    public event EventHandler<ProcessOutputEventArgs>? OutputReceived;
    public event EventHandler<ProcessOutputEventArgs>? ErrorReceived;

    public ProcessEventHandler(ILogger? logger)
    {
        _logger = logger;
    }

    public void AttachHandlers(Process process)
    {
        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                _logger?.LogDebug(LogEvents.ProcessOutput, "Process output: {Output}", e.Data);
                OutputReceived?.Invoke(this, new ProcessOutputEventArgs(e.Data));
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                _logger?.LogDebug(LogEvents.ProcessError, "Process error: {Error}", e.Data);
                ErrorReceived?.Invoke(this, new ProcessOutputEventArgs(e.Data));
            }
        };

        process.Exited += (s, e) =>
        {
            _logger?.LogInformation(LogEvents.ProcessStopped, "Process exited with code: {ExitCode}", process.ExitCode);
        };
    }
}
