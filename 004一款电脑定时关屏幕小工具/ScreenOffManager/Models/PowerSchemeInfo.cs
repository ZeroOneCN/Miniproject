namespace ScreenOffManager.Models;

/// <summary>
/// 电源方案与屏幕关闭时间信息
/// </summary>
public class PowerSchemeInfo
{
    public string SchemeName { get; set; } = "读取中…";
    public int? AcSeconds { get; set; }
    public int? DcSeconds { get; set; }
    public bool IsAdmin { get; set; }

    /// <summary>实际使用的读取方式：powrprof（原生）或 powercfg（降级）</summary>
    public string Engine { get; set; } = "powrprof";

    public string TimeoutDisplay
    {
        get
        {
            if (AcSeconds is null && DcSeconds is null)
                return "未知（读取失败）";

            // AC/DC 一致时显示统一值
            if (AcSeconds is not null && DcSeconds is not null && AcSeconds == DcSeconds)
            {
                var val = AcSeconds.Value;
                return val == 0 ? "永不关闭" : $"{FormatMinutes(val)} 分钟后关闭";
            }

            // AC/DC 不一致时分别显示
            var acText = AcSeconds is null ? "未知" : (AcSeconds.Value == 0 ? "永不" : $"{FormatMinutes(AcSeconds.Value)} 分钟");
            var dcText = DcSeconds is null ? "未知" : (DcSeconds.Value == 0 ? "永不" : $"{FormatMinutes(DcSeconds.Value)} 分钟");
            return $"(AC: {acText} / DC: {dcText})";
        }
    }

    /// <summary>秒转分钟：整除会把 90 秒显示成「1 分钟」，非整分钟保留一位小数</summary>
    private static string FormatMinutes(int seconds)
        => seconds % 60 == 0
            ? (seconds / 60).ToString()
            : Math.Round(seconds / 60.0, 1).ToString("0.#");
}
