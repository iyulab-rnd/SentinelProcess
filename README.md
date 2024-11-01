# SentinelProcess

SentinelProcess is a robust library for managing and monitoring the lifecycle of .NET processes. It provides parent-child process monitoring, graceful shutdown handling, and platform-optimized process management features.

## Key Features

- Process lifecycle management (start, stop, monitoring)
- Automatic termination linked to parent process monitoring
- Platform-optimized process termination handling (Windows/Unix)
- Process output stream capture
- Extensible logging system
- Flexible configuration options
- Asynchronous operation support

## Installation

```bash
dotnet add package SentinelProcess
```

## Usage

### Basic Example

Here's a complete example of a basic parent-child process monitoring scenario.

#### Parent Process (MainApp)

```csharp
using SentinelProcess.Builder;
using SentinelProcess.Extensions;
using SentinelProcess.Logging;

// Custom console logger implementation
class ConsoleLogger : ISentinelLogger
{
    public void LogDebug(string message) =>
        Console.WriteLine($"[DEBUG] {message}");

    public void LogError(string message, Exception? exception = null) =>
        Console.WriteLine($"[ERROR] {message} {exception?.Message}");

    public void LogInformation(string message) =>
        Console.WriteLine($"[INFO] {message}");

    public void LogWarning(string message) =>
        Console.WriteLine($"[WARN] {message}");
}

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine($"MainApp started (PID: {Environment.ProcessId})");
        var logger = new ConsoleLogger();

        try
        {
            var sentinel = ProcessSentinelBuilder.Create()
                .ConfigureProcess(config =>
                {
                    config.ProcessName = "SubApp";
                    config.ExecutablePath = "path/to/SubApp.exe";
                    config.Arguments = "argument1 argument2";
                    config.RunInBackground = false;
                    config.MonitorParentProcess = true;
                    config.ShutdownTimeout = TimeSpan.FromSeconds(5);
                    config.EnvironmentVariables = new Dictionary<string, string>
                    {
                        ["CUSTOM_VARIABLE"] = "test_value"
                    };
                    config.WorkingDirectory = Path.GetDirectoryName(config.ExecutablePath);
                })
                .UseLogger(logger)
                .Build();

            // Register event handlers
            sentinel.OutputReceived += (s, e) =>
                Console.WriteLine($"SubApp output: {e.Data}");

            sentinel.ErrorReceived += (s, e) =>
                Console.WriteLine($"SubApp error: {e.Data}");

            sentinel.StateChanged += (s, e) =>
                Console.WriteLine($"SubApp state changed: {e.PreviousState} -> {e.CurrentState}");

            // Start SubApp
            await sentinel.StartAsync();

            Console.WriteLine("Press any key to stop SubApp...");
            Console.ReadKey();

            // Gracefully stop SubApp
            await sentinel.StopAsync();
            await sentinel.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError("Unexpected error occurred", ex);
        }
    }
}
```

#### Child Process (SubApp)

```csharp
class Program
{
    private static readonly CancellationTokenSource _cts = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine($"SubApp started (PID: {Environment.ProcessId})");
        Console.WriteLine($"Parent process ID: {Environment.GetEnvironmentVariable("SENTINEL_PARENT_PID")}");
        Console.WriteLine($"Custom environment variable: {Environment.GetEnvironmentVariable("CUSTOM_VARIABLE")}");
        Console.WriteLine($"Received arguments: {string.Join(", ", args)}");

        // Register Ctrl+C handler
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            _cts.Cancel();
        };

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Working...");
                await Task.Delay(1000, _cts.Token);
            }

            // Cleanup on normal shutdown
            Console.WriteLine("Performing graceful shutdown...");
            await Task.Delay(500);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation cancelled");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error occurred: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
```

## Advanced Configuration Options

### Configuration Details

| Option | Description | Default | Usage Example |
|--------|-------------|---------|---------------|
| ProcessName | Process identifier | - | `config.ProcessName = "MyApp"` |
| ExecutablePath | Executable file path | - | `config.ExecutablePath = "path/to/app.exe"` |
| Arguments | Command line arguments | - | `config.Arguments = "--port 8080"` |
| RunInBackground | Run in background | true | `config.RunInBackground = false` |
| MonitorParentProcess | Enable parent process monitoring | true | `config.MonitorParentProcess = true` |
| ShutdownTimeout | Shutdown wait time | 5 seconds | `config.ShutdownTimeout = TimeSpan.FromSeconds(10)` |
| WorkingDirectory | Working directory | Current directory | `config.WorkingDirectory = "path/to/dir"` |
| EnvironmentVariables | Environment variable settings | Empty dictionary | `config.EnvironmentVariables["KEY"] = "value"` |

## Process States and Events

### Process State Definitions

```csharp
public enum ProcessState
{
    NotStarted,  // Initial state
    Starting,    // Starting up
    Running,     // Running
    Stopping,    // Shutting down
    Stopped,     // Normally terminated
    Failed       // Error occurred
}
```

### Event Handling

The library provides the following events:

- `OutputReceived`: Standard output received from process
- `ErrorReceived`: Error output received from process
- `StateChanged`: Process state changed

```csharp
// Event handling example
sentinel.OutputReceived += (sender, args) =>
{
    Console.WriteLine($"Output: {args.Data}");
    Console.WriteLine($"Timestamp: {args.Timestamp}");
};

sentinel.StateChanged += (sender, args) =>
{
    Console.WriteLine($"Previous state: {args.PreviousState}");
    Console.WriteLine($"Current state: {args.CurrentState}");
    Console.WriteLine($"Timestamp: {args.Timestamp}");
};
```

## Logging System

### ISentinelLogger Interface

```csharp
public interface ISentinelLogger
{
    void LogInformation(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? exception = null);
    void LogDebug(string message);
}
```

### Custom Logger Implementation Example

```csharp
class FileLogger : ISentinelLogger
{
    private readonly string _logPath;
    
    public FileLogger(string logPath)
    {
        _logPath = logPath;
    }

    public void LogInformation(string message) =>
        WriteLog("INFO", message);

    public void LogWarning(string message) =>
        WriteLog("WARN", message);

    public void LogError(string message, Exception? exception = null) =>
        WriteLog("ERROR", $"{message} {exception?.ToString() ?? ""}");

    public void LogDebug(string message) =>
        WriteLog("DEBUG", message);

    private void WriteLog(string level, string message)
    {
        var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        File.AppendAllLines(_logPath, new[] { logMessage });
    }
}
```

## Platform-Specific Process Termination

### Windows
- Attempts graceful shutdown via `CloseMainWindow()` method
- Calls `Kill(true)` if no response after specified timeout

### Unix
- Attempts graceful shutdown via SIGTERM signal
- Sends SIGKILL signal if no response after specified timeout

## Best Practices and Considerations

1. **Resource Management**
   - Release resources through `using` statement or explicit `DisposeAsync` calls
   - Set appropriate shutdown timeouts

2. **Exception Handling**
   - Handle exceptions that may occur during process start/stop
   - Track issues through logging

3. **Environment Variables**
   - `SENTINEL_PARENT_PID`: Parent process ID transmission
   - Utilize custom environment variables

4. **Working Directory**
   - Recommended to set explicit working directory
   - Exercise caution when using relative paths