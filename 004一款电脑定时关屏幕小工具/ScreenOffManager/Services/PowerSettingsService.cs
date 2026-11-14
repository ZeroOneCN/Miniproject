using System.Security.Principal;
using ScreenOffManager.Models;

namespace ScreenOffManager.Services;

/// <summary>
/// 电源设置服务：优先走 powrprof 原生 API，失败时自动降级到 powercfg 命令行。
/// </summary>
public class PowerSettingsService
{
    /// <summary>当前进程是否以管理员权限运行</summary>
    public static bool IsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读取当前电源方案与屏幕关闭时间</summary>
    public PowerSchemeInfo GetCurrentScheme()
    {
        var info = new PowerSchemeInfo { IsAdmin = IsAdmin(), Engine = "powrprof" };

        try
        {
            var scheme = PowerNative.GetActiveSchemeGuid();
            info.SchemeName = PowerNative.GetSchemeName(scheme);

            var (ac, dc) = PowerNative.ReadVideoIdleSeconds(scheme);
            info.AcSeconds = ac;
            info.DcSeconds = dc;
        }
        catch (Exception ex)
        {
            Log.Error("原生电源 API 读取失败，降级到 powercfg", ex);
            info.Engine = "powercfg";
            info.SchemeName = PowerCfgFallback.ReadSchemeName();

            var (ac, dc) = PowerCfgFallback.ReadTimeouts();
            info.AcSeconds = ac;
            info.DcSeconds = dc;
        }

        return info;
    }

    /// <summary>设置屏幕关闭时间，minutes=0 表示永不关闭</summary>
    public void SetScreenTimeout(int minutes)
    {
        var seconds = Math.Max(0, minutes) * 60;
        Log.Info($"开始设置屏幕关闭时间：{minutes} 分钟（{seconds} 秒）");

        try
        {
            var scheme = PowerNative.GetActiveSchemeGuid();
            PowerNative.WriteVideoIdleSeconds(scheme, seconds);
        }
        catch (Exception ex)
        {
            Log.Error("原生电源 API 写入失败，降级到 powercfg", ex);
            PowerCfgFallback.SetScreenTimeout(minutes);
            return;
        }

        Log.Info("设置成功");
    }
}
