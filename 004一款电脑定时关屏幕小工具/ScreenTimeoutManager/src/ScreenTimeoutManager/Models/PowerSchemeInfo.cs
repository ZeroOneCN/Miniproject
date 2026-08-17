namespace ScreenTimeoutManager.Models;

/// <summary>
/// 电源方案与屏幕关闭时间信息
/// </summary>
public class PowerSchemeInfo
{
    public string SchemeName { get; set; } = "读取中…";
    public int? AcSeconds { get; set; }
    public int? DcSeconds { get; set; }
    public bool IsAdmin { get; set; }

    public string TimeoutDisplay
    {
        get
        {
            if (AcSeconds is null && DcSeconds is null)
                return "未知（读取失败）";

            // 如果 AC/DC 一致，显示统一值
            if (AcSeconds is not null && DcSeconds is not null && AcSeconds == DcSeconds)
            {
                var val = AcSeconds.Value;
                return val == 0 ? "永不关闭" : $"{val / 60} 分钟后关闭";
            }

            // AC/DC 不一致时分别显示
            var acText = AcSeconds is null ? "未知" : (AcSeconds.Value == 0 ? "永不" : $"{AcSeconds.Value / 60} 分钟");
            var dcText = DcSeconds is null ? "未知" : (DcSeconds.Value == 0 ? "永不" : $"{DcSeconds.Value / 60} 分钟");
            return $"(AC: {acText} / DC: {dcText})";
        }
    }
}