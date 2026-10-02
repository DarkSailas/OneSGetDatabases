namespace OneSGetDatabases.Core.Models;

/// <summary>Публикация информационной базы 1С на IIS (приложение IIS с default.vrd).</summary>
public record IisPublication
{
    public required string Host { get; init; }
    public required string SiteName { get; init; }
    public required string AppPath { get; init; }
    public required string Url { get; init; }
    public string PhysicalPath { get; init; } = "";

    /// <summary>Пул приложений IIS, в котором работает публикация.</summary>
    public string AppPool { get; init; } = "";

    /// <summary>Серверы из Srvr="..." строки подключения (host или host:port).</summary>
    public List<string> IbServers { get; init; } = [];

    /// <summary>Имя информационной базы из Ref="...".</summary>
    public string IbRef { get; init; } = "";
}

public record IisPublicationsConfig
{
    public bool Enabled { get; init; } = true;

    /// <summary>Серверы IIS помимо хостов кластеров 1С.</summary>
    public List<string> ExtraHosts { get; init; } = [];

    /// <summary>Базовый адрес публикаций для сервера IIS: { "app-dev01": "http://app-dev.service.consul" }.
    /// Без записи адрес строится из привязки сайта IIS.</summary>
    public Dictionary<string, string> BaseUrls { get; init; } = [];

    /// <summary>Путь к applicationHost.config относительно системного диска сервера.</summary>
    public string ApplicationHostConfigPath { get; init; } = @"Windows\System32\inetsrv\config\applicationHost.config";

    public int TimeoutSecondsPerHost { get; init; } = 60;
}
