using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PortMonitor.Services;

/// <summary>
/// 单个被扫描到的开放端口。
/// </summary>
public sealed class PortScanResult
{
    public int Port { get; init; }

    /// <summary>TCP 握手耗时（毫秒）</summary>
    public int ElapsedMs { get; init; }

    /// <summary>按端口号推测的常见服务名，未知为 "-"</summary>
    public string ServiceName { get; init; } = "-";

    /// <summary>本机端口才会有占用进程</summary>
    public int OwnerPid { get; init; }

    public string OwnerProcess { get; init; } = "-";

    public string OwnerText => OwnerPid > 0 ? $"{OwnerProcess}（{OwnerPid}）" : "-";
}

public readonly record struct ScanProgress(int Scanned, int Total, int Open);

/// <summary>
/// 一次扫描的结果汇总（取消时返回已扫完的部分）。
/// </summary>
public sealed class PortScanOutcome
{
    public List<PortScanResult> Results { get; init; } = new();

    public int Scanned { get; init; }

    public int Total { get; init; }

    public bool Cancelled { get; init; }

    public bool TargetIsLocal { get; init; }

    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// 端口扫描：TCP connect 扫描，固定数量的工作协程从端口区间里领任务。
/// 本机目标会额外反查占用进程，扫完就知道是谁在监听。
/// </summary>
public static class PortScanService
{
    private static readonly Dictionary<int, string> ServiceMap = new()
    {
        [21] = "FTP", [22] = "SSH", [23] = "Telnet", [25] = "SMTP", [53] = "DNS",
        [67] = "DHCP", [69] = "TFTP", [80] = "HTTP", [110] = "POP3", [111] = "RPC",
        [123] = "NTP", [135] = "MSRPC", [137] = "NetBIOS", [139] = "NetBIOS-SSN",
        [143] = "IMAP", [161] = "SNMP", [389] = "LDAP", [443] = "HTTPS", [445] = "SMB",
        [465] = "SMTPS", [514] = "Syslog", [587] = "SMTP-Submission", [636] = "LDAPS",
        [873] = "Rsync", [993] = "IMAPS", [995] = "POP3S", [1080] = "Socks",
        [1433] = "SQL Server", [1521] = "Oracle", [1723] = "PPTP", [1883] = "MQTT",
        [2049] = "NFS", [2181] = "ZooKeeper", [2375] = "Docker", [2376] = "Docker TLS",
        [3000] = "Node / 前端调试", [3306] = "MySQL", [3389] = "远程桌面", [4444] = "Metasploit",
        [5000] = "Flask / UPnP", [5432] = "PostgreSQL", [5601] = "Kibana", [5672] = "RabbitMQ",
        [5900] = "VNC", [5985] = "WinRM", [6379] = "Redis", [7001] = "WebLogic",
        [8000] = "HTTP-Alt", [8009] = "AJP", [8080] = "HTTP-Proxy", [8081] = "HTTP-Alt",
        [8086] = "InfluxDB", [8443] = "HTTPS-Alt", [8500] = "Consul", [8848] = "Nacos",
        [8888] = "HTTP-Alt", [9000] = "SonarQube", [9090] = "Prometheus", [9092] = "Kafka",
        [9200] = "Elasticsearch", [9300] = "Elasticsearch 集群", [11211] = "Memcached",
        [15672] = "RabbitMQ 管理", [27017] = "MongoDB", [50000] = "SAP",
    };

    // 后端开发最常用的几个端口，界面上的快捷按钮直接用
    public static string ServiceNameOf(int port)
        => ServiceMap.TryGetValue(port, out var name) ? name : "-";

    public static IPAddress? ResolveTarget(string host, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(host))
        {
            error = "请填写扫描目标";
            return null;
        }

        host = host.Trim();

        if (IPAddress.TryParse(host, out var parsed)) return parsed;

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            var picked = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                         ?? addresses.FirstOrDefault();

            if (picked is null)
            {
                error = $"无法解析主机名 {host}";
                return null;
            }

