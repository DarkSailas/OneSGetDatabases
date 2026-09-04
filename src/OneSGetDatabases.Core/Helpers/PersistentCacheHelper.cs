using System.Text.Json;

namespace OneSGetDatabases.Core.Helpers;

public static class PersistentCacheHelper
{
    private static readonly string CacheDirectory = Path.Combine(AppContext.BaseDirectory, "cache");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    static PersistentCacheHelper()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }
        catch
        {
            // Ignore directory creation errors
        }
    }

    public static T? LoadFromDisk<T>(string fileName) where T : class
    {
        try
        {
            string filePath = Path.Combine(CacheDirectory, fileName);
            if (!File.Exists(filePath)) return null;

            string json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json)) return null;

            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveToDisk<T>(string fileName, T data)
    {
        _ = Task.Run(() =>
        {
            try
            {
                string filePath = Path.Combine(CacheDirectory, fileName);
                string json = JsonSerializer.Serialize(data, JsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch
            {
                // Non-fatal disk write error
            }
        });
    }
}
