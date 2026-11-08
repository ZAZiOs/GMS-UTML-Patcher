using System;

namespace GMS_Patcher_Diff;

class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  GMS-Patcher-Diff - GameMaker Patch Generator");
        Console.WriteLine("=================================================");

        var options = CommandLineOptions.Parse(args);
        if (options == null)
        {
            return 2;
        }

        if (options.ShowHelp)
        {
            CommandLineOptions.PrintHelp();
            return 0;
        }

        try
        {
            return DiffEngine.Run(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Fatal Error: {ex.Message}");
            if (options.Verbose)
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            return 1;
        }
    }
}
