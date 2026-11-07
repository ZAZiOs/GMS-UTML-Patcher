using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GMS_UTML_Patcher.Services;

namespace GMS_UTML_Patcher.Engine;

public class GameValidationResult
{
    public bool IsValid { get; set; }
    public string NormalizedPath { get; set; } = string.Empty;
    public List<string> MissingFiles { get; set; } = new();
    public long AvailableSpaceMB { get; set; }
    public long RequiredSpaceMB { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class MultiPatchRunner
{
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static string NormalizeGamePath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return string.Empty;

        inputPath = inputPath.Trim('\"', '\'', ' ');

        // 1. Resolve .lnk or .url shortcuts
        string? resolved = ShortcutResolver.ResolveShortcut(inputPath);
        if (!string.IsNullOrEmpty(resolved))
        {
            if (resolved.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
            {
                string appId = resolved.Substring(6);
                string? steamDir = SteamLibraryDetector.FindGameDirectory("DELTARUNE", appId);
                if (!string.IsNullOrEmpty(steamDir))
                    inputPath = steamDir;
            }
            else
            {
                inputPath = resolved;
            }
        }

        // 2. If it's a file, get directory or handle .app
        if (File.Exists(inputPath))
        {
            inputPath = Path.GetDirectoryName(inputPath) ?? inputPath;
        }

        // 3. macOS specific normalization:
        if (IsMacOS)
        {
            // If path ends with Contents/Resources, find .app
            if (inputPath.EndsWith("Contents/Resources", StringComparison.OrdinalIgnoreCase) ||
                inputPath.EndsWith("Contents" + Path.DirectorySeparatorChar + "Resources", StringComparison.OrdinalIgnoreCase))
            {
                string? parentApp = Directory.GetParent(Directory.GetParent(inputPath)?.FullName ?? "")?.FullName;
                if (!string.IsNullOrEmpty(parentApp) && parentApp.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    return parentApp;
                }
            }
        }

        return Path.GetFullPath(inputPath);
    }

    public static GameValidationResult ValidateGameFolder(string rawPath, PatchSuiteConfig suite)
    {
        var result = new GameValidationResult();
        string normalized = NormalizeGamePath(rawPath);
        result.NormalizedPath = normalized;

        if (string.IsNullOrEmpty(normalized) || !Directory.Exists(normalized))
        {
            result.IsValid = false;
            result.ErrorMessage = "Указанная папка игры не существует.";
            return result;
        }

        // Check required target files
        foreach (var target in suite.Targets)
        {
            if (!target.Required)
                continue;

            string relative = GetTargetRelativePath(target);
            string fullTarget = ResolveGameFilePath(normalized, relative);

            if (!File.Exists(fullTarget))
            {
                result.MissingFiles.Add(relative);
            }
        }

        result.RequiredSpaceMB = suite.RequiredSpaceMB;
        result.AvailableSpaceMB = SafeFileOperations.GetFreeSpaceMB(normalized);

        if (result.MissingFiles.Count > 0)
        {
            result.IsValid = false;
            result.ErrorMessage = $"Не найдены обязательные файлы игры: {string.Join(", ", result.MissingFiles)}";
            return result;
        }

        result.IsValid = true;
        return result;
    }

    public static string GetTargetRelativePath(PatchTargetConfig target)
    {
        if (IsMacOS && !string.IsNullOrEmpty(target.MacRelativePath))
            return target.MacRelativePath;

        return target.RelativePath;
    }

    public static string? GetTargetBackupRelativePath(PatchTargetConfig target)
    {
        if (IsMacOS && !string.IsNullOrEmpty(target.MacBackupFile))
            return target.MacBackupFile;

        return target.BackupFile;
    }

    public static string ResolveGameFilePath(string gameRoot, string relativePath)
    {
        if (IsMacOS && gameRoot.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(gameRoot, "Contents", "Resources", relativePath);
        }

        return Path.Combine(gameRoot, relativePath);
    }

    public static async Task<int> ApplySuiteAsync(
        string gameRoot,
        PatchSuiteConfig suite,
        IProgress<PatchProgress>? progress = null,
        Func<int, string, Task<bool>>? onWarningConfirmation = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedRoot = NormalizeGamePath(gameRoot);
        var validation = ValidateGameFolder(normalizedRoot, suite);
        if (!validation.IsValid)
        {
            Out.ERROR("SUITE", "gray", 100, validation.ErrorMessage ?? "Validation failed");
            return 100;
        }

        int totalTargets = suite.Targets.Count;
        int currentTargetIndex = 0;

        var tempFiles = new List<string>();
        var filesToReplace = new Dictionary<string, string>();
        var foldersToCopy = new Dictionary<string, string>();

        try
        {
            foreach (var target in suite.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string targetRelative = GetTargetRelativePath(target);
                string targetFullPath = ResolveGameFilePath(normalizedRoot, targetRelative);

                if (!File.Exists(targetFullPath))
                {
                    if (target.Required)
                    {
                        Out.ERROR("SUITE", "gray", 100, $"Required target file not found: {targetFullPath}");
                        return 100;
                    }
                    continue;
                }

                currentTargetIndex++;
                int basePercentage = (int)((currentTargetIndex - 1) / (double)(totalTargets + 1) * 80);
                string title = string.IsNullOrEmpty(target.Title) ? target.Id : target.Title;

                progress?.Report(new PatchProgress(basePercentage, $"Патчим {title}...", target.Id));

                string patchConfigFile = Path.IsPathRooted(target.PatchConfig)
                    ? target.PatchConfig
                    : Path.Combine(suite.SuiteDirectory ?? ".", target.PatchConfig);

                if (!File.Exists(patchConfigFile))
                {
                    Out.ERROR("SUITE", "gray", 103, $"Patch config file not found: {patchConfigFile}");
                    return 103;
                }

                // Prepare temp output path
                string tempOutput = Path.Combine(Path.GetTempPath(), $"gms_patch_{target.Id}_{Guid.NewGuid():N}.win");
                tempFiles.Add(tempOutput);

                var patchOptions = new PatchOptions
                {
                    DataPath = targetFullPath,
                    PatcherFile = patchConfigFile,
                    OutputPath = tempOutput,
                    SkipTimestampCheck = false,
                    SkipHashCheck = false
                };

                // Create sub-progress reporter
                var subProgress = new Progress<PatchProgress>(p =>
                {
                    int scaled = basePercentage + (int)(p.Percentage * 0.8 / (totalTargets + 1));
                    progress?.Report(new PatchProgress(scaled, $"{title}: {p.Status}", target.Id));
                });

                int result = PatcherEngine.ApplyPatch(patchOptions, subProgress, cancellationToken);

                // Handle warning codes 204, 205, 206 with interactive confirmation
                if (result == 204 || result == 205 || result == 206)
                {
                    bool userConfirmed = false;
                    if (onWarningConfirmation != null)
                    {
                        string warnMessage = result switch
                        {
                            204 => "Хэш файла не совпадает (возможно, игра уже модифицирована). Всё равно продолжить?",
                            205 => "Версия игры новее, чем версия перевода. Перевод может работать некорректно. Продолжить?",
                            206 => "Версия игры и перевода не совпадает. Всё равно продолжить?",
                            _ => "Обнаружено несоответствие версии. Продолжить?"
                        };

                        userConfirmed = await onWarningConfirmation(result, warnMessage);
                    }

                    if (!userConfirmed)
                    {
                        Out.WARN("SUITE", "gray", "Установка отменена пользователем из-за предупреждения версии.");
                        return result;
                    }

                    // Retry with skipped checks
                    patchOptions.SkipTimestampCheck = true;
                    patchOptions.SkipHashCheck = true;
                    result = PatcherEngine.ApplyPatch(patchOptions, subProgress, cancellationToken);
                }

                if (result != 0)
                {
                    Out.ERROR("SUITE", "gray", result, $"Ошибка при патчинге цели {target.Id}");
                    return result;
                }

                // Prepare backup
                string? backupRelative = GetTargetBackupRelativePath(target);
                if (!string.IsNullOrEmpty(backupRelative))
                {
                    string backupFullPath = ResolveGameFilePath(normalizedRoot, backupRelative);
                    SafeFileOperations.EnsureBackup(targetFullPath, backupFullPath);
                }

                filesToReplace[tempOutput] = targetFullPath;
            }

            // Copy operations
            progress?.Report(new PatchProgress(85, "Копируем сопутствующие файлы..."));

            if (suite.CopyOperations != null)
            {
                foreach (var folderRule in suite.CopyOperations.Folders)
                {
                    string src = Path.IsPathRooted(folderRule.Source)
                        ? folderRule.Source
                        : Path.Combine(suite.SuiteDirectory ?? ".", folderRule.Source);

                    string destRelative = (IsMacOS && !string.IsNullOrEmpty(folderRule.MacDest))
                        ? folderRule.MacDest
                        : folderRule.Dest;

                    string dst = ResolveGameFilePath(normalizedRoot, destRelative);

                    if (Directory.Exists(src))
                    {
                        foldersToCopy[src] = dst;
                    }
                }

                foreach (var fileRule in suite.CopyOperations.Files)
                {
                    string src = Path.IsPathRooted(fileRule.Source)
                        ? fileRule.Source
                        : Path.Combine(suite.SuiteDirectory ?? ".", fileRule.Source);

                    string destRelative = (IsMacOS && !string.IsNullOrEmpty(fileRule.MacDest))
                        ? fileRule.MacDest
                        : fileRule.Dest;

                    string dst = ResolveGameFilePath(normalizedRoot, destRelative);

                    if (File.Exists(src))
                    {
                        filesToReplace[src] = dst;
                    }
                }
            }

            // Execute atomic batch replacement
            progress?.Report(new PatchProgress(90, "Применяем изменения к игре..."));

            bool copyOk = SafeFileOperations.ExecuteBatchCopy(
                filesToReplace,
                foldersToCopy,
                msg => Out.INFO("COPY", "gray", msg)
            );

            if (!copyOk)
            {
                Out.ERROR("SUITE", "gray", 104, "Не удалось скопировать файлы (отказ в доступе).");
                return 104;
            }

            progress?.Report(new PatchProgress(95, "Очистка временных файлов..."));
        }
        finally
        {
            // Clean up temporary files
            foreach (var temp in tempFiles)
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch { }
            }
        }

        progress?.Report(new PatchProgress(100, "Установка успешно завершена!"));
        Out.SUCCESS("SUITE", "gray", "All targets patched successfully.");
        return 0;
    }
}

