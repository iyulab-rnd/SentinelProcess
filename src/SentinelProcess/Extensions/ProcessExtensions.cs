using SentinelProcess.Builder;
using SentinelProcess.Configuration;
using SentinelProcess.Logging;

namespace SentinelProcess.Extensions;

public static class ProcessExtensions
{
    public static ProcessSentinelBuilder ConfigureProcess(this ProcessSentinelBuilder builder, Action<SentinelConfiguration> configure)
    {
        configure(builder.Configuration);
        return builder;
    }

    public static ProcessSentinelBuilder UseLogger(this ProcessSentinelBuilder builder, ISentinelLogger logger)
    {
        builder.Logger = logger;
        return builder;
    }
}