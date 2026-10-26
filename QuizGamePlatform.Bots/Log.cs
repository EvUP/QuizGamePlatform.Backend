namespace QuizGamePlatform.Bots;

// вывод от нескольких ботов сразу: строки не перемешиваются, у каждого бота свой цвет
public static class Log
{
    private static readonly object Sync = new();

    private static readonly ConsoleColor[] Colors =
    [
        ConsoleColor.Cyan, ConsoleColor.Yellow, ConsoleColor.Green, ConsoleColor.Magenta,
        ConsoleColor.Blue, ConsoleColor.DarkYellow, ConsoleColor.DarkCyan, ConsoleColor.DarkGreen,
    ];

    public static void Bot(int index, string name, string message) => Write(Colors[index % Colors.Length], name, message);

    public static void System(string message) => Write(ConsoleColor.Gray, "боты", message);

    public static void Error(string name, string message) => Write(ConsoleColor.Red, name, message);

    private static void Write(ConsoleColor color, string name, string message)
    {
        lock (Sync)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"{DateTime.Now:HH:mm:ss} ");
            Console.ForegroundColor = color;
            Console.Write($"[{name}] ");
            Console.ResetColor();
            Console.WriteLine(message);
        }
    }
}
