namespace JustDanceEditor.Logging;

[System.Obsolete("JustDanceEditor.Logging.Logger has been removed. Use Microsoft.Extensions.Logging.ILogger instead.")]
public static class Logger
{
    public static void Log(string message, object? _ = null) => throw new NotSupportedException("Legacy Logger removed. Use Microsoft.Extensions.Logging.ILogger.");
}