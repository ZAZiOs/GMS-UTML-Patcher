using System;
using System.IO;
using GMS_UTML_Patcher.Engine;

class Program
{
    public static bool Debug = false;
    public static Arguments? arguments;

    static int Main(string[] args)
    {
        Out.Copyright();
        try
        {
            arguments = Arguments.Parse(args);
        }
        catch (Exception ex)
        {
            Out.ERROR("ARGUMENTS", "gray", 2, $"Failed to parse arguments: {ex.Message}");
            return 2;
        }

        var options = PatchOptions.FromArguments(arguments);
        options.Debug = Debug;

        if (Debug)
        {
            Out.INFO("DEBUG", "red", $"DataPath: {options.DataPath}");
            Out.INFO("DEBUG", "red", $"OutputPath: {options.OutputPath}");
            Out.INFO("DEBUG", "red", $"Patcher: {options.PatcherFile}");
            Out.INFO("DEBUG", "red", $"SkipTimestampCheck: {options.SkipTimestampCheck}");
            Out.INFO("DEBUG", "red", $"SkipHashCheck: {options.SkipHashCheck}");
            Out.INFO("DEBUG", "red", $"TestMode: {options.TestMode}");
            Console.WriteLine();
        }

        return PatcherEngine.ApplyPatch(options);
    }
}
