using System.Text.RegularExpressions;

namespace OneSGetDatabases.Core.Helpers;

public static class ServerNameHelper
{
    /// <summary>
    /// Normalizes server names by removing domain suffixes (.example.corp, .domain.local, etc.)
    /// and standardizing casing, while preserving explicit port numbers if specified.
    /// E.g. "sql-dev01.example.corp.example.corp" -> "sql-dev01.example.corp"
    /// E.g. "pg-prod01.example.corp:5432" -> "pg-prod01:5432"
    /// </summary>
    public static string NormalizeServerName(string? serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName)) return "";
        string host = serverName.Trim();

        // If port is present (e.g., host:5432), split host and port
        string port = "";
        int colonIdx = host.IndexOf(':');
        if (colonIdx > 0)
        {
            port = host[colonIdx..];
            host = host[..colonIdx];
        }

        // If domain suffix is present, strip it
        if (host.EndsWith(".example.corp", StringComparison.OrdinalIgnoreCase))
        {
            host = host[..^12];
        }
        else if (host.Contains('.'))
        {
            // Take hostname before the first dot
            host = host.Split('.')[0];
        }

        return (host + port).ToLowerInvariant();
    }

    /// <summary>
    /// Checks whether two server name representations point to the same physical host.
    /// E.g. "sql-dev01.example.corp" and "sql-dev01.example.corp.example.corp" -> TRUE
    /// E.g. "sql-dev01.example.corp" and "sql-dev01.example.corp01" -> FALSE
    /// </summary>
    public static bool IsSameServer(string? serverA, string? serverB)
    {
        if (string.IsNullOrWhiteSpace(serverA) || string.IsNullOrWhiteSpace(serverB)) return false;
        return string.Equals(NormalizeServerName(serverA), NormalizeServerName(serverB), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns priority index for environments: PROD (0), DEV (1), others (2).
    /// </summary>
    public static int GetEnvironmentPriority(string? env)
    {
        if (string.Equals(env, "PROD", StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(env, "DEV", StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    /// <summary>
    /// Produces a deterministic natural sort key for 1C clusters:
    /// 1. SQL / Primary database clusters
    /// 2. Production application clusters
    /// 3. Development / Staging clusters
    /// 4. Regional and other clusters
    /// </summary>
    public static string GetClusterSortKey(string? cluster)
    {
        if (string.IsNullOrWhiteSpace(cluster)) return "99_zzzz";
        string c = cluster.Trim().ToLowerInvariant();

        // Zero-pad any sequence of digits (e.g. app1 -> app000001, port 1540 -> 001540) for natural alphanumeric sorting
        string padded = Regex.Replace(c, @"\d+", m => m.Value.PadLeft(6, '0'));

        if (c.Contains("sql")) return "00_" + padded;
        if (c.Contains("prod")) return "10_" + padded;
        if (c.Contains("dev")) return "20_" + padded;
        if (c.Contains("test") || c.Contains("qa") || c.Contains("stage")) return "30_" + padded;

        return "80_" + padded;
    }
}
