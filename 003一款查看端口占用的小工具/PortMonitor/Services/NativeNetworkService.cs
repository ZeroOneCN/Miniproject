using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using PortMonitor.Models;

namespace PortMonitor.Services;

public static class NativeNetworkService
{
    // ── P/Invoke declarations ──────────────────────────────────────────

    private const string Iphlpapi = "iphlpapi.dll";

    private enum TcpTableClass
    {
        BasicListener,
        BasicConnections,
        BasicAll,
        OwnerPidListener,
        OwnerPidConnections,
        OwnerPidAll,
        OwnerModuleListener,
        OwnerModuleConnections,
        OwnerModuleAll,
    }

    private enum UdpTableClass
    {
        Basic,
        OwnerPid,
        OwnerModule,
    }

    [DllImport(Iphlpapi, SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder,
        int ulAf, TcpTableClass tableClass, int reserved = 0);

    [DllImport(Iphlpapi, SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref int pdwSize, bool bOrder,
        int ulAf, UdpTableClass tableClass, int reserved = 0);

    // ── Structures ─────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcprowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;   // network byte order
        public uint RemoteAddr;
        public uint RemotePort;  // network byte order
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdprowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort;   // network byte order
        public uint OwningPid;
    }

    // ── TCP state mapping ──────────────────────────────────────────────

    private static readonly Dictionary<uint, string> TcpStateNames = new()
    {
        { 1,  "CLOSED" },
        { 2,  "LISTEN" },
        { 3,  "SYN_SENT" },
        { 4,  "SYN_RCVD" },
        { 5,  "ESTABLISHED" },
        { 6,  "FIN_WAIT1" },
        { 7,  "FIN_WAIT2" },
        { 8,  "CLOSE_WAIT" },
        { 9,  "CLOSING" },
        { 10, "LAST_ACK" },
        { 11, "TIME_WAIT" },
        { 12, "DELETE_TCB" },
    };

    // ── Public API ─────────────────────────────────────────────────────

    /// <summary>
    /// Query all TCP connections that match the given port.
    /// </summary>
    public static List<ConnectionInfo> GetTcpConnections(int port)
    {
        var results = new List<ConnectionInfo>();
        int size = 0;
        uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false,
            AF_INET, TcpTableClass.OwnerPidAll);

        if (result != 0 && result != ERROR_INSUFFICIENT_BUFFER)
            return results;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(buffer, ref size, false,
                AF_INET, TcpTableClass.OwnerPidAll);

            if (result != 0)
                return results;

            int numEntries = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<MibTcprowOwnerPid>();
            IntPtr rowPtr = buffer + 4;

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MibTcprowOwnerPid>(rowPtr);
                int localPort = NetworkToHostShort(row.LocalPort);
                int remotePort = NetworkToHostShort(row.RemotePort);

                if (localPort != port && remotePort != port)
                {
                    rowPtr += rowSize;
                    continue;
                }

                var info = new ConnectionInfo
                {
                    Pid = (int)row.OwningPid,
                    Protocol = ProtocolType.Tcp,
                    LocalAddress = $"{AddrToString(row.LocalAddr)}:{localPort}",
                    RemoteAddress = row.RemoteAddr == 0
                        ? "-"
                        : $"{AddrToString(row.RemoteAddr)}:{remotePort}",
                };

                // Append state to local address for listeners
                if (TcpStateNames.TryGetValue(row.State, out var stateName))
                {
                    info.LocalAddress = $"{info.LocalAddress} [{stateName}]";
                }

                ResolveProcessInfo(info);
                results.Add(info);
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }

    /// <summary>
    /// Query all UDP listeners that match the given port.
    /// </summary>
    public static List<ConnectionInfo> GetUdpConnections(int port)
    {
        var results = new List<ConnectionInfo>();
        int size = 0;
        uint result = GetExtendedUdpTable(IntPtr.Zero, ref size, false,
            AF_INET, UdpTableClass.OwnerPid);

        if (result != 0 && result != ERROR_INSUFFICIENT_BUFFER)
            return results;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedUdpTable(buffer, ref size, false,
                AF_INET, UdpTableClass.OwnerPid);

            if (result != 0)
                return results;

            int numEntries = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<MibUdprowOwnerPid>();
            IntPtr rowPtr = buffer + 4;

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MibUdprowOwnerPid>(rowPtr);
                int localPort = NetworkToHostShort(row.LocalPort);

                if (localPort != port)
                {
                    rowPtr += rowSize;
                    continue;
                }

                var info = new ConnectionInfo
                {
                    Pid = (int)row.OwningPid,
                    Protocol = ProtocolType.Udp,
                    LocalAddress = $"{AddrToString(row.LocalAddr)}:{localPort}",
                    RemoteAddress = "-",
                };

                ResolveProcessInfo(info);
                results.Add(info);
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private const int AF_INET = 2;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    private static int NetworkToHostShort(uint networkShort)
    {
        return (int)IPAddress.NetworkToHostOrder((short)networkShort);
    }

    private static string AddrToString(uint raw)
    {
        return new IPAddress(raw).ToString();
    }

    private static readonly Dictionary<int, string> ProcessNameCache = new();
    private static readonly Dictionary<int, string> ProcessCmdCache = new();
    private static DateTime _lastCacheClear = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(3);

    private static void ResolveProcessInfo(ConnectionInfo info)
    {
        if (info.Pid <= 0)
        {
            info.ProcessName = "未知";
            return;
        }

        // Clear cache periodically
        if (DateTime.UtcNow - _lastCacheClear > CacheDuration)
        {
            ProcessNameCache.Clear();
            ProcessCmdCache.Clear();
            _lastCacheClear = DateTime.UtcNow;
        }

        // Try cache
        if (ProcessNameCache.TryGetValue(info.Pid, out var cachedName))
        {
            info.ProcessName = cachedName;
            ProcessCmdCache.TryGetValue(info.Pid, out var cachedCmd);
            info.CmdLine = cachedCmd;
            return;
        }

        // Get process name
        try
        {
            var process = Process.GetProcessById(info.Pid);
            info.ProcessName = process.ProcessName;
            ProcessNameCache[info.Pid] = info.ProcessName;

            // Get command line via WMI
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {info.Pid}");
                foreach (var obj in searcher.Get())
                {
                    info.CmdLine = obj["CommandLine"]?.ToString() ?? "";
                    break;
                }
            }
            catch
            {
                // Fallback: try main module
                try { info.CmdLine = process.MainModule?.FileName; }
                catch { info.CmdLine = null; }
            }

            ProcessCmdCache[info.Pid] = info.CmdLine ?? "";
        }
        catch (ArgumentException)
        {
            info.ProcessName = "进程已退出";
            info.CmdLine = null;
            ProcessNameCache[info.Pid] = info.ProcessName;
            ProcessCmdCache[info.Pid] = info.CmdLine ?? "";
        }
        catch (Exception)
        {
            info.ProcessName = "无法访问";
            info.CmdLine = null;
            ProcessNameCache[info.Pid] = info.ProcessName;
            ProcessCmdCache[info.Pid] = info.CmdLine ?? "";
        }
    }
}