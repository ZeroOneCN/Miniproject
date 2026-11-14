using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PortMonitor.Services;

namespace PortMonitor.ViewModels;

/// <summary>
/// 端口扫描：TCP connect 扫描指定主机 + 端口区间，本机目标会自动反查占用进程。
/// </summary>
public class PortScanViewModel : ObservableObject
{
    private readonly AsyncRelayCommand _startCommand;
    private readonly RelayCommand _stopCommand;
    private readonly AsyncRelayCommand _killCommand;
    private readonly RelayCommand _openLocationCommand;
    private readonly RelayCommand _presetCommand;

    private CancellationTokenSource? _cts;

    public PortScanViewModel()
    {
        Results = new ObservableCollection<PortScanResult>();

        _startCommand = new AsyncRelayCommand(_ => StartAsync(), _ => !IsScanning);
        _stopCommand = new RelayCommand(Stop, () => IsScanning);
        _killCommand = new AsyncRelayCommand(_ => KillSelectedAsync(), _ => SelectedResult is { OwnerPid: > 0 });
        _openLocationCommand = new RelayCommand(OpenProcessLocation, () => SelectedResult is { OwnerPid: > 0 });
        _presetCommand = new RelayCommand(ApplyPreset, _ => !IsScanning);
    }

    // ── 扫描参数 ───────────────────────────────────────────────────────

    private string _host = "127.0.0.1";
    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    private int _startPort = 1;
    public int StartPort
    {
        get => _startPort;
        set => SetProperty(ref _startPort, value);
    }

    private int _endPort = 1024;
    public int EndPort
    {
        get => _endPort;
        set => SetProperty(ref _endPort, value);
    }

    private int _timeoutMs = 300;
    public int TimeoutMs
    {
        get => _timeoutMs;
        set => SetProperty(ref _timeoutMs, value);
    }

    private int _concurrency = 1024;
    public int Concurrency
    {
        get => _concurrency;
        set => SetProperty(ref _concurrency, value);
    }