            return picked;
        }
        catch (Exception ex)
        {
            error = $"无法解析主机名 {host}：{ex.Message}";
            return null;
        }
    }

    public static bool IsLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.Equals(address)) return true;
                }
            }
        }
        catch
        {
            // 读网卡信息失败时按“非本机”处理，只是少了进程反查
        }

        return false;
    }

    /// <summary>
    /// 代理软件（Clash / Surge 等）的 TUN 模式会把任意域名解析成 198.18.0.0/15 的虚拟 IP。
    /// 这种目标其实连到的是本机代理，扫描结果没有参考价值，需要提示用户。
    /// </summary>
    public static bool IsVirtualProxyAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19);
    }

    /// <summary>
    /// 本机端口 → 占用进程。取自连接表，只读一次，供扫描结果反查使用。
    /// </summary>
    public static Dictionary<int, (int Pid, string Name)> GetLocalPortOwners()
    {
        var map = new Dictionary<int, (int, string)>();

        try
        {
            foreach (var item in PortService.QueryAll(Models.ProtocolFilter.All))
            {
                if (item.Pid <= 0) continue;
                if (item.State != "LISTEN" && item.Protocol == Models.ProtocolType.Tcp) continue;
                if (!map.ContainsKey(item.LocalPort)) map[item.LocalPort] = (item.Pid, item.ProcessName);
            }
        }
        catch
        {
            // 连接表不可用时只返回已收集到的部分
        }

        return map;
    }

    public static async Task<PortScanOutcome> ScanAsync(
        IPAddress address,
        int startPort,
        int endPort,
        int timeoutMs,
        int concurrency,
        IProgress<ScanProgress>? progress,
        CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();

        int total = endPort - startPort + 1;
        int scanned = 0;
        int openCount = 0;
        int cursor = startPort - 1;

        var hits = new ConcurrentBag<PortScanResult>();

        // 固定数量的工作协程从递增游标里领端口：端口再多也只创建 concurrency 个任务
        async Task WorkerAsync()
        {
            while (!token.IsCancellationRequested)
            {
                int port = Interlocked.Increment(ref cursor);
                if (port > endPort) return;

                int elapsed = await ProbeAsync(address, port, timeoutMs, token).ConfigureAwait(false);
                if (elapsed >= 0)
                {
                    hits.Add(new PortScanResult
                    {
                        Port = port,
                        ElapsedMs = elapsed,
                        ServiceName = ServiceNameOf(port),
                    });
                    Interlocked.Increment(ref openCount);
                }

                int done = Interlocked.Increment(ref scanned);
                if (done % 32 == 0 || done == total)
                    progress?.Report(new ScanProgress(done, total, Volatile.Read(ref openCount)));
            }
        }

        int workerCount = Math.Clamp(concurrency, 1, Math.Max(1, total));
        var workers = new Task[workerCount];
        for (int i = 0; i < workerCount; i++) workers[i] = WorkerAsync();
        await Task.WhenAll(workers).ConfigureAwait(false);

        var results = hits.OrderBy(r => r.Port).ToList();
        bool isLocal = IsLocalAddress(address);

        if (isLocal && results.Count > 0)
        {
            var owners = GetLocalPortOwners();
            for (int i = 0; i < results.Count; i++)
            {
                if (!owners.TryGetValue(results[i].Port, out var owner)) continue;

                results[i] = new PortScanResult
                {
                    Port = results[i].Port,
                    ElapsedMs = results[i].ElapsedMs,
                    ServiceName = results[i].ServiceName,
                    OwnerPid = owner.Pid,
                    OwnerProcess = owner.Name,
                };
            }
        }

        stopwatch.Stop();

        return new PortScanOutcome
        {
            Results = results,
            Scanned = Volatile.Read(ref scanned),
            Total = total,
            Cancelled = token.IsCancellationRequested,
            TargetIsLocal = isLocal,
            Elapsed = stopwatch.Elapsed,
        };
    }

    /// <summary>
    /// 单端口探测：返回握手耗时（毫秒），-1 表示未开放或超时。
    /// </summary>
    private static async Task<int> ProbeAsync(IPAddress address, int port, int timeoutMs, CancellationToken token)
    {
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(timeoutMs);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), timeout.Token).ConfigureAwait(false);
            return (int)stopwatch.ElapsedMilliseconds;
        }
        catch
        {
            // 拒绝连接 / 超时 / 目标不可达，一律按“未开放”处理
            return -1;
        }
    }
}
