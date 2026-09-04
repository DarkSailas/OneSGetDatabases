using System.Text;
using Microsoft.AspNetCore.Mvc;
using OneSGetDatabases.Core.Helpers;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExportController : ControllerBase
{
    private readonly IDatabaseCacheService _cache;
    private readonly IDbmsInspectorService _dbmsInspector;

    public ExportController(IDatabaseCacheService cache, IDbmsInspectorService dbmsInspector)
    {
        _cache = cache;
        _dbmsInspector = dbmsInspector;
    }

    [HttpGet("excel")]
    public IActionResult ExportExcel(
        [FromQuery] string? environment = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sqlServer = null,
        [FromQuery] string? platform = null,
        [FromQuery] string? cluster = null)
    {
        var items = _cache.GetAll().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(environment) && !environment.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var envParts = environment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (envParts.Length > 0 && !envParts.Contains("ALL", StringComparer.OrdinalIgnoreCase))
            {
                items = items.Where(b => envParts.Any(e => e.Equals(b.Environment, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(cluster) && !cluster.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var clusterParts = cluster.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (clusterParts.Length > 0)
            {
                items = items.Where(b => clusterParts.Any(c => c.Equals(b.Cluster, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(sqlServer) && !sqlServer.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var sqlParts = sqlServer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sqlParts.Length > 0)
            {
                items = items.Where(b => sqlParts.Any(s => ServerNameHelper.IsSameServer(b.SQL, s)));
            }
        }

        if (!string.IsNullOrWhiteSpace(platform) && !platform.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var platParts = platform.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (platParts.Length > 0)
            {
                items = items.Where(b => platParts.Any(p => b.Platform.Contains(p, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            items = items.Where(b =>
                b.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.AccessGroup.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SQLDbName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Cluster.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SQL.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Consul.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return GenerateExcelFile(items.ToList());
    }

    [HttpPost("excel")]
    public IActionResult ExportSelectedExcel([FromBody] List<InfoBaseItem> selectedItems)
    {
        var items = selectedItems != null && selectedItems.Count > 0 ? selectedItems : _cache.GetAll().ToList();
        return GenerateExcelFile(items);
    }

    [HttpGet("files/excel")]
    public async Task<IActionResult> ExportFilesExcelGet(
        [FromQuery] string? environment = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sqlServer = null,
        [FromQuery] string? cluster = null,
        CancellationToken cancellationToken = default)
    {
        var allRows = await BuildFilesSummaryRowsAsync(environment, cancellationToken);
        var items = allRows.AsEnumerable();

        if (string.Equals(status, "EXISTS", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(b => b.TotalSizeBytes > 0);
        }
        else if (string.Equals(status, "MISSING", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(b => b.TotalSizeBytes == 0);
        }

        if (!string.IsNullOrWhiteSpace(cluster) && !cluster.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var clusterParts = cluster.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (clusterParts.Length > 0)
            {
                items = items.Where(b => clusterParts.Any(c => c.Equals(b.Cluster, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(sqlServer) && !sqlServer.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var sqlParts = sqlServer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sqlParts.Length > 0)
            {
                items = items.Where(b => sqlParts.Any(s => ServerNameHelper.IsSameServer(b.SqlServer, s)));
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            items = items.Where(b =>
                b.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SqlDbName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.DataFilesPath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.LogFilesPath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Cluster.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SqlServer.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return GenerateFilesExcelFile(items.ToList());
    }

    [HttpPost("files/excel")]
    public IActionResult ExportFilesExcel([FromBody] List<DatabaseDbmsSummaryRow> selectedItems)
    {
        return GenerateFilesExcelFile(selectedItems ?? new List<DatabaseDbmsSummaryRow>());
    }

    [HttpGet("json")]
    public IActionResult ExportJson(
        [FromQuery] string? environment = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sqlServer = null,
        [FromQuery] string? platform = null,
        [FromQuery] string? cluster = null)
    {
        var items = _cache.GetAll().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(environment) && !environment.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var envParts = environment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (envParts.Length > 0 && !envParts.Contains("ALL", StringComparer.OrdinalIgnoreCase))
            {
                items = items.Where(b => envParts.Any(e => e.Equals(b.Environment, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(cluster) && !cluster.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var clusterParts = cluster.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (clusterParts.Length > 0)
            {
                items = items.Where(b => clusterParts.Any(c => c.Equals(b.Cluster, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(sqlServer) && !sqlServer.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var sqlParts = sqlServer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sqlParts.Length > 0)
            {
                items = items.Where(b => sqlParts.Any(s => ServerNameHelper.IsSameServer(b.SQL, s)));
            }
        }

        if (!string.IsNullOrWhiteSpace(platform) && !platform.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var platParts = platform.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (platParts.Length > 0)
            {
                items = items.Where(b => platParts.Any(p => b.Platform.Contains(p, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            items = items.Where(b =>
                b.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.AccessGroup.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SQLDbName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Cluster.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SQL.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Consul.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        string fileName = $"1C_Databases_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        return File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(items.ToList(), new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }), "application/json", fileName);
    }

    [HttpPost("json")]
    public IActionResult ExportSelectedJson([FromBody] List<InfoBaseItem> selectedItems)
    {
        var items = selectedItems != null && selectedItems.Count > 0 ? selectedItems : _cache.GetAll().ToList();
        string fileName = $"1C_Databases_Selected_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        return File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(items, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }), "application/json", fileName);
    }

    [HttpGet("files/json")]
    public async Task<IActionResult> ExportFilesJsonGet(
        [FromQuery] string? environment = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sqlServer = null,
        [FromQuery] string? cluster = null,
        CancellationToken cancellationToken = default)
    {
        var allRows = await BuildFilesSummaryRowsAsync(environment, cancellationToken);
        var items = allRows.AsEnumerable();

        if (string.Equals(status, "EXISTS", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(b => b.TotalSizeBytes > 0);
        }
        else if (string.Equals(status, "MISSING", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(b => b.TotalSizeBytes == 0);
        }

        if (!string.IsNullOrWhiteSpace(cluster) && !cluster.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var clusterParts = cluster.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (clusterParts.Length > 0)
            {
                items = items.Where(b => clusterParts.Any(c => c.Equals(b.Cluster, StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (!string.IsNullOrWhiteSpace(sqlServer) && !sqlServer.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var sqlParts = sqlServer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sqlParts.Length > 0)
            {
                items = items.Where(b => sqlParts.Any(s => ServerNameHelper.IsSameServer(b.SqlServer, s)));
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            items = items.Where(b =>
                b.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SqlDbName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.DataFilesPath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.LogFilesPath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Cluster.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.SqlServer.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        string fileName = $"1C_DBMS_Files_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        return File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(items.ToList(), new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }), "application/json", fileName);
    }

    [HttpPost("files/json")]
    public IActionResult ExportFilesJson([FromBody] List<DatabaseDbmsSummaryRow> selectedItems)
    {
        string fileName = $"1C_DBMS_Files_Selected_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        return File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(selectedItems ?? new List<DatabaseDbmsSummaryRow>(), new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }), "application/json", fileName);
    }

    private async Task<List<DatabaseDbmsSummaryRow>> BuildFilesSummaryRowsAsync(string? environment, CancellationToken cancellationToken)
    {
        string envKey = string.IsNullOrWhiteSpace(environment) ? "ALL" : environment.ToUpperInvariant();
        var diskCache = PersistentCacheHelper.LoadFromDisk<Dictionary<string, List<DatabaseDbmsSummaryRow>>>("dbms_summary_rows.json");
        if (diskCache != null && diskCache.TryGetValue(envKey, out var cachedRows) && cachedRows.Count > 0)
        {
            return cachedRows;
        }

        var baseList = _cache.GetAll();
        if (!string.IsNullOrWhiteSpace(environment) && !environment.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var envParts = environment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (envParts.Length > 0 && !envParts.Contains("ALL", StringComparer.OrdinalIgnoreCase))
            {
                baseList = baseList.Where(b => envParts.Any(e => e.Equals(b.Environment, StringComparison.OrdinalIgnoreCase))).ToList();
            }
        }

        var uniqueSqlServers = baseList
            .Select(b => b.SQL)
            .Where(s => !string.IsNullOrEmpty(s) && !s.Equals("Неизвестно", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var serverFileTasks = uniqueSqlServers.ToDictionary(
            s => s,
            s => _dbmsInspector.GetServerAllDatabaseFilesAsync(s, cancellationToken),
            StringComparer.OrdinalIgnoreCase
        );

        await Task.WhenAll(serverFileTasks.Values);

        var allRows = new List<DatabaseDbmsSummaryRow>();
        foreach (var b in baseList)
        {
            if (string.IsNullOrEmpty(b.SQL) || b.SQL.Equals("Неизвестно", StringComparison.OrdinalIgnoreCase)) continue;

            List<DbmsFileItem>? files = null;
            if (serverFileTasks.TryGetValue(b.SQL, out var task) && task.IsCompletedSuccessfully)
            {
                task.Result.TryGetValue(b.SQLDbName, out files);
            }

            if (files == null)
            {
                var matchedTask = serverFileTasks.FirstOrDefault(kvp => ServerNameHelper.IsSameServer(kvp.Key, b.SQL)).Value;
                if (matchedTask != null && matchedTask.IsCompletedSuccessfully)
                {
                    matchedTask.Result.TryGetValue(b.SQLDbName, out files);
                }
            }

            if (files != null && files.Count > 0)
            {
                var dataFiles = files.Where(f => !f.FileType.Equals("LOG", StringComparison.OrdinalIgnoreCase)).ToList();
                var logFiles = files.Where(f => f.FileType.Equals("LOG", StringComparison.OrdinalIgnoreCase)).ToList();

                string dataPaths = string.Join("; ", dataFiles.Select(f => f.PhysicalPath).Where(p => !string.IsNullOrEmpty(p)));
                string logPaths = string.Join("; ", logFiles.Select(f => f.PhysicalPath).Where(p => !string.IsNullOrEmpty(p)));

                allRows.Add(new DatabaseDbmsSummaryRow
                {
                    Environment = b.Environment,
                    Name = b.Name,
                    Cluster = b.Cluster,
                    SqlServer = b.SQL,
                    SqlDbName = b.SQLDbName,
                    TotalSizeBytes = files.Sum(f => f.SizeBytes),
                    DataFilesPath = !string.IsNullOrEmpty(dataPaths) ? dataPaths : "—",
                    LogFilesPath = !string.IsNullOrEmpty(logPaths) ? logPaths : "—"
                });
            }
            else
            {
                allRows.Add(new DatabaseDbmsSummaryRow
                {
                    Environment = b.Environment,
                    Name = b.Name,
                    Cluster = b.Cluster,
                    SqlServer = b.SQL,
                    SqlDbName = b.SQLDbName,
                    TotalSizeBytes = 0,
                    DataFilesPath = "—",
                    LogFilesPath = "—"
                });
            }
        }

        return allRows;
    }

    private IActionResult GenerateExcelFile(IReadOnlyList<InfoBaseItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:html=\"http://www.w3.org/TR/REC-html40\">");

        // Styles
        sb.AppendLine(" <Styles>");
        sb.AppendLine("  <Style ss:ID=\"Default\" ss:Name=\"Normal\">");
        sb.AppendLine("   <Alignment ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Color=\"#000000\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Header\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/>");
        sb.AppendLine("   <Interior ss:Color=\"#1E1E1E\" ss:Pattern=\"Solid\"/>");
        sb.AppendLine("   <Borders>");
        sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#333333\"/>");
        sb.AppendLine("   </Borders>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Prod\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9\" ss:Bold=\"1\" ss:Color=\"#D46B38\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Dev\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9\" ss:Bold=\"1\" ss:Color=\"#4A809B\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Code\">");
        sb.AppendLine("   <Font ss:FontName=\"Consolas\" ss:Size=\"9.5\" ss:Color=\"#222222\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine(" </Styles>");

        sb.AppendLine(" <Worksheet ss:Name=\"Базы 1С\">");
        sb.AppendLine("  <Table>");
        sb.AppendLine("   <Column ss:Width=\"35\"/>");  // №
        sb.AppendLine("   <Column ss:Width=\"55\"/>");  // Среда
        sb.AppendLine("   <Column ss:Width=\"150\"/>"); // Имя базы
        sb.AppendLine("   <Column ss:Width=\"220\"/>"); // Описание
        sb.AppendLine("   <Column ss:Width=\"120\"/>"); // Кластер
        sb.AppendLine("   <Column ss:Width=\"85\"/>");  // Платформа
        sb.AppendLine("   <Column ss:Width=\"110\"/>"); // SQL Сервер
        sb.AppendLine("   <Column ss:Width=\"130\"/>"); // База в СУБД
        sb.AppendLine("   <Column ss:Width=\"160\"/>"); // Группа AD
        sb.AppendLine("   <Column ss:Width=\"100\"/>"); // IP сервера
        sb.AppendLine("   <Column ss:Width=\"150\"/>"); // RA-группа
        sb.AppendLine("   <Column ss:Width=\"120\"/>"); // 1C-группа
        sb.AppendLine("   <Column ss:Width=\"120\"/>"); // Ярлык v8i

        // Header Row
        sb.AppendLine("   <Row ss:Height=\"22\" ss:StyleID=\"Header\">");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">№</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Среда</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">База 1С</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Описание</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Кластер 1С</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Платформа</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Сервер СУБД</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">База в СУБД</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Группа доступа AD</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">IP сервера</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">RA-группа</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">1C-группа</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Ярлык v8i</Data></Cell>");
        sb.AppendLine("   </Row>");

        // Data Rows
        int num = 1;
        foreach (var b in items)
        {
            string envStyle = b.Environment.Equals("PROD", StringComparison.OrdinalIgnoreCase) ? " ss:StyleID=\"Prod\"" : " ss:StyleID=\"Dev\"";

            sb.AppendLine("   <Row ss:Height=\"18\">");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{num++}</Data></Cell>");
            sb.AppendLine($"    <Cell{envStyle}><Data ss:Type=\"String\">{XmlEscape(b.Environment)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(b.Name)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(b.Description)}</Data></Cell>");
            sb.AppendLine($"    <Cell ss:StyleID=\"Code\"><Data ss:Type=\"String\">{XmlEscape(b.Cluster)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(b.Platform)}</Data></Cell>");
            sb.AppendLine($"    <Cell ss:StyleID=\"Code\"><Data ss:Type=\"String\">{XmlEscape(b.SQL)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(b.SQLDbName)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(b.AccessGroup)}</Data></Cell>");
            sb.AppendLine($"    <Cell ss:StyleID=\"Code\"><Data ss:Type=\"String\">{XmlEscape(b.ServerIP)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(b.RaGroup)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(b.OneCGroup)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(b.V8iFile)}</Data></Cell>");
            sb.AppendLine("   </Row>");
        }

        sb.AppendLine("  </Table>");
        sb.AppendLine(" </Worksheet>");
        sb.AppendLine("</Workbook>");

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        string fileName = $"1C_Databases_{DateTime.Now:yyyyMMdd_HHmmss}.xls";
        return File(bytes, "application/vnd.ms-excel", fileName);
    }

    private IActionResult GenerateFilesExcelFile(IReadOnlyList<DatabaseDbmsSummaryRow> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:html=\"http://www.w3.org/TR/REC-html40\">");

        // Styles
        sb.AppendLine(" <Styles>");
        sb.AppendLine("  <Style ss:ID=\"Default\" ss:Name=\"Normal\">");
        sb.AppendLine("   <Alignment ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Color=\"#000000\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Header\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/>");
        sb.AppendLine("   <Interior ss:Color=\"#1E1E1E\" ss:Pattern=\"Solid\"/>");
        sb.AppendLine("   <Borders>");
        sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#333333\"/>");
        sb.AppendLine("   </Borders>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Prod\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9\" ss:Bold=\"1\" ss:Color=\"#D46B38\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Dev\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9\" ss:Bold=\"1\" ss:Color=\"#4A809B\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Code\">");
        sb.AppendLine("   <Font ss:FontName=\"Consolas\" ss:Size=\"9.5\" ss:Color=\"#222222\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Size\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Right\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Bold=\"1\" ss:Color=\"#2E7D32\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine(" </Styles>");

        sb.AppendLine(" <Worksheet ss:Name=\"Файлы и размеры СУБД\">");
        sb.AppendLine("  <Table>");
        sb.AppendLine("   <Column ss:Width=\"35\"/>");  // №
        sb.AppendLine("   <Column ss:Width=\"55\"/>");  // Среда
        sb.AppendLine("   <Column ss:Width=\"160\"/>"); // База 1С
        sb.AppendLine("   <Column ss:Width=\"140\"/>"); // Кластер 1С
        sb.AppendLine("   <Column ss:Width=\"130\"/>"); // Сервер СУБД
        sb.AppendLine("   <Column ss:Width=\"140\"/>"); // База в СУБД
        sb.AppendLine("   <Column ss:Width=\"110\"/>"); // Общий размер
        sb.AppendLine("   <Column ss:Width=\"320\"/>"); // Файлы данных
        sb.AppendLine("   <Column ss:Width=\"260\"/>"); // Файл журнала

        // Header Row
        sb.AppendLine("   <Row ss:Height=\"22\" ss:StyleID=\"Header\">");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">№</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Среда</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">База 1С</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Кластер 1С</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Сервер СУБД</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">База в СУБД</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Общий размер (GB)</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Файлы данных (MDF / NDF)</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Файл журнала (LDF)</Data></Cell>");
        sb.AppendLine("   </Row>");

        // Data Rows
        int num = 1;
        foreach (var f in items)
        {
            string envStyle = f.Environment.Equals("PROD", StringComparison.OrdinalIgnoreCase) ? " ss:StyleID=\"Prod\"" : " ss:StyleID=\"Dev\"";

            sb.AppendLine("   <Row ss:Height=\"18\">");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{num++}</Data></Cell>");
            sb.AppendLine($"    <Cell{envStyle}><Data ss:Type=\"String\">{XmlEscape(f.Environment)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(f.Name)}</Data></Cell>");
            sb.AppendLine($"    <Cell ss:StyleID=\"Code\"><Data ss:Type=\"String\">{XmlEscape(f.Cluster)}</Data></Cell>");
            sb.AppendLine($"    <Cell ss:StyleID=\"Code\"><Data ss:Type=\"String\">{XmlEscape(f.SqlServer)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(f.SqlDbName)}</Data></Cell>");
            sb.AppendLine($"    <Cell ss:StyleID=\"Size\"><Data ss:Type=\"Number\">{f.TotalSizeGb}</Data></Cell>");
            sb.AppendLine($"    <Cell ss:StyleID=\"Code\"><Data ss:Type=\"String\">{XmlEscape(f.DataFilesPath)}</Data></Cell>");
            sb.AppendLine($"    <Cell ss:StyleID=\"Code\"><Data ss:Type=\"String\">{XmlEscape(f.LogFilesPath)}</Data></Cell>");
            sb.AppendLine("   </Row>");
        }

        sb.AppendLine("  </Table>");
        sb.AppendLine(" </Worksheet>");
        sb.AppendLine("</Workbook>");

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        string fileName = $"1C_DBMS_Files_{DateTime.Now:yyyyMMdd_HHmmss}.xls";
        return File(bytes, "application/vnd.ms-excel", fileName);
    }

    [HttpPost("adgroup/excel")]
    public IActionResult ExportAdGroupExcel([FromBody] ExportAdGroupRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.GroupName))
        {
            return BadRequest(new { Message = "Данные группы AD не переданы" });
        }

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:html=\"http://www.w3.org/TR/REC-html40\">");

        // Styles
        sb.AppendLine(" <Styles>");
        sb.AppendLine("  <Style ss:ID=\"Default\" ss:Name=\"Normal\">");
        sb.AppendLine("   <Alignment ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Color=\"#000000\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Title\">");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"13\" ss:Bold=\"1\" ss:Color=\"#1E1E1E\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"SubTitle\">");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Italic=\"1\" ss:Color=\"#666666\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Header\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/>");
        sb.AppendLine("   <Interior ss:Color=\"#1E1E1E\" ss:Pattern=\"Solid\"/>");
        sb.AppendLine("   <Borders>");
        sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#333333\"/>");
        sb.AppendLine("   </Borders>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"StatusActive\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Bold=\"1\" ss:Color=\"#2E7D32\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"StatusDisabled\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Bold=\"1\" ss:Color=\"#D84315\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"StatusGroup\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Color=\"#666666\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Code\">");
        sb.AppendLine("   <Font ss:FontName=\"Consolas\" ss:Size=\"9.5\" ss:Color=\"#222222\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine(" </Styles>");

        string sheetName = XmlEscape(request.GroupName);
        if (sheetName.Length > 30) sheetName = sheetName[..30];

        sb.AppendLine($" <Worksheet ss:Name=\"{sheetName}\">");
        sb.AppendLine("  <Table>");
        sb.AppendLine("   <Column ss:Width=\"35\"/>");  // №
        sb.AppendLine("   <Column ss:Width=\"220\"/>"); // ФИО
        sb.AppendLine("   <Column ss:Width=\"150\"/>"); // SAM
        sb.AppendLine("   <Column ss:Width=\"220\"/>"); // Должность
        sb.AppendLine("   <Column ss:Width=\"240\"/>"); // Отдел
        sb.AppendLine("   <Column ss:Width=\"220\"/>"); // Email
        sb.AppendLine("   <Column ss:Width=\"90\"/>");  // Статус

        // Title Info
        sb.AppendLine("   <Row ss:Height=\"24\">");
        sb.AppendLine($"    <Cell ss:MergeAcross=\"6\" ss:StyleID=\"Title\"><Data ss:Type=\"String\">Группа безопасности AD: {XmlEscape(request.GroupName)} ({request.Members.Count} участников)</Data></Cell>");
        sb.AppendLine("   </Row>");
        if (!string.IsNullOrEmpty(request.Description))
        {
            sb.AppendLine("   <Row ss:Height=\"18\">");
            sb.AppendLine($"    <Cell ss:MergeAcross=\"6\" ss:StyleID=\"SubTitle\"><Data ss:Type=\"String\">{XmlEscape(request.Description)}</Data></Cell>");
            sb.AppendLine("   </Row>");
        }
        sb.AppendLine("   <Row ss:Height=\"8\"/>");

        // Header Row
        sb.AppendLine("   <Row ss:Height=\"22\" ss:StyleID=\"Header\">");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">№</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">ФИО / Имя</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Логин (SAM)</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Должность</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Отдел</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Email</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Статус</Data></Cell>");
        sb.AppendLine("   </Row>");

        // Data Rows
        int num = 1;
        foreach (var m in request.Members)
        {
            string statusStyle = m.IsGroup ? " ss:StyleID=\"StatusGroup\"" : (m.Enabled ? " ss:StyleID=\"StatusActive\"" : " ss:StyleID=\"StatusDisabled\"");
            string statusText = m.IsGroup ? "Группа" : (m.Enabled ? "Активен" : "Отключен");

            sb.AppendLine("   <Row ss:Height=\"18\">");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{num++}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(m.DisplayName)}</Data></Cell>");
            sb.AppendLine($"    <Cell ss:StyleID=\"Code\"><Data ss:Type=\"String\">{XmlEscape(m.SamAccountName)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(m.Title)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(m.Department)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(m.Email)}</Data></Cell>");
            sb.AppendLine($"    <Cell{statusStyle}><Data ss:Type=\"String\">{statusText}</Data></Cell>");
            sb.AppendLine("   </Row>");
        }

        sb.AppendLine("  </Table>");
        sb.AppendLine(" </Worksheet>");
        sb.AppendLine("</Workbook>");

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        string fileName = $"AD_Group_{request.GroupName}_{DateTime.Now:yyyyMMdd_HHmmss}.xls";
        return File(bytes, "application/vnd.ms-excel", fileName);
    }

    [HttpPost("details/excel")]
    public IActionResult ExportDatabaseDetailsExcel([FromBody] ExportDatabaseDetailsRequest request)
    {
        if (request == null || request.Database == null)
        {
            return BadRequest(new { Message = "Данные базы данных не переданы" });
        }

        var b = request.Database;
        var details = request.DbmsDetails;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:html=\"http://www.w3.org/TR/REC-html40\">");

        // Styles
        sb.AppendLine(" <Styles>");
        sb.AppendLine("  <Style ss:ID=\"Default\" ss:Name=\"Normal\">");
        sb.AppendLine("   <Alignment ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Color=\"#000000\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Title\">");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"13\" ss:Bold=\"1\" ss:Color=\"#1E1E1E\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Header\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/>");
        sb.AppendLine("   <Interior ss:Color=\"#1E1E1E\" ss:Pattern=\"Solid\"/>");
        sb.AppendLine("   <Borders>");
        sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#333333\"/>");
        sb.AppendLine("   </Borders>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"ParamName\">");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Bold=\"1\" ss:Color=\"#555555\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"ParamValue\">");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Color=\"#111111\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Code\">");
        sb.AppendLine("   <Font ss:FontName=\"Consolas\" ss:Size=\"9.5\" ss:Color=\"#222222\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style ss:ID=\"Size\">");
        sb.AppendLine("   <Alignment ss:Horizontal=\"Right\" ss:Vertical=\"Center\"/>");
        sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Bold=\"1\" ss:Color=\"#2E7D32\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine(" </Styles>");

        // --- SHEET 1: Свойства 1С ---
        sb.AppendLine(" <Worksheet ss:Name=\"Свойства базы 1С\">");
        sb.AppendLine("  <Table>");
        sb.AppendLine("   <Column ss:Width=\"220\"/>");
        sb.AppendLine("   <Column ss:Width=\"450\"/>");

        void AddParamRow(string label, string? value, bool isCode = false)
        {
            string style = isCode ? " ss:StyleID=\"Code\"" : " ss:StyleID=\"ParamValue\"";
            sb.AppendLine("   <Row ss:Height=\"20\">");
            sb.AppendLine($"    <Cell ss:StyleID=\"ParamName\"><Data ss:Type=\"String\">{XmlEscape(label)}</Data></Cell>");
            sb.AppendLine($"    <Cell{style}><Data ss:Type=\"String\">{XmlEscape(value ?? "—")}</Data></Cell>");
            sb.AppendLine("   </Row>");
        }

        sb.AppendLine("   <Row ss:Height=\"24\">");
        sb.AppendLine($"    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"Title\"><Data ss:Type=\"String\">Информационная база: {XmlEscape(b.Name)} ({XmlEscape(b.Environment)})</Data></Cell>");
        sb.AppendLine("   </Row>");
        sb.AppendLine("   <Row ss:Height=\"6\"/>");

        AddParamRow("Имя базы в 1С", b.Name);
        AddParamRow("Описание", b.Description);
        AddParamRow("Среда окружения", b.Environment);
        AddParamRow("Кластер серверов 1С", b.Cluster, true);
        AddParamRow("IP-адрес сервера 1С", b.ServerIP);
        AddParamRow("UUID базы в кластере", b.UUID, true);
        AddParamRow("UUID кластера", b.ClusterUUID, true);
        AddParamRow("Версия платформы", b.Platform);
        AddParamRow("Имя службы 1С", b.ServiceName);
        AddParamRow("Пользователь службы", b.ServiceUser);
        AddParamRow("Каталог кластера (-d)", b.ClusterPath, true);
        AddParamRow("Сервер СУБД", b.SQL, true);
        AddParamRow("База данных в СУБД", b.SQLDbName, true);
        AddParamRow("Основная группа доступа AD", b.AccessGroup);
        AddParamRow("RA-группа (RemoteApp)", b.RaGroup);
        AddParamRow("1С-группа платформы", b.OneCGroup);
        AddParamRow("Файл ярлыка v8i", b.V8iFile, true);
        AddParamRow("Регистрация в Consul", b.Consul);

        if (details != null)
        {
            AddParamRow("Тип СУБД", details.DbmsType);
            AddParamRow("Статус базы в СУБД", details.State);
            AddParamRow("Владелец (Owner)", details.Owner);
            AddParamRow("Модель восстановления", details.RecoveryModel);
            AddParamRow("Кодировка (Collation)", details.Collation);
            AddParamRow("Общий размер базы СУБД", $"{details.TotalSizeGb} GB ({details.TotalSizeMb} MB)");
            AddParamRow("Дата последней копии", details.LastBackupDate.HasValue ? details.LastBackupDate.Value.ToString("dd.MM.yyyy HH:mm:ss") : "Не обнаружена");
        }

        sb.AppendLine("  </Table>");
        sb.AppendLine(" </Worksheet>");

        // --- SHEET 2: Файлы СУБД ---
        if (details?.Files != null && details.Files.Count > 0)
        {
            sb.AppendLine(" <Worksheet ss:Name=\"Файлы СУБД\">");
            sb.AppendLine("  <Table>");
            sb.AppendLine("   <Column ss:Width=\"35\"/>");
            sb.AppendLine("   <Column ss:Width=\"180\"/>");
            sb.AppendLine("   <Column ss:Width=\"90\"/>");
            sb.AppendLine("   <Column ss:Width=\"100\"/>");
            sb.AppendLine("   <Column ss:Width=\"100\"/>");
            sb.AppendLine("   <Column ss:Width=\"450\"/>");

            sb.AppendLine("   <Row ss:Height=\"22\" ss:StyleID=\"Header\">");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">№</Data></Cell>");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">Имя файла</Data></Cell>");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">Тип</Data></Cell>");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">Размер (MB)</Data></Cell>");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">Размер (GB)</Data></Cell>");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">Физический путь</Data></Cell>");
            sb.AppendLine("   </Row>");

            int fNum = 1;
            foreach (var file in details.Files)
            {
                sb.AppendLine("   <Row ss:Height=\"18\">");
                sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{fNum++}</Data></Cell>");
                sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(file.FileName)}</Data></Cell>");
                sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(file.FileType)}</Data></Cell>");
                sb.AppendLine($"    <Cell ss:StyleID=\"Size\"><Data ss:Type=\"Number\">{file.SizeMb}</Data></Cell>");
                sb.AppendLine($"    <Cell ss:StyleID=\"Size\"><Data ss:Type=\"Number\">{file.SizeGb}</Data></Cell>");
                sb.AppendLine($"    <Cell ss:StyleID=\"Code\"><Data ss:Type=\"String\">{XmlEscape(file.PhysicalPath)}</Data></Cell>");
                sb.AppendLine("   </Row>");
            }

            sb.AppendLine("  </Table>");
            sb.AppendLine(" </Worksheet>");
        }

        // --- SHEET 3: Пользователи СУБД ---
        if (details?.Permissions != null && details.Permissions.Count > 0)
        {
            sb.AppendLine(" <Worksheet ss:Name=\"Пользователи СУБД\">");
            sb.AppendLine("  <Table>");
            sb.AppendLine("   <Column ss:Width=\"35\"/>");
            sb.AppendLine("   <Column ss:Width=\"220\"/>");
            sb.AppendLine("   <Column ss:Width=\"150\"/>");
            sb.AppendLine("   <Column ss:Width=\"220\"/>");
            sb.AppendLine("   <Column ss:Width=\"100\"/>");

            sb.AppendLine("   <Row ss:Height=\"22\" ss:StyleID=\"Header\">");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">№</Data></Cell>");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">Пользователь / Принципал</Data></Cell>");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">Тип учетной записи</Data></Cell>");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">Роль / Разрешение</Data></Cell>");
            sb.AppendLine("    <Cell><Data ss:Type=\"String\">Состояние</Data></Cell>");
            sb.AppendLine("   </Row>");

            int pNum = 1;
            foreach (var perm in details.Permissions)
            {
                sb.AppendLine("   <Row ss:Height=\"18\">");
                sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{pNum++}</Data></Cell>");
                sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(perm.PrincipalName)}</Data></Cell>");
                sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(perm.PrincipalType)}</Data></Cell>");
                sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(perm.RoleOrPermission)}</Data></Cell>");
                sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{XmlEscape(perm.State)}</Data></Cell>");
                sb.AppendLine("   </Row>");
            }

            sb.AppendLine("  </Table>");
            sb.AppendLine(" </Worksheet>");
        }

        sb.AppendLine("</Workbook>");

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        string fileName = $"1C_Details_{b.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.xls";
        return File(bytes, "application/vnd.ms-excel", fileName);
    }

    private static string XmlEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

public record ExportAdGroupRequest
{
    public required string GroupName { get; init; }
    public string Description { get; init; } = "";
    public List<AdGroupMember> Members { get; init; } = [];
}

public record ExportDatabaseDetailsRequest
{
    public required InfoBaseItem Database { get; init; }
    public DbmsDetails? DbmsDetails { get; init; }
}
