using SentinelProcess.Configuration;
using SentinelProcess.Core;
using SentinelProcess.Logging;

namespace SentinelProcess.Builder;

public class ProcessSentinelBuilder
{
    public SentinelConfiguration Configuration { get; } = new();
    public ISentinelLogger? Logger { get; set; }

    public static ProcessSentinelBuilder Create() => new();

    public ProcessSentinel Build()
    {
        return new ProcessSentinel(Configuration, Logger);
    }
}