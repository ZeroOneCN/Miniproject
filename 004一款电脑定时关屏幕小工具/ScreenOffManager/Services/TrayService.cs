using System.Drawing;
using WinForms = System.Windows.Forms;

namespace ScreenOffManager.Services;

/// <summary>
/// 系统托盘。关窗后程序留在托盘，右键即可快速切换屏幕关闭时间。
/// 使用 WinForms 的 NotifyIcon：System.Windows.Forms 已在 csproj 中启用，不引入任何 NuGet 包。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _autoStartItem;
    private readonly Icon _icon;
    private bool _disposed;

    /// <summary>请求显示主界面</summary>
    public event Action? OpenRequested;

    /// <summary>请求应用预设（分钟数）</summary>
    public event Action<int>? PresetRequested;

    /// <summary>托盘菜单切换了开机自启</summary>
    public event Action<bool>? AutoStartToggled;

    /// <summary>请求退出程序</summary>
    public event Action? ExitRequested;

    public TrayService(bool autoStartEnabled)
    {
        _icon = LoadIcon();

        var menu = new WinForms.ContextMenuStrip();

        var open = new WinForms.ToolStripMenuItem("打开主界面") { Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke();

        var never = new WinForms.ToolStripMenuItem("永不关闭屏幕");
        never.Click += (_, _) => PresetRequested?.Invoke(0);

        var oneMinute = new WinForms.ToolStripMenuItem("1 分钟后关闭");
        oneMinute.Click += (_, _) => PresetRequested?.Invoke(1);

        var tenMinutes = new WinForms.ToolStripMenuItem("10 分钟后关闭");
        tenMinutes.Click += (_, _) => PresetRequested?.Invoke(10);

        _autoStartItem = new WinForms.ToolStripMenuItem("开机自动启动")
        {
            CheckOnClick = true,
            Checked = autoStartEnabled
        };
        _autoStartItem.Click += (_, _) => AutoStartToggled?.Invoke(_autoStartItem.Checked);

        var exit = new WinForms.ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            open,
            new WinForms.ToolStripSeparator(),
            never, oneMinute, tenMinutes,
            new WinForms.ToolStripSeparator(),
            _autoStartItem,
            new WinForms.ToolStripSeparator(),
            exit
        });

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = "熄屏管家",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    /// <summary>同步托盘勾选状态（主界面改动后调用）</summary>
    public void SyncAutoStart(bool enabled)
    {
        if (_autoStartItem.Checked != enabled)
            _autoStartItem.Checked = enabled;
    }

    /// <summary>弹出气泡提示（后台操作的结果反馈，不打断用户）</summary>
    public void ShowTip(string title, string text, bool warning = false)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(3000);
        }
        catch (Exception ex)
        {
            Log.Error("显示托盘气泡失败", ex);
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon is not null) return icon;
            }
        }
        catch (Exception ex)
        {
            Log.Error("读取程序图标失败，使用系统图标", ex);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _icon.Dispose();
        }
        catch
        {
            // 退出阶段忽略清理异常
        }
    }
}
