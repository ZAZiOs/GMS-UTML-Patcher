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
            Out.ERROR("PATCHER", "white", 103, $"Patcher file not found: {patcherFilePath}");
            return 103;
        }

        Out.INFO("PATCHER", "white", "Applying patch to file...");

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
                    Out.INFO("PATCHER", "white", "Importing audio files...");
                    var audio_result = AudioImporter.Import(data, config);
                    if (audio_result != 0)
                    {
                        return audio_result;
                    }
                }
                else
                {
                    Out.WARN("PATCHER", "white", "Failed to deserialize audio config.");
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
                    Out.INFO("PATCHER", "white", "Importing sprites...");
                    var result = GraphicsImporter.Import(data, graphicsConfig);
                    if (result != 0)
                        return result;
                }
                else
                {
                    Out.WARN("PATCHER", "white", "Failed to deserialize graphics config.");
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
                    Out.INFO("PATCHER", "white", "Importing fonts...");
                    var result = FontsImporter.Import(data, fontsConfig);
                    if (result != 0)
                        return result;
                }
                else
                {
                    Out.WARN("PATCHER", "white", "Failed to deserialize fonts config.");
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
                    Out.INFO("PATCHER", "white", "Importing code files...");
                    var result = GMLImporter.Import(data, gmlConfig);
                    if (result != 0)
                        return result;
                }
                else
                {
                    Out.WARN("PATCHER", "white", "Failed to deserialize GML config.");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Out.ERROR("PATCHER", "white", 300, $"Failed to apply patch file: {ex.Message}");
            Out.INFO("PATCHER", "white", ex.StackTrace);
            return 300;
        }
    }
}
