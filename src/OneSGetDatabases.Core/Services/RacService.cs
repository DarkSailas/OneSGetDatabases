using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public partial class RacService : IRacService
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
                return new RacResult("", $"RAC timeout exceeded ({timeout}s) for args: {MaskSecrets(args)}", -2);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception running RAC with args: {Args}", MaskSecrets(args));
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
        var result = await RunSmartRacDetailedAsync(rasAddress, mode, action, clusterId, adminUser, adminPwd, cancellationToken);
        return result.Success ? result.Output : "";
    }

    public async Task<RacResult> RunSmartRacDetailedAsync(
        string rasAddress,
        string mode,
        string action,
        string? clusterId = null,
        string? adminUser = null,
        string? adminPwd = null,
        CancellationToken cancellationToken = default)
    {
        string cidPart = string.IsNullOrEmpty(clusterId) ? "" : $"--cluster={EscapeRacParam(clusterId)}";
        RacResult? lastFailure = null;

        // 1. Try cached working configuration if present
        if (_configCache.TryGetValue(rasAddress, out var cached))
        {
            var (cachedRes, isNetErr, failure) = await TryWithPatternAsync(rasAddress, mode, action, cidPart, adminUser, adminPwd,
                cached.PatternIdx, cached.AuthMode, cached.ForceQuotes, cancellationToken);

            if (cachedRes != null)
            {
                return cachedRes;
            }

            lastFailure = MoreInformative(lastFailure, failure);
            if (isNetErr) return LogFailure(rasAddress, mode, action, lastFailure, adminPwd);

            _configCache.TryRemove(rasAddress, out _);
        }

        // Ordered auth modes: ClusterOnly is the standard for cluster administration.
        // Agent-level commands ("agent ...", "cluster list") do not accept --cluster-user;
        // they may need the central server administrator (--agent-user) instead.
        bool agentLevel = IsAgentLevelCommand(mode, action);
        var authModesToTry = new List<string>();
        if (agentLevel)
        {
            authModesToTry.Add("Anon");
            if (!string.IsNullOrEmpty(adminUser))
                authModesToTry.Add("AgentOnly");
        }
        else if (!string.IsNullOrEmpty(adminUser))
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
                var (res, isNetErr, failure) = await TryWithPatternAsync(rasAddress, mode, action, cidPart, adminUser, adminPwd,
                    pIdx, authMode, null, cancellationToken);

                if (res != null)
                {
                    return res;
                }

                lastFailure = MoreInformative(lastFailure, failure);
                if (isNetErr)
                {
                    // Unreachable socket/server -> no need to try other combinations
                    return LogFailure(rasAddress, mode, action, lastFailure, adminPwd);
                }
            }
        }

        return LogFailure(rasAddress, mode, action, lastFailure, adminPwd);
    }

    internal static bool IsAgentLevelCommand(string mode, string action)
        => mode.Equals("agent", StringComparison.OrdinalIgnoreCase)
           || (mode.Equals("cluster", StringComparison.OrdinalIgnoreCase)
               && action.TrimStart().StartsWith("list", StringComparison.OrdinalIgnoreCase));

    // A parameter parse error only says that this argument layout is wrong; the real reason
    // (authentication, missing cluster) comes from another attempt and must not be overwritten
    internal static bool IsParseError(RacResult? r)
    {
        if (r == null) return false;
        string text = r.Error + " " + r.Output;
        return text.Contains("Ошибка разбора параметра", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Неизвестный параметр", StringComparison.OrdinalIgnoreCase)
               || text.Contains("parse error", StringComparison.OrdinalIgnoreCase)
               || text.Contains("unknown option", StringComparison.OrdinalIgnoreCase)
               || text.Contains("unknown parameter", StringComparison.OrdinalIgnoreCase);
    }

    internal static RacResult? MoreInformative(RacResult? current, RacResult? candidate)
    {
        if (candidate == null) return current;
        if (current == null) return candidate;
        if (IsParseError(current) && !IsParseError(candidate)) return candidate;
        return IsParseError(candidate) ? current : candidate;
    }

    // Failures used to be silent (empty output looked like an empty cluster); keep the reason in the service log
    private RacResult LogFailure(string rasAddress, string mode, string action, RacResult? failure, string? adminPwd)
    {
        string raw = failure == null ? "нет ответа" : (string.IsNullOrWhiteSpace(failure.Error) ? failure.Output : failure.Error);
        string reason = MaskSecrets(string.Join(" / ", raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        if (!string.IsNullOrEmpty(adminPwd))
            reason = reason.Replace(adminPwd, "***", StringComparison.Ordinal);

        _logger.LogWarning("rac {Mode} {Action} на {RasAddress} не выполнен: {Reason}", mode, MaskSecrets(action), rasAddress, reason);
        return new RacResult("", string.IsNullOrWhiteSpace(reason) ? "нет ответа" : reason, failure?.ExitCode is int code && code != 0 ? code : -1);
    }

    private async Task<(RacResult? Success, bool IsNetworkError, RacResult? LastFailure)> TryWithPatternAsync(
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
        RacResult? lastFailure = null;

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
            if (authMode == "AgentOnly")
            {
                authPart = $"--agent-user={uParam} --agent-pwd={pParam}";
            }
            else if (authMode == "ClusterOnly")
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
            var result = await RunRacAsync(args, timeoutSeconds: _timeoutSeconds, cancellationToken: cancellationToken);

            // A busy RAS (many clusters on one host) may answer slowly: one retry with a doubled timeout
            if (result.ExitCode == -2)
            {
                _logger.LogInformation("rac {Mode} {Action} на {RasAddress}: таймаут {Timeout} с, повтор с увеличенным таймаутом",
                    mode, MaskSecrets(action), rasAddress, _timeoutSeconds);
                result = await RunRacAsync(args, timeoutSeconds: _timeoutSeconds * 2, cancellationToken: cancellationToken);
            }

            if (result.Success)
            {
                _configCache[rasAddress] = new CachedRacConfig(patternIdx, authMode, forceQuotes);
                return (result, false, null);
            }

            lastFailure = MoreInformative(lastFailure, result);
            if (IsSocketNetworkError(result.ExitCode, result.Error))
            {
                return (null, true, lastFailure);
            }
        }

        return (null, false, lastFailure);
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

    [GeneratedRegex(@"(--[\w-]*pwd[= ])(""(?:\\.|[^""])*""|\S*)", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordArgRegex();

    private static string MaskSecrets(string args) => PasswordArgRegex().Replace(args, "$1***");

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
