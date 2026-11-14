using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using PortMonitor.Models;
using PortMonitor.Services;

namespace PortMonitor.ViewModels;

/// <summary>
/// 文件占用（句柄）查询页：删除文件提示“被占用”时，定位到底是哪个进程攥着它。
/// </summary>
public class FileLockViewModel : ObservableObject
{
    private readonly AsyncRelayCommand _quickScanCommand;
    private readonly AsyncRelayCommand _deepScanCommand;
    private readonly AsyncRelayCommand _killCommand;
    private readonly AsyncRelayCommand _closeHandleCommand;
    private readonly RelayCommand _openLocationCommand;
    private readonly RelayCommand _copyCommand;

    private string? _resolvedPath;

    public FileLockViewModel()
    {
        Results = new ObservableCollection<FileLockInfo>();

        _quickScanCommand = new AsyncRelayCommand(_ => ScanAsync(deep: false));
        _deepScanCommand = new AsyncRelayCommand(_ => ScanAsync(deep: true));
        _killCommand = new AsyncRelayCommand(_ => KillSelectedAsync(), _ => SelectedResult != null);
        _closeHandleCommand = new AsyncRelayCommand(_ => CloseHandleAsync(), _ => SelectedResult is { CanCloseHandle: true });
        _openLocationCommand = new RelayCommand(OpenFileLocation, () => _resolvedPath != null);
        _copyCommand = new RelayCommand(CopyResults, () => Results.Count > 0);

        BrowseCommand = new RelayCommand(Browse);
        ClearCommand = new RelayCommand(ClearResults);
    }

