using System.Runtime.InteropServices;

class Program
{
    static async Task Main(string[] args)
    {
        int duration = args.Length > 0 ? int.Parse(args[0]) : 30; // 기본 30초
        Console.WriteLine($"SubApp started. Will run for {duration} seconds.");
        Console.WriteLine($"Process ID: {Environment.ProcessId}");
        Console.WriteLine($"Parent PID: {Environment.GetEnvironmentVariable("SENTINEL_PARENT_PID")}");

        // 환경변수 출력
        Console.WriteLine("\nEnvironment Variables:");
        Console.WriteLine($"APP_ENV: {Environment.GetEnvironmentVariable("APP_ENV")}");
        Console.WriteLine($"CUSTOM_SETTING: {Environment.GetEnvironmentVariable("CUSTOM_SETTING")}");

        try
        {
            // 시스템 정보 출력
            Console.WriteLine($"\nRunning on: {RuntimeInformation.OSDescription}");
            Console.WriteLine($"Framework: {RuntimeInformation.FrameworkDescription}");

            // 정기적으로 상태 업데이트 출력
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Received shutdown signal...");
            };

            var startTime = DateTime.Now;
            while (!cts.Token.IsCancellationRequested)
            {
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                if (elapsed >= duration)
                {
                    Console.WriteLine("Duration completed, shutting down...");
                    break;
                }

                Console.WriteLine($"Running... Elapsed: {elapsed:F1}s / {duration}s");
                await Task.Delay(1000, cts.Token);

                // 가끔 에러 메시지 출력 (테스트용)
                if (elapsed % 10 == 0)
                {
                    Console.Error.WriteLine($"Test error message at {elapsed:F1}s");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation was cancelled");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error occurred: {ex.Message}");
            return;
        }

        Console.WriteLine("SubApp shutting down gracefully...");
    }
}