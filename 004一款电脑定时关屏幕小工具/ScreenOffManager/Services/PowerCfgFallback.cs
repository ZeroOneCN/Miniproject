using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ScreenOffManager.Services;

/// <summary>
/// 降级路径：直接调 powercfg 命令行。
/// 仅在 powrprof 原生调用不可用时使用，因此解析逻辑必须做到语言无关。
/// </summary>
internal static class PowerCfgFallback
{
    public static string ReadSchemeName()
    {
        try
        {
            var (_, stdout, _) = RunPowerCfg("/GETACTIVESCHEME");
            if (string.IsNullOrWhiteSpace(stdout)) return "未知方案";

            var m = Regex.Match(stdout, @"\((.+)\)");
            return m.Success ? m.Groups[1].Value : stdout.Trim();
        }
        catch (Exception ex)
        {
            Log.Error("powercfg 读取方案名失败", ex);
            return "读取失败";
        }
    }

    /// <summary>
    /// 解析屏幕关闭秒数。英文系统按关键字精确解析；
    /// 中文等本地化系统标签被翻译（「当前交流电源设置索引」），关键字必然失配，
    /// 因此兜底取输出中最后两个 0x 值 —— powercfg 输出恒以「AC → DC」收尾，与语言无关。
    /// </summary>
    public static (int? ac, int? dc) ReadTimeouts()
    {
        try
        {
            var (_, stdout, _) = RunPowerCfg("-q SCHEME_CURRENT SUB_VIDEO VIDEOIDLE");
            if (string.IsNullOrWhiteSpace(stdout)) return (null, null);

            int? ac = ParseFirstHex(stdout, @"Current AC Power Setting Index:\s*(0x[0-9A-Fa-f]+)");
            int? dc = ParseFirstHex(stdout, @"Current DC Power Setting Index:\s*(0x[0-9A-Fa-f]+)");

            if (ac is null || dc is null)
            {
                var matches = Regex.Matches(stdout, @"0x([0-9A-Fa-f]{1,8})");
                if (matches.Count >= 2)
                {
                    ac ??= ReadHexAt(matches, matches.Count - 2);
                    dc ??= ReadHexAt(matches, matches.Count - 1);
                }
                else if (matches.Count == 1)
                {
                    ac ??= ReadHexAt(matches, 0);
                }
            }

            return (ac, dc);
        }
        catch (Exception ex)
        {
            Log.Error("powercfg 读取超时时间失败", ex);
            return (null, null);
        }
    }

    public static void SetScreenTimeout(int minutes)
    {
        var seconds = Math.Max(0, minutes) * 60;

        var ac = RunPowerCfg($"/SETACVALUEINDEX SCHEME_CURRENT SUB_VIDEO VIDEOIDLE {seconds}");
        if (ac.exitCode != 0)
            throw new InvalidOperationException(
                $"设置交流(AC)超时失败，可能需要管理员权限。\n{ac.stderr.Trim()}");

        // DC 软失败：无电池设备不必因它整体报错
        var dc = RunPowerCfg($"/SETDCVALUEINDEX SCHEME_CURRENT SUB_VIDEO VIDEOIDLE {seconds}");
        if (dc.exitCode != 0)
            Log.Info($"powercfg 写入 DC 失败，已忽略：{dc.stderr.Trim()}");

        RunPowerCfg("/S SCHEME_CURRENT");
    }

    private static int? ParseFirstHex(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : null;
    }

    /// <summary>安全取十六进制值：用 ToInt64 规避 0xFFFFFFFF 溢出，超过 24 小时视为无效</summary>
    private static int? ReadHexAt(MatchCollection matches, int index)
    {
        if (index < 0 || index >= matches.Count) return null;

        var raw = Convert.ToInt64(matches[index].Groups[1].Value, 16);
        return raw <= 86400 ? (int?)raw : null;
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
                // 不指定编码：powercfg 按系统 ANSI 代码页输出，交给运行时默认解码
                // （写死 UTF-8 会让中文方案名变乱码，虽然不影响 0x 数值解析）
            }
        };
        proc.Start();

        // 先异步把两条管道读空再等退出：先 WaitForExit 后 ReadToEnd 遇到大输出会死锁
        var stdOutTask = proc.StandardOutput.ReadToEndAsync();
        var stdErrTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(5000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* 进程可能已退出 */ }
            throw new TimeoutException("powercfg 执行超时（5 秒），已强制结束。");
        }

        return (proc.ExitCode, stdOutTask.GetAwaiter().GetResult(), stdErrTask.GetAwaiter().GetResult());
    }
}
