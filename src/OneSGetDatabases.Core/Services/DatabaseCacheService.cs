using System.Collections.Concurrent;
using OneSGetDatabases.Core.Helpers;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public class DatabaseCacheService : IDatabaseCacheService
{
    private const string CacheFileName = "infobases_cache.json";
    private readonly ConcurrentDictionary<string, InfoBaseItem> _items = new(StringComparer.OrdinalIgnoreCase);
    public DateTime LastScanTime { get; private set; } = DateTime.MinValue;
    public DateTime LastConfluenceSyncTime { get; private set; } = DateTime.MinValue;

    public DatabaseCacheService()
    {
        // Immediately pre-populate from disk cache for instant 0ms startup
        var saved = PersistentCacheHelper.LoadFromDisk<List<InfoBaseItem>>(CacheFileName);
        if (saved != null && saved.Count > 0)
        {
            foreach (var b in saved)
            {
                _items[b.UniqueKey] = b;
            }
            LastScanTime = DateTime.UtcNow;
        }
    }

    public IReadOnlyList<InfoBaseItem> GetAll()
    {
        return _items.Values.OrderBy(b => b.Environment).ThenBy(b => b.Name).ToList();
    }

    public IReadOnlyList<InfoBaseItem> GetDev()
    {
        return _items.Values.Where(b => b.Environment.Equals("DEV", StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.Name).ToList();
    }

    public IReadOnlyList<InfoBaseItem> GetProd()
    {
        return _items.Values.Where(b => b.Environment.Equals("PROD", StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.Name).ToList();
    }

    public InfoBaseItem? Find(string environment, string cluster, string name)
    {
        string key = $"{environment}_{cluster}_{name}";
        _items.TryGetValue(key, out var item);
        return item;
    }

    public void Update(List<InfoBaseItem> allBases)
    {
        _items.Clear();
        foreach (var b in allBases)
        {
            _items[b.UniqueKey] = b;
        }
        LastScanTime = DateTime.UtcNow;
        PersistentCacheHelper.SaveToDisk(CacheFileName, allBases);
    }

    public void MarkConfluenceSyncCompleted()
    {
        LastConfluenceSyncTime = DateTime.UtcNow;
    }
}
