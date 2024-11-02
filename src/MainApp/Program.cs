using Microsoft.Extensions.Logging;
using SentinelProcess.Builder;
using SentinelProcess.Extensions;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole()
           .SetMinimumLevel(LogLevel.Debug);
});

var logger = loggerFactory.CreateLogger<Program>();
logger.LogInformation("Starting MainApp...");

// SubApp의 경로 설정
var subAppPath = Path.Combine(AppContext.BaseDirectory, "SubApp.exe");

// ProcessSentinel 설정 및 시작
var sentinel = ProcessSentinelBuilder.Create()
    .ConfigureProcess(config =>
    {
        config.ProcessName = "SubApp";
        config.ExecutablePath = subAppPath;
        config.Arguments = "60"; // 60초 동안 실행
        config.ShutdownTimeout = TimeSpan.FromSeconds(5);
        config.EnvironmentVariables = new Dictionary<string, string>
        {
            { "APP_ENV", "development" },
            { "CUSTOM_SETTING", "test-value" }
        };
    })
    .UseLogger(logger)
    .Build();

// 이벤트 핸들러 등록
sentinel.OutputReceived += (sender, e) =>
{
    logger.LogInformation("SubApp Output: {Output}", e.Data);
};

sentinel.ErrorReceived += (sender, e) =>
{
    logger.LogError("SubApp Error: {Error}", e.Data);
};

sentinel.StateChanged += (sender, e) =>
{
    logger.LogInformation("SubApp State Changed: {PreviousState} -> {CurrentState}",
        e.PreviousState, e.CurrentState);
};

try
{
    // 프로세스 시작
    await sentinel.StartAsync();
    logger.LogInformation("SubApp started successfully");

    // 20초 후에 프로세스 중지
    await Task.Delay(TimeSpan.FromSeconds(20));
    logger.LogInformation("Initiating SubApp shutdown...");

    await sentinel.StopAsync();
    logger.LogInformation("SubApp stopped successfully");
}
catch (Exception ex)
{
    logger.LogError(ex, "Error occurred while running SubApp");
}
finally
{
    await sentinel.DisposeAsync();
}