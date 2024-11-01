using Microsoft.Extensions.Logging;
using SentinelProcess.Builder;
using SentinelProcess.Configuration;

namespace SentinelProcess.Extensions;

public static class ProcessExtensions
{
    public static ProcessSentinelBuilder ConfigureProcess(
        this ProcessSentinelBuilder builder,
        Action<SentinelConfiguration> configure)
    {
        configure(builder.Configuration);
        return builder;
    }

    public static ProcessSentinelBuilder UseLogger(
        this ProcessSentinelBuilder builder,
        ILogger logger)
    {
        builder.Logger = logger;
        return builder;
    }

    public static ProcessSentinelBuilder UseLogger<T>(
        this ProcessSentinelBuilder builder,
        ILogger<T> logger)
    {
        return builder.UseLogger((ILogger)logger);
    }
}
