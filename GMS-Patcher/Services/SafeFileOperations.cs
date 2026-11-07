using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GMS_UTML_Patcher.Services;

public static class SafeFileOperations
{
    public static bool EnsureBackup(string originalFile, string backupFile)
    {
        try
        {
            if (File.Exists(originalFile) && !File.Exists(backupFile))
            {
                string? backupDir = Path.GetDirectoryName(backupFile);
                if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                File.Copy(originalFile, backupFile, overwrite: false);
                Out.INFO("BACKUP", "gray", $"Created backup: {Path.GetFileName(backupFile)}");
                return true;
            }
        }
        catch (Exception ex)
        {
            Out.WARN("BACKUP", "gray", $"Failed to create backup: {ex.Message}");
        }

        return false;
    }

    public static long GetFreeSpaceMB(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            if (string.IsNullOrEmpty(root))
                root = Path.DirectorySeparatorChar.ToString();

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace / (1024 * 1024);
        }
        catch
        {
            return -1;
        }
    }

    public static bool HasWriteAccess(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string testFile = Path.Combine(directoryPath, $".write_test_{Guid.NewGuid():N}.tmp");
            using (var fs = File.Create(testFile))
            {
                fs.WriteByte(1);
            }
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void CopyDirectory(string sourceDir, string destDir, bool overwrite = true)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir, overwrite);
        }
    }

    public static bool ExecuteBatchCopy(
        Dictionary<string, string> files,
        Dictionary<string, string> folders,
        Action<string>? logFunc = null)
    {
        // 1. First, attempt standard copying without elevation
        bool needsElevation = false;
        try
        {
            foreach (var (src, dst) in folders)
            {
                logFunc?.Invoke($"Copying folder {Path.GetFileName(src)} -> {dst}");
                CopyDirectory(src, dst, overwrite: true);
            }

            foreach (var (src, dst) in files)
            {
                string? dir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                logFunc?.Invoke($"Copying file {Path.GetFileName(src)} -> {dst}");
                File.Copy(src, dst, overwrite: true);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            needsElevation = true;
        }

        // 2. If unauthorized and on Windows, run elevated batch script
        if (needsElevation && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            logFunc?.Invoke("Access denied. Requesting administrator privileges to replace files...");
            return RunElevatedBatchCopyWindows(files, folders, logFunc);
        }

        return false;
    }

    private static bool RunElevatedBatchCopyWindows(
        Dictionary<string, string> files,
        Dictionary<string, string> folders,
        Action<string>? logFunc)
    {
        string tempBat = Path.Combine(Path.GetTempPath(), $"patch_copy_{Guid.NewGuid():N}.bat");
        string doneFlag = Path.Combine(Path.GetTempPath(), $"patch_flag_{Guid.NewGuid():N}.flag");

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");

            foreach (var (src, dst) in folders)
            {
                sb.AppendLine($"robocopy \"{src}\" \"{dst}\" /E /R:2 /W:1 /NP /NJH /NJS");
                sb.AppendLine("if errorlevel 8 goto ERROR");
            }

            foreach (var (src, dst) in files)
            {
                string? dir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir))
                    sb.AppendLine($"if not exist \"{dir}\" mkdir \"{dir}\"");
                sb.AppendLine($"copy /Y \"{src}\" \"{dst}\" >nul");
                sb.AppendLine("if errorlevel 1 goto ERROR");
            }

            sb.AppendLine($"echo DONE > \"{doneFlag}\"");
            sb.AppendLine("goto END");
            sb.AppendLine(":ERROR");
            sb.AppendLine("echo ERROR_OCCURRED");
            sb.AppendLine(":END");

            File.WriteAllText(tempBat, sb.ToString(), Encoding.UTF8);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{tempBat}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            process.WaitForExit(60000);

            bool success = File.Exists(doneFlag);
            return success;
        }
        catch (Exception ex)
        {
            logFunc?.Invoke($"Elevated copy error: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (File.Exists(tempBat)) File.Delete(tempBat); } catch { }
            try { if (File.Exists(doneFlag)) File.Delete(doneFlag); } catch { }
        }
    }
}

