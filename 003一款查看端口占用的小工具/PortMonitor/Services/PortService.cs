using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using PortMonitor.Models;

namespace PortMonitor.Services;

/// <summary>
/// 端口连接枚举：IPv4 + IPv6、TCP + UDP 全覆盖。
/// </summary>
public static class PortService
{
    private static readonly string[] TcpStateNames =
    {
        "UNKNOWN", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD", "ESTABLISHED",
        "FIN_WAIT1", "FIN_WAIT2", "CLOSE_WAIT", "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB",
    };

    /// <summary>
    /// 查询指定端口的所有连接（IPv4 + IPv6，按协议筛选）。
    /// </summary>
    public static List<ConnectionInfo> Query(int port, ProtocolFilter filter)
    {
        var found = new List<ConnectionInfo>(16);

        bool wantTcp = filter is ProtocolFilter.All or ProtocolFilter.TcpOnly;
        bool wantUdp = filter is ProtocolFilter.All or ProtocolFilter.UdpOnly;

        if (wantTcp)
        {
            ReadTcp(port, NativeMethods.AF_INET, found);
            ReadTcp(port, NativeMethods.AF_INET6, found);
        }

        if (wantUdp)
        {
            ReadUdp(port, NativeMethods.AF_INET, found);
            ReadUdp(port, NativeMethods.AF_INET6, found);
        }

        // 同一连接可能在 IPv4/IPv6 表中重复出现，按稳定键去重
        var unique = found
            .GroupBy(c => c.Key)
            .Select(g => g.First())
            .OrderBy(c => c.ProtocolName == "UDP" ? 1 : 0)
            .ThenBy(c => c.State == "LISTEN" ? 0 : 1)
            .ThenBy(c => c.LocalAddress, StringComparer.Ordinal)
            .ToList();

        // 进程名一次性批量解析（比逐条 WMI 快两个数量级）
        var names = ProcessInfoService.GetProcessNames();
        foreach (var item in unique)
        {
            item.ProcessName = item.Pid == 0
                ? "System Idle"
                : names.TryGetValue(item.Pid, out var name) ? name : "无法访问";
        }

        return unique;
    }

    // ── TCP ────────────────────────────────────────────────────────────

