using System;
using System.Reflection;
using System.Windows;
using PortMonitor.Services;

namespace PortMonitor.ViewModels;

/// <summary>
/// 主窗口视图模型：聚合两个功能页 + 权限状态。
/// </summary>
public class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Port = new PortViewModel();
        FileLock = new FileLockViewModel();

        ElevateCommand = new RelayCommand(Elevate, () => !IsAdministrator);

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "2.0.0";

        // 去掉 SourceLink 附加的 +commit 后缀
        int plus = version.IndexOf('+');
        if (plus > 0) version = version[..plus];

        AppTitle = $"端口占用与文件句柄管理  v{version}";
    }

    public PortViewModel Port { get; }

    public FileLockViewModel FileLock { get; }

    public string AppTitle { get; }

    public bool IsAdministrator { get; } = ElevationService.IsAdministrator;

    public string PrivilegeText => IsAdministrator ? "管理员权限" : "普通权限";

    public string PrivilegeTip => IsAdministrator
        ? "已具备管理员权限，可结束任意进程、执行句柄深度扫描"
        : "普通权限：无法结束系统进程，深度扫描句柄需要管理员";

    public RelayCommand ElevateCommand { get; }

    private void Elevate()
    {
        if (!Dialogs.Confirm(
                "将以管理员身份重新启动本程序，当前窗口会关闭。\n\n继续吗？",
                "以管理员身份重启"))
            return;

        if (ElevationService.RestartElevated())
            Application.Current.Shutdown();
        else
            Dialogs.Info("已取消提权。", "提示");
    }
}
