using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ScreenOffManager.Services;

/// <summary>一条电源请求：谁、哪一类、原始文本</summary>
public sealed class PowerRequestItem
{
    public string Section { get; init; } = "";
    public string Target { get; init; } = "";
    public string RawLine { get; init; } = "";
}

/// <summary>
/// 显示器电源相关的两项能力：
/// 1) 强制熄屏：广播 SC_MONITORPOWER，绕过空闲计时与任何程序持有的显示请求；
/// 2) 占用诊断：解析 powercfg /requests，找出正在阻止显示器关闭的进程。
/// </summary>
public static class DisplayPowerService
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;
    private const int MONITOR_OFF = 2;
    private const int SMTO_ABORTIFHUNG = 0x0002;

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, int flags, int timeout, out IntPtr result);

    /// <summary>立即关闭显示器。不依赖系统空闲判定，也不受其他程序的显示请求影响。</summary>
    public static void TurnOffMonitor()
    {
        var ok = SendMessageTimeout(
            HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)MONITOR_OFF,
            SMTO_ABORTIFHUNG, 2000, out _);

        if (ok == IntPtr.Zero)
            throw new InvalidOperationException("调用系统接口关闭显示器失败，可能被安全策略拦截。");

        Log.Info("已强制关闭显示器（SC_MONITORPOWER）");
    }

    /// <summary>
    /// 读取 powercfg /requests。该命令需要管理员权限，非管理员执行会返回错误文本。
    /// </summary>
    public static (List<PowerRequestItem> Items, string Raw, string Error) GetRequests()
    {
        var items = new List<PowerRequestItem>();

        try
        {
            var (exitCode, stdout, stderr) = RunPowerCfg("/requests");
            var raw = (stdout + "\n" + stderr).Trim();

            if (exitCode != 0 && string.IsNullOrWhiteSpace(stdout))
                return (items, raw, "读取失败，可能需要管理员权限。");

            foreach (var item in Parse(stdout))
                items.Add(item);

            return (items, raw, "");
        }
        catch (Exception ex)
        {
            Log.Error("读取电源请求失败", ex);
            return (items, "", ex.Message);
        }
    }

    /// <summary>
    /// powercfg /requests 按「段落: 内容」输出，段落名随语言变化（EXECUTION 在中文系统显示为「执行」），
    /// 因此不按段落名硬编码匹配，只按行首标记 [PROCESS]/[DRIVER]/[SERVICE] 归集。
    /// </summary>
    private static IEnumerable<PowerRequestItem> Parse(string text)
    {
        string section = "";

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line.EndsWith(':') && line.Length <= 40)
            {
                section = line.TrimEnd(':').Trim();
                continue;
            }

            if (line.StartsWith('['))
            {
                yield return new PowerRequestItem
                {
                    Section = section,
                    Target = line,
                    RawLine = line
                };
            }
        }
    }

    private static (int exitCode, string stdout, string stderr) RunPowerCfg(string args)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        proc.Start();

        // 先并行把两条管道读空再等退出：先 WaitForExit 后读取遇到大输出会死锁
        var outTask = Task.Run(() => ReadDecoded(proc.StandardOutput.BaseStream));
        var errTask = Task.Run(() => ReadDecoded(proc.StandardError.BaseStream));

        if (!proc.WaitForExit(5000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* 进程可能已退出 */ }
            throw new TimeoutException("powercfg 执行超时（5 秒），已强制结束。");
        }

        return (proc.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
    }

    /// <summary>
    /// powercfg 按控制台代码页输出（中文系统为 GBK），.NET 默认按 UTF-8 解码会出乱码，
    /// 因此读原始字节后按 ANSI 代码页解码，出现替换字符再退回 UTF-8。
    /// </summary>
    private static string ReadDecoded(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0) return "";

        var ansi = GetAnsiEncoding();
        if (ansi is not null)
        {
            var text = ansi.GetString(bytes);
            if (!text.Contains('\uFFFD')) return text;
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static System.Text.Encoding? GetAnsiEncoding()
    {
        try
        {
            if (_ansiEncoding is not null) return _ansiEncoding;

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            _ansiEncoding = System.Text.Encoding.GetEncoding(
                System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            return _ansiEncoding;
        }
        catch (Exception ex)
        {
            Log.Error("获取控制台代码页失败，改用 UTF-8 解码", ex);
            return null;
        }
    }

    private static System.Text.Encoding? _ansiEncoding;
}
