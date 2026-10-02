using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public class AuditLogService : IAuditLogService
{
    private static readonly ConcurrentDictionary<string, string> _dnsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly AuditLogConfig _config;
    private readonly ILogger<AuditLogService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<AuditLogEntry> _entries = [];
    private readonly string _fullPath;

    public AuditLogService(IOptions<AuditLogConfig> config, ILogger<AuditLogService> logger)
    {
        _config = config.Value ?? new AuditLogConfig();
        _logger = logger;

        _fullPath = Path.IsPathRooted(_config.LogFilePath)
            ? _config.LogFilePath
            : Path.Combine(AppContext.BaseDirectory, _config.LogFilePath);

        try
        {
            var dir = Path.GetDirectoryName(_fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            LoadAndPruneLogs();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize audit log file at {Path}: {Msg}", _fullPath, ex.Message);
        }
    }

    private void LoadAndPruneLogs()
    {
        if (!File.Exists(_fullPath)) return;

        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _config.RetentionDays));
            var lines = File.ReadAllLines(_fullPath);
            var validEntries = new List<AuditLogEntry>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<AuditLogEntry>(line);
                    if (entry != null && entry.TimestampUtc >= cutoff)
                    {
                        string normIp = NormalizeIp(entry.ClientIp);
                        if (normIp != entry.ClientIp) entry = entry with { ClientIp = normIp };
                        validEntries.Add(entry);
                    }
                }
                catch { }
            }

            _entries.Clear();
            _entries.AddRange(validEntries);

            var fileInfo = new FileInfo(_fullPath);
            long maxBytes = _config.MaxLogSizeBytes > 0 ? _config.MaxLogSizeBytes : 524288000; // 500 MB default

            // If file has expired entries or exceeds maximum size, rewrite cleanly
            if (validEntries.Count < lines.Length || fileInfo.Length > maxBytes)
            {
                // If over size limit, keep newest entries that fit within 80% of limit
                if (fileInfo.Length > maxBytes && _entries.Count > 100)
                {
                    int keepCount = (int)(_entries.Count * 0.8);
                    var trimmed = _entries.TakeLast(keepCount).ToList();
                    _entries.Clear();
                    _entries.AddRange(trimmed);
                }

                RewriteFileWithoutLock();
            }

            _logger.LogInformation("Audit log initialized. {Count} records loaded from {Path}", _entries.Count, _fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load/prune audit logs: {Msg}", ex.Message);
        }
    }

    public static string ResolveHostName(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "127.0.0.1" || ip == "::1")
        {
            return Environment.MachineName;
        }

        if (_dnsCache.TryGetValue(ip, out var cached))
        {
            return cached;
        }

        _dnsCache[ip] = ip;
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
                var entry = await Dns.GetHostEntryAsync(ip, cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(entry.HostName))
                {
                    string shortName = entry.HostName.Split('.')[0];
                    _dnsCache[ip] = shortName;
                }
            }
            catch
            {
                // DNS lookup failed or timed out, keep IP in cache
            }
        });

        return ip;
    }

    public async Task LogActionAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        string cleanIp = NormalizeIp(entry.ClientIp);
        if (cleanIp != entry.ClientIp)
        {
            entry = entry with { ClientIp = cleanIp };
        }

        if (string.IsNullOrWhiteSpace(entry.ClientHostName) && !string.IsNullOrWhiteSpace(entry.ClientIp))
        {
            entry = entry with { ClientHostName = ResolveHostName(entry.ClientIp) };
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            _entries.Add(entry);

            var jsonLine = JsonSerializer.Serialize(entry) + Environment.NewLine;
            await File.AppendAllTextAsync(_fullPath, jsonLine, cancellationToken);

            var fileInfo = new FileInfo(_fullPath);
            long maxBytes = _config.MaxLogSizeBytes > 0 ? _config.MaxLogSizeBytes : 524288000; // 500 MB default
            if (fileInfo.Length > maxBytes && _entries.Count > 100)
            {
                int keepCount = (int)(_entries.Count * 0.8);
                var trimmed = _entries.TakeLast(keepCount).ToList();
                _entries.Clear();
                _entries.AddRange(trimmed);
                RewriteFileWithoutLock();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log entry: {Msg}", ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetEntriesAsync(
        int limit = 500, string? search = null, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var query = _entries.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                query = query.Where(e =>
                    e.Host.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.ServiceName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.Action.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.ClientIp.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.ClientHostName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.Status.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.ErrorMessage.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return query
                .OrderByDescending(e => e.TimestampUtc)
                .Take(Math.Clamp(limit, 1, 5000))
                .Select(e => string.IsNullOrWhiteSpace(e.ClientHostName) && !string.IsNullOrWhiteSpace(e.ClientIp)
                    ? e with { ClientHostName = ResolveHostName(e.ClientIp) }
                    : e)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    private void RewriteFileWithoutLock()
    {
        try
        {
            var tempPath = _fullPath + ".tmp";
            using (var writer = new StreamWriter(tempPath, false))
            {
                foreach (var entry in _entries)
                {
                    writer.WriteLine(JsonSerializer.Serialize(entry));
                }
            }
            File.Move(tempPath, _fullPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rewrite pruned audit log: {Msg}", ex.Message);
        }
    }

    public static string NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "127.0.0.1";
        ip = ip.Trim();
        if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            ip = ip[7..];
        if (ip == "::1")
            return "127.0.0.1";
        return ip;
    }
}
