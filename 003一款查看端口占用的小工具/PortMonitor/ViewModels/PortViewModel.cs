using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PortMonitor.Models;
using PortMonitor.Services;

namespace PortMonitor.ViewModels;

/// <summary>
/// 端口占用查询页。
/// </summary>
public class PortViewModel : ObservableObject
{
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly RelayCommand _copyPidCommand;
    private readonly RelayCommand _copyCmdLineCommand;
    private readonly RelayCommand _copyAllCommand;
    private readonly RelayCommand _openLocationCommand;
    private readonly AsyncRelayCommand _killCommand;

    private bool _isQuerying;
    private int _lastQueryPort = -1;

    public PortViewModel()
    {
        Connections = new ObservableCollection<ConnectionInfo>();

        _autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _autoRefreshTimer.Tick += OnAutoRefreshTick;

        CheckCommand = new AsyncRelayCommand(_ => QueryAsync());
        ClearCommand = new RelayCommand(ClearResults);
        QuickQueryCommand = new RelayCommand(p =>
        {
            if (p is int port)
            {
                PortText = port.ToString();
                _ = QueryAsync();
            }
        });

        _killCommand = new AsyncRelayCommand(_ => KillSelectedAsync(), _ => SelectedConnection != null);
        _copyPidCommand = new RelayCommand(CopyPid, () => SelectedConnection != null);
        _copyCmdLineCommand = new RelayCommand(CopyCmdLine, () => SelectedConnection != null);
        _copyAllCommand = new RelayCommand(CopyAll, () => Connections.Count > 0);
        _openLocationCommand = new RelayCommand(OpenProcessLocation, () => SelectedConnection != null);
    }

    // ── 绑定属性 ───────────────────────────────────────────────────────

    private string _portText = string.Empty;
    public string PortText
    {
        get => _portText;
        set => SetProperty(ref _portText, value);
    }

    public string[] ProtocolOptions { get; } = { "全部", "TCP", "UDP" };

    public int[] QuickPorts { get; } = { 3000, 3306, 5173, 6379, 8080, 9090, 8848 };

    private int _selectedFilterIndex;
    public int SelectedFilterIndex
    {
        get => _selectedFilterIndex;
        set => SetProperty(ref _selectedFilterIndex, value);
    }

    private bool _isAutoRefresh;
    public bool IsAutoRefresh
    {
        get => _isAutoRefresh;
        set
        {
            if (!SetProperty(ref _isAutoRefresh, value)) return;

            if (value)
            {
                EnsureAutoRefreshTarget();
                _autoRefreshTimer.Start();
            }
            else
            {
                _autoRefreshTimer.Stop();
            }
        }
    }

    private bool _showCmdLine = true;
    public bool ShowCmdLine
    {
        get => _showCmdLine;
        set
        {
            if (SetProperty(ref _showCmdLine, value)) UpdateDetail();
        }
    }

    private string _statusText = "输入端口号后按 Enter 或点击“检查端口”";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _detailText = string.Empty;
    public string DetailText
    {
        get => _detailText;
        set => SetProperty(ref _detailText, value);
    }

    public string CountText => Connections.Count == 0 ? string.Empty : $"共 {Connections.Count} 条";

    public ObservableCollection<ConnectionInfo> Connections { get; }

    private ConnectionInfo? _selectedConnection;
    public ConnectionInfo? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (!SetProperty(ref _selectedConnection, value)) return;

            UpdateDetail();
            RefreshCommandStates();

