using System;
using System.IO;
using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;

public static class PatchFile
{
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

            // Здесь можно добавить поддержку других секций патча.

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PATCHER][ERROR 300] Failed to apply patch file: {ex.Message}");
            return 300;
        }
    }
}
