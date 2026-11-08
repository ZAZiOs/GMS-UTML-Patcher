using System;

namespace GMS_Patcher_Diff;

public class CommandLineOptions
{
    public string OriginalPath { get; set; } = string.Empty;
    public string ModdedPath { get; set; } = string.Empty;
    public string OutputDir { get; set; } = "patch_output";
    public string? GameName { get; set; }

    public bool SkipCode { get; set; }
    public bool SkipGraphics { get; set; }
    public bool SkipAudio { get; set; }
    public bool SkipFonts { get; set; }
    public bool SkipObjects { get; set; }
    public bool SkipRooms { get; set; }
    public bool SkipShaders { get; set; }
    public bool SkipSettings { get; set; }

    public bool Verbose { get; set; }
    public bool ShowHelp { get; set; }

    public static CommandLineOptions? Parse(string[] args)
    {
        var options = new CommandLineOptions();
        var positionals = new System.Collections.Generic.List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg is "-h" or "--help" or "/?")
            {
                options.ShowHelp = true;
                return options;
            }
            else if (arg is "-v" or "--verbose")
            {
                options.Verbose = true;
            }
            else if (arg is "-o" or "--output")
            {
                if (i + 1 < args.Length)
                {
                    options.OutputDir = args[++i];
                }
                else
                {
                    Console.Error.WriteLine("Error: Missing argument for -o/--output");
                    return null;
                }
            }
            else if (arg is "--name")
            {
                if (i + 1 < args.Length)
                {
                    options.GameName = args[++i];
                }
                else
                {
                    Console.Error.WriteLine("Error: Missing argument for --name");
                    return null;
                }
            }
            else if (arg == "--skip-code") options.SkipCode = true;
            else if (arg == "--skip-graphics") options.SkipGraphics = true;
            else if (arg == "--skip-audio") options.SkipAudio = true;
            else if (arg == "--skip-fonts") options.SkipFonts = true;
            else if (arg == "--skip-objects") options.SkipObjects = true;
            else if (arg == "--skip-rooms") options.SkipRooms = true;
            else if (arg == "--skip-shaders") options.SkipShaders = true;
            else if (arg == "--skip-settings") options.SkipSettings = true;
            else if (!arg.StartsWith("-"))
            {
                positionals.Add(arg);
            }
            else
            {
                Console.Error.WriteLine($"Warning: Unknown option '{arg}'");
            }
        }

        if (positionals.Count < 2)
        {
            options.ShowHelp = true;
            return options;
        }

        options.OriginalPath = positionals[0];
        options.ModdedPath = positionals[1];

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("GMS-Patcher-Diff - GameMaker data.win Patch Generator");
        Console.WriteLine("Usage: gms-diff <original.win> <modded.win> [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <original.win>          Path to vanilla/original GameMaker data file");
        Console.WriteLine("  <modded.win>            Path to modified GameMaker data file");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <dir>      Output directory for generated patch (default: ./patch_output)");
        Console.WriteLine("  --name <name>           Override game name in checks");
        Console.WriteLine("  --skip-code             Skip comparing GML scripts/code");
        Console.WriteLine("  --skip-graphics         Skip comparing sprites and textures");
        Console.WriteLine("  --skip-audio            Skip comparing audio/sounds");
        Console.WriteLine("  --skip-fonts            Skip comparing fonts");
        Console.WriteLine("  --skip-objects          Skip comparing game objects (v1.3.0)");
        Console.WriteLine("  --skip-rooms            Skip comparing rooms (v1.3.0)");
        Console.WriteLine("  --skip-shaders          Skip comparing shaders (v1.3.1)");
        Console.WriteLine("  --skip-settings         Skip comparing settings & entrypoints (v1.3.1)");
        Console.WriteLine("  -v, --verbose           Enable verbose console output");
        Console.WriteLine("  -h, --help              Show this help message");
    }
}

