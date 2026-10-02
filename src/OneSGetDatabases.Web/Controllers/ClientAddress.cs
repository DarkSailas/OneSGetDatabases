using System.Net;

namespace OneSGetDatabases.Web.Controllers;

/// <summary>Client IP for the audit log.</summary>
public static class ClientAddress
{
    public static string Get(HttpContext context) =>
        Resolve(context.Connection.RemoteIpAddress, context.Request.Headers["X-Forwarded-For"].ToString());

    /// <summary>
    /// X-Forwarded-For is honoured only from a local reverse proxy: any other caller could write
    /// an arbitrary address into the audit log with it.
    /// </summary>
    public static string Resolve(IPAddress? remoteIp, string? forwardedFor)
    {
        if (remoteIp != null && IPAddress.IsLoopback(remoteIp) && !string.IsNullOrWhiteSpace(forwardedFor)
            && IPAddress.TryParse(forwardedFor.Split(',')[0].Trim(), out var forwarded))
        {
            return Format(forwarded);
        }

        return remoteIp != null ? Format(remoteIp) : "127.0.0.1";
    }

    private static string Format(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return IPAddress.IsLoopback(ip) ? "127.0.0.1" : ip.ToString();
    }
}
