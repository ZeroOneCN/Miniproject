using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PortMonitor.Models;

public enum ProtocolType
{
    Tcp,
    Udp
}

public partial class ConnectionInfo : INotifyPropertyChanged
{
    private int _pid;
    private string _processName = string.Empty;
    private string? _cmdLine;
    private ProtocolType _protocol;
    private string _localAddress = string.Empty;
    private string? _remoteAddress;

    public int Pid
    {
        get => _pid;
        set { _pid = value; OnPropertyChanged(); }
    }

    public string ProcessName
    {
        get => _processName;
        set { _processName = value; OnPropertyChanged(); }
    }

    public string? CmdLine
    {
        get => _cmdLine;
        set { _cmdLine = value; OnPropertyChanged(); }
    }

    public ProtocolType Protocol
    {
        get => _protocol;
        set { _protocol = value; OnPropertyChanged(); }
    }

    public string ProtocolName => Protocol == ProtocolType.Tcp ? "TCP" : "UDP";

    public string LocalAddress
    {
        get => _localAddress;
        set { _localAddress = value; OnPropertyChanged(); }
    }

    public string? RemoteAddress
    {
        get => _remoteAddress;
        set { _remoteAddress = value; OnPropertyChanged(); }
    }

    public string DisplayRemoteAddress => RemoteAddress ?? "-";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
