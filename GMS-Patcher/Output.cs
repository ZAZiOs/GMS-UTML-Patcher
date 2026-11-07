using System;
using System.Collections.Generic;

public class LogMessageEventArgs : EventArgs
{
    public string Prefix { get; init; } = "";
    public string Level { get; init; } = "";
    public string Message { get; init; } = "";
    public string FullMessage { get; init; } = "";
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

public static class Out
{
    public static event Action<LogMessageEventArgs>? OnLog;

    private static readonly Dictionary<string, ConsoleColor> ColorMap = new Dictionary<string, ConsoleColor>(StringComparer.OrdinalIgnoreCase)
    {
        { "black", ConsoleColor.Black },
        { "blue", ConsoleColor.Blue },
        { "cyan", ConsoleColor.Cyan },
        { "gray", ConsoleColor.DarkGray },
        { "green", ConsoleColor.Green },
        { "magenta", ConsoleColor.Magenta },
        { "red", ConsoleColor.Red },
        { "white", ConsoleColor.White },
        { "yellow", ConsoleColor.Yellow }
    };

    private static void Print(string prefix, string prefixColor, string type, ConsoleColor typeColor, string message)
    {
        string prefixBlock = "[" + prefix + "]";
        string cleanType = type.Trim('[', ']');
        string typeBlock = "[" + cleanType + "]";
        string fullMessage = $"{prefixBlock} {typeBlock,-10} {message}";

        try
        {
            OnLog?.Invoke(new LogMessageEventArgs
            {
                Prefix = prefix,
                Level = cleanType,
                Message = message,
                FullMessage = fullMessage,
                Timestamp = DateTime.Now
            });
        }
        catch { }

        try
        {
            SetColor(prefixColor);
            Console.Write(prefixBlock);
            ResetColor();

            Console.ForegroundColor = typeColor;
            Console.Write(typeBlock);
            ResetColor();

            int currentPos = 0;
            try
            {
                currentPos = Console.CursorLeft;
            }
            catch
            {
                // In non-console or redirected environments, CursorLeft throws IOException
            }

            int messageStartPos = 20;
            string padding = new string(' ', messageStartPos);
            string[] lines = message.Split('\n');

            if (currentPos > 0 && currentPos < messageStartPos)
                Console.Write(new string(' ', messageStartPos - currentPos));
            else
                Console.Write(" ");

            Console.Write(lines[0]);

            for (int i = 1; i < lines.Length; i++)
            {
                Console.WriteLine();
                Console.Write(padding);
                Console.Write(lines[i]);
            }

            if (!message.EndsWith("\n"))
                Console.WriteLine();
        }
        catch
        {
            // Ignore console writing errors if no console is attached (e.g., in GUI mode)
        }
    }



    private static void SetColor(string colorName)
    {
        if (ColorMap.TryGetValue(colorName, out ConsoleColor color))
            Console.ForegroundColor = color;
        else
            Console.ForegroundColor = ConsoleColor.Gray;
    }

    private static void ResetColor() => Console.ResetColor();

    public static void INFO(string prefix, string prefixColor, string message)
        => Print(prefix, prefixColor, "[INFO]", ConsoleColor.White, message);

    public static void WARN(string prefix, string prefixColor, string message)
        => Print(prefix, prefixColor, "[WARN]", ConsoleColor.Yellow, message);

    public static void ERROR(string prefix, string prefixColor, int errorCode, string message)
        => Print(prefix, prefixColor, errorCode == 0 ? "[ERROR]" : $"[ERROR {errorCode}]", ConsoleColor.Red, message);
    
    public static void SUCCESS(string prefix, string prefixColor, string message)
        => Print(prefix, prefixColor, "[SUCCESS]", ConsoleColor.Green, message);

    public static void PASS(string prefix, string prefixColor, string message)
        => Print(prefix, prefixColor, "[PASS]", ConsoleColor.Green, message);

    public static void FAIL(string prefix, string prefixColor, string message)
        => Print(prefix, prefixColor, "[FAIL]", ConsoleColor.Red, message); 

    public static void SKIP(string prefix, string prefixColor, string message)
        => Print(prefix, prefixColor, "[SKIP]", ConsoleColor.Cyan, message); 
    
    public static void VERBOSE(string prefix, string prefixColor, string message)
        => Print(prefix, prefixColor, "[VERBOSE]", ConsoleColor.Cyan, message);
    
    public static void Copyright()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("=== GameMaker Patcher ===");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("> By ZAZiOs @ Cozy Inn ");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine("[https://deltarune.cozy-inn.ru/]");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("> Using UndertaleModLib");
    Console.WriteLine("> Import scripts based on scripts by Samuel Roy, Jockeholm and Kneesnap");
    Console.ResetColor();

    Console.WriteLine();
}


}