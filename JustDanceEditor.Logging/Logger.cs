namespace JustDanceEditor.Logging;

public static class Logger
{
    public static LogLevel PrintLevel { get; set; } = LogLevel.Info;

    static readonly SemaphoreSlim _semaphore = new(1, 1);

    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        LogAsync(message, level).Wait();
    }

    public static async Task LogAsync(string message, LogLevel level = LogLevel.Info)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string logEntry = $"{timestamp} [{level}] | {message}";
        string logMessage = $"{timestamp} | {message}";

        // Prevent multiple threads from writing to the log file at the same time
        await _semaphore.WaitAsync();

        try
        {
            // Write to ./logs/log.txt
            if (!Directory.Exists("logs"))
                Directory.CreateDirectory("logs");

            await using (StreamWriter logFile = File.AppendText("logs/log.txt"))
                logFile.WriteLine(logEntry);

            // Print to the console
            if (level >= PrintLevel)
                PrintToScreen(logMessage, level);
        }
        finally
        {
            // Release the semaphore
            _semaphore.Release();
        }
    }

    static void PrintToScreen(string message, LogLevel level)
    {
        ConsoleColor originalColor = Console.ForegroundColor;

        switch (level)
        {
            case LogLevel.Debug:
                Console.ForegroundColor = ConsoleColor.Gray;
                break;
            case LogLevel.Info:
                Console.ForegroundColor = ConsoleColor.White;
                break;
            case LogLevel.Important:
            case LogLevel.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                break;
            case LogLevel.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                break;
            case LogLevel.Fatal:
                Console.ForegroundColor = ConsoleColor.DarkRed;
                break;
        }

        Console.WriteLine(message);
        Console.ForegroundColor = originalColor;
    }

    public static void ClearLog()
    {
        if (File.Exists("logs/log.txt"))
            File.Delete("logs/log.txt");
    }
}
