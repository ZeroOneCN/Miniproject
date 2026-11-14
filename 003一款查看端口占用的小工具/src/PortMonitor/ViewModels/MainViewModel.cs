using System;
using System.Windows;
using PortMonitor.Services;

namespace PortMonitor.ViewModels;

/// <summary>
/// 主窗口视图模型：聚合四个功能页 + 权限状态。
/// </summary>
public class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Port = new PortViewModel();
        AllPorts = new AllPortsViewModel(OpenPortInQueryTab);
        PortScan = new PortScanViewModel();
        FileLock = new FileLockViewModel();

        ElevateCommand = new RelayCommand(Elevate, () => !IsAdministrator);

        AppTitle = "端口占用与文件句柄管理";
    }

    public PortViewModel Port { get; }

    public AllPortsViewModel AllPorts { get; }

    public PortScanViewModel PortScan { get; }

    public FileLockViewModel FileLock { get; }

    public string AppTitle { get; }

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public bool IsAdministrator { get; } = ElevationService.IsAdministrator;

    public string PrivilegeText => IsAdministrator ? "管理员权限" : "普通权限";

    public string PrivilegeTip => IsAdministrator
        ? "已具备管理员权限，可结束任意进程、执行句柄深度扫描"
        : "普通权限：无法结束系统进程，深度扫描句柄需要管理员";

    public RelayCommand ElevateCommand { get; }

    /// <summary>
    /// 全端口总览里点“在端口页查询” → 带着端口号跳到“端口占用”页。
    /// </summary>
    private void OpenPortInQueryTab(int port)
    {
        Port.PortText = port.ToString();
        SelectedTabIndex = 0;
        _ = Port.QueryAsync();
    }

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
