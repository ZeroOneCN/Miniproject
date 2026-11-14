using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PortMonitor.Models;

public enum ProtocolType
{
    Tcp,
    Udp
}

/// <summary>
/// 一条端口连接记录。
/// </summary>
public class ConnectionInfo : INotifyPropertyChanged
{
    private string _processName = string.Empty;
    private string? _cmdLine;

    public int Pid { get; init; }

    public ProtocolType Protocol { get; init; }

    public string ProtocolName => Protocol == ProtocolType.Tcp ? "TCP" : "UDP";

    /// <summary>本地地址，IPv6 会带方括号，例如 [::]:5173</summary>
    public string LocalAddress { get; init; } = string.Empty;

    /// <summary>远端地址，未连接时为 "-"</summary>
    public string RemoteAddress { get; init; } = "-";

    /// <summary>TCP 状态（LISTEN / ESTABLISHED / TIME_WAIT ...），UDP 为 "-"</summary>
    public string State { get; init; } = "-";

    /// <summary>本地端口（已转换为主机字节序）</summary>
    public int LocalPort { get; init; }

    public string ProcessName
    {
        get => _processName;
        set
        {
            if (_processName == value) return;
            _processName = value;
            OnPropertyChanged();
        }
    }

    public string? CmdLine
    {
        get => _cmdLine;
        set
        {
            if (_cmdLine == value) return;
            _cmdLine = value;
            OnPropertyChanged();
        }
    }

    /// <summary>稳定标识，用于刷新后恢复选中行</summary>
    public string Key => $"{Pid}|{ProtocolName}|{LocalAddress}|{State}";

    /// <summary>状态列着色用的颜色值</summary>
    public string StateColor => State switch
    {
        "LISTEN" => "#0F7B3F",
        "ESTABLISHED" => "#1D4ED8",
        "SYN_SENT" or "SYN_RCVD" => "#B45309",
        "TIME_WAIT" or "CLOSE_WAIT" or "FIN_WAIT1" or "FIN_WAIT2" or "LAST_ACK" or "CLOSING" => "#6B7280",
        _ => "#374151",
    };

    /// <summary>列表里显示的一行摘要（复制用）</summary>
    public string ToDisplayLine() =>
        $"{Pid,-7} {ProcessName,-24} {ProtocolName,-4} {State,-12} {LocalAddress,-28} {RemoteAddress}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
