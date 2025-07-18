using System;
using System.IO;
using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;

public static class PatchFile
{
    public static string? GetRelativePath(string relativePathFromJson)
    {
        if (string.IsNullOrEmpty(relativePathFromJson))
            return null;

        var jsonPath = Program.arguments?.PatcherFile;
        if (string.IsNullOrEmpty(jsonPath))
            throw new InvalidOperationException("JSON file path was not specified in arguments.");

        var jsonDirectory = Path.GetDirectoryName(Path.GetFullPath(jsonPath));
        if (string.IsNullOrEmpty(jsonDirectory))
            throw new ArgumentException("Invalid JSON file path.");

        var fullPath = Path.Combine(jsonDirectory, relativePathFromJson);
        return Path.GetFullPath(fullPath);
    }

    public static int Apply(UndertaleData data, string patcherFilePath)
    {
        if (!File.Exists(patcherFilePath))
        {
            Console.WriteLine($"[PATCHER][ERROR 103] Patcher file not found: {patcherFilePath}");
            return 103;
        }

        Console.WriteLine("[PATCHER][INFO] Applying patch to file...");

        try
        {
            using var jsonStream = File.OpenRead(patcherFilePath);
            using var doc = JsonDocument.Parse(jsonStream);

            if (doc.RootElement.TryGetProperty("audio", out var audioElement))
            {
                var rawJson = audioElement.GetRawText();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var config = JsonSerializer.Deserialize<AudioImportConfig>(rawJson, options);
                if (config is not null)
                {
                    Console.WriteLine("[PATCHER][INFO] Importing audio files...");
                    var audio_result = AudioImporter.Import(data, config);
                    if (audio_result != 0)
                    {
                        return audio_result;
                    }
                }
                else
                {
                    Console.WriteLine("[PATCHER][WARNING] Failed to deserialize audio config.");
                }
            }

            if (doc.RootElement.TryGetProperty("graphics", out var graphicsElement))
            {
                var rawJson = graphicsElement.GetRawText();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var graphicsConfig = JsonSerializer.Deserialize<GraphicsImportConfig>(rawJson, options);
                if (graphicsConfig is not null)
                {
                    Console.WriteLine("[PATCHER][INFO] Importing sprites...");
                    var result = GraphicsImporter.Import(data, graphicsConfig);
                    if (result != 0)
                        return result;
                }
                else
                {
                    Console.WriteLine("[PATCHER][WARNING] Failed to deserialize graphics config.");
                }
            }

            if (doc.RootElement.TryGetProperty("fonts", out var fontsElement))
            {
                var rawJson = fontsElement.GetRawText();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var fontsConfig = JsonSerializer.Deserialize<GraphicsImportConfig>(rawJson, options);
                if (fontsConfig is not null)
                {
                    Console.WriteLine("[PATCHER][INFO] Importing fonts...");
                    var result = FontsImporter.Import(data, fontsConfig);
                    if (result != 0)
                        return result;
                }
                else
                {
                    Console.WriteLine("[PATCHER][WARNING] Failed to deserialize fonts config.");
                }
            }

            if (doc.RootElement.TryGetProperty("gml", out var gmlElement))
            {
                var rawJson = gmlElement.GetRawText();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var gmlConfig = JsonSerializer.Deserialize<GMLImportConfig>(rawJson, options);
                if (gmlConfig is not null)
                {
                    Console.WriteLine("[PATCHER][INFO] Importing code files...");
                    var result = GMLImporter.Import(data, gmlConfig);
                    if (result != 0)
                        return result;
                }
                else
                {
                    Console.WriteLine("[PATCHER][WARNING] Failed to deserialize GML config.");
                }
            }


            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PATCHER][ERROR 300] Failed to apply patch file: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 300;
        }
    }
}
