using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public class RacService : IRacService
{
    private readonly ILogger<RacService> _logger;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentDictionary<string, CachedRacConfig> _configCache = new(StringComparer.OrdinalIgnoreCase);
    private string _racPath;
    private readonly int _timeoutSeconds;

    private record CachedRacConfig(int PatternIdx, string AuthMode, bool ForceQuotes);

    public string RacPath => _racPath;

    public RacService(IOptions<RacConfig> racOptions, ILogger<RacService> logger)
    {
        _logger = logger;
        var config = racOptions.Value;
        _timeoutSeconds = config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 30;
        _semaphore = new SemaphoreSlim(config.MaxConcurrency > 0 ? config.MaxConcurrency : 16);

        _racPath = ResolveRacPath(config.RacPath);
    }

    private string ResolveRacPath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        const string commonRoot = @"C:\Program Files\1cv8";
        if (Directory.Exists(commonRoot))
        {
            try
            {
                var found = Directory.GetFiles(commonRoot, "rac.exe", SearchOption.AllDirectories)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.Directory?.Parent?.Name)
                    .FirstOrDefault();

                if (found != null && File.Exists(found.FullName))
                {
                    _logger.LogInformation("Auto-detected rac.exe at {Path}", found.FullName);
                    return found.FullName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching for rac.exe in {Path}", commonRoot);
            }
        }

        return configuredPath;
    }

    public async Task<RacResult> RunRacAsync(string args, int? timeoutSeconds = null, CancellationToken cancellationToken = default)
    {
        int timeout = timeoutSeconds ?? _timeoutSeconds;
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(_racPath))
            {
                _racPath = ResolveRacPath(_racPath);
                if (!File.Exists(_racPath))
                {
                    return new RacResult("", $"rac.exe not found at '{_racPath}'", -1);
                }
            }

            Encoding encoding;
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                encoding = Encoding.GetEncoding(866);
            }
            catch
            {
                encoding = Encoding.UTF8;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _racPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = encoding,
                    StandardErrorEncoding = encoding
                }
            };

            process.Start();

            var outTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            try
            {
                await process.WaitForExitAsync(cts.Token);
                string stdOut = await outTask;
                string stdErr = await errTask;
                return new RacResult(stdOut, stdErr, process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new RacResult("", $"RAC timeout exceeded ({timeout}s) for args: {args}", -2);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception running RAC with args: {Args}", args);
            return new RacResult("", ex.Message, -1);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string> RunSmartRacAsync(
        string rasAddress,
        string mode,
        string action,
        string? clusterId = null,
        string? adminUser = null,
        string? adminPwd = null,
        CancellationToken cancellationToken = default)
    {
        string cidPart = string.IsNullOrEmpty(clusterId) ? "" : $"--cluster={EscapeRacParam(clusterId)}";

        // 1. Try cached working configuration if present
        if (_configCache.TryGetValue(rasAddress, out var cached))
        {
            var (cachedRes, isNetErr) = await TryWithPatternAsync(rasAddress, mode, action, cidPart, adminUser, adminPwd,
                cached.PatternIdx, cached.AuthMode, cached.ForceQuotes, cancellationToken);

            if (cachedRes != null)
            {
                return cachedRes;
            }

            if (isNetErr) return "";

            _configCache.TryRemove(rasAddress, out _);
        }

        // Ordered auth modes: ClusterOnly is the standard for cluster administration
        var authModesToTry = new List<string>();
        if (!string.IsNullOrEmpty(adminUser))
        {
            authModesToTry.Add("ClusterOnly");
            authModesToTry.Add("Anon");
            authModesToTry.Add("Auth");
            authModesToTry.Add("InfobaseOnly");
        }
        else
        {
            authModesToTry.Add("Anon");
        }

        // Try standard pattern index 0 and 4 first (most common for 1C rac)
        int[] patternOrder = [0, 4, 1, 2, 3];

        foreach (int pIdx in patternOrder)
        {
            foreach (var authMode in authModesToTry)
            {
                var (res, isNetErr) = await TryWithPatternAsync(rasAddress, mode, action, cidPart, adminUser, adminPwd,
                    pIdx, authMode, null, cancellationToken);

                if (res != null)
                {
                    return res;
                }

                if (isNetErr)
                {
                    // Unreachable socket/server -> no need to try other combinations
                    return "";
                }
            }
        }

        _logger.LogDebug("Unable to find working RAC parameter combination for {RasAddress} (mode: {Mode}, action: {Action})",
            rasAddress, mode, action);
        return "";
    }

    private async Task<(string? Output, bool IsNetworkError)> TryWithPatternAsync(
        string rasAddress,
        string mode,
        string action,
        string cidPart,
        string? adminUser,
        string? adminPwd,
        int patternIdx,
        string authMode,
        bool? specificQuotes,
        CancellationToken cancellationToken)
    {
        var quoteOptions = specificQuotes.HasValue ? [specificQuotes.Value] : new[] { false, true };

        foreach (bool forceQuotes in quoteOptions)
        {
            string uParam = adminUser ?? "";
            string pParam = adminPwd ?? "";

            if (forceQuotes)
            {
                uParam = $"\"{uParam.Replace("\"", "\\\"")}\"";
                pParam = $"\"{pParam.Replace("\"", "\\\"")}\"";
            }
            else
            {
                uParam = EscapeRacParam(uParam);
                pParam = EscapeRacParam(pParam);
            }

            string authPart = "";
            if (authMode == "ClusterOnly")
            {
                authPart = $"--cluster-user={uParam} --cluster-pwd={pParam}";
            }
            else if (authMode == "InfobaseOnly")
            {
                if (mode is "infobase" or "scheduled-job" or "session" or "lock")
                {
                    authPart = $"--infobase-user={uParam} --infobase-pwd={pParam}";
                }
                else
                {
                    continue;
                }
            }
            else if (authMode == "Auth")
            {
                authPart = $"--cluster-user={uParam} --cluster-pwd={pParam}";
                if (mode is "infobase" or "scheduled-job" or "session" or "lock")
                {
                    authPart += $" --infobase-user={uParam} --infobase-pwd={pParam}";
                }
            }

            string args = BuildArgsString(patternIdx, rasAddress, mode, action, cidPart, authPart);
            var result = await RunRacAsync(args, timeoutSeconds: 15, cancellationToken: cancellationToken);

            if (result.Success)
            {
                _configCache[rasAddress] = new CachedRacConfig(patternIdx, authMode, forceQuotes);
                return (result.Output, false);
            }

            if (IsSocketNetworkError(result.ExitCode, result.Error))
            {
                return (null, true);
            }
        }

        return (null, false);
    }

    private static string BuildArgsString(int patternIdx, string r, string m, string a, string c, string u)
    {
        string cNoEq = c.Replace("=", " ");
        string uNoEq = u.Replace("=", " ");

        return patternIdx switch
        {
            0 => $"{r} {m} {a} {c} {u}".Trim(),
            1 => $"{r} {m} {a} {cNoEq} {uNoEq}".Trim(),
            2 => $"{m} {a} {r} {c} {u}".Trim(),
            3 => $"{m} {a} {r} {cNoEq} {uNoEq}".Trim(),
            _ => $"{r} {m} {c} {u} {a}".Trim()
        };
    }

    private static string EscapeRacParam(string? param)
    {
        if (string.IsNullOrEmpty(param)) return "\"\"";
        param = param.Trim();
        if (param.AsSpan().IndexOfAny(" &|><^=!%()") >= 0)
        {
            return $"\"{param.Replace("\"", "\\\"")}\"";
        }
        return param.Replace("\"", "\\\"");
    }

    private static bool IsSocketNetworkError(int exitCode, string err)
    {
        if (exitCode == -2) return true; // timeout
        if (string.IsNullOrWhiteSpace(err)) return false;

        return err.Contains("10061", StringComparison.OrdinalIgnoreCase) ||
               err.Contains("10060", StringComparison.OrdinalIgnoreCase) ||
               err.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ||
               err.Contains("Требуемый адрес для своего контекста неверен", StringComparison.OrdinalIgnoreCase) ||
               err.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase);
    }
}
