namespace FolderMirrorer;

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6,
}


public class SingletonMultiLogger
{
    // Consider:
    // Some form of dependency injection, so there can be separate loggers for console and file
    // Formatting (for file at least) as JSON
    // Using the proper logging framework. It's silly making my own LogLevel enum

    private static readonly Lazy<SingletonMultiLogger> lazy = new(new SingletonMultiLogger());
    public static SingletonMultiLogger Instance => lazy.Value;
    private SingletonMultiLogger()
    {
        LogPath = Path.Combine(AppContext.BaseDirectory, "mirrors.log");
    }
    private readonly string LogPath;

    public async Task Info(string message) => await Log(message, LogLevel.Information);
    public async Task Error(string message) => await Log(message, LogLevel.Error);
    public async Task Log(string message, LogLevel logLevel)
    {
        Console.ForegroundColor = GetColor(logLevel);
        await DoLog(Enumerable.Repeat(message, 1), logLevel, DateTime.Now);
        Console.ResetColor();
    }

    public async Task Info(IEnumerable<string> messages) => await Log(messages, LogLevel.Information);
    public async Task Error(IEnumerable<string> messages) => await Log(messages, LogLevel.Error);
    public async Task Log(IEnumerable<string> messages, LogLevel logLevel)
    {
        Console.ForegroundColor = GetColor(logLevel);
        await DoLog(messages, logLevel, DateTime.Now);
        Console.ResetColor();
    }

    public static void NewLine() => Console.WriteLine();

    private async Task DoLog(IEnumerable<string> rawLines, LogLevel logLevel, DateTimeOffset timestamp)
    {
        var lines = rawLines.Select(message => $"{timestamp} [{logLevel}]: {message}").ToArray();

        var writeTask = File.AppendAllLinesAsync(LogPath, lines);
        foreach (var logLine in lines)
        {
            Console.WriteLine(logLine);
        }
        await writeTask;
    }

    private static ConsoleColor GetColor(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => ConsoleColor.White,
        LogLevel.Debug => ConsoleColor.White,
        LogLevel.Information => ConsoleColor.Green,
        LogLevel.Warning => ConsoleColor.Red,
        LogLevel.Error => ConsoleColor.Red,
        LogLevel.Critical => ConsoleColor.Red,
        LogLevel.None => ConsoleColor.White,
        _ => ConsoleColor.White,
    };
}
