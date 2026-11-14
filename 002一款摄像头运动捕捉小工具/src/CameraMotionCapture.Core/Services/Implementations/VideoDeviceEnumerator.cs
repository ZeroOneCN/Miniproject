using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>一路本地视频输入设备的描述</summary>
public sealed record VideoDevice(int Index, string Name, bool IsVirtual = false)
{
    /// <summary>形如 "USB Camera（#1）"，用于界面与日志</summary>
    public string Display => $"{Name}（#{Index}）";
}

/// <summary>
/// DirectShow 视频输入设备枚举。
///
/// <para>OpenCV 只给设备序号，不给名字，所以我们自己枚举一遍
/// <c>CLSID_VideoInputDeviceCategory</c>。COM 枚举顺序与 OpenCV 的
/// <c>CAP_DSHOW</c> 索引顺序一致（实测 <c>[0] Integrated Webcam</c> 就是 DSHOW 索引 0），
/// 因此可以按名字与类型决定"这一路要不要打开"。</para>
///
/// <para>判别虚拟摄像头不靠名字：物理设备由 PnP 绑定，moniker 的 DisplayName 形如
/// <c>@device:pnp:\\?\usb#vid_...</c>；而手机投屏、直播/会议软件注册的是纯软件过滤器，
/// 形如 <c>@device:sw:{860BB310-...}\{CLSID}</c> 且没有 DevicePath。
/// 靠这个差异判断，比维护一份厂商名单可靠得多。</para>
/// </summary>
public static class VideoDeviceEnumerator
{
    private const string PnpPrefix = "@device:pnp:";
    private const string SoftwarePrefix = "@device:sw:";

    private static readonly Guid CategoryClsid = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    private static readonly Guid DevEnumClsid = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");

    /// <summary>
    /// 名称里出现这些词时也判为虚拟设备（作为驱动特征的兜底，
    /// 少数虚拟摄像头会伪装成 PnP 设备）。
    /// </summary>
    private static readonly string[] VirtualKeywords =
    {
        "virtual", "obs virtual", "obs-camera", "manycam", "splitcam", "xsplit",
        "droidcam", "ivcam", "epoccam", "reincubate camo", "snap camera", "youcam",
        "mmhmm", "vcam", "e2esoft", "screen capture",
        "虚拟", "投屏", "手机", "直播", "屏幕捕获"
    };

    /// <summary>枚举全部视频输入设备（按 DirectShow 顺序）。失败返回空列表。</summary>
    public static IReadOnlyList<VideoDevice> Enumerate()
    {
        var result = new List<VideoDevice>();

        object? devEnumObj = null;
        IEnumMoniker? enumMoniker = null;

        try
        {
            var type = Type.GetTypeFromCLSID(DevEnumClsid);
            if (type is null) return result;

            devEnumObj = Activator.CreateInstance(type);
            if (devEnumObj is not ICreateDevEnum devEnum) return result;

            var category = CategoryClsid;
            if (devEnum.CreateClassEnumerator(ref category, out enumMoniker, 0) != 0 || enumMoniker is null)
                return result;   // S_FALSE：该分类下没有设备

            var monikers = new IMoniker[1];
            var index = 0;

            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var moniker = monikers[0];
                if (moniker is null) continue;

                try
                {
                    var name = ReadProperty(moniker, "FriendlyName") ?? $"摄像头 #{index}";
                    var devicePath = ReadProperty(moniker, "DevicePath");
                    var displayName = ReadDisplayName(moniker);

                    result.Add(new VideoDevice(index, name, IsVirtualDevice(displayName, devicePath, name)));
                    index++;
                }
                finally
                {
                    Marshal.ReleaseComObject(moniker);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "枚举 DirectShow 视频设备失败");
        }
        finally
        {
            if (enumMoniker is not null) Marshal.ReleaseComObject(enumMoniker);
            if (devEnumObj is not null) Marshal.ReleaseComObject(devEnumObj);
        }

        return result;
    }

    /// <summary>是否虚拟摄像头（手机投屏 / 直播软件 / 屏幕捕获）</summary>
    public static bool IsVirtual(string name) => HasVirtualKeyword(name);

    private static bool IsVirtualDevice(string? displayName, string? devicePath, string name)
    {
        // 有明确的 PnP 绑定 → 物理设备
        if (displayName?.StartsWith(PnpPrefix, StringComparison.OrdinalIgnoreCase) == true)
            return false;

        // 软件过滤器 → 虚拟设备
        if (displayName?.StartsWith(SoftwarePrefix, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        // 拿不到 DisplayName 时退回经验判断：有 DevicePath 视为物理设备，否则看名字
        if (!string.IsNullOrWhiteSpace(devicePath) &&
            devicePath.Contains("usb#", StringComparison.OrdinalIgnoreCase))
            return false;

        return HasVirtualKeyword(name);
    }

    private static bool HasVirtualKeyword(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var lower = name.ToLowerInvariant();
        return VirtualKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal));
    }

    private static string? ReadDisplayName(IMoniker moniker)
    {
        try
        {
            moniker.GetDisplayName(null!, null!, out var display);
            return display;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadProperty(IMoniker moniker, string property)
    {
        object? bagObj = null;
        try
        {
            var bagId = typeof(IPropertyBag).GUID;
            moniker.BindToStorage(null!, null!, ref bagId, out bagObj);
            if (bagObj is not IPropertyBag bag) return null;

            var hr = bag.Read(property, out var value, IntPtr.Zero);
            return hr == 0 ? value as string : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (bagObj is not null) Marshal.ReleaseComObject(bagObj);
        }
    }

    // ───────────────────────── DirectShow COM 声明 ─────────────────────────

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid pType, out IEnumMoniker ppEnumMoniker, int dwFlags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName,
            [MarshalAs(UnmanagedType.Struct)] out object pVar, IntPtr pErrorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName,
            [MarshalAs(UnmanagedType.Struct)] in object pVar);
    }
}
