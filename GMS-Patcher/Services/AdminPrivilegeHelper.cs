using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GMS_UTML_Patcher.Services;

public static class AdminPrivilegeHelper
{
    private static bool? _isAdmin;

    public static bool IsAdministrator
    {
        get
        {
            if (_isAdmin.HasValue)
                return _isAdmin.Value;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    _isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    _isAdmin = false;
                }
            }
            else
            {
                // On Unix/macOS, check if EUID == 0 (root)
                _isAdmin = false;
            }

            return _isAdmin.Value;
        }
    }
}

