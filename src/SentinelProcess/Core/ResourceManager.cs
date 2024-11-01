using Microsoft.Extensions.Logging;
using SentinelProcess.Configuration;

namespace SentinelProcess.Core;

public class ResourceManager : IDisposable
{
    private readonly SentinelConfiguration _configuration;
    private readonly ProcessManager _processManager;
    private readonly ILogger? _logger;
    private readonly string _pidFilePath;
    private bool _disposed;

    public string ParentPidFile { get; }

    public ResourceManager(
        SentinelConfiguration configuration, 
        ProcessManager processManager,
        ILogger? logger)
    {
        _configuration = configuration;
        _processManager = processManager;
        _logger = logger;

        _pidFilePath = Path.Combine(
            Path.GetTempPath(),
            $"SentinelProcess_{configuration.ProcessName}_{Environment.ProcessId}.pid"
        );

        ParentPidFile = Path.Combine(
            Path.GetTempPath(),
            $"SentinelProcess_*_{Environment.GetEnvironmentVariable("SENTINEL_PARENT_PID")}.pid"
        );
    }

    public void SaveProcessInfo(int processId)
    {
        try
        {
            File.WriteAllText(_pidFilePath, processId.ToString());
        }
        catch (Exception ex)
        {
            _logger?.LogError(LogEvents.ResourceCleanup, ex, "Failed to save process information");
        }
    }

    public void RegisterShutdownHooks(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            // 동기적으로 처리하여 프로세스 종료 전 완료 보장
            _processManager.StopAsync(cancellationToken).GetAwaiter().GetResult();
        };

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            // 동기적으로 처리하여 Ctrl+C 처리 전 완료 보장
            _processManager.StopAsync(cancellationToken).GetAwaiter().GetResult();
        };
    }

    private void CleanupResources()
    {
        try
        {
            if (File.Exists(_pidFilePath))
            {
                File.Delete(_pidFilePath);
                _logger?.LogInformation(LogEvents.ResourceCleanup,
                    "Successfully cleaned up PID file: {PidFile}", _pidFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(LogEvents.ResourceCleanup, ex, "Failed to cleanup resources");

        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        CleanupResources();
        _disposed = true;
    }
}