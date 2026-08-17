namespace PortMonitor.Models;

public enum ProtocolFilter
{
    All,
    TcpOnly,
    UdpOnly
}

public static class ProtocolFilterExtensions
{
    public static string ToDisplayString(this ProtocolFilter filter)
    {
        return filter switch
        {
            ProtocolFilter.All => "全部",
            ProtocolFilter.TcpOnly => "TCP",
            ProtocolFilter.UdpOnly => "UDP",
            _ => "全部"
        };
    }

    public static bool Matches(this ProtocolFilter filter, ProtocolType protocol)
    {
        return filter switch
        {
            ProtocolFilter.All => true,
            ProtocolFilter.TcpOnly => protocol == ProtocolType.Tcp,
            ProtocolFilter.UdpOnly => protocol == ProtocolType.Udp,
            _ => true
        };
    }
}