    // ── 状态 ───────────────────────────────────────────────────────────

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (!SetProperty(ref _isScanning, value)) return;
            RefreshCommandStates();
        }
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    private string _statusText = "默认扫描本机 1-1024 端口，点“开始扫描”";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string CountText => Results.Count == 0 ? string.Empty : $"共 {Results.Count} 个开放端口";

    public ObservableCollection<PortScanResult> Results { get; }

    private PortScanResult? _selectedResult;
    public PortScanResult? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (!SetProperty(ref _selectedResult, value)) return;
            RefreshCommandStates();
        }
    }

    // ── 命令 ───────────────────────────────────────────────────────────

    public AsyncRelayCommand StartCommand => _startCommand;
    public RelayCommand StopCommand => _stopCommand;
    public AsyncRelayCommand KillCommand => _killCommand;
    public RelayCommand OpenLocationCommand => _openLocationCommand;
    public RelayCommand PresetCommand => _presetCommand;

    // ── 扫描 ───────────────────────────────────────────────────────────

    private async Task StartAsync()
    {
        if (IsScanning) return;

        string? error = Validate();
        if (error is not null)
        {
            StatusText = error;
            Dialogs.Error(error, "参数有误");
            return;
        }

        var address = PortScanService.ResolveTarget(Host, out var resolveError);
        if (address is null)
        {
            StatusText = resolveError ?? "目标无效";
            Dialogs.Error(resolveError ?? "目标无效", "解析失败");
            return;
        }

        bool virtualTarget = PortScanService.IsVirtualProxyAddress(address);

        IsScanning = true;
        Results.Clear();
        OnPropertyChanged(nameof(CountText));
        ProgressValue = 0;
        StatusText = $"正在扫描 {Host} ...";

        _cts = new CancellationTokenSource();

        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.Total <= 0) return;
            ProgressValue = p.Scanned * 100.0 / p.Total;
            StatusText = $"正在扫描 {Host}：{p.Scanned}/{p.Total}，已发现 {p.Open} 个开放端口";
        });

        try
        {
            var outcome = await Task.Run(() => PortScanService.ScanAsync(
                address, StartPort, EndPort, TimeoutMs, Concurrency, progress, _cts.Token));

            ApplyOutcome(outcome, virtualTarget);
        }
        catch (Exception ex)
        {
            StatusText = $"扫描失败：{ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ApplyOutcome(PortScanOutcome outcome, bool virtualTarget)
    {
        Results.Clear();
        foreach (var item in outcome.Results) Results.Add(item);

        OnPropertyChanged(nameof(CountText));
        ProgressValue = outcome.Total <= 0 ? 0 : outcome.Scanned * 100.0 / outcome.Total;

        var seconds = outcome.Elapsed.TotalSeconds.ToString("0.0");
        var localHint = outcome.TargetIsLocal ? "，已反查本机占用进程" : string.Empty;
        var virtualHint = virtualTarget ? "；目标解析到 198.18.x.x 虚拟地址（代理软件 fake-ip），结果不可信，建议改用真实 IP" : string.Empty;

        if (outcome.Cancelled)
        {
            StatusText = $"已停止：扫完 {outcome.Scanned}/{outcome.Total}，发现 {outcome.Results.Count} 个开放端口 · 用时 {seconds}s{virtualHint}";
            return;
        }

        StatusText = outcome.Results.Count > 0
            ? $"扫描完成：{outcome.Total} 个端口发现 {outcome.Results.Count} 个开放{localHint} · 用时 {seconds}s{virtualHint}"
            : $"扫描完成：{outcome.Total} 个端口均未开放（目标可能开启了防火墙） · 用时 {seconds}s{virtualHint}";
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Host)) return "请填写扫描目标（IP 或主机名）";

        if (StartPort is < 1 or > 65535) return "起始端口必须在 1 - 65535 之间";
        if (EndPort is < 1 or > 65535) return "结束端口必须在 1 - 65535 之间";
        if (StartPort > EndPort) return "起始端口不能大于结束端口";

        if (TimeoutMs is < 50 or > 5000) return "超时时间建议在 50 - 5000 毫秒之间";
        if (Concurrency is < 8 or > 2048) return "并发数建议在 8 - 2048 之间";

        return null;
    }

    private void Stop()
    {
        if (_cts is not { IsCancellationRequested: false }) return;
        _cts.Cancel();
        StatusText = "正在停止扫描...";
    }

    private void ApplyPreset(object? parameter)
    {
        if (IsScanning) return;

        switch (parameter as string)
        {
            case "common":
                StartPort = 1;
                EndPort = 1024;
                break;
            case "all":
                StartPort = 1;
                EndPort = 65535;
                break;
            case "dev":
                StartPort = 3000;
                EndPort = 9999;
                break;
        }
    }

    // ── 结果操作 ───────────────────────────────────────────────────────

    private async Task KillSelectedAsync()
    {
        if (SelectedResult is not { OwnerPid: > 0 } item) return;

        if (ProcessGuard.IsProtected(item.OwnerPid, item.OwnerProcess))
        {
            Dialogs.Error(ProcessGuard.ProtectedReason(item.OwnerProcess), "已阻止");
            return;
        }

        if (!Dialogs.Confirm(
                $"端口 {item.Port} 由 {item.OwnerProcess}（PID {item.OwnerPid}）占用。\n\n确定结束该进程吗？",
                "确认结束进程"))
            return;

        StatusText = $"正在结束 PID {item.OwnerPid} ...";
        var (ok, message) = await ProcessInfoService.KillAsync(item.OwnerPid);
        StatusText = ok ? $"PID {item.OwnerPid}：{message}" : $"结束失败：{message}";
    }

    private void OpenProcessLocation()
    {
        if (SelectedResult is not { OwnerPid: > 0 } item) return;

        var path = ProcessInfoService.GetExecutablePath(item.OwnerPid);
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

    private void RefreshCommandStates()
    {
        _startCommand.RaiseCanExecuteChanged();
        _stopCommand.RaiseCanExecuteChanged();
        _killCommand.RaiseCanExecuteChanged();
        _openLocationCommand.RaiseCanExecuteChanged();
        _presetCommand.RaiseCanExecuteChanged();
    }
}
