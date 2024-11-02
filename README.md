# SentinelProcess

SentinelProcess is a robust .NET library for managing and monitoring process lifecycles. It provides comprehensive features for parent-child process monitoring, graceful shutdown handling, and platform-optimized process management.

## Features

- **Parent-Child Process Monitoring**: Automatically monitors parent process status and handles cleanup when the parent process terminates
- **Cross-Platform Process Management**: Optimized for both Windows and Unix-like systems
- **Graceful Shutdown Handling**: Supports configurable shutdown timeouts and graceful termination
- **Process Group Management**: Manages process groups for better process lifecycle control
- **Event-Based Monitoring**: Provides events for process output, errors, and state changes
- **Configurable Environment**: Supports custom environment variables and working directory settings
- **Logging Integration**: Built-in support for Microsoft.Extensions.Logging
- **Resource Cleanup**: Automatic cleanup of temporary files and process resources

## Installation

Install the package via NuGet:

```bash
dotnet add package SentinelProcess
```

## Quick Start

Here's a basic example of how to use SentinelProcess:

```csharp
using SentinelProcess.Builder;
using Microsoft.Extensions.Logging;

// Create and configure the process sentinel
var sentinel = ProcessSentinelBuilder.Create()
    .ConfigureProcess(config =>
    {
        config.ProcessName = "MyApp";
        config.ExecutablePath = "path/to/your/app.exe";
        config.Arguments = "--some-arg value";
        config.ShutdownTimeout = TimeSpan.FromSeconds(5);
    })
    .UseLogger(logger)
    .Build();

// Register event handlers
sentinel.OutputReceived += (sender, e) => 
    Console.WriteLine($"Output: {e.Data}");

sentinel.ErrorReceived += (sender, e) => 
    Console.WriteLine($"Error: {e.Data}");

// Start the process
await sentinel.StartAsync();

// Stop the process when needed
await sentinel.StopAsync();
```

## Configuration Options

The `SentinelConfiguration` class provides various options to customize process behavior:

```csharp
var config = new SentinelConfiguration
{
    ProcessName = "MyApp",
    ExecutablePath = "path/to/app.exe",
    Arguments = "--arg value",
    ShutdownTimeout = TimeSpan.FromSeconds(5),
    WorkingDirectory = "path/to/working/dir",
    EnvironmentVariables = new Dictionary<string, string>
    {
        ["KEY"] = "value"
    }
};
```

## Process Lifecycle Events

SentinelProcess provides events to monitor the process lifecycle:

```csharp
sentinel.StateChanged += (sender, e) =>
{
    Console.WriteLine($"Process state changed from {e.PreviousState} to {e.CurrentState}");
};

sentinel.OutputReceived += (sender, e) =>
{
    Console.WriteLine($"Process output: {e.Data}");
};

sentinel.ErrorReceived += (sender, e) =>
{
    Console.WriteLine($"Process error: {e.Data}");
};
```

## Platform-Specific Features

### Windows
- Uses Job Objects for process group management
- Supports graceful window closing before forced termination

### Unix-like Systems
- Uses Process Groups (PGID) for process management
- Implements SIGTERM/SIGKILL signal handling

## Advanced Usage

### Custom Logger Integration

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole()
           .SetMinimumLevel(LogLevel.Debug);
});

var sentinel = ProcessSentinelBuilder.Create()
    .UseLogger(loggerFactory.CreateLogger<ProcessSentinel>())
    .ConfigureProcess(config => { /* configuration */ })
    .Build();
```

### Async Disposal

```csharp
await using var sentinel = ProcessSentinelBuilder.Create()
    .ConfigureProcess(config => { /* configuration */ })
    .Build();

await sentinel.StartAsync();
// Process will be automatically disposed when the using block ends
```

## Best Practices

1. Always use the async disposal pattern (`await using`) or explicitly call `DisposeAsync()`
2. Configure appropriate shutdown timeouts based on your application needs
3. Handle process events to monitor the application state
4. Use logging for better debugging and monitoring
5. Set up proper error handling around process lifecycle events

## Requirements

- .NET 8.0 or later
- Microsoft.Extensions.Logging.Abstractions package

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Support

If you encounter any issues or have questions, please file an issue on the GitHub repository.