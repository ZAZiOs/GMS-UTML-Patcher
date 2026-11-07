using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GMS_UTML_Patcher.Services;

public static class MacSecurityHelper
{
    public static bool IsRunningFromDmgOrTranslocation()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return false;

        string appPath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        if (appPath.StartsWith("/Volumes/", StringComparison.OrdinalIgnoreCase) ||
            appPath.StartsWith("/private/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static bool FixMacPermissions(string filePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || !File.Exists(filePath))
            return true;

        try
        {
            // Remove Apple quarantine attribute
            using var xattr = Process.Start(new ProcessStartInfo
            {
                FileName = "xattr",
                Arguments = $"-d com.apple.quarantine \"{filePath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            xattr?.WaitForExit(3000);
        }
        catch { }

        try
        {
            // Set executable permission
            using var chmod = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            chmod?.WaitForExit(3000);
            return chmod?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

