using Microsoft.Extensions.Logging;
using SentinelProcess.Configuration;
using SentinelProcess.Events;
using SentinelProcess.Monitoring;

namespace SentinelProcess.Core;

public class ProcessSentinel : IAsyncDisposable, IDisposable
{
    private readonly ProcessManager _processManager;
    private readonly ProcessEventHandler _eventHandler;
    private readonly ProcessMonitor _processMonitor;
    private readonly ResourceManager _resourceManager;
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
        _processManager = new ProcessManager(configuration, _eventHandler, logger);
        _resourceManager = new ResourceManager(configuration, _processManager, logger);
        _processMonitor = new ProcessMonitor(configuration, _processManager, _resourceManager, logger);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            await _processManager.StartAsync(linkedCts.Token);
            await _processMonitor.StartMonitoringAsync(linkedCts.Token);
            _resourceManager.RegisterShutdownHooks(_cts.Token);
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
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _cts.Dispose();
            _processManager.Dispose();
            _processMonitor.Dispose();
            _resourceManager.Dispose();
        }

        _disposed = true;
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
            Dispose(disposing: true);
        }

        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ProcessSentinel));
    }
}