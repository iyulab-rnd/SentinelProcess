using SentinelProcess.Events;
using SentinelProcess.Logging;
using System.Diagnostics;

namespace SentinelProcess.Core;

public class ProcessEventHandler
{
    private readonly ISentinelLogger? _logger;

    public event EventHandler<ProcessOutputEventArgs>? OutputReceived;
    public event EventHandler<ProcessOutputEventArgs>? ErrorReceived;

    public ProcessEventHandler(ISentinelLogger? logger)
    {
        _logger = logger;
    }

    public void AttachHandlers(Process process)
    {
        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                _logger?.LogDebug($"Process output: {e.Data}");
                OutputReceived?.Invoke(this, new ProcessOutputEventArgs(e.Data));
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                _logger?.LogDebug($"Process error: {e.Data}");
                ErrorReceived?.Invoke(this, new ProcessOutputEventArgs(e.Data));
            }
        };

        process.Exited += (s, e) =>
        {
            _logger?.LogInformation($"Process exited with code: {process.ExitCode}");
        };
    }
}
