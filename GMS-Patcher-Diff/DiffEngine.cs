using System;
using System.IO;
using System.Security.Cryptography;
using GMS_Patcher_Diff.Differs;
using UndertaleModLib;

namespace GMS_Patcher_Diff;

public class DiffEngine
{
    public static int Run(CommandLineOptions options)
    {
        if (!File.Exists(options.OriginalPath))
        {
            Console.Error.WriteLine($"Error: Original file not found at '{options.OriginalPath}'");
            return 1;
        }

        if (!File.Exists(options.ModdedPath))
        {
            Console.Error.WriteLine($"Error: Modded file not found at '{options.ModdedPath}'");
            return 1;
        }

        Directory.CreateDirectory(options.OutputDir);

        // Clean any existing patch subdirectories to prevent orphaned files from previous runs
        string[] patchSubdirs = { "code", "graphics", "audio", "fonts", "objects", "rooms", "shaders", "graphics-temp", "fonts-temp" };
        foreach (var dir in patchSubdirs)
        {
            string p = Path.Combine(options.OutputDir, dir);
            if (Directory.Exists(p))
            {
                try { Directory.Delete(p, true); } catch { }
            }
        }

        Console.WriteLine($"[1/10] Computing SHA256 of original file...");
        string sha256;
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(options.OriginalPath))
        {
            byte[] hash = sha.ComputeHash(stream);
            sha256 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        Console.WriteLine($"[2/10] Loading original: {Path.GetFileName(options.OriginalPath)}...");
        UndertaleData originalData;
        using (var fs = File.OpenRead(options.OriginalPath))
        {
            originalData = UndertaleIO.Read(fs);
        }

        Console.WriteLine($"[3/10] Loading modded: {Path.GetFileName(options.ModdedPath)}...");
        UndertaleData moddedData;
        using (var fs = File.OpenRead(options.ModdedPath))
        {
            moddedData = UndertaleIO.Read(fs);
        }

        string gameName = options.GameName ??
                          originalData.GeneralInfo?.DisplayName?.Content ??
                          originalData.GeneralInfo?.Name?.Content ??
                          "Game";

        bool isGms2 = originalData.IsGameMaker2();
        bool isYyc = originalData.IsYYC();
        ulong timestamp = originalData.GeneralInfo?.Timestamp ?? 0;

        Console.WriteLine($"Game: {gameName} | GMS2: {isGms2} | YYC: {isYyc} | Timestamp: {timestamp}");

        if (originalData.IsGameMaker2() != moddedData.IsGameMaker2())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine("  [!] WARNING: Different GameMaker Engine Generations Detected!");
            Console.WriteLine($"      Original : {(originalData.IsGameMaker2() ? "GameMaker Studio 2" : "GameMaker: Studio 1.4")} (Bytecode {originalData.GeneralInfo?.BytecodeVersion})");
            Console.WriteLine($"      Modded   : {(moddedData.IsGameMaker2() ? "GameMaker Studio 2" : "GameMaker: Studio 1.4")} (Bytecode {moddedData.GeneralInfo?.BytecodeVersion})");
            Console.WriteLine("      Cross-engine patching has inherent compatibility limits (GMS2 layers,");
            Console.WriteLine("      cameras, array literals cannot run under GMS 1.4 runner executable).");
            Console.WriteLine("      GMS2 runtime compatibility shims will be excluded from the patch.");
            Console.WriteLine();
            Console.ResetColor();
        }

        // 4. Code
        int codeCount = 0;
        if (!options.SkipCode)
        {
            Console.WriteLine($"[4/10] Comparing GML Code...");
            codeCount = CodeDiffer.Process(originalData, moddedData, options.OutputDir, options.Verbose);
        }

        // 5. Graphics
        var graphicsResult = new GraphicsDifferResult();
        if (!options.SkipGraphics)
        {
            Console.WriteLine($"[5/10] Comparing Sprites (ExportAllSprites flat + padded)...");
            graphicsResult = GraphicsDiffer.Process(originalData, moddedData, options.OutputDir, options.Verbose);
        }

        // 6. Audio
        int audioCount = 0;
        if (!options.SkipAudio)
        {
            Console.WriteLine($"[6/10] Comparing Audio...");
            audioCount = AudioDiffer.Process(originalData, moddedData, options.OutputDir, options.Verbose);
        }

        // 7. Fonts
        int fontCount = 0;
        if (!options.SkipFonts)
        {
            Console.WriteLine($"[7/10] Comparing Fonts...");
            fontCount = FontDiffer.Process(originalData, moddedData, options.OutputDir, options.Verbose);
        }

        // 8. Objects (v1.3.0)
        int objectCount = 0;
        if (!options.SkipObjects)
        {
            Console.WriteLine($"[8/10] Comparing Game Objects (v1.3.0)...");
            objectCount = ObjectDiffer.Process(originalData, moddedData, options.OutputDir, options.Verbose);
        }

        // 9. Rooms (v1.3.0)
        int roomCount = 0;
        if (!options.SkipRooms)
        {
            Console.WriteLine($"[9/10] Comparing Rooms (v1.3.0)...");
            roomCount = RoomDiffer.Process(originalData, moddedData, options.OutputDir, options.Verbose);
        }

        // 10. Shaders & Settings (v1.3.1)
        int shaderCount = 0;
        if (!options.SkipShaders)
        {
            Console.WriteLine($"[10/10] Comparing Shaders (v1.3.1)...");
            shaderCount = ShaderDiffer.Process(originalData, moddedData, options.OutputDir, options.Verbose);
        }

        SettingsImportConfig? settingsConfig = null;
        if (!options.SkipSettings)
        {
            settingsConfig = SettingsDiffer.Process(originalData, moddedData, options.Verbose);
        }

        // Determine patch config filename based on original data filename
        string origFileName = Path.GetFileName(options.OriginalPath);
        string configFileName = string.IsNullOrEmpty(origFileName) ? "data.win.json" : $"{origFileName}.json";

        string configPath = Path.Combine(options.OutputDir, configFileName);
        Console.WriteLine($"Generating patch configuration: {configFileName}...");

        PatchConfigGenerator.Generate(
            configPath,
            gameName,
            isGms2,
            isYyc,
            timestamp,
            sha256,
            codeCount,
            graphicsResult,
            audioCount,
            fontCount,
            objectCount,
            roomCount,
            shaderCount,
            settingsConfig);

        Console.WriteLine();
        Console.WriteLine("=================================================");
        Console.WriteLine("  GMS-Patcher-Diff: Patch Generation Complete!");
        Console.WriteLine("=================================================");
        Console.WriteLine($"  Output Directory : {Path.GetFullPath(options.OutputDir)}");
        Console.WriteLine($"  Config File      : {configFileName}");
        Console.WriteLine($"  GML Scripts      : {codeCount}");
        Console.WriteLine($"  Sprite Frames    : {graphicsResult.ExportedCount}");
        Console.WriteLine($"  Audio Files      : {audioCount}");
        Console.WriteLine($"  Fonts            : {fontCount}");
        Console.WriteLine($"  Objects (1.3.0)  : {objectCount}");
        Console.WriteLine($"  Rooms (1.3.0)    : {roomCount}");
        Console.WriteLine($"  Shaders (1.3.1)  : {shaderCount}");
        Console.WriteLine($"  Settings (1.3.1) : {(settingsConfig != null ? "Yes" : "None")}");
        Console.WriteLine("=================================================");

        return 0;
    }
}
