using Microsoft.Win32;

namespace ScreenOffManager.Services;

/// <summary>
/// 开机自启：写 HKCU 下的 Run 项。
/// 相比任务计划，它免管理员权限、立即生效、卸载干净（进设置取消即可）。
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenOffManager";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string s && s.Length > 0;
            }
            catch (Exception ex)
            {
                Log.Error("读取开机自启状态失败", ex);
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key is null)
            {
                Log.Error("无法打开注册表 Run 项，开机自启设置失败");
                return;
            }

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                {
                    Log.Error("无法获取可执行文件路径，开机自启设置失败");
                    return;
                }

                // 直接启动主界面：程序不常驻后台，启动即是可用状态
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            Log.Info($"开机自启已{(enabled ? "开启" : "关闭")}");
        }
        catch (Exception ex)
        {
            Log.Error($"设置开机自启({enabled})失败", ex);
            throw;
        }
    }
}