    // ── 绑定属性 ───────────────────────────────────────────────────────

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value)) _resolvedPath = null;
        }
    }

    private string _statusText = "选择被占用的文件，点击“快速检测”开始";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _detailText = "选中一条结果可查看进程路径与句柄信息";
    public string DetailText
    {
        get => _detailText;
        set => SetProperty(ref _detailText, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string CountText => Results.Count == 0 ? string.Empty : $"共 {Results.Count} 条";

    public bool IsAdministrator => ElevationService.IsAdministrator;

    public ObservableCollection<FileLockInfo> Results { get; }

    private FileLockInfo? _selectedResult;
    public FileLockInfo? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (!SetProperty(ref _selectedResult, value)) return;
            UpdateDetail();
            RefreshCommandStates();
        }
    }

    // ── 命令 ───────────────────────────────────────────────────────────

    public RelayCommand BrowseCommand { get; }
    public RelayCommand ClearCommand { get; }
    public AsyncRelayCommand QuickScanCommand => _quickScanCommand;
    public AsyncRelayCommand DeepScanCommand => _deepScanCommand;
    public AsyncRelayCommand KillCommand => _killCommand;
    public AsyncRelayCommand CloseHandleCommand => _closeHandleCommand;
    public RelayCommand OpenLocationCommand => _openLocationCommand;
    public RelayCommand CopyCommand => _copyCommand;

    // ── 扫描 ───────────────────────────────────────────────────────────

    private async Task ScanAsync(bool deep)
    {
        if (!FileLockService.TryResolvePath(FilePath, out string path, out string? error))
        {
            StatusText = error ?? "路径无效";
            Dialogs.Error(error ?? "路径无效");
            return;
        }

        FilePath = path;
        _resolvedPath = path;

        if (deep && !ElevationService.IsAdministrator)
        {
            if (Dialogs.Confirm(
                    "深度扫描需要读取系统全部进程的句柄，必须管理员权限。\n\n是否以管理员身份重启程序？",
                    "需要管理员权限"))
            {
                if (ElevationService.RestartElevated()) Application.Current.Shutdown();
                else StatusText = "已取消提权";
            }
            return;
        }

        IsBusy = true;
        StatusText = deep ? "正在深度扫描系统句柄..." : "正在检测文件占用...";

        try
        {
            if (deep)
            {
                var progress = new Progress<string>(message => StatusText = message);
                var found = await Task.Run(() => FileLockService.FindByHandleScan(path, progress));
                ApplyResults(found, replace: false);   // 与快速检测结果合并，互补而不是覆盖
            }
            else
            {
                var found = await Task.Run(() => FileLockService.FindByRestartManager(path));
                ApplyResults(found, replace: true);

                if (found.Count == 0)
                {
                    StatusText = "快速检测未发现占用。若确实删不掉，请尝试“深度扫描”（需管理员）。";
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"扫描失败：{ex.Message}";
            Dialogs.Error(ex.Message, "扫描失败");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CountText));
            RefreshCommandStates();
        }
    }

    private void ApplyResults(List<FileLockInfo> incoming, bool replace)
    {
        var selectedKey = SelectedResult?.Key;
        var merged = replace ? new List<FileLockInfo>() : Results.ToList();

        foreach (var item in incoming)
        {
            if (item.CanCloseHandle)
            {
                // 有了句柄明细，就把同进程的“重启管理器”汇总行替换掉
                merged.RemoveAll(x => x.Pid == item.Pid && !x.CanCloseHandle);
                if (!merged.Any(x => x.Key == item.Key)) merged.Add(item);
            }
            else if (!merged.Any(x => x.Pid == item.Pid))
            {
                merged.Add(item);
            }
        }

        merged = merged
            .OrderBy(x => x.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Pid)
            .ToList();

        Results.Clear();
        foreach (var item in merged) Results.Add(item);

        if (selectedKey is not null)
            SelectedResult = Results.FirstOrDefault(r => r.Key == selectedKey);

        OnPropertyChanged(nameof(CountText));
        _copyCommand.RaiseCanExecuteChanged();

        if (Results.Count > 0)
        {
            var processes = Results.Select(r => r.Pid).Distinct().Count();
            StatusText = $"发现 {processes} 个进程、{Results.Count} 条记录占用该文件 · {DateTime.Now:HH:mm:ss}";
        }
        else if (replace)
        {
            StatusText = "未发现占用该文件的进程";
        }
    }

    private void ClearResults()
    {
        Results.Clear();
        SelectedResult = null;
        DetailText = "选中一条结果可查看进程路径与句柄信息";
        StatusText = "已清空";
        OnPropertyChanged(nameof(CountText));
        RefreshCommandStates();
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择被占用的文件",
            CheckFileExists = true,
            Filter = "所有文件|*.*",
        };

        if (dialog.ShowDialog() != true) return;

        FilePath = dialog.FileName;
        _ = ScanAsync(deep: false);
    }

    // ── 详情 ───────────────────────────────────────────────────────────

    private void UpdateDetail()
    {
        if (SelectedResult is not { } item)
        {
            DetailText = "选中一条结果可查看进程路径与句柄信息";
            return;
        }

        var lines = new List<string>
        {
            $"进程：{item.ProcessName}（PID {item.Pid}）",
            $"来源：{item.Source}",
        };

        var exePath = ProcessInfoService.GetExecutablePath(item.Pid);
        if (!string.IsNullOrEmpty(exePath)) lines.Add($"路径：{exePath}");
        if (item.CanCloseHandle) lines.Add($"句柄：{item.HandleText}");
        if (!string.IsNullOrEmpty(item.ServiceName)) lines.Add($"服务：{item.ServiceName}");
        if (!string.IsNullOrEmpty(item.MatchedPath)) lines.Add($"对象：{item.MatchedPath}");
        if (_resolvedPath != null) lines.Add($"文件：{_resolvedPath}");

        DetailText = string.Join(Environment.NewLine, lines);
    }

    // ── 操作 ───────────────────────────────────────────────────────────

    private async Task KillSelectedAsync()
    {
        if (SelectedResult is not { } item) return;

        if (ProcessGuard.IsProtected(item.Pid, item.ProcessName))
        {
            Dialogs.Error(ProcessGuard.ProtectedReason(item.ProcessName), "已阻止");
            return;
        }

        if (!Dialogs.Confirm(
                $"确定结束进程 {item.ProcessName}（PID {item.Pid}）吗？\n\n" +
                "如果它是你正在使用的编辑器 / 预览工具，未保存的内容会丢失。",
                "确认结束进程"))
            return;

        StatusText = $"正在结束 PID {item.Pid} ...";
        var (ok, message) = await ProcessInfoService.KillAsync(item.Pid);
        StatusText = ok ? $"PID {item.Pid}：{message}" : $"结束失败：{message}";

        if (ok) await ScanAsync(deep: false);
    }

    private async Task CloseHandleAsync()
    {
        if (SelectedResult is not { CanCloseHandle: true } item) return;

        if (!Dialogs.Confirm(
                $"强制关闭句柄可能让 {item.ProcessName} 崩溃或丢失未保存数据。\n\n" +
                $"句柄：{item.HandleText}\n进程：{item.ProcessName}（PID {item.Pid}）\n\n" +
                "建议优先选择“结束进程”。确实要关闭句柄吗？",
                "强制关闭句柄（高风险）"))
            return;

        string? error = null;
        bool ok = await Task.Run(() => FileLockService.CloseRemoteHandle(item.Pid, item.HandleValue, out error));

        if (ok)
        {
            StatusText = "句柄已关闭，现在可以重试删除文件了";
            Dialogs.Info("句柄已关闭，请重新尝试删除 / 重命名该文件。\n\n若仍被占用，说明还有其他进程持有句柄，请再次扫描。", "完成");
            await ScanAsync(deep: true);
        }
        else
        {
            StatusText = $"关闭句柄失败：{error}";
            Dialogs.Error(error ?? "关闭句柄失败", "失败");
        }
    }

    private void OpenFileLocation()
    {
        var path = _resolvedPath ?? FilePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"打开失败：{ex.Message}";
        }
    }

    private void CopyResults()
    {
        if (Results.Count == 0) return;

        var header = $"{"PID",-7} {"进程",-28} {"来源",-12} {"句柄",-12} 说明";
        var body = Results.Select(r => $"{r.Pid,-7} {r.ProcessName,-28} {r.Source,-12} {r.HandleText,-12} {r.Description}");
        var text = header + Environment.NewLine + string.Join(Environment.NewLine, body);

        try
        {
            Clipboard.SetText(text);
            StatusText = $"已复制 {Results.Count} 条记录";
        }
        catch (Exception ex)
        {
            StatusText = $"复制失败：{ex.Message}";
        }
    }

    private void RefreshCommandStates()
    {
        _killCommand.RaiseCanExecuteChanged();
        _closeHandleCommand.RaiseCanExecuteChanged();
        _openLocationCommand.RaiseCanExecuteChanged();
        _copyCommand.RaiseCanExecuteChanged();
    }
}
