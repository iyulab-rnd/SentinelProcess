using Microsoft.Extensions.Logging;
using SentinelProcess.Configuration;
using SentinelProcess.Core;

namespace SentinelProcess.Builder;

public class ProcessSentinelBuilder
{
    public SentinelConfiguration Configuration { get; } = new();
    public ILogger? Logger { get; set; }

    public static ProcessSentinelBuilder Create() => new();

    public ProcessSentinel Build()
    {
        return new ProcessSentinel(Configuration, Logger);
    }
}
