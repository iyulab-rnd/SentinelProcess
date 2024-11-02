namespace SentinelProcess.Configuration;

public class SentinelConfiguration
{
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public string? WorkingDirectory { get; set; }

    public static SentinelConfiguration Default => new();
}