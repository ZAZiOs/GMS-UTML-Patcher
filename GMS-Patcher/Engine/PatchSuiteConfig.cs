using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GMS_UTML_Patcher.Engine;

public class GameDetectionConfig
{
    [JsonPropertyName("windowsExe")]
    public string? WindowsExe { get; set; }

    [JsonPropertyName("macApp")]
    public string? MacApp { get; set; }

    [JsonPropertyName("steamAppId")]
    public string? SteamAppId { get; set; }
}

public class PatchTargetConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("macRelativePath")]
    public string? MacRelativePath { get; set; }

    [JsonPropertyName("patchDir")]
    public string? PatchDir { get; set; }

    [JsonPropertyName("patchConfig")]
    public string PatchConfig { get; set; } = string.Empty;

    [JsonPropertyName("backupFile")]
    public string? BackupFile { get; set; }

    [JsonPropertyName("macBackupFile")]
    public string? MacBackupFile { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;
}

public class FolderCopyRule
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("dest")]
    public string Dest { get; set; } = string.Empty;

    [JsonPropertyName("macDest")]
    public string? MacDest { get; set; }
}

public class FileCopyRule
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("dest")]
    public string Dest { get; set; } = string.Empty;

    [JsonPropertyName("macDest")]
    public string? MacDest { get; set; }
}

public class CopyOperationsConfig
{
    [JsonPropertyName("folders")]
    public List<FolderCopyRule> Folders { get; set; } = new();

    [JsonPropertyName("files")]
    public List<FileCopyRule> Files { get; set; } = new();
}

public class PatchSuiteConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Game Patch";

    [JsonPropertyName("gameId")]
    public string? GameId { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("requiredSpaceMB")]
    public long RequiredSpaceMB { get; set; } = 500;

    [JsonPropertyName("detection")]
    public GameDetectionConfig? Detection { get; set; }

    [JsonPropertyName("targets")]
    public List<PatchTargetConfig> Targets { get; set; } = new();

    [JsonPropertyName("copyOperations")]
    public CopyOperationsConfig? CopyOperations { get; set; }

    [JsonIgnore]
    public string? SuiteDirectory { get; set; }

    public static PatchSuiteConfig? LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return null;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        string json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<PatchSuiteConfig>(json, options);
        if (config != null)
        {
            config.SuiteDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
        }

        return config;
    }
}

