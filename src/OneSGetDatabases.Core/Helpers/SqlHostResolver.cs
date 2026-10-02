using System.Net;

namespace OneSGetDatabases.Core.Helpers;

/// <summary>
/// Finds the machine behind a DBMS server name from the cluster: a Consul service
/// (sql-1c-ERP.service.consul → its Address) or a DNS alias (SQL-1C-Retail.example.org → canonical name).
/// </summary>
public static class SqlHostResolver
{
    private const string ConsulSuffix = ".service.consul";

    /// <summary>
    /// Splits "host\INSTANCE", "host,1433" and "host:5432" into the host and the suffix kept as is.
    /// </summary>
    public static (string Host, string Suffix) SplitServer(string server)
    {
        string s = server.Trim();
        if (s.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) s = s[4..];

        int idx = s.IndexOfAny(['\\', ',', ':']);
        return idx > 0 ? (s[..idx], s[idx..]) : (s, "");
    }

    /// <summary>
    /// Returns the short lowercase machine name with the instance/port suffix, or "" when it cannot be found.
    /// </summary>
    public static async Task<string> ResolveAsync(
        string? server,
        Func<string, CancellationToken, Task<string?>> consulLookup,
        Func<string, CancellationToken, Task<string?>> dnsLookup,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(server) || server.Equals("Неизвестно", StringComparison.OrdinalIgnoreCase))
            return "";

        var (host, suffix) = SplitServer(server);
        if (host.Length == 0 || host == "." || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith('(') || IPAddress.TryParse(host, out _))
            return "";

        string? machine = null;
        if (host.EndsWith(ConsulSuffix, StringComparison.OrdinalIgnoreCase))
        {
            string serviceName = host[..^ConsulSuffix.Length];
            machine = await consulLookup(serviceName, cancellationToken);
            if (machine != null && IPAddress.TryParse(machine, out _))
                machine = await dnsLookup(machine, cancellationToken) ?? machine;
        }
        else
        {
            machine = await dnsLookup(host, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(machine) || IPAddress.TryParse(machine, out _))
            return "";

        return ServerNameHelper.NormalizeServerName(machine) + suffix.ToLowerInvariant();
    }

    /// <summary>
    /// DNS canonical name: CNAME chains end at the machine's A record, an IP resolves via PTR.
    /// </summary>
    public static async Task<string?> DnsCanonicalNameAsync(string hostOrIp, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var entry = await Dns.GetHostEntryAsync(hostOrIp, cts.Token);
            return string.IsNullOrWhiteSpace(entry.HostName) ? null : entry.HostName;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null;
        }
    }
}
