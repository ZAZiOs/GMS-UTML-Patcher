using System;
using System.IO;
using UndertaleModLib;

class Program
{
    public static bool Debug = false;
    public static Arguments? arguments;
    static int Main(string[] args)
    {
        try
        {
            arguments = Arguments.Parse(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR 2] Failed to parse arguments: {ex.Message}");
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
            Console.WriteLine("[ERROR 10] --data-path is required.");
            return 10;
        }

        if (string.IsNullOrEmpty(arguments.PatcherFile))
        {
            Console.WriteLine("[ERROR 11] --patcher-file is required.");
            return 11;
        }

        if (!File.Exists(arguments.DataPath))
        {
            Console.WriteLine($"[ERROR 100] File not found: {arguments.DataPath}");
            return 100;
        }

        UndertaleData data;
        try
        {
            Console.WriteLine($"[INFO] Loading data.win from: {arguments.DataPath}");
            using var stream = File.OpenRead(arguments.DataPath);

            data = UndertaleIO.Read(stream);

            Console.WriteLine("[SUCCESS] Loaded data.win successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR 102] Failed to load data.win: {ex.Message}");
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
                Console.WriteLine($"[ERROR 20] Failed to generate/update checks in file: {ex.Message}");
                return 20;
            }
        }

        if (!File.Exists(arguments.PatcherFile))
        {
            Console.WriteLine($"[ERROR 103] Patcher file not found: {arguments.PatcherFile}");
            return 103;
        }

        int valid = TestFileValidator.Validate(arguments.DataPath, data, arguments);
        if (valid != 0)
        {
            Console.WriteLine($"[ERROR {valid}] Test file validation failed.");
            return valid;
        }
        else
        {
            Console.WriteLine("[SUCCESS] File validated successfully.");
        }


        int patchResult = PatchFile.Apply(data, arguments.PatcherFile);
        if (patchResult != 0)
        {
            return patchResult;
        }

        try
        {
            Console.WriteLine($"[INFO] Saving patched data to: {arguments.OutputPath}");
            using var outStream = File.Create(arguments.OutputPath);
            UndertaleIO.Write(outStream, data);
            Console.WriteLine("[SUCCESS] Data saved successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR 104] Failed to save patched data: {ex.Message}");
            return 104;
        }

        return 0;
    }
}
