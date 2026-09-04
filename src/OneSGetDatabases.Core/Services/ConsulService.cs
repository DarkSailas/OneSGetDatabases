using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public class ConsulService : IConsulService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ConsulService> _logger;
    private readonly ConsulConfig _config;

    private Dictionary<string, JsonElement>? _cachedServices;
    private DateTime _lastFetched = DateTime.MinValue;
    private readonly SemaphoreSlim _semaphore = new(1);

    public ConsulService(HttpClient httpClient, IOptions<ConsulConfig> config, ILogger<ConsulService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config.Value;
        if (!string.IsNullOrEmpty(_config.Url))
        {
            _httpClient.BaseAddress = new Uri(_config.Url);
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds > 0 ? _config.TimeoutSeconds : 5);
    }

    private async Task EnsureServicesLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cachedServices != null && (DateTime.UtcNow - _lastFetched).TotalMinutes < 15)
        {
            return;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_cachedServices != null && (DateTime.UtcNow - _lastFetched).TotalMinutes < 15)
            {
                return;
            }

            var response = await _httpClient.GetFromJsonAsync<Dictionary<string, JsonElement>>("/v1/agent/services", cancellationToken);
            if (response != null)
            {
                _cachedServices = response;
                _lastFetched = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to query Consul at {Url}: {Message}", _config.Url, ex.Message);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<(string ConsulName, string SqlServer)> ResolveServiceAsync(string infobaseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(infobaseName))
            return ("Отсутствует", "Неизвестно");

        try
        {
            await EnsureServicesLoadedAsync(cancellationToken);
            if (_cachedServices == null || _cachedServices.Count == 0)
                return ("Отсутствует", "Неизвестно");

            string pattern = $"-{infobaseName}";
            var matching = _cachedServices.Keys
                .Where(k => k.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();

            string consulName = "Отсутствует";
            string sqlServer = "Неизвестно";

            var appService = matching.FirstOrDefault(k => k.StartsWith("app", StringComparison.OrdinalIgnoreCase));
            if (appService != null)
            {
                consulName = $"{appService}.service.consul";
            }

            var sqlService = matching.FirstOrDefault(k => k.StartsWith("sql", StringComparison.OrdinalIgnoreCase));
            if (sqlService != null && _cachedServices.TryGetValue(sqlService, out var sqlElem))
            {
                if (sqlElem.TryGetProperty("Address", out var addrElem))
                {
                    string? addr = addrElem.GetString();
                    if (!string.IsNullOrEmpty(addr))
                    {
                        sqlServer = addr;
                    }
                }
            }

            return (consulName, sqlServer);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error resolving Consul for {Base}: {Message}", infobaseName, ex.Message);
            return ("Отсутствует", "Неизвестно");
        }
    }
}
