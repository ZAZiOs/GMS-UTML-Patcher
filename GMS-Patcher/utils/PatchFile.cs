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
            Out.ERROR("PATCHER", "gray", 103, $"Patcher file not found: {patcherFilePath}");
            return 103;
        }

        Out.INFO("PATCHER", "gray", "Applying patch to file...");

        try
        {
            using var jsonStream = File.OpenRead(patcherFilePath);
            using var doc = JsonDocument.Parse(jsonStream);

            ProcessPatchSections(doc, data);

            return 0;
        }
        catch (Exception ex)
        {
            Out.ERROR("PATCHER", "gray", 300, $"Failed to apply patch file: {ex.Message}");
            Out.INFO("PATCHER", "gray", ex.StackTrace);
            return 300;
        }
    }
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int ProcessPatchSections(JsonDocument doc, UndertaleData data)
    {
        if (TryProcessSection(doc, "audio", out var audioElement))
        {
            try
            {
                var config = JsonSerializer.Deserialize<AudioImportConfig>(audioElement.GetRawText(), _jsonOptions);
                if (config is null)
                {
                    Out.WARN("PATCHER", "gray", "Failed to deserialize audio config.");
                    return 0;
                }

                Out.INFO("PATCHER", "gray", "Importing audio files...");
                return AudioImporter.Import(data, config) switch
                {
                    0 => 0,
                    var result => result
                };
            }
            catch (Exception ex)
            {
                Out.ERROR("PATCHER", "gray", 310, $"Audio import failed: {ex.Message}");
                return 310;
            }
        }

        if (TryProcessSection(doc, "graphics", out var graphicsElement))
        {
            try
            {
                var config = JsonSerializer.Deserialize<GraphicsImportConfig>(graphicsElement.GetRawText(), _jsonOptions);
                if (config is null)
                {
                    Out.WARN("PATCHER", "gray", "Failed to deserialize graphics config.");
                    return 0;
                }

                Out.INFO("PATCHER", "gray", "Importing sprites...");
                return GraphicsImporter.Import(data, config) switch
                {
                    0 => 0,
                    var result => result
                };
            }
            catch (Exception ex)
            {
                Out.ERROR("PATCHER", "gray", 320, $"Graphics import failed: {ex.Message}");
                return 320;
            }
        }

        if (TryProcessSection(doc, "fonts", out var fontsElement))
        {
            try
            {
                var config = JsonSerializer.Deserialize<GraphicsImportConfig>(fontsElement.GetRawText(), _jsonOptions);
                if (config is null)
                {
                    Out.WARN("PATCHER", "gray", "Failed to deserialize fonts config.");
                    return 0;
                }

                Out.INFO("PATCHER", "gray", "Importing fonts...");
                return FontsImporter.Import(data, config) switch
                {
                    0 => 0,
                    var result => result
                };
            }
            catch (Exception ex)
            {
                Out.ERROR("PATCHER", "gray", 330, $"Fonts import failed: {ex.Message}");
                return 330;
            }
        }

        if (TryProcessSection(doc, "gml", out var gmlElement))
        {
            try
            {
                var config = JsonSerializer.Deserialize<GMLImportConfig>(gmlElement.GetRawText(), _jsonOptions);
                if (config is null)
                {
                    Out.WARN("PATCHER", "gray", "Failed to deserialize GML config.");
                    return 0;
                }

                Out.INFO("PATCHER", "gray", "Importing code files...");
                return GMLImporter.Import(data, config) switch
                {
                    0 => 0,
                    var result => result
                };
            }
            catch (Exception ex)
            {
                Out.ERROR("PATCHER", "gray", 340, $"GML import failed: {ex.Message}");
                return 340;
            }
        }

        return 0;
    }

    private static bool TryProcessSection(JsonDocument doc, string sectionName, out JsonElement element)
    {
        return doc.RootElement.TryGetProperty(sectionName, out element);
    }
}
