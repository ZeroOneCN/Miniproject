using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PortMonitor.Models;
using PortMonitor.Services;

namespace PortMonitor.ViewModels;

/// <summary>
/// 全端口总览：不用先知道端口号，直接看全系统谁在监听什么。
/// 数据源为系统连接表，可反复刷新而不产生任何网络流量。
/// </summary>
public class AllPortsViewModel : ObservableObject
{
    private readonly List<ConnectionInfo> _all = new();
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly Action<int>? _openInPortTab;
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand _killCommand;
    private readonly RelayCommand _openLocationCommand;
    private readonly RelayCommand _openInPortCommand;

    private bool _isRefreshing;

    public AllPortsViewModel(Action<int>? openInPortTab = null)
    {
        _openInPortTab = openInPortTab;
        Entries = new ObservableCollection<ConnectionInfo>();

        _autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _autoRefreshTimer.Tick += async (_, _) => await RefreshAsync(silent: true);

        _refreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        _killCommand = new AsyncRelayCommand(_ => KillSelectedAsync(), _ => SelectedEntry != null);
        _openLocationCommand = new RelayCommand(OpenProcessLocation, () => SelectedEntry != null);
        _openInPortCommand = new RelayCommand(OpenInPortTab, () => SelectedEntry != null);
    }

    // ── 绑定属性 ───────────────────────────────────────────────────────

    private string _keyword = string.Empty;
    public string Keyword
    {
        get => _keyword;
        set
        {
            if (SetProperty(ref _keyword, value)) ApplyFilter();
        }
    }

    private bool _listeningOnly = true;
    public bool ListeningOnly
    {
        get => _listeningOnly;
        set
        {
            if (SetProperty(ref _listeningOnly, value)) ApplyFilter();
        }
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
                if (_all.Count == 0) _ = RefreshAsync();
                _autoRefreshTimer.Start();
            }
            else
            {
                _autoRefreshTimer.Stop();
            }
        }
    }

    private string _statusText = "点击“刷新”读取当前系统全部端口";
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

    public string CountText => Entries.Count == 0 ? string.Empty : $"共 {Entries.Count} 条";

    public ObservableCollection<ConnectionInfo> Entries { get; }

    private ConnectionInfo? _selectedEntry;
    public ConnectionInfo? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value)) return;

            UpdateDetail();
            RefreshCommandStates();

            if (value is { Pid: > 0 } item && item.CmdLine is null)
                _ = LoadCommandLineAsync(item);
        }
    }

    // ── 命令 ───────────────────────────────────────────────────────────

    public AsyncRelayCommand RefreshCommand => _refreshCommand;
    public AsyncRelayCommand KillCommand => _killCommand;
    public RelayCommand OpenLocationCommand => _openLocationCommand;
    public RelayCommand OpenInPortCommand => _openInPortCommand;

    // ── 刷新与筛选 ─────────────────────────────────────────────────────

    public async Task RefreshAsync(bool silent = false)
    {
        if (_isRefreshing) return;
        _isRefreshing = true;

        if (!silent) StatusText = "正在读取系统端口表...";

        try
        {
            var data = await Task.Run(() => PortService.QueryAll(ProtocolFilter.All));

            _all.Clear();
            _all.AddRange(data);
            ApplyFilter();

            if (!silent)
                StatusText = $"读取完成：{_all.Count} 条记录 · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText = $"读取失败：{ex.Message}";
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ApplyFilter()
    {
        var selectedKey = SelectedEntry?.Key;
        string keyword = Keyword?.Trim() ?? string.Empty;

        IEnumerable<ConnectionInfo> query = _all;

        if (ListeningOnly)
            query = query.Where(c => c.State == "LISTEN" || c.Protocol == ProtocolType.Udp);

        if (keyword.Length > 0)
            query = query.Where(c =>
                c.LocalPort.ToString().Contains(keyword, StringComparison.Ordinal)
                || c.Pid.ToString() == keyword
                || c.ProcessName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || c.LocalAddress.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        Entries.Clear();
        foreach (var item in query) Entries.Add(item);

        if (selectedKey is not null)
            SelectedEntry = Entries.FirstOrDefault(c => c.Key == selectedKey);

        OnPropertyChanged(nameof(CountText));
    }

    // ── 详情 ───────────────────────────────────────────────────────────

    private void UpdateDetail()
    {
        if (SelectedEntry is not { } item)
        {
            DetailText = string.Empty;
            return;
        }

        DetailText = item.Pid <= 0
            ? "系统空闲进程不提供命令行"
            : item.CmdLine ?? "正在读取命令行...";
    }

    private async Task LoadCommandLineAsync(ConnectionInfo item)
    {
        var commandLine = await Task.Run(() => ProcessInfoService.GetCommandLine(item.Pid));
        item.CmdLine = commandLine ?? "（读不到命令行：可能是受保护进程，或进程已退出）";

        if (ReferenceEquals(SelectedEntry, item)) DetailText = item.CmdLine;
    }

    // ── 操作 ───────────────────────────────────────────────────────────

    private void OpenInPortTab()
    {
        if (SelectedEntry is not { } item) return;
        _openInPortTab?.Invoke(item.LocalPort);
    }

    private void OpenProcessLocation()
    {
        if (SelectedEntry is not { } item) return;

        var path = ProcessInfoService.GetExecutablePath(item.Pid);
        if (string.IsNullOrEmpty(path))
        {
            StatusText = "读不到该进程的文件路径（可能权限不足）";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"打开失败：{ex.Message}";
        }
    }

    private async Task KillSelectedAsync()
    {
        if (SelectedEntry is not { } item) return;

        if (ProcessGuard.IsProtected(item.Pid, item.ProcessName))
        {
            Dialogs.Error(ProcessGuard.ProtectedReason(item.ProcessName), "已阻止");
            return;
        }

        if (!Dialogs.Confirm(
                $"确定结束进程 {item.ProcessName}（PID {item.Pid}）吗？\n\n" +
                "将先请求进程正常退出，2.5 秒无响应再强制结束。",
                "确认结束进程"))
            return;

        StatusText = $"正在结束 PID {item.Pid} ...";
        var (ok, message) = await ProcessInfoService.KillAsync(item.Pid);
        StatusText = ok ? $"PID {item.Pid}：{message}" : $"结束失败：{message}";

        await RefreshAsync(silent: true);
    }

    private void RefreshCommandStates()
    {
        _killCommand.RaiseCanExecuteChanged();
        _openLocationCommand.RaiseCanExecuteChanged();
        _openInPortCommand.RaiseCanExecuteChanged();
    }
}
