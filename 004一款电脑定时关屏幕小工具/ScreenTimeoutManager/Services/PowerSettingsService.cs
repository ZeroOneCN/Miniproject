using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Security.Principal;
using ScreenTimeoutManager.Models;

namespace ScreenTimeoutManager.Services;

/// <summary>
/// 通过 powercfg 管理 Windows 电源方案中的屏幕关闭时间
/// </summary>
public class PowerSettingsService
{
    /// <summary>检查当前进程是否以管理员权限运行</summary>
    public static bool IsAdmin()
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

    /// <summary>获取当前电源方案信息</summary>
    public PowerSchemeInfo GetCurrentScheme()
    {
        var info = new PowerSchemeInfo
        {
            IsAdmin = IsAdmin(),
            SchemeName = ReadSchemeName(),
        };

        var (ac, dc) = ReadTimeouts();
        info.AcSeconds = ac;
        info.DcSeconds = dc;

        return info;
    }

    /// <summary>设置屏幕关闭时间（AC/DC 同步），minutes=0 表示永不关闭</summary>
    public void SetScreenTimeout(int minutes)
    {
        var seconds = Math.Max(0, minutes) * 60;

        var r1 = RunPowerCfg(
            $"/SETACVALUEINDEX SCHEME_CURRENT SUB_VIDEO VIDEOIDLE {seconds}");

        var r2 = RunPowerCfg(
            $"/SETDCVALUEINDEX SCHEME_CURRENT SUB_VIDEO VIDEOIDLE {seconds}");

        if (r1.exitCode != 0 || r2.exitCode != 0)
        {
            var err = (r1.stderr + r2.stderr).Trim();
            throw new InvalidOperationException(
                $"设置失败，可能需要管理员权限。\n{err}");
        }

        // 激活当前方案使设置生效
        RunPowerCfg("/S SCHEME_CURRENT");
    }

    // ---------- private helpers ----------

    private static string ReadSchemeName()
    {
        try
        {
            var (_, stdout, _) = RunPowerCfg("/GETACTIVESCHEME");
            if (string.IsNullOrWhiteSpace(stdout))
                return "未知方案（需要管理员权限或系统限制）";

            var m = Regex.Match(stdout, @"\((.+)\)");
            return m.Success ? m.Groups[1].Value : stdout.Trim();
        }
        catch
        {
            return "读取失败";
        }
    }

    private static (int? ac, int? dc) ReadTimeouts()
    {
        try
        {
            var (_, stdout, _) = RunPowerCfg("-q SCHEME_CURRENT SUB_VIDEO VIDEOIDLE");
            if (string.IsNullOrWhiteSpace(stdout))
                return (null, null);

            int? ac = ParseHexValue(stdout, @"Current AC Power Setting Index:\s*(0x[0-9A-Fa-f]+)");
            int? dc = ParseHexValue(stdout, @"Current DC Power Setting Index:\s*(0x[0-9A-Fa-f]+)");

            // 兜底：尝试本地化输出中的 AC/DC 行
            ac ??= ParseHexValue(stdout, @"AC[^\n]*?(0x[0-9A-Fa-f]+)");
            dc ??= ParseHexValue(stdout, @"DC[^\n]*?(0x[0-9A-Fa-f]+)");

            return (ac, dc);
        }
        catch
        {
            return (null, null);
        }
    }

    private static int? ParseHexValue(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : null;
    }

    private static (int exitCode, string stdout, string stderr) RunPowerCfg(string args)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        proc.Start();
        proc.WaitForExit();
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        return (proc.ExitCode, stdout, stderr);
    }
}