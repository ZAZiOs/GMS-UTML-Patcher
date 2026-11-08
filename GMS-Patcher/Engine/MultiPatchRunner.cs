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

public class PatchSuiteResult
{
    public int ExitCode { get; set; }
    public string? ErrorMessage { get; set; }

    public PatchSuiteResult(int exitCode, string? errorMessage = null)
    {
        ExitCode = exitCode;
        ErrorMessage = errorMessage;
    }

    public static implicit operator int(PatchSuiteResult r) => r.ExitCode;
    public static implicit operator PatchSuiteResult(int code) => new(code);
}

public static class MultiPatchRunner
{
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static string NormalizeGamePath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return string.Empty;

        inputPath = inputPath.Trim('\"', '\'', ' ');

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

        if (File.Exists(inputPath))
        {
            inputPath = Path.GetDirectoryName(inputPath) ?? inputPath;
        }

        if (IsMacOS)
        {

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
            result.ErrorMessage = "Specified game folder does not exist.";
            return result;
        }

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
            result.ErrorMessage = $"Required game files not found: {string.Join(", ", result.MissingFiles)}";
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

    public static string GetTargetPatchConfig(PatchTargetConfig target)
    {
        if (IsMacOS && !string.IsNullOrEmpty(target.MacPatchConfig))
            return target.MacPatchConfig;

        return target.PatchConfig;
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

    public static async Task<PatchSuiteResult> ApplySuiteAsync(
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
            string err = validation.ErrorMessage ?? "Validation failed";
            Out.ERROR("SUITE", "gray", 100, err);
            return new PatchSuiteResult(100, err);
        }

        int totalTargets = suite.Targets.Count;
        int currentTargetIndex = 0;
        bool userAlreadyConfirmedWarnings = false;

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
                        string err = $"Required target file not found: {targetFullPath}";
                        Out.ERROR("SUITE", "gray", 100, err);
                        return new PatchSuiteResult(100, err);
                    }
                    continue;
                }

                currentTargetIndex++;
                int basePercentage = (int)((currentTargetIndex - 1) / (double)(totalTargets + 1) * 80);
                string title = string.IsNullOrEmpty(target.Title) ? target.Id : target.Title;

                progress?.Report(new PatchProgress(basePercentage, $"Patching {title}...", target.Id));

                string configName = GetTargetPatchConfig(target);
                string? patchConfigFile = suite.ResolvePath(configName);

                if (string.IsNullOrEmpty(patchConfigFile) || !File.Exists(patchConfigFile))
                {
                    string err = $"Patch config file not found: {configName}";
                    Out.ERROR("SUITE", "gray", 103, err);
                    return new PatchSuiteResult(103, err);
                }

                string tempOutput = Path.Combine(Path.GetTempPath(), $"gms_patch_{target.Id}_{Guid.NewGuid():N}.win");
                tempFiles.Add(tempOutput);

                var patchOptions = new PatchOptions
                {
                    DataPath = targetFullPath,
                    PatcherFile = patchConfigFile,
                    OutputPath = tempOutput,
                    SkipTimestampCheck = userAlreadyConfirmedWarnings,
                    SkipHashCheck = userAlreadyConfirmedWarnings
                };

                var subProgress = new Progress<PatchProgress>(p =>
                {
                    int scaled = basePercentage + (int)(p.Percentage * 0.8 / (totalTargets + 1));
                    progress?.Report(new PatchProgress(scaled, $"{title}: {p.Status}", target.Id));
                });

                int result = PatcherEngine.ApplyPatch(patchOptions, subProgress, cancellationToken);

                if (result == 204 || result == 205 || result == 206)
                {
                    if (userAlreadyConfirmedWarnings)
                    {
                        patchOptions.SkipTimestampCheck = true;
                        patchOptions.SkipHashCheck = true;
                        result = PatcherEngine.ApplyPatch(patchOptions, subProgress, cancellationToken);
                    }
                    else
                    {
                        bool userConfirmed = false;
                        if (onWarningConfirmation != null)
                        {
                            string warnMessage = result switch
                            {
                                204 => "Checksum mismatch (the game might already be modified).\nContinue anyway?",
                                205 => "The game version is newer than the translation version. The patch may not work properly.\nContinue?",
                                206 => "The game and translation versions do not match.\nContinue anyway?",
                                _   => "A version mismatch was detected.\nContinue anyway?"
                            };

                            userConfirmed = await onWarningConfirmation(result, warnMessage);
                        }

                        if (!userConfirmed)
                        {
                            string err = "Installation cancelled by user due to version warning.";
                            Out.WARN("SUITE", "gray", err);
                            return new PatchSuiteResult(result, err);
                        }

                        userAlreadyConfirmedWarnings = true;
                        patchOptions.SkipTimestampCheck = true;
                        patchOptions.SkipHashCheck = true;
                        result = PatcherEngine.ApplyPatch(patchOptions, subProgress, cancellationToken);
                    }
                }

                if (result != 0)
                {
                    string err = $"Error patching target '{target.Id}' (code {result})";
                    Out.ERROR("SUITE", "gray", result, err);
                    return new PatchSuiteResult(result, err);
                }

                string? backupRelative = GetTargetBackupRelativePath(target);
                if (!string.IsNullOrEmpty(backupRelative))
                {
                    string backupFullPath = ResolveGameFilePath(normalizedRoot, backupRelative);
                    SafeFileOperations.EnsureBackup(targetFullPath, backupFullPath);
                }

                filesToReplace[tempOutput] = targetFullPath;
            }

            progress?.Report(new PatchProgress(85, "Copying additional files..."));

            if (suite.CopyOperations != null)
            {
                foreach (var folderRule in suite.CopyOperations.Folders)
                {
                    string? src = suite.ResolvePath(folderRule.Source);
                    string destRelative = (IsMacOS && !string.IsNullOrEmpty(folderRule.MacDest))
                        ? folderRule.MacDest
                        : folderRule.Dest;

                    string dst = ResolveGameFilePath(normalizedRoot, destRelative);

                    if (!string.IsNullOrEmpty(src) && Directory.Exists(src))
                    {
                        foldersToCopy[src] = dst;
                    }
                }

                foreach (var fileRule in suite.CopyOperations.Files)
                {
                    string? src = suite.ResolvePath(fileRule.Source);
                    string destRelative = (IsMacOS && !string.IsNullOrEmpty(fileRule.MacDest))
                        ? fileRule.MacDest
                        : fileRule.Dest;

                    string dst = ResolveGameFilePath(normalizedRoot, destRelative);

                    if (!string.IsNullOrEmpty(src) && File.Exists(src))
                    {
                        filesToReplace[src] = dst;
                    }
                }
            }

            progress?.Report(new PatchProgress(90, "Applying changes to game..."));

            bool copyOk = SafeFileOperations.ExecuteBatchCopy(
                filesToReplace,
                foldersToCopy,
                msg => Out.INFO("COPY", "gray", msg)
            );

            if (!copyOk)
            {
                string err = "Failed to copy patched files (permission denied or I/O error).";
                Out.ERROR("SUITE", "gray", 104, err);
                return new PatchSuiteResult(104, err);
            }

            progress?.Report(new PatchProgress(95, "Cleaning up temporary files..."));
        }
        finally
        {

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

        progress?.Report(new PatchProgress(100, "Installation completed successfully!"));
        Out.SUCCESS("SUITE", "gray", "All targets patched successfully.");
        return new PatchSuiteResult(0);
    }
}

