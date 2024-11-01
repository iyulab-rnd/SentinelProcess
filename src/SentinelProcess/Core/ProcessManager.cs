using Microsoft.Extensions.Logging;
using SentinelProcess.Configuration;
using SentinelProcess.Events;
using SentinelProcess.Monitoring;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SentinelProcess.Core;

public class ProcessManager : IDisposable
{
    private readonly SentinelConfiguration _configuration;
    private readonly ProcessEventHandler _eventHandler;
    private readonly ILogger? _logger;
    private Process? _managedProcess;
    private ProcessState _currentState;
    private bool _disposed;

    public ProcessState State
    {
        get => _currentState;
        private set
        {
            var previousState = _currentState;
            _currentState = value;
            OnStateChanged(previousState, value);
        }
    }

    public event EventHandler<ProcessStateChangedEventArgs>? StateChanged;

    public ProcessManager(
        SentinelConfiguration configuration,
        ProcessEventHandler eventHandler,
        ILogger? logger)
    {
        _configuration = configuration;
        _eventHandler = eventHandler;
        _logger = logger;
        _currentState = ProcessState.NotStarted;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (State != ProcessState.NotStarted)
            throw new InvalidOperationException($"Cannot start process in state: {State}");

        State = ProcessState.Starting;
        await StartProcessAsync(cancellationToken);
        State = ProcessState.Running;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (State != ProcessState.Running)
            return;

        State = ProcessState.Stopping;
        await StopProcessAsync(cancellationToken);
        State = ProcessState.Stopped;
    }

    private async Task StartProcessAsync(CancellationToken cancellationToken)
    {
        var startInfo = CreateProcessStartInfo();
        _managedProcess = new Process { StartInfo = startInfo };
        _eventHandler.AttachHandlers(_managedProcess);

        if (!_managedProcess.Start())
        {
            throw new InvalidOperationException("Failed to start process");
        }

        _managedProcess.BeginOutputReadLine();
        _managedProcess.BeginErrorReadLine();
    }

    private ProcessStartInfo CreateProcessStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _configuration.ExecutablePath,
            Arguments = _configuration.Arguments,
            UseShellExecute = false,
            CreateNoWindow = _configuration.RunInBackground,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = _configuration.WorkingDirectory ?? Directory.GetCurrentDirectory()
        };

        startInfo.Environment["SENTINEL_PARENT_PID"] = Environment.ProcessId.ToString();
        foreach (var env in _configuration.EnvironmentVariables)
        {
            startInfo.Environment[env.Key] = env.Value;
        }

        return startInfo;
    }

    private async Task StopProcessAsync(CancellationToken cancellationToken)
    {
        if (_managedProcess == null || _managedProcess.HasExited)
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await StopWindowsProcessAsync(cancellationToken);
        }
        else
        {
            await StopUnixProcessAsync(cancellationToken);
        }
    }

    private async Task StopWindowsProcessAsync(CancellationToken cancellationToken)
    {
        if (_managedProcess == null) return;

        try
        {
            bool gracefulShutdown = _managedProcess.CloseMainWindow();
            if (!gracefulShutdown)
            {
                _managedProcess.Kill(true);
            }

            using var timeoutCts = new CancellationTokenSource(_configuration.ShutdownTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _managedProcess.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger?.LogWarning(LogEvents.ProcessStopping,
                    "Shutdown timeout reached, forcing process termination");
                if (!_managedProcess.HasExited)
                {
                    _managedProcess.Kill(true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(LogEvents.ProcessFailed, ex, "Failed to stop Windows process");
            throw;
        }
    }

    private async Task StopUnixProcessAsync(CancellationToken cancellationToken)
    {
        if (_managedProcess == null) return;

        try
        {
            _managedProcess.Kill(false); // SIGTERM

            using var timeoutCts = new CancellationTokenSource(_configuration.ShutdownTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _managedProcess.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger?.LogWarning("Shutdown timeout reached, sending SIGKILL");
                if (!_managedProcess.HasExited)
                {
                    _managedProcess.Kill(true); // SIGKILL
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Failed to stop Unix process", ex);
            throw;
        }
    }

    private void OnStateChanged(ProcessState previousState, ProcessState currentState)
    {
        if (_managedProcess != null)
        {
            StateChanged?.Invoke(this, new ProcessStateChangedEventArgs(
                _managedProcess, previousState, currentState));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _managedProcess?.Dispose();
        _disposed = true;
    }
}