    private static void ReadTcp(int port, int family, List<ConnectionInfo> output)
    {
        int size = 0;
        uint rc = NativeMethods.GetExtendedTcpTable(
            IntPtr.Zero, ref size, false, family, NativeMethods.TcpTableClass.OwnerPidAll, 0);

        if (rc != NativeMethods.ERROR_SUCCESS && rc != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
            return;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            rc = NativeMethods.GetExtendedTcpTable(
                buffer, ref size, false, family, NativeMethods.TcpTableClass.OwnerPidAll, 0);
            if (rc != NativeMethods.ERROR_SUCCESS) return;

            int rowCount = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = buffer + 4;

            if (family == NativeMethods.AF_INET)
            {
                int rowSize = Marshal.SizeOf<NativeMethods.MibTcpRowOwnerPid>();
                for (int i = 0; i < rowCount; i++, rowPtr += rowSize)
                {
                    var row = Marshal.PtrToStructure<NativeMethods.MibTcpRowOwnerPid>(rowPtr);
                    int localPort = NetPortToHost(row.LocalPort);
                    int remotePort = NetPortToHost(row.RemotePort);
                    if (localPort != port && remotePort != port) continue;

                    output.Add(new ConnectionInfo
                    {
                        Pid = (int)row.OwningPid,
                        Protocol = ProtocolType.Tcp,
                        LocalPort = localPort,
                        LocalAddress = $"{new IPAddress(row.LocalAddr)}:{localPort}",
                        RemoteAddress = row.RemoteAddr == 0
                            ? "-"
                            : $"{new IPAddress(row.RemoteAddr)}:{remotePort}",
                        State = StateName(row.State),
                    });
                }
            }
            else
            {
                int rowSize = Marshal.SizeOf<NativeMethods.MibTcp6RowOwnerPid>();
                for (int i = 0; i < rowCount; i++, rowPtr += rowSize)
                {
                    var row = Marshal.PtrToStructure<NativeMethods.MibTcp6RowOwnerPid>(rowPtr);
                    int localPort = NetPortToHost(row.LocalPort);
                    int remotePort = NetPortToHost(row.RemotePort);
                    if (localPort != port && remotePort != port) continue;

                    bool remoteEmpty = row.RemotePort == 0;
                    output.Add(new ConnectionInfo
                    {
                        Pid = (int)row.OwningPid,
                        Protocol = ProtocolType.Tcp,
                        LocalPort = localPort,
                        LocalAddress = $"{AddrV6(row.LocalAddr, row.LocalScopeId)}:{localPort}",
                        RemoteAddress = remoteEmpty
                            ? "-"
                            : $"{AddrV6(row.RemoteAddr, row.RemoteScopeId)}:{remotePort}",
                        State = StateName(row.State),
                    });
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ── UDP ────────────────────────────────────────────────────────────

    private static void ReadUdp(int port, int family, List<ConnectionInfo> output)
    {
        int size = 0;
        uint rc = NativeMethods.GetExtendedUdpTable(
            IntPtr.Zero, ref size, false, family, NativeMethods.UdpTableClass.OwnerPid, 0);

        if (rc != NativeMethods.ERROR_SUCCESS && rc != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
            return;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            rc = NativeMethods.GetExtendedUdpTable(
                buffer, ref size, false, family, NativeMethods.UdpTableClass.OwnerPid, 0);
            if (rc != NativeMethods.ERROR_SUCCESS) return;

            int rowCount = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = buffer + 4;

            if (family == NativeMethods.AF_INET)
            {
                int rowSize = Marshal.SizeOf<NativeMethods.MibUdpRowOwnerPid>();
                for (int i = 0; i < rowCount; i++, rowPtr += rowSize)
                {
                    var row = Marshal.PtrToStructure<NativeMethods.MibUdpRowOwnerPid>(rowPtr);
                    int localPort = NetPortToHost(row.LocalPort);
                    if (localPort != port) continue;

                    output.Add(new ConnectionInfo
                    {
                        Pid = (int)row.OwningPid,
                        Protocol = ProtocolType.Udp,
                        LocalPort = localPort,
                        LocalAddress = $"{new IPAddress(row.LocalAddr)}:{localPort}",
                        RemoteAddress = "-",
                        State = "-",
                    });
                }
            }
            else
            {
                int rowSize = Marshal.SizeOf<NativeMethods.MibUdp6RowOwnerPid>();
                for (int i = 0; i < rowCount; i++, rowPtr += rowSize)
                {
                    var row = Marshal.PtrToStructure<NativeMethods.MibUdp6RowOwnerPid>(rowPtr);
                    int localPort = NetPortToHost(row.LocalPort);
                    if (localPort != port) continue;

                    output.Add(new ConnectionInfo
                    {
                        Pid = (int)row.OwningPid,
                        Protocol = ProtocolType.Udp,
                        LocalPort = localPort,
                        LocalAddress = $"{AddrV6(row.LocalAddr, row.LocalScopeId)}:{localPort}",
                        RemoteAddress = "-",
                        State = "-",
                    });
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ── 辅助 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 端口号按网络字节序存放在 DWORD 的低 16 位。
    /// 禁止用 (int)IPAddress.NetworkToHostOrder((short)raw) —— (short) 截断会让 &gt;= 32768 的端口变成负数。
    /// </summary>
    public static int NetPortToHost(uint raw)
        => (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));

    private static string StateName(uint state)
        => state < TcpStateNames.Length ? TcpStateNames[state] : $"STATE_{state}";

    private static string AddrV6(byte[] addr, uint scopeId)
    {
        if (addr is not { Length: 16 }) return "::";
        var ip = new IPAddress(addr);
        var text = ip.ToString();
        if (ip.IsIPv6LinkLocal && scopeId != 0) text += $"%{scopeId}";
        return $"[{text}]";
    }
}
