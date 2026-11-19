using System.Text;
using System.Windows.Input;
using ScreenOffManager.Services;

namespace ScreenOffManager.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly PowerSettingsService _service = new();
    private readonly AppSettings _settings;

    /// <summary>需要在界面上弹窗提示</summary>
    public event Action<string, string>? MessageRequested;

    private string _schemeName = "读取中…";
    private string _timeoutText = "读取中…";
    private string _adminText = "";
    private string _customMinutesText;
    private bool _isRefreshing;
    private bool _isAutoStart;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        _customMinutesText = settings.LastCustomMinutes;
        _isAutoStart = StartupManager.IsEnabled;

        SetNeverCommand = new RelayCommand(async () => await ApplyPresetAsync(0));
        SetOneMinuteCommand = new RelayCommand(async () => await ApplyPresetAsync(1));
        SetDefaultCommand = new RelayCommand(async () => await ApplyPresetAsync(10));
        ApplyCustomCommand = new RelayCommand(async () => await ApplyCustomAsync());
        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        TurnOffScreenCommand = new RelayCommand(async () => await TurnOffScreenAsync());
        DiagnoseCommand = new RelayCommand(async () => await DiagnoseAsync());

        // 初始加载
        _ = RefreshAsync();
    }

    // ---------- 状态属性 ----------

    public string SchemeName
    {
        get => _schemeName;
        set => SetProperty(ref _schemeName, value);
    }

    public string TimeoutText
    {
        get => _timeoutText;
        set => SetProperty(ref _timeoutText, value);
    }

    public string AdminText
    {
        get => _adminText;
        set => SetProperty(ref _adminText, value);
    }

    /// <summary>自定义分钟数输入（字符串 + 统一校验，避免非法输入被静默丢弃）</summary>
    public string CustomMinutesText
    {
        get => _customMinutesText;
        set => SetProperty(ref _customMinutesText, value);
    }

    /// <summary>开机自启：改动即写入注册表，失败则回滚勾选状态</summary>
    public bool IsAutoStart
    {
        get => _isAutoStart;
        set
        {
            if (!SetProperty(ref _isAutoStart, value)) return;

            try
            {
                StartupManager.SetEnabled(value);
            }
            catch (Exception ex)
            {
                _isAutoStart = !value;
                OnPropertyChanged();
                MessageRequested?.Invoke("提示", $"开机自启设置失败：{ex.Message}");
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set
        {
            if (SetProperty(ref _isRefreshing, value))
                OnPropertyChanged(nameof(IsNotRefreshing));
        }
    }

    /// <summary>供 XAML 绑定，用于禁用忙碌期间的按钮</summary>
    public bool IsNotRefreshing => !_isRefreshing;

    // ---------- Commands ----------

    public ICommand SetNeverCommand { get; }
    public ICommand SetOneMinuteCommand { get; }
    public ICommand SetDefaultCommand { get; }
    public ICommand ApplyCustomCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand TurnOffScreenCommand { get; }
    public ICommand DiagnoseCommand { get; }

    // ---------- 业务逻辑 ----------

    private int _refreshLock;

    public async Task RefreshAsync()
    {
        // 防重入：用 int 锁而非 IsRefreshing（设置流程内部也会调用刷新）
        if (Interlocked.Exchange(ref _refreshLock, 1) != 0) return;
        IsRefreshing = true;

        try
        {
            var info = await Task.Run(() => _service.GetCurrentScheme());
            SchemeName = info.SchemeName;
            TimeoutText = info.TimeoutDisplay;
            AdminText = info.IsAdmin
                ? $"管理员权限：是 · 引擎：{info.Engine}"
                : $"管理员权限：否 · 引擎：{info.Engine}（部分设置可能需要提权）";
        }
        catch (Exception ex)
        {
            Log.Error("刷新状态失败", ex);
            SchemeName = "读取失败";
            TimeoutText = $"错误：{ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
            _refreshLock = 0;
        }
    }

    /// <summary>应用预设分钟数</summary>
    public async Task ApplyPresetAsync(int minutes)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;

        var label = minutes == 0 ? "永不关闭" : $"{minutes} 分钟";

        try
        {
            await Task.Run(() => _service.SetScreenTimeout(minutes));
            await RefreshAsync();
            MessageRequested?.Invoke("设置成功", $"屏幕关闭时间已设置为「{label}」。");
        }
        catch (Exception ex)
        {
            Log.Error($"设置屏幕关闭时间失败（{label}）", ex);
            MessageRequested?.Invoke("设置失败", ex.Message);
            await RefreshAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>应用自定义分钟数：先校验输入，再交给统一设置流程</summary>
    private async Task ApplyCustomAsync()
    {
        if (!int.TryParse(_customMinutesText?.Trim(), out var minutes))
        {
            MessageRequested?.Invoke("提示", "请输入 0-1440 之间的整数分钟数（0 表示永不关闭）。");
            return;
        }

        if (minutes is < 0 or > 1440)
        {
            MessageRequested?.Invoke("提示", "分钟数超出范围，请输入 0-1440 之间的整数。");
            return;
        }

        // 记住这次的输入，下次启动直接带回
        _settings.LastCustomMinutes = minutes.ToString();
        _settings.Save();

        await ApplyPresetAsync(minutes);
    }

    /// <summary>立即熄屏：不依赖系统空闲判定，也不受其他程序占用影响</summary>
    private async Task TurnOffScreenAsync()
    {
        try
        {
            await Task.Run(DisplayPowerService.TurnOffMonitor);
            // 屏幕已经黑了，成功时不弹窗（弹了也看不见）
        }
        catch (Exception ex)
        {
            Log.Error("强制关闭显示器失败", ex);
            MessageRequested?.Invoke("熄屏失败", ex.Message);
        }
    }

    /// <summary>诊断：找出正在阻止显示器关闭的程序</summary>
    private async Task DiagnoseAsync()
    {
        IsRefreshing = true;
        try
        {
            var (items, raw, error) = await Task.Run(DisplayPowerService.GetRequests);

            if (!string.IsNullOrWhiteSpace(raw))
                Log.Info($"电源请求原始输出：\n{raw}");

            if (!string.IsNullOrEmpty(error))
            {
                MessageRequested?.Invoke("检查失败", error);
                return;
            }

            var display = items.Where(i => i.Section.Contains("DISPLAY", StringComparison.OrdinalIgnoreCase)).ToList();
            var others = items.Except(display).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"当前关闭时间：{TimeoutText}");
            sb.AppendLine();

            if (display.Count == 0)
            {
                sb.AppendLine("显示器占用：无");
            }
            else
            {
                sb.AppendLine($"显示器占用：{display.Count} 项（就是它们让屏幕不灭）");
                foreach (var d in display.Take(6))
                    sb.AppendLine("  " + ShortenTarget(d.Target));
            }

            if (others.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("其他电源请求（阻止睡眠，不影响熄屏）：");
                foreach (var o in others.Take(4))
                    sb.AppendLine("  " + ShortenTarget(o.Target));
            }

            sb.AppendLine();
            sb.AppendLine(display.Count > 0
                ? "处理：退出上面的程序；或点「立即熄屏」直接关屏。"
                : "系统当前没有程序阻止熄屏。若屏幕仍常亮，检查：\n"
                  + "· 「关闭时间」是否被其他软件改回「永不关闭」\n"
                  + "· 鼠标键盘是否有轻微输入（会重置空闲计时）\n"
                  + "· 显示器自身的节能/自动待机设置");

            MessageRequested?.Invoke("显示占用诊断", sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            Log.Error("诊断显示占用失败", ex);
            MessageRequested?.Invoke("检查失败", ex.Message);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private static string ShortenTarget(string target)
    {
        var t = target
            .Replace("[PROCESS] ", "")
            .Replace("[SERVICE] ", "")
            .Replace("[DRIVER] ", "")
            .Trim();

        return t.Length <= 64 ? t : "…" + t[^62..];
    }
}
