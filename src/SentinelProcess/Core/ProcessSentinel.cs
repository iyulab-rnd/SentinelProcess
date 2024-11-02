using Microsoft.Extensions.Logging;
using SentinelProcess.Configuration;
using SentinelProcess.Events;

namespace SentinelProcess.Core;

public class ProcessSentinel : IAsyncDisposable
{
    private readonly ProcessManager _processManager;
    private readonly ProcessEventHandler _eventHandler;
    private readonly ProcessGroupManager _groupManager;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts;
    private bool _disposed;

    public SentinelConfiguration Configuration { get; }
    public ProcessState State => _processManager.State;

    public event EventHandler<ProcessOutputEventArgs>? OutputReceived
    {
        add => _eventHandler.OutputReceived += value;
        remove => _eventHandler.OutputReceived -= value;
    }

    public event EventHandler<ProcessOutputEventArgs>? ErrorReceived
    {
        add => _eventHandler.ErrorReceived += value;
        remove => _eventHandler.ErrorReceived -= value;
    }

    public event EventHandler<ProcessStateChangedEventArgs>? StateChanged
    {
        add => _processManager.StateChanged += value;
        remove => _processManager.StateChanged -= value;
    }

    public ProcessSentinel(SentinelConfiguration configuration, ILogger? logger = null)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
        _cts = new CancellationTokenSource();

        _eventHandler = new ProcessEventHandler(logger);
        _groupManager = new ProcessGroupManager(configuration.ProcessName, logger);
        _processManager = new ProcessManager(configuration, _eventHandler, _groupManager, logger);

        RegisterShutdownHooks();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            await _processManager.StartAsync(linkedCts.Token);
            _logger?.LogInformation(LogEvents.MonitoringStarted, "Process sentinel started");
        }
        catch (Exception)
        {
            await StopAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _processManager.StopAsync(cancellationToken);
        _logger?.LogInformation(LogEvents.MonitoringStopped, "Process sentinel stopped");
    }

    private void RegisterShutdownHooks()
    {
        AppDomain.CurrentDomain.ProcessExit += async (s, e) =>
        {
            await StopAsync();
        };

        Console.CancelKeyPress += async (s, e) =>
        {
            e.Cancel = true;
            await StopAsync();
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            await StopAsync();
        }
        finally
        {
            _cts.Dispose();
            _processManager.Dispose();
            _groupManager.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ProcessSentinel));
    }
}