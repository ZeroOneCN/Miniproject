using OpenCvSharp;
using CameraMotionCapture.Core.Models;
using CameraMotionCapture.Core.Services.Interfaces;
using CameraMotionCapture.Shared;
using Serilog;

namespace CameraMotionCapture.Core.Services.Implementations;

/// <summary>
/// 摄像头枚举实现。
///
/// <para>流程分两步：先用 <see cref="VideoDeviceEnumerator"/> 拿到设备<b>名字</b>与类型，
/// 再逐路验证真的能读到画面。相比"盲扫 0~7 号索引"，好处有三个：</para>
///
/// <list type="number">
/// <item>能识别并跳过虚拟摄像头（手机投屏、直播软件注册的设备）——打开这类设备会触发
/// 驱动去连手机，弹窗干扰用户，画面还是张占位图；</item>
/// <item>设备名可以显示在界面上，"USB Camera"比"摄像头 3"有用得多；</item>
/// <item>只验证真实存在的设备，不再对不存在的序号空跑一遍超时。</item>
/// </list>
///
/// <para>验证必须<b>串行</b>：DirectShow 同时初始化多个采集设备会互相干扰，
/// 实测并发打开时物理摄像头会读帧失败，而串行打开全部正常。</para>
/// </summary>
public sealed class CameraDiscovery : ICameraDiscovery
{
    public async Task<IReadOnlyList<VideoDevice>> DiscoverAsync(CameraDiscoveryOptions options,
        CancellationToken ct = default)
    {
        options ??= new CameraDiscoveryOptions();

        // MSMF 的索引空间里没有 DirectShow 的设备名，无法按类型过滤，只能退回盲扫
        if (options.Backend == CaptureBackend.Msmf)
            return await Task.Run(() => BlindScan(VideoCaptureAPIs.MSMF, options.MaxIndex), ct);

        var all = VideoDeviceEnumerator.Enumerate();

        if (all.Count == 0)
        {
            // COM 枚举不可用（极少数精简系统），退回盲扫
            Log.Warning("未能枚举 DirectShow 设备名，改用索引盲扫");
            return await Task.Run(() => BlindScan(VideoCaptureAPIs.DSHOW, options.MaxIndex), ct);
        }

        var candidates = new List<VideoDevice>(all.Count);
        foreach (var device in all)
        {
            // 虚拟摄像头一律不打开：手机投屏 / 直播软件注册的设备一开就会去连手机，
            // 弹窗打断用户，画面也只是张占位图。这里不提供开关。
            if (device.IsVirtual)
            {
                Log.Information("跳过虚拟摄像头：{Name}（#{Index}）", device.Name, device.Index);
                continue;
            }

            candidates.Add(device);
        }

        // 逐个验证设备真的能出画面。放后台线程，避免卡住 UI。
        var usable = await Task.Run(() => Verify(candidates), ct);

        Log.Information("检测到可用摄像头：{Cameras}",
            usable.Count == 0
                ? "无"
                : string.Join("；", usable.Select(d => d.Display)));

        return usable;
    }

    /// <summary>逐路打开验证：能打开且能读到帧才算可用（必须串行，见类注释）。</summary>
    private static List<VideoDevice> Verify(List<VideoDevice> candidates)
    {
        var usable = new List<VideoDevice>(candidates.Count);

        foreach (var device in candidates)
        {
            if (CanReadFrames(device.Index))
                usable.Add(device);
            else
                Log.Warning("设备 {Name}（#{Index}）无法读取画面，已忽略", device.Name, device.Index);
        }

        if (!usable.Any(d => !d.IsVirtual) && candidates.Any(d => !d.IsVirtual))
            Log.Warning("所有物理摄像头都无法读取画面，可能是被其他程序占用");

        return usable;
    }

    /// <summary>没有设备名可用时的兜底：扫描索引区间。</summary>
    private static List<VideoDevice> BlindScan(VideoCaptureAPIs api, int maxIndex)
    {
        var found = new List<VideoDevice>();

        for (var index = 0; index < maxIndex; index++)
        {
            if (CanReadFrames(index, api))
                found.Add(new VideoDevice(index, $"摄像头 #{index}"));
        }

        return found;
    }

    private static bool CanReadFrames(int index, VideoCaptureAPIs api = VideoCaptureAPIs.DSHOW)
    {
        VideoCapture? cap = null;
        try
        {
            cap = new VideoCapture(index, api);
            if (!cap.IsOpened()) return false;

            using var frame = new Mat();
            for (var i = 0; i < 2; i++)
            {
                if (cap.Read(frame) && !frame.Empty()) return true;
                Thread.Sleep(20);
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                cap?.Release();
                cap?.Dispose();
            }
            catch { }
        }
    }
}
