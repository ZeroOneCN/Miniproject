using System.Windows.Input;
using ScreenTimeoutManager.Models;
using ScreenTimeoutManager.Services;

namespace ScreenTimeoutManager.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly PowerSettingsService _service = new();

    /// <summary>弹出消息框请求（View 订阅后显示 MessageBox）</summary>
    public event Action<string, string>? MessageRequested;

    private string _schemeName = "读取中…";
    private string _timeoutText = "读取中…";
    private string _adminText = "";
    private int _customMinutes = 10;
    private bool _isRefreshing;

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

    public int CustomMinutes
    {
        get => _customMinutes;
        set => SetProperty(ref _customMinutes, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }

    // ---------- Commands ----------

    public ICommand SetNeverCommand { get; }
    public ICommand SetOneMinuteCommand { get; }
    public ICommand SetDefaultCommand { get; }
    public ICommand ApplyCustomCommand { get; }
    public ICommand RefreshCommand { get; }

    public MainViewModel()
    {
        SetNeverCommand = new RelayCommand(async () => await SetTimeoutAsync(0));
        SetOneMinuteCommand = new RelayCommand(async () => await SetTimeoutAsync(1));
        SetDefaultCommand = new RelayCommand(async () => await SetTimeoutAsync(10));
        ApplyCustomCommand = new RelayCommand(async () => await SetTimeoutAsync(CustomMinutes));
        RefreshCommand = new RelayCommand(async () => await RefreshAsync());

        // 初始加载
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;

        try
        {
            // 在后台线程执行 powercfg 调用
            var info = await Task.Run(() => _service.GetCurrentScheme());
            SchemeName = info.SchemeName;
            TimeoutText = info.TimeoutDisplay;
            AdminText = info.IsAdmin ? "管理员权限：是" : "管理员权限：否（部分设置可能失败）";
        }
        catch (Exception ex)
        {
            SchemeName = "读取失败";
            TimeoutText = $"错误：{ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task SetTimeoutAsync(int minutes)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;

        try
        {
            var label = minutes == 0 ? "永不关闭" : $"{minutes} 分钟";
            await Task.Run(() => _service.SetScreenTimeout(minutes));
            await RefreshAsync();
            MessageRequested?.Invoke("提示", $"屏幕关闭时间已设置为「{label}」。");
        }
        catch (Exception ex)
        {
            SchemeName = "错误";
            TimeoutText = $"设置失败：{ex.Message}";
            MessageRequested?.Invoke("错误", $"设置失败：{ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}