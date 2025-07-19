using System;
using System.IO;
using UndertaleModLib;

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

        if (Debug)
        {
            Console.WriteLine($"DataPath: {arguments.DataPath}");
            Console.WriteLine($"OutputPath: {arguments.OutputPath}");
            Console.WriteLine($"Patcher: {arguments.PatcherFile}");
            Console.WriteLine($"SkipTimestampCheck: {arguments.SkipTimestampCheck}");
            Console.WriteLine($"SkipHashCheck: {arguments.SkipHashCheck}");
            Console.WriteLine($"TestMode: {arguments.TestMode}");
            Console.WriteLine($"PatcherMode: {arguments.PatcherMode}");
        }

        if (string.IsNullOrEmpty(arguments.DataPath))
        {
            Out.ERROR("ARGUMENTS", "gray", 10, "--data-path is required.");
            return 10;
        }

        if (string.IsNullOrEmpty(arguments.PatcherFile))
        {
            Out.ERROR("ARGUMENTS", "gray", 11, "--patcher-file is required.");
            return 11;
        }

        if (!File.Exists(arguments.DataPath))
        {
            Out.ERROR("IO", "gray", 100, $"File not found: {arguments.DataPath}");
            return 100;
        }

        UndertaleData data;
        try
        {
            Out.INFO("IO", "gray", $"Loading data.win from: {arguments.DataPath}");
            using var stream = File.OpenRead(arguments.DataPath);

            data = UndertaleIO.Read(stream);

            Out.SUCCESS("IO", "gray", "Loaded data.win successfully.");
        }
        catch (Exception ex)
        {
            Out.ERROR("IO", "gray", 102, $"Failed to load data.win: {ex.Message}");
            return 102;
        }


        if (arguments.TestMode)
        {
            try
            {
                TestFileGenerator.Generate(arguments.DataPath, data, arguments.PatcherFile);
                return 0;
            }
            catch (Exception ex)
            {
                Out.ERROR("CHECKS", "blue", 20, $"Failed to generate/update checks in file: {ex.Message}");
                return 20;
            }
        }

        if (!File.Exists(arguments.PatcherFile))
        {
            Out.ERROR("IO", "gray", 103, $"Patcher file not found: {arguments.PatcherFile}");
            return 103;
        }

        int valid = TestFileValidator.Validate(arguments.DataPath, data, arguments);
        if (valid != 0)
        {
            Out.ERROR("CHECKS", "blue", valid, "Test file validation failed");
            return valid;
        }
        else
        {
            Out.SUCCESS("CHECKS", "blue", "File validated successfully.");
        }


        int patchResult = PatchFile.Apply(data, arguments.PatcherFile);
        if (patchResult != 0)
        {
            return patchResult;
        }

        try
        {
            Out.INFO("IO", "gray", $"Saving patched data to: {arguments.OutputPath}");
            using var outStream = File.Create(arguments.OutputPath);
            UndertaleIO.Write(outStream, data);
            Out.SUCCESS("IO", "gray", "Data saved successfully.");
        }
        catch (Exception ex)
        {
            Out.ERROR("IO", "gray", 104, $"Failed to save patched data: {ex.Message}");
            return 104;
        }

        return 0;
    }
}