            if (value is { Pid: > 0 } item && item.CmdLine is null)
                _ = LoadCommandLineAsync(item);
        }
    }

    // ── 命令 ───────────────────────────────────────────────────────────

    public AsyncRelayCommand CheckCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand QuickQueryCommand { get; }
    public AsyncRelayCommand KillCommand => _killCommand;
    public RelayCommand CopyPidCommand => _copyPidCommand;
    public RelayCommand CopyCmdLineCommand => _copyCmdLineCommand;
    public RelayCommand CopyAllCommand => _copyAllCommand;
    public RelayCommand OpenLocationCommand => _openLocationCommand;

    // ── 查询 ───────────────────────────────────────────────────────────

    public async Task QueryAsync()
    {
        if (_isQuerying) return;

        if (!int.TryParse(PortText?.Trim(), out int port) || port is < 0 or > 65535)
        {
            StatusText = "请输入 0 - 65535 之间的端口号";
            return;
        }

        _isQuerying = true;
        StatusText = $"正在查询端口 {port} ...";

        try
        {
            var filter = SelectedFilterIndex switch
            {
                1 => ProtocolFilter.TcpOnly,
                2 => ProtocolFilter.UdpOnly,
                _ => ProtocolFilter.All,
            };

            var result = await Task.Run(() => PortService.Query(port, filter));
            ApplyResults(result, port);
        }
        catch (Exception ex)
        {
            StatusText = $"查询失败：{ex.Message}";
        }
        finally
        {
            _isQuerying = false;
        }
    }

    private void ApplyResults(List<ConnectionInfo> result, int port)
    {
        _lastQueryPort = port;
        var selectedKey = SelectedConnection?.Key;

        // 只在数据真正变化时重建集合：避免自动刷新时选中行被 Clear 掉、列表反复闪烁
        bool changed = result.Count != Connections.Count;
        if (!changed)
        {
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Key != Connections[i].Key)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            Connections.Clear();
            foreach (var item in result) Connections.Add(item);

            if (selectedKey is not null)
                SelectedConnection = Connections.FirstOrDefault(c => c.Key == selectedKey);

            OnPropertyChanged(nameof(CountText));
            _copyAllCommand.RaiseCanExecuteChanged();
        }

        var time = DateTime.Now.ToString("HH:mm:ss");
        StatusText = result.Count > 0
            ? $"端口 {port} 被占用 · {result.Count} 条记录 · {time}"
            : $"端口 {port} 当前空闲 · {time}";
    }

    private void ClearResults()
    {
        Connections.Clear();
        DetailText = string.Empty;
        SelectedConnection = null;
        StatusText = "已清空";
        OnPropertyChanged(nameof(CountText));
        _copyAllCommand.RaiseCanExecuteChanged();
    }

    private async void OnAutoRefreshTick(object? sender, EventArgs e)
    {
        // 自动刷新只处理合法端口，避免状态栏被无效输入反复刷屏
        if (int.TryParse(PortText?.Trim(), out _)) await QueryAsync();
    }

    private void EnsureAutoRefreshTarget()
    {
        if (int.TryParse(PortText?.Trim(), out int port) && port is >= 0 and <= 65535 && port != _lastQueryPort)
            _ = QueryAsync();
    }

    // ── 详情与复制 ─────────────────────────────────────────────────────

    private void UpdateDetail()
    {
        if (SelectedConnection is not { } item || !ShowCmdLine)
        {
            DetailText = string.Empty;
            return;
        }

        DetailText = item.CmdLine ?? "正在读取命令行...";
    }

    private async Task LoadCommandLineAsync(ConnectionInfo item)
    {
        var commandLine = await Task.Run(() => ProcessInfoService.GetCommandLine(item.Pid));
        item.CmdLine = commandLine ?? "（读不到命令行：可能是受保护进程，或进程已退出）";

        if (ReferenceEquals(SelectedConnection, item) && ShowCmdLine) DetailText = item.CmdLine;
    }

    private void CopyPid()
    {
        if (SelectedConnection is not { } item) return;
        SetClipboard(item.Pid.ToString(), $"已复制 PID：{item.Pid}");
    }

    private void CopyCmdLine()
    {
        if (SelectedConnection?.CmdLine is not { Length: > 0 } text)
        {
            StatusText = "当前进程没有可复制的命令行";
            return;
        }
        SetClipboard(text, "已复制命令行");
    }

    private void CopyAll()
    {
        if (Connections.Count == 0) return;

        var header = $"{"PID",-7} {"进程",-24} {"协议",-4} {"状态",-12} {"本地地址",-28} 远端地址";
        var body = string.Join(Environment.NewLine, Connections.Select(c => c.ToDisplayLine()));
        SetClipboard(header + Environment.NewLine + body, $"已复制 {Connections.Count} 条记录");
    }

    private void SetClipboard(string text, string successMessage)
    {
        try
        {
            Clipboard.SetText(text);
            StatusText = successMessage;
        }
        catch (Exception ex)
        {
            StatusText = $"复制失败：{ex.Message}";
        }
    }

    private void OpenProcessLocation()
    {
        if (SelectedConnection is not { } item) return;

        var path = ProcessInfoService.GetExecutablePath(item.Pid);
        if (string.IsNullOrEmpty(path))
        {
            StatusText = "读不到该进程的文件路径（可能权限不足）";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"打开失败：{ex.Message}";
        }
    }

    // ── 结束进程 ───────────────────────────────────────────────────────

    private async Task KillSelectedAsync()
    {
        if (SelectedConnection is not { } item) return;

        if (ProcessGuard.IsProtected(item.Pid, item.ProcessName))
        {
            Dialogs.Error(ProcessGuard.ProtectedReason(item.ProcessName), "已阻止");
            return;
        }

        var confirmed = Dialogs.Confirm(
            $"确定结束进程 PID {item.Pid}（{item.ProcessName}）吗？\n\n" +
            "将先请求进程正常退出，2.5 秒无响应再强制结束。",
            "确认结束进程");

        if (!confirmed) return;

        StatusText = $"正在结束 PID {item.Pid} ...";
        var (ok, message) = await ProcessInfoService.KillAsync(item.Pid);

        StatusText = ok ? $"PID {item.Pid}：{message}" : $"结束失败：{message}";
        if (!ok && message.Contains("权限")) Dialogs.Error(message, "权限不足");

        await QueryAsync();
    }

    private void RefreshCommandStates()
    {
        _killCommand.RaiseCanExecuteChanged();
        _copyPidCommand.RaiseCanExecuteChanged();
        _copyCmdLineCommand.RaiseCanExecuteChanged();
        _openLocationCommand.RaiseCanExecuteChanged();
    }
}
