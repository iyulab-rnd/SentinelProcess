using Microsoft.Extensions.Logging;
using SentinelProcess.Configuration;
using SentinelProcess.Events;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SentinelProcess.Core;

public class ProcessManager : IDisposable
{
    private readonly SentinelConfiguration _configuration;
    private readonly ProcessEventHandler _eventHandler;
    private readonly ProcessGroupManager _groupManager;
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
        ProcessGroupManager groupManager,
        ILogger? logger)
    {
        _configuration = configuration;
        _eventHandler = eventHandler;
        _groupManager = groupManager;
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

    private Task StartProcessAsync(CancellationToken cancellationToken)
    {
        var startInfo = CreateProcessStartInfo();
        _managedProcess = new Process { StartInfo = startInfo };
        _eventHandler.AttachHandlers(_managedProcess);

        if (!_managedProcess.Start())
        {
            throw new InvalidOperationException("Failed to start process");
        }

        _groupManager.AssignProcess(_managedProcess);

        _managedProcess.BeginOutputReadLine();
        _managedProcess.BeginErrorReadLine();
        return Task.CompletedTask;
    }

    private ProcessStartInfo CreateProcessStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _configuration.ExecutablePath,
            Arguments = _configuration.Arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = _configuration.WorkingDirectory ?? Directory.GetCurrentDirectory()
        };

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

        try
        {
            // Windows와 Unix 플랫폼별로 다른 종료 방식 적용
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: CloseMainWindow 시도
                bool gracefulShutdown = _managedProcess.CloseMainWindow();
                _logger?.LogInformation(LogEvents.ProcessStopping,
                    "Windows process graceful shutdown attempt: {Result}", gracefulShutdown);
            }
            else
            {
                // Unix: SIGTERM 시그널 전송
                _managedProcess.Kill(false); // SIGTERM
                _logger?.LogInformation(LogEvents.ProcessStopping,
                    "Unix process SIGTERM signal sent to process {ProcessId}", _managedProcess.Id);
            }

            // 프로세스 종료 대기
            using var timeoutCts = new CancellationTokenSource(_configuration.ShutdownTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _managedProcess.WaitForExitAsync(linkedCts.Token);
                _logger?.LogInformation(LogEvents.ProcessStopped,
                    "Process exited successfully with code: {ExitCode}", _managedProcess.ExitCode);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger?.LogWarning(LogEvents.ProcessStopping,
                    "Shutdown timeout reached, forcing process termination");

                if (!_managedProcess.HasExited)
                {
                    _managedProcess.Kill(true); // SIGKILL on Unix, forceful termination on Windows
                    _logger?.LogWarning(LogEvents.ProcessStopping,
                        "Process forcefully terminated");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(LogEvents.ProcessFailed, ex, "Failed to stop process");
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

        if (_managedProcess != null)
        {
            if (!_managedProcess.HasExited)
            {
                StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            _managedProcess.Dispose();
        }

        _disposed = true;
    }
}