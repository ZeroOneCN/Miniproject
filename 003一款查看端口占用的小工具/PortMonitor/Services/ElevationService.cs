using System;
using System.Diagnostics;
using System.Security.Principal;

namespace PortMonitor.Services;

/// <summary>
/// 管理员权限检测与提权重启。
/// </summary>
public static class ElevationService
{
    public static bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 以管理员身份重启当前程序（会弹 UAC）。
    /// </summary>
    public static bool RestartElevated()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;

        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            // 用户取消了 UAC
            return false;
        }
    }
}
