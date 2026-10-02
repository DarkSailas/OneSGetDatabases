using System.Management;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public partial class CimService : ICimService
{
    private readonly ILogger<CimService> _logger;
    private readonly TimeSpan _queryTimeout;

    public CimService(IOptions<ClusterDiscoveryConfig> discoveryConfig, ILogger<CimService> logger)
    {
        _logger = logger;
        _queryTimeout = (discoveryConfig.Value ?? new ClusterDiscoveryConfig()).WmiQueryTimeout;
    }

    [GeneratedRegex(@"-d\s+""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedClusterDirRegex();

    [GeneratedRegex(@"-d\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex UnquotedClusterDirRegex();

    [GeneratedRegex(@"-port\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PortRegex();

    public async ValueTask<(string ServiceUser, string ServiceName, string ClusterPath, string? Error)> GetServiceInfoAsync(
        string hostName, int port, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            return ("Неизвестно", "Неизвестно", "Неизвестно", "Имя хоста не указано");

        // Quick port check for WinRM (5985) or RPC (135) to avoid CIM hangs
        bool canConnect = await TestPortAsync(hostName, 5985, 1000) || await TestPortAsync(hostName, 135, 1000);
        if (!canConnect)
        {
            return ("Неизвестно", "Неизвестно", "Неизвестно",
                $"Сервер {hostName} недоступен по портам WinRM (5985) и RPC (135). Запрос CIM отменен.");
        }

        var queryTask = Task.Run<(string ServiceUser, string ServiceName, string ClusterPath, string? Error)>(() =>
        {
            try
            {
                var options = new ConnectionOptions
                {
                    Timeout = TimeSpan.FromSeconds(5),
                    EnablePrivileges = true,
                    Impersonation = ImpersonationLevel.Impersonate,
                    Authentication = AuthenticationLevel.PacketPrivacy
                };

                ManagementScope scope;
                try
                {
                    scope = new ManagementScope($@"\\{hostName}\root\cimv2", options);
                    scope.Connect();
                }
                catch (UnauthorizedAccessException) when (!hostName.Contains('.'))
                {
                    string fqdn = hostName;
                    try
                    {
                        var entry = System.Net.Dns.GetHostEntry(hostName);
                        if (!string.IsNullOrEmpty(entry.HostName) && entry.HostName.Contains('.'))
                        {
                            fqdn = entry.HostName;
                        }
                    }
                    catch
                    {
                        // Ignore DNS lookup failure
                    }

                    if (!string.Equals(fqdn, hostName, StringComparison.OrdinalIgnoreCase))
                    {
                        scope = new ManagementScope($@"\\{fqdn}\root\cimv2", options);
                        scope.Connect();
                    }
                    else
                    {
                        throw;
                    }
                }

                var query = new SelectQuery("Win32_Service", "Name LIKE '%1C:Enterprise 8%Server Agent%'");
                // ConnectionOptions.Timeout covers only Connect(); the enumeration needs its own limit
                var enumOptions = new System.Management.EnumerationOptions { Timeout = _queryTimeout, ReturnImmediately = true, Rewindable = false };
                using var searcher = new ManagementObjectSearcher(scope, query, enumOptions);
                using var results = searcher.Get();

                foreach (ManagementObject service in results)
                {
                    string pathName = service["PathName"]?.ToString() ?? "";
                    string serviceName = service["Name"]?.ToString() ?? "Неизвестно";
                    string startName = service["StartName"]?.ToString() ?? "Неизвестно";

                    var portMatch = PortRegex().Match(pathName);
                    bool isMatchingPort = false;

                    if (portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out int svcPort))
                    {
                        isMatchingPort = svcPort == port;
                    }
                    else if (port == 1540 && !portMatch.Success)
                    {
                        // Default 1540 might not have -port parameter explicitly specified
                        isMatchingPort = true;
                    }

                    if (isMatchingPort)
                    {
                        string clusterPath = "Неизвестно";
                        var qMatch = QuotedClusterDirRegex().Match(pathName);
                        if (qMatch.Success)
                        {
                            clusterPath = qMatch.Groups[1].Value;
                        }
                        else
                        {
                            var uMatch = UnquotedClusterDirRegex().Match(pathName);
                            if (uMatch.Success)
                            {
                                clusterPath = uMatch.Groups[1].Value;
                            }
                        }

                        return (startName, serviceName, clusterPath, null);
                    }
                }

                return ("Неизвестно", "Неизвестно", "Неизвестно", $"Служба 1C для порта {port} на {hostName} не найдена");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("WMI/CIM query failed for {Host}:{Port}: {Message}", hostName, port, ex.Message);
                return ("Неизвестно", "Неизвестно", "Неизвестно", $"Ошибка CIM на {hostName}:{port}: {ex.Message}");
            }
        }, cancellationToken);

        try
        {
            // Safety net for a WMI provider that hangs regardless of the enumeration timeout
            return await queryTask.WaitAsync(_queryTimeout * 2, cancellationToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("WMI/CIM query timed out for {Host}:{Port} after {Seconds}s", hostName, port, (int)(_queryTimeout * 2).TotalSeconds);
            return ("Неизвестно", "Неизвестно", "Неизвестно", $"Таймаут CIM-запроса к {hostName}:{port}");
        }
    }

    private static async Task<bool> TestPortAsync(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var delayTask = Task.Delay(timeoutMs);

            if (await Task.WhenAny(connectTask, delayTask) == connectTask)
            {
                return client.Connected;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
