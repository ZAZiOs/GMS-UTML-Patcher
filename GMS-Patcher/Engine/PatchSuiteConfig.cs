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

    [JsonPropertyName("macPatchConfig")]
    public string? MacPatchConfig { get; set; }

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

    public static string? FindSuiteConfigFile(string? customPath = null)
    {
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            return Path.GetFullPath(customPath);

        string baseDir = AppContext.BaseDirectory;
        string cwd = Directory.GetCurrentDirectory();

        var candidates = new List<string>
        {
            Path.Combine(baseDir, "data", "patch_suite.json"),
            Path.Combine(baseDir, "data", "patches.json"),
            Path.Combine(baseDir, "patch_suite.json"),
            Path.Combine(baseDir, "patches.json"),
            Path.Combine(cwd, "data", "patch_suite.json"),
            Path.Combine(cwd, "data", "patches.json"),
            Path.Combine(cwd, "patch_suite.json"),
            Path.Combine(cwd, "patches.json")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }

        DirectoryInfo? cur = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && cur != null; i++)
        {
            string p1 = Path.Combine(cur.FullName, "data", "patch_suite.json");
            if (File.Exists(p1)) return Path.GetFullPath(p1);

            string p2 = Path.Combine(cur.FullName, "data", "patches.json");
            if (File.Exists(p2)) return Path.GetFullPath(p2);

            string p3 = Path.Combine(cur.FullName, "patch_suite.json");
            if (File.Exists(p3)) return Path.GetFullPath(p3);

            string p4 = Path.Combine(cur.FullName, "patches.json");
            if (File.Exists(p4)) return Path.GetFullPath(p4);

            cur = cur.Parent;
        }

        return null;
    }

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

    public string? ResolvePath(string? relativeOrSubPath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrSubPath))
            return null;

        if (Path.IsPathRooted(relativeOrSubPath))
            return File.Exists(relativeOrSubPath) || Directory.Exists(relativeOrSubPath) ? Path.GetFullPath(relativeOrSubPath) : null;

        string baseDir = AppContext.BaseDirectory;
        string cwd = Directory.GetCurrentDirectory();

        var searchDirs = new List<string>();
        if (!string.IsNullOrEmpty(SuiteDirectory))
        {
            searchDirs.Add(SuiteDirectory);
            searchDirs.Add(Path.Combine(SuiteDirectory, "data"));
            searchDirs.Add(Path.Combine(SuiteDirectory, "patches"));
            searchDirs.Add(Path.Combine(SuiteDirectory, "data", "patches"));
            searchDirs.Add(Path.Combine(SuiteDirectory, "patch"));
            searchDirs.Add(Path.Combine(SuiteDirectory, "data", "patch"));
        }

        searchDirs.Add(baseDir);
        searchDirs.Add(Path.Combine(baseDir, "data"));
        searchDirs.Add(Path.Combine(baseDir, "patches"));
        searchDirs.Add(Path.Combine(baseDir, "data", "patches"));
        searchDirs.Add(Path.Combine(baseDir, "patch"));
        searchDirs.Add(Path.Combine(baseDir, "data", "patch"));

        searchDirs.Add(cwd);
        searchDirs.Add(Path.Combine(cwd, "data"));
        searchDirs.Add(Path.Combine(cwd, "patches"));
        searchDirs.Add(Path.Combine(cwd, "data", "patches"));

        DirectoryInfo? cur = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && cur != null; i++)
        {
            searchDirs.Add(cur.FullName);
            searchDirs.Add(Path.Combine(cur.FullName, "data"));
            searchDirs.Add(Path.Combine(cur.FullName, "patches"));
            searchDirs.Add(Path.Combine(cur.FullName, "data", "patches"));
            searchDirs.Add(Path.Combine(cur.FullName, "data", "patch"));
            cur = cur.Parent;
        }

        foreach (var dir in searchDirs)
        {
            string candidate = Path.Combine(dir, relativeOrSubPath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }
}

