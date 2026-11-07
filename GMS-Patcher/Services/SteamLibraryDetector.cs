using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GMS_UTML_Patcher.Services;

public static class SteamLibraryDetector
{
    public static List<string> GetSteamCommonFolders()
    {
        var commonFolders = new List<string>();
        string? steamRoot = GetSteamRootPath();

        if (!string.IsNullOrEmpty(steamRoot) && Directory.Exists(steamRoot))
        {
            string defaultCommon = Path.Combine(steamRoot, "steamapps", "common");
            if (Directory.Exists(defaultCommon))
                commonFolders.Add(Path.GetFullPath(defaultCommon));

            string vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    string vdfContent = File.ReadAllText(vdfPath);
                    var matches = Regex.Matches(vdfContent, @"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        if (match.Groups.Count > 1)
                        {
                            string rawPath = match.Groups[1].Value.Replace(@"\\", @"\");
                            string common = Path.Combine(rawPath, "steamapps", "common");
                            if (Directory.Exists(common))
                            {
                                string fullCommon = Path.GetFullPath(common);
                                if (!commonFolders.Contains(fullCommon, StringComparer.OrdinalIgnoreCase))
                                {
                                    commonFolders.Add(fullCommon);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Out.WARN("STEAM", "gray", $"Failed to parse libraryfolders.vdf: {ex.Message}");
                }
            }
        }

        return commonFolders;
    }

    public static string? FindGameDirectory(string gameFolderName, string? appId = null)
    {
        var commons = GetSteamCommonFolders();

        foreach (var common in commons)
        {
            try
            {
                string candidate = Path.Combine(common, gameFolderName);
                if (Directory.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

                // Check case-insensitive directories
                var dirs = Directory.GetDirectories(common, $"{gameFolderName}*", SearchOption.TopDirectoryOnly);
                if (dirs.Length > 0)
                {
                    return Path.GetFullPath(dirs[0]);
                }
            }
            catch { }
        }

        return null;
    }

    private static string? GetSteamRootPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string steamPath)
                {
                    return steamPath.Replace('/', '\\');
                }
            }
            catch { }

            try
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam") ??
                                    Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (hklmKey?.GetValue("InstallPath") is string installPath)
                {
                    return installPath;
                }
            }
            catch { }

            string defaultWin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            if (Directory.Exists(defaultWin))
                return defaultWin;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string macSteam = Path.Combine(home, "Library", "Application Support", "Steam");
            if (Directory.Exists(macSteam))
                return macSteam;

            string macAppSteam = "/Applications/Steam.app/Contents/MacOS/steamapps";
            if (Directory.Exists(macAppSteam))
                return macAppSteam;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string linuxSteam = Path.Combine(home, ".steam", "steam");
            if (Directory.Exists(linuxSteam))
                return linuxSteam;

            string linuxShareSteam = Path.Combine(home, ".local", "share", "Steam");
            if (Directory.Exists(linuxShareSteam))
                return linuxShareSteam;
        }

        return null;
    }
}

