using System.Runtime.CompilerServices;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public static class RacParser
{
    private static List<Dictionary<string, string>> ParseBlocks(string output)
    {
        var result = new List<Dictionary<string, string>>();
        if (string.IsNullOrWhiteSpace(output)) return result;

        var span = output.AsSpan();
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int start = 0;
        int length = span.Length;

        while (start < length)
        {
            int nextLineEnd = span[start..].IndexOf('\n');
            int lineLength = nextLineEnd == -1 ? length - start : nextLineEnd;
            var line = span.Slice(start, lineLength).Trim();

            if (line.IsEmpty)
            {
                if (current.Count > 0)
                {
                    result.Add(current);
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            else
            {
                int colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                {
                    var key = line[..colonIdx].Trim().ToString();
                    var val = line[(colonIdx + 1)..].Trim().ToString();
                    current[key] = val;
                }
            }

            if (nextLineEnd == -1) break;
            start += nextLineEnd + 1;
        }

        if (current.Count > 0) result.Add(current);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string Get(Dictionary<string, string> block, string key, string fallback = "")
        => block.TryGetValue(key, out var val) ? val.Trim('"') : fallback;

    public static List<(string UUID, string Name)> ParseClusters(string output)
    {
        var result = new List<(string UUID, string Name)>();
        foreach (var block in ParseBlocks(output))
        {
            var id = Get(block, "cluster");
            var name = Get(block, "name");
            if (!string.IsNullOrEmpty(id))
            {
                result.Add((id, !string.IsNullOrEmpty(name) ? name : id));
            }
        }
        return result;
    }

    public static string ParsePlatformVersion(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "Unknown";
        foreach (var rawLine in output.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty) continue;

            // Platform version e.g. 8.3.25.1445 or 8.5.1.1150
            int dotCount = 0;
            bool isVer = true;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '.') dotCount++;
                else if (!char.IsAsciiDigit(c)) { isVer = false; break; }
            }
            if (isVer && dotCount >= 2)
            {
                return line.ToString();
            }
        }
        return "Unknown";
    }

    public static List<(string UUID, string Name, string Description)> ParseInfobases(string output)
    {
        var result = new List<(string UUID, string Name, string Description)>();
        foreach (var block in ParseBlocks(output))
        {
            var id = Get(block, "infobase");
            var name = Get(block, "name");
            var desc = Get(block, "descr");
            if (!string.IsNullOrEmpty(id))
            {
                result.Add((id, name, desc));
            }
        }
        return result;
    }

    public static (string DbServer, string DbName, string DbmsType) ParseInfobaseDetails(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return ("", "", "");
        string dbServer = "";
        string dbName = "";
        string dbmsType = "";

        foreach (var block in ParseBlocks(output))
        {
            dbServer = Get(block, "db-server");
            dbName = Get(block, "db-name");
            dbmsType = Get(block, "dbms");
            if (!string.IsNullOrEmpty(dbServer) || !string.IsNullOrEmpty(dbName))
                break;
        }

        return (dbServer, dbName, dbmsType);
    }
}
