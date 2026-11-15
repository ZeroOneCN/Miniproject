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
}
