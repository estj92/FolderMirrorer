namespace FolderMirrorer;

public static class Printer
{
    public static void Print(IEnumerable<string> message)
    {
        foreach (var item in message)
        {
            Print(item);
        }
    }
    public static void Print(string message) => Console.WriteLine(message);
    public static void Print(string message, ConsoleColor foregroundColor)
    {
        Console.ForegroundColor = foregroundColor;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
