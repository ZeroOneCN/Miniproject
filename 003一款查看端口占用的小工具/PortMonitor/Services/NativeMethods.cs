using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PortMonitor.Services;

/// <summary>
/// Windows 原生 API 声明集中处（iphlpapi / ntdll / kernel32 / rstrtmgr）。
/// </summary>
internal static class NativeMethods
{
    // ── 通用 ───────────────────────────────────────────────────────────

    public const int AF_INET = 2;
    public const int AF_INET6 = 23;

    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_INSUFFICIENT_BUFFER = 122;
    public const uint ERROR_MORE_DATA = 234;

    public const uint STATUS_BUFFER_OVERFLOW = 0x80000005;
    public const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    public const uint STATUS_BUFFER_TOO_SMALL = 0xC0000023;

    // ── 端口连接表 ─────────────────────────────────────────────────────

    public enum TcpTableClass
    {
        BasicListener,
        BasicConnections,
        BasicAll,
        OwnerPidListener,
        OwnerPidConnections,
        OwnerPidAll,
    }

    public enum UdpTableClass
    {
        Basic,
        OwnerPid,
        OwnerModule,
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder,
        int ulAf, TcpTableClass tableClass, int reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref int pdwSize, bool bOrder,
        int ulAf, UdpTableClass tableClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;    // 网络字节序，仅低 16 位有效
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }   // 56 字节

    [StructLayout(LayoutKind.Sequential)]
    public struct MibUdpRowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibUdp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        public uint OwningPid;
    }   // 28 字节

    // ── 句柄枚举（ntdll） ──────────────────────────────────────────────

    public const int SystemExtendedHandleInformation = 64;
    public const int ObjectNameInformation = 1;

    [DllImport("ntdll.dll")]
    public static extern uint NtQuerySystemInformation(
        int systemInformationClass, IntPtr systemInformation,
        int systemInformationLength, out int returnLength);

    [DllImport("ntdll.dll")]
    public static extern uint NtQueryObject(
        IntPtr handle, int objectInformationClass,
        IntPtr objectInformation, int objectInformationLength, out int returnLength);

    /// <summary>SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX（x64 下 40 字节）</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SystemHandleEntry
    {
        public IntPtr Object;
        public IntPtr UniqueProcessId;
        public IntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    // ── kernel32 ──────────────────────────────────────────────────────

    public const uint PROCESS_DUP_HANDLE = 0x0040;
    public const uint DUPLICATE_CLOSE_SOURCE = 0x0001;
    public const uint DUPLICATE_SAME_ACCESS = 0x0002;
    public const uint FILE_TYPE_DISK = 0x0001;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle, IntPtr hSourceHandle, IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle, uint dwDesiredAccess, bool bInheritHandle, uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetFileType(IntPtr hFile);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

    // ── 重启管理器（Restart Manager） ──────────────────────────────────

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    public static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmRegisterResources(
        uint pSessionHandle, uint nFiles, string[] rgsFilenames,
        uint nApplications, [In] RmUniqueProcess[]? rgApplications,
        uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    public static extern int RmGetList(
        uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
        [In, Out] RmProcessInfo[]? rgAffectedApps, out uint lpdwRebootReasons);

    /// <summary>
    /// RM_UNIQUE_PROCESS：C 侧对齐为 4（FILETIME 只有 4 字节对齐），总长 12 字节。
    /// 必须显式 Pack=4，否则 x64 下 CLR 会把 long 对齐到 8，导致后续字符串整体错位。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct RmUniqueProcess
    {
        public int dwProcessId;
        public long ProcessStartTime;
    }

    /// <summary>
    /// RM_PROCESS_INFO（Pack=4 且 CharSet=Unicode 时总长 668 字节）。
    /// 注意 CharSet 必须显式声明：默认 Ansi 会让 ByValTStr 按 1 字节/字符计算，结构体缩成 348 字节，
    /// 结果就是进程名读出乱码——而且不会抛任何异常。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
    public struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }
}
