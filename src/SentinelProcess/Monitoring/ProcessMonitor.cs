using SentinelProcess.Configuration;
using SentinelProcess.Core;
using SentinelProcess.Logging;
using System.Diagnostics;

namespace SentinelProcess.Monitoring;

public class ProcessMonitor : IDisposable
{
    private readonly SentinelConfiguration _configuration;
    private readonly ProcessManager _processManager;
    private readonly ResourceManager _resourceManager;
    private readonly ISentinelLogger? _logger;
    private readonly FileSystemWatcher? _parentWatcher;
    private readonly bool _isChildProcess;
    private bool _disposed;

    public ProcessMonitor(
        SentinelConfiguration configuration,
        ProcessManager processManager,
        ResourceManager resourceManager,
        ISentinelLogger? logger)
    {
        _configuration = configuration;
        _processManager = processManager;
        _resourceManager = resourceManager;
        _logger = logger;

        _isChildProcess = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SENTINEL_PARENT_PID"));

        if (_isChildProcess && configuration.MonitorParentProcess)
        {
            _parentWatcher = new FileSystemWatcher(Path.GetTempPath())
            {
                Filter = Path.GetFileName(_resourceManager.ParentPidFile),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
            };
        }
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken)
    {
        if (_configuration.MonitorParentProcess)
        {
            if (_isChildProcess)
            {
                await StartParentFileMonitoringAsync(cancellationToken);
            }
            else
            {
                await StartParentProcessMonitoringAsync(cancellationToken);
            }
        }
    }

    private async Task StartParentFileMonitoringAsync(CancellationToken cancellationToken)
    {
        if (_parentWatcher == null) return;

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());

            _parentWatcher.Deleted += async (sender, e) =>
            {
                try
                {
                    _logger?.LogWarning("Parent process PID file was deleted. Initiating shutdown.");
                    await _processManager.StopAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogError("Error during file deletion handling", ex);
                }
            };

            _parentWatcher.Error += (sender, e) =>
            {
                _logger?.LogError("File watcher error occurred");
                tcs.TrySetException(new IOException("File watcher error occurred"));
            };

            _parentWatcher.EnableRaisingEvents = true;

            // 초기 파일 존재 확인
            await Task.Run(() =>
            {
                if (!File.Exists(_resourceManager.ParentPidFile))
                {
                    _logger?.LogWarning("Parent process PID file not found. Initiating shutdown.");
                    throw new FileNotFoundException("Parent PID file not found", _resourceManager.ParentPidFile);
                }
            }, cancellationToken);

            // 모니터링 시작 표시
            tcs.TrySetResult(true);

            // 취소될 때까지 대기
            await tcs.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation("File monitoring cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Error setting up parent process file monitoring", ex);
            await _processManager.StopAsync(cancellationToken);
            throw;
        }
    }


    private async Task StartParentProcessMonitoringAsync(CancellationToken cancellationToken)
    {
        var parentProcessId = Environment.ProcessId;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!IsProcessRunning(parentProcessId))
                {
                    _logger?.LogWarning("Parent process is no longer running. Stopping managed process.");
                    await _processManager.StopAsync(cancellationToken);
                    break;
                }

                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation("Parent process monitoring cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Error monitoring parent process", ex);
            await _processManager.StopAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _parentWatcher?.Dispose();
        _disposed = true;
    }
}