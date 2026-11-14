using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenOffManager.Services;

/// <summary>
/// 直接调用 powrprof.dll 读写电源方案，替代 powercfg 子进程。
/// 好处：毫秒级返回、不弹控制台窗口、不依赖系统语言（powercfg 的文本输出会随语言变化）。
/// </summary>
internal static class PowerNative
{
    // GUID_VIDEO_SUBGROUP / GUID_VIDEOIDLE（「在此时间后关闭显示」）
    private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid,
        IntPtr subGroupGuid, IntPtr powerSettingGuid, byte[]? buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subGroupGuid, ref Guid powerSettingGuid, out uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subGroupGuid, ref Guid powerSettingGuid, out uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subGroupGuid, ref Guid powerSettingGuid, uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subGroupGuid, ref Guid powerSettingGuid, uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>获取当前活动电源方案的 GUID</summary>
    public static Guid GetActiveSchemeGuid()
    {
        var err = PowerGetActiveScheme(IntPtr.Zero, out var ptr);
        if (err != 0) throw new Win32Exception((int)err, "读取当前电源方案失败");

        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    /// <summary>读取电源方案的友好名称（如「平衡」）</summary>
    public static string GetSchemeName(Guid scheme)
    {
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (size == 0) return "未知方案";

        var buffer = new byte[size];
        var err = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
        if (err != 0) return "未知方案";

        return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0').Trim();
    }

    /// <summary>读取屏幕关闭秒数（AC/DC），失败的一项返回 null</summary>
    public static (int? ac, int? dc) ReadVideoIdleSeconds(Guid scheme)
    {
        var sub = SubVideo;
        var idle = VideoIdle;

        var acErr = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref idle, out var acSeconds);
        var dcErr = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref idle, out var dcSeconds);

        return (acErr == 0 ? (int)acSeconds : null,
                dcErr == 0 ? (int)dcSeconds : null);
    }

    /// <summary>
    /// 写入屏幕关闭秒数（AC/DC 同步）。
    /// AC 视为硬失败（写不进去必须报错）；DC 为软失败（台式机/无电池设备可能不支持）。
    /// </summary>
    public static void WriteVideoIdleSeconds(Guid scheme, int seconds)
    {
        var sub = SubVideo;
        var idle = VideoIdle;
        var value = (uint)Math.Max(0, seconds);

        var acErr = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref idle, value);
        if (acErr != 0)
            throw new Win32Exception((int)acErr, "写入交流(AC)超时失败，可能需要管理员权限");

        var dcErr = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref idle, value);
        if (dcErr != 0)
            Log.Info($"写入直流(DC)超时失败，已忽略（错误码 {dcErr}），通常是本机无电池。");

        // 让新设置立即生效（等价于 powercfg /S SCHEME_CURRENT）
        var activateErr = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        if (activateErr != 0)
            Log.Info($"重新激活电源方案返回错误码 {activateErr}，设置可能需手动刷新。");
    }
}
