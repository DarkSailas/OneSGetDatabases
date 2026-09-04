using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public class ConfluencePublisher : IConfluencePublisher
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ConfluencePublisher> _logger;
    private readonly ConfluenceConfig _config;

    public ConfluencePublisher(HttpClient httpClient, IOptions<ConfluenceConfig> config, ILogger<ConfluencePublisher> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config.Value;

        if (!string.IsNullOrEmpty(_config.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_config.BaseUrl);
        }
        if (!string.IsNullOrEmpty(_config.BearerToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.BearerToken);
        }
    }

    public async Task<bool> PublishAllAsync(
        IReadOnlyList<InfoBaseItem> devBases,
        IReadOnlyList<InfoBaseItem> prodBases,
        CancellationToken cancellationToken = default)
    {
        bool allSuccess = true;
        string dateStr = DateTime.Now.ToString("dd.MM.yyyy");

        try
        {
            // 1. DEV Page
            string devTableHtml = BuildMainTableHtml(devBases);
            bool devOk = await UpdatePageAsync(
                _config.PageIdDev,
                "Базы 1С (DEV)",
                _config.SpaceKey,
                _config.AncestorIdDevProd,
                $"<p>Данные на {dateStr}</p>{devTableHtml}",
                cancellationToken);

            if (!devOk) allSuccess = false;

            // 2. PROD Page
            string prodTableHtml = BuildMainTableHtml(prodBases);
            bool prodOk = await UpdatePageAsync(
                _config.PageIdProd,
                "Базы 1С (PROD)",
                _config.SpaceKey,
                _config.AncestorIdDevProd,
                $"<p>Данные на {dateStr}</p>{prodTableHtml}",
                cancellationToken);

            if (!prodOk) allSuccess = false;

            // 3. SA_INFO Page
            string saInfoTableHtml = BuildSaInfoTableHtml(prodBases);
            bool saOk = await UpdatePageAsync(
                _config.PageIdSaInfo,
                "Название баз 1С",
                _config.SpaceKeySaInfo,
                _config.AncestorIdSaInfo,
                $"<p>Данные на {dateStr}</p>{saInfoTableHtml}",
                cancellationToken);

            if (!saOk) allSuccess = false;

            return allSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception publishing to Confluence: {Message}", ex.Message);
            return false;
        }
    }

    public static string BuildMainTableHtml(IReadOnlyList<InfoBaseItem> bases)
    {
        var sb = new StringBuilder();
        sb.Append("<table><tr><th>№</th><th>База</th><th>Описание</th><th>Группа доступа</th><th>Кластер</th><th>IP сервера</th><th>Платформа</th><th>Consul</th><th>SQL Server</th><th>Имя базы в СУБД</th><th>Пользователь службы 1С</th><th>Имя службы</th><th>Каталог сервера</th></tr>");

        int index = 1;
        foreach (var b in bases)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{index}</td>");
            sb.Append($"<td>{EscapeHtml(b.Name)}</td>");
            sb.Append($"<td>{EscapeHtml(b.Description)}</td>");
            sb.Append($"<td>{EscapeHtml(b.AccessGroup)}</td>");
            sb.Append($"<td>{EscapeHtml(b.Cluster)}</td>");
            sb.Append($"<td>{EscapeHtml(b.ServerIP)}</td>");
            sb.Append($"<td>{EscapeHtml(b.Platform)}</td>");
            sb.Append($"<td>{EscapeHtml(b.Consul)}</td>");
            sb.Append($"<td>{EscapeHtml(b.SQL)}</td>");
            sb.Append($"<td>{EscapeHtml(b.SQLDbName)}</td>");
            sb.Append($"<td>{EscapeHtml(b.ServiceUser)}</td>");
            sb.Append($"<td>{EscapeHtml(b.ServiceName)}</td>");
            sb.Append($"<td>{EscapeHtml(b.ClusterPath)}</td>");
            sb.Append("</tr>");
            index++;
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    public static string BuildSaInfoTableHtml(IReadOnlyList<InfoBaseItem> bases)
    {
        var sb = new StringBuilder();
        sb.Append("<table><tr><th>№</th><th>База</th><th>Наименование базы</th><th>Ярлык v8i</th><th>RA-группа</th><th>1C группа</th><th>Платформа</th></tr>");

        int index = 1;
        foreach (var b in bases)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{index}</td>");
            sb.Append($"<td>{EscapeHtml(b.Name)}</td>");
            sb.Append($"<td>{EscapeHtml(b.Description)}</td>");
            sb.Append($"<td>{EscapeHtml(b.V8iFile)}</td>");
            sb.Append($"<td>{EscapeHtml(b.RaGroup)}</td>");
            sb.Append($"<td>{EscapeHtml(b.OneCGroup)}</td>");
            sb.Append($"<td>{EscapeHtml(b.Platform)}</td>");
            sb.Append("</tr>");
            index++;
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    private async Task<bool> UpdatePageAsync(
        string pageId,
        string title,
        string spaceKey,
        string ancestorId,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        try
        {
            string getUri = $"/rest/api/content/{pageId}";
            var response = await _httpClient.GetAsync(getUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to GET Confluence page {PageId}: {Status} - {Err}", pageId, response.StatusCode, err);
                return false;
            }

            var jsonNode = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            int currentVersion = jsonNode?["version"]?["number"]?.GetValue<int>() ?? 0;
            int newVersion = currentVersion + 1;

            var updatePayload = new JsonObject
            {
                ["id"] = pageId,
                ["type"] = "page",
                ["title"] = title,
                ["status"] = "current",
                ["space"] = new JsonObject { ["key"] = spaceKey },
                ["version"] = new JsonObject { ["number"] = newVersion },
                ["ancestors"] = new JsonArray { new JsonObject { ["id"] = ancestorId } },
                ["body"] = new JsonObject
                {
                    ["storage"] = new JsonObject
                    {
                        ["value"] = htmlBody,
                        ["representation"] = "storage"
                    }
                }
            };

            string putUri = $"/rest/api/content/{pageId}";
            var putContent = new StringContent(updatePayload.ToJsonString(), Encoding.UTF8, "application/json");
            var putResponse = await _httpClient.PutAsync(putUri, putContent, cancellationToken);

            if (putResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully updated Confluence page '{Title}' (ID: {Id}) to version {Ver}", title, pageId, newVersion);
                return true;
            }
            else
            {
                string err = await putResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to PUT Confluence page {PageId}: {Status} - {Err}", pageId, putResponse.StatusCode, err);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Confluence page {PageId}: {Msg}", pageId, ex.Message);
            return false;
        }
    }

    private static string EscapeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return System.Net.WebUtility.HtmlEncode(text);
    }
}
