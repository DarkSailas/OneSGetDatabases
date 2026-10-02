using System.Collections.Concurrent;
using System.DirectoryServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSGetDatabases.Core.Helpers;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public class ActiveDirectoryService : IActiveDirectoryService
{
    private const string AdCacheFileName = "ad_groups_cache.json";
    private readonly ILogger<ActiveDirectoryService> _logger;
    private readonly ActiveDirectoryConfig _config;
    private readonly ConcurrentDictionary<string, string> _groupCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DateTime CachedAt, AdGroupDetails Details)> _groupMembersCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _allGroupNames = [];
    private readonly Lock _lock = new();

    public ActiveDirectoryService(IOptions<ActiveDirectoryConfig> config, ILogger<ActiveDirectoryService> logger)
    {
        _logger = logger;
        _config = config.Value;

        // Load pre-existing AD group members cache from disk
        var diskCache = PersistentCacheHelper.LoadFromDisk<Dictionary<string, AdGroupDetails>>(AdCacheFileName);
        if (diskCache != null)
        {
            foreach (var kvp in diskCache)
            {
                _groupMembersCache[kvp.Key] = (DateTime.UtcNow, kvp.Value);
            }
        }
    }

    private DirectoryEntry CreateRootEntry()
    {
        string path = !string.IsNullOrEmpty(_config.LdapServer)
            ? $"LDAP://{_config.LdapServer}"
            : (!string.IsNullOrEmpty(_config.Domain) ? $"LDAP://{_config.Domain}" : "");

        string? user = _config.Username;
        if (!string.IsNullOrEmpty(user) && !user.Contains('\\') && !user.Contains('@') && !string.IsNullOrEmpty(_config.Domain))
        {
            user = $"{_config.Domain}\\{user}";
        }

        return string.IsNullOrEmpty(path)
            ? new DirectoryEntry()
            : (!string.IsNullOrEmpty(user)
                ? new DirectoryEntry(path, user, _config.Password)
                : new DirectoryEntry(path));
    }

    public async Task RefreshCacheAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Pre-loading Active Directory access groups...");
                using var root = CreateRootEntry();

                // Broad filter for 1C related security groups
                using var searcher = new DirectorySearcher(root)
                {
                    Filter = "(|(cn=rdp_1c*)(cn=1cbases*)(cn=1c_*)(cn=*_1c_*))",
                    PageSize = 1000,
                    SearchScope = SearchScope.Subtree
                };

                searcher.PropertiesToLoad.Add("cn");

                using var results = searcher.FindAll();
                var loaded = new List<string>();

                foreach (SearchResult result in results)
                {
                    if (result.Properties.Contains("cn") && result.Properties["cn"].Count > 0)
                    {
                        string? cn = result.Properties["cn"][0]?.ToString();
                        if (!string.IsNullOrWhiteSpace(cn))
                        {
                            _groupCache[cn] = cn;
                            loaded.Add(cn);
                        }
                    }
                }

                lock (_lock)
                {
                    _allGroupNames.Clear();
                    _allGroupNames.AddRange(loaded);
                }

                _logger.LogInformation("Successfully cached {Count} AD groups", loaded.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Active Directory groups: {Message}", ex.Message);
            }
        }, cancellationToken);
    }

    public bool HasGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return false;
        return _groupCache.ContainsKey(groupName);
    }

    public string ResolveAccessGroup(string infobaseName)
    {
        if (string.IsNullOrWhiteSpace(infobaseName)) return "Отсутствует";
        string cleanName = infobaseName.Trim();

        // 1. Direct match rdp_1c_{Name}
        string standard = $"rdp_1c_{cleanName}";
        if (_groupCache.TryGetValue(standard, out var exact)) return exact;

        // 2. Direct match rdp_1c_talant_{Name}
        string talant = $"rdp_1c_talant_{cleanName}";
        if (_groupCache.TryGetValue(talant, out var talantGroup)) return talantGroup;

        // 3. Match 1cbases83_{Name} / 1cbases_{Name}
        string oneCBase = $"1cbases83_{cleanName}";
        if (_groupCache.TryGetValue(oneCBase, out var oneCGroup)) return oneCGroup;

        string oneCBasePlain = $"1cbases_{cleanName}";
        if (_groupCache.TryGetValue(oneCBasePlain, out var oneCPlain)) return oneCPlain;

        // 4. Normalized variations (hyphens to underscores)
        string normName = cleanName.Replace("-", "_");
        if (_groupCache.TryGetValue($"rdp_1c_{normName}", out var normMatch)) return normMatch;
        if (_groupCache.TryGetValue($"rdp_1c_talant_{normName}", out var normTalant)) return normTalant;

        // 5. Look in all cached group names for unique match
        lock (_lock)
        {
            var suffixMatches = _allGroupNames
                .Where(g => g.EndsWith($"_{cleanName}", StringComparison.OrdinalIgnoreCase) ||
                            g.EndsWith($"_{normName}", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (suffixMatches.Count == 1)
            {
                return suffixMatches[0];
            }
            if (suffixMatches.Count > 1)
            {
                var rdpMatch = suffixMatches.FirstOrDefault(g => g.StartsWith("rdp_1c", StringComparison.OrdinalIgnoreCase));
                if (rdpMatch != null) return rdpMatch;
                return suffixMatches[0];
            }

            var partialMatches = _allGroupNames
                .Where(g => g.Contains($"_{cleanName}", StringComparison.OrdinalIgnoreCase) ||
                            g.Contains($"_{normName}", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (partialMatches.Count > 0)
            {
                var rdpPartial = partialMatches.FirstOrDefault(g => g.StartsWith("rdp_1c", StringComparison.OrdinalIgnoreCase));
                if (rdpPartial != null) return rdpPartial;
                return partialMatches[0];
            }
        }

        return "Отсутствует";
    }

    public (string RaGroup, string OneCGroup, string V8iFile) ResolveSaInfoGroups(string infobaseName, string platform)
    {
        string accessGroup = ResolveAccessGroup(infobaseName);
        string raGroup = accessGroup != "Отсутствует" ? accessGroup : $"rdp_1c_{infobaseName}";

        string platformDigits = "";
        foreach (char c in platform)
        {
            if (char.IsAsciiDigit(c))
            {
                platformDigits += c;
                if (platformDigits.Length == 2) break;
            }
        }
        if (string.IsNullOrEmpty(platformDigits)) platformDigits = "83";

        string expected1CGroup = $"1cbases{platformDigits}_{infobaseName}";
        string oneCGroup;

        if (_groupCache.TryGetValue(expected1CGroup, out var found1C))
        {
            oneCGroup = found1C;
        }
        else if (_groupCache.TryGetValue($"1cbases_{infobaseName}", out var foundPlain1C))
        {
            oneCGroup = foundPlain1C;
        }
        else
        {
            lock (_lock)
            {
                var match = _allGroupNames.FirstOrDefault(g =>
                    g.StartsWith("1cbases", StringComparison.OrdinalIgnoreCase) &&
                    g.EndsWith($"_{infobaseName}", StringComparison.OrdinalIgnoreCase));

                oneCGroup = match ?? "—";
            }
        }

        string v8iFileName = $"{infobaseName}.v8i";
        string fullV8iPath = Path.Combine(_config.V8iBasePath, v8iFileName);
        string v8iResult = v8iFileName;

        try
        {
            if (!File.Exists(fullV8iPath))
            {
                v8iResult = "—";
            }
        }
        catch
        {
            v8iResult = "—";
        }

        return (raGroup, oneCGroup, v8iResult);
    }

    public async Task<AdGroupDetails> GetGroupMembersAsync(string groupName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupName) || groupName == "Отсутствует" || groupName == "—")
        {
            return new AdGroupDetails { GroupName = groupName, Error = "Некорректное имя группы" };
        }

        // Instant Cache Hit (< 1ms)
        if (_groupMembersCache.TryGetValue(groupName, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 30)
        {
            return cached.Details;
        }

        return await Task.Run(() =>
        {
            try
            {
                using var root = CreateRootEntry();
                using var searcher = new DirectorySearcher(root)
                {
                    Filter = $"(&(objectClass=group)(|(cn={EscapeLdapFilter(groupName)})(sAMAccountName={EscapeLdapFilter(groupName)})))",
                    SearchScope = SearchScope.Subtree
                };

                searcher.PropertiesToLoad.Add("distinguishedName");
                searcher.PropertiesToLoad.Add("description");
                searcher.PropertiesToLoad.Add("member");

                var groupResult = searcher.FindOne();
                if (groupResult == null)
                {
                    return new AdGroupDetails
                    {
                        GroupName = groupName,
                        Error = $"Группа безопасности '{groupName}' не найдена в Active Directory"
                    };
                }

                string desc = groupResult.Properties.Contains("description") && groupResult.Properties["description"].Count > 0
                    ? groupResult.Properties["description"][0]?.ToString() ?? ""
                    : "";

                var membersList = new List<AdGroupMember>();

                if (groupResult.Properties.Contains("member"))
                {
                    var memberDns = new List<string>();
                    foreach (var memberDnObj in groupResult.Properties["member"])
                    {
                        string? memberDn = memberDnObj?.ToString();
                        if (!string.IsNullOrWhiteSpace(memberDn)) memberDns.Add(memberDn);
                    }

                    // Batch query members in chunks of 25 to reduce LDAP network latency
                    foreach (var batch in memberDns.Chunk(25))
                    {
                        try
                        {
                            var filterBuilder = new StringBuilder("(&(objectClass=*)(|");
                            foreach (var dn in batch)
                            {
                                filterBuilder.Append($"(distinguishedName={EscapeLdapFilter(dn)})");
                            }
                            filterBuilder.Append("))");

                            using var memberSearcher = new DirectorySearcher(root)
                            {
                                Filter = filterBuilder.ToString(),
                                SearchScope = SearchScope.Subtree,
                                PageSize = 50
                            };

                            memberSearcher.PropertiesToLoad.Add("distinguishedName");
                            memberSearcher.PropertiesToLoad.Add("sAMAccountName");
                            memberSearcher.PropertiesToLoad.Add("displayName");
                            memberSearcher.PropertiesToLoad.Add("title");
                            memberSearcher.PropertiesToLoad.Add("department");
                            memberSearcher.PropertiesToLoad.Add("mail");
                            memberSearcher.PropertiesToLoad.Add("userAccountControl");
                            memberSearcher.PropertiesToLoad.Add("objectClass");

                            using var results = memberSearcher.FindAll();
                            var foundDns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                            foreach (SearchResult memberResult in results)
                            {
                                string dn = GetProp(memberResult, "distinguishedName");
                                if (!string.IsNullOrEmpty(dn)) foundDns.Add(dn);

                                string sam = GetProp(memberResult, "sAMAccountName");
                                string display = GetProp(memberResult, "displayName");
                                string title = GetProp(memberResult, "title");
                                string dept = GetProp(memberResult, "department");
                                string mail = GetProp(memberResult, "mail");

                                bool isGroup = false;
                                if (memberResult.Properties.Contains("objectClass"))
                                {
                                    foreach (var oc in memberResult.Properties["objectClass"])
                                    {
                                        if (oc?.ToString()?.Equals("group", StringComparison.OrdinalIgnoreCase) == true)
                                        {
                                            isGroup = true;
                                            break;
                                        }
                                    }
                                }

                                if (isGroup)
                                {
                                    continue; // Исключаем вложенные группы безопасности
                                }

                                bool isEnabled = true;
                                if (memberResult.Properties.Contains("userAccountControl") && memberResult.Properties["userAccountControl"].Count > 0)
                                {
                                    if (int.TryParse(memberResult.Properties["userAccountControl"][0]?.ToString(), out int uac))
                                    {
                                        isEnabled = (uac & 2) == 0;
                                    }
                                }

                                membersList.Add(new AdGroupMember
                                {
                                    SamAccountName = sam,
                                    DisplayName = !string.IsNullOrEmpty(display) ? display : sam,
                                    Title = title,
                                    Department = dept,
                                    Email = mail,
                                    Enabled = isEnabled,
                                    IsGroup = false
                                });
                            }

                            // Fallback for DNs not returned in LDAP search (only if not an AD group)
                            foreach (var dn in batch)
                            {
                                if (!foundDns.Contains(dn))
                                {
                                    string cn = ExtractCnFromDn(dn);
                                    if (_groupCache.ContainsKey(cn) || cn.StartsWith("rdp_1c", StringComparison.OrdinalIgnoreCase) || cn.StartsWith("1cbases", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    membersList.Add(new AdGroupMember
                                    {
                                        SamAccountName = cn,
                                        DisplayName = cn,
                                        Enabled = true,
                                        IsGroup = false
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Batch search failed for {Count} members", batch.Length);
                            foreach (var dn in batch)
                            {
                                string cn = ExtractCnFromDn(dn);
                                membersList.Add(new AdGroupMember { SamAccountName = cn, DisplayName = cn, Enabled = true });
                            }
                        }
                    }
                }

                // Sort members alphabetically by DisplayName
                var sortedMembers = membersList
                    .OrderBy(m => m.IsGroup ? 1 : 0)
                    .ThenBy(m => m.DisplayName)
                    .ToList();

                var details = new AdGroupDetails
                {
                    GroupName = groupName,
                    Description = desc,
                    Members = sortedMembers
                };

                _groupMembersCache[groupName] = (DateTime.UtcNow, details);

                // Save snapshot to disk cache asynchronously
                var cacheSnapshot = _groupMembersCache.ToDictionary(k => k.Key, v => v.Value.Details, StringComparer.OrdinalIgnoreCase);
                PersistentCacheHelper.SaveToDisk(AdCacheFileName, cacheSnapshot);

                return details;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching members for AD group {GroupName}", groupName);
                return new AdGroupDetails
                {
                    GroupName = groupName,
                    Error = $"Ошибка обращения к Active Directory: {ex.Message}"
                };
            }
        }, cancellationToken);
    }

    private const int AdsPropertyAppend = 3;
    private const int AdsPropertyDelete = 4;
    private const int HResultAccessDenied = unchecked((int)0x80070005);
    private const int HResultInsufficientRights = unchecked((int)0x80072098); // ERROR_DS_INSUFF_ACCESS_RIGHTS

    // Only 1C access groups (ActiveDirectory:ManageableGroupPatterns) can be modified from the console
    public bool IsManagedGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return false;
        string name = groupName.Trim();
        return _config.ManageableGroupPatterns.Any(p =>
            !string.IsNullOrWhiteSpace(p)
            && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(p.Trim(), name, ignoreCase: true));
    }

    public async Task<IReadOnlyList<AdGroupMember>> SearchUsersAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        string q = (query ?? "").Trim();
        if (q.Length < 2) return [];
        limit = Math.Clamp(limit, 1, 50);

        return await Task.Run<IReadOnlyList<AdGroupMember>>(() =>
        {
            string e = EscapeLdapFilter(q);
            using var root = CreateRootEntry();
            using var searcher = new DirectorySearcher(root)
            {
                Filter = $"(&(objectCategory=person)(objectClass=user)(|(sAMAccountName={e}*)(displayName=*{e}*)(mail={e}*)(sn={e}*)))",
                SearchScope = SearchScope.Subtree,
                SizeLimit = limit
            };
            searcher.PropertiesToLoad.AddRange(["sAMAccountName", "displayName", "title", "department", "mail", "userAccountControl"]);

            using var results = searcher.FindAll();
            var list = new List<AdGroupMember>();
            foreach (SearchResult r in results)
            {
                string sam = GetProp(r, "sAMAccountName");
                if (string.IsNullOrEmpty(sam)) continue;
                string display = GetProp(r, "displayName");
                list.Add(new AdGroupMember
                {
                    SamAccountName = sam,
                    DisplayName = !string.IsNullOrEmpty(display) ? display : sam,
                    Title = GetProp(r, "title"),
                    Department = GetProp(r, "department"),
                    Email = GetProp(r, "mail"),
                    Enabled = IsEnabled(r)
                });
            }

            return list
                .OrderByDescending(m => m.Enabled)
                .ThenBy(m => m.DisplayName)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<AdMembershipResult> AddMemberAsync(string groupName, string samAccountName, CancellationToken cancellationToken = default)
        => ChangeMembershipAsync(groupName, samAccountName, add: true, cancellationToken);

    public Task<AdMembershipResult> RemoveMemberAsync(string groupName, string samAccountName, CancellationToken cancellationToken = default)
        => ChangeMembershipAsync(groupName, samAccountName, add: false, cancellationToken);

    private async Task<AdMembershipResult> ChangeMembershipAsync(string groupName, string samAccountName, bool add, CancellationToken cancellationToken)
    {
        if (!IsManagedGroup(groupName))
            return new AdMembershipResult(false, $"Группа '{groupName}' не относится к группам доступа 1С — изменение запрещено", Forbidden: true);

        string sam = NormalizeSam(samAccountName);
        if (sam.Length == 0 || sam.Length > 104 || sam.IndexOfAny(InvalidSamChars) >= 0)
            return new AdMembershipResult(false, $"Некорректный логин '{samAccountName}'");

        var result = await Task.Run(() =>
        {
            try
            {
                using var root = CreateRootEntry();

                // sAMAccountName is domain-unique; cn is unique only within an OU, so an ambiguous cn is rejected
                string escGroup = EscapeLdapFilter(groupName.Trim());
                var group = FindOne(root, $"(&(objectClass=group)(sAMAccountName={escGroup}))");
                if (group == null)
                {
                    var byCn = FindAll(root, $"(&(objectClass=group)(cn={escGroup}))", 2);
                    if (byCn.Count > 1)
                        return new AdMembershipResult(false, $"В Active Directory несколько групп с именем '{groupName}' — изменение невозможно");
                    group = byCn.FirstOrDefault();
                }
                if (group == null)
                    return new AdMembershipResult(false, $"Группа '{groupName}' не найдена в Active Directory", NotFound: true);

                var user = FindOne(root, $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={EscapeLdapFilter(sam)}))");
                if (user == null)
                    return new AdMembershipResult(false, $"Пользователь '{sam}' не найден в Active Directory", NotFound: true);

                string groupDn = GetProp(group, "distinguishedName");
                string userDn = GetProp(user, "distinguishedName");

                // Server-side membership check: works for large groups where the member attribute is range-limited
                bool isMember = FindOne(root, $"(&(objectClass=group)(distinguishedName={EscapeLdapFilter(groupDn)})(member={EscapeLdapFilter(userDn)}))") != null;
                if (add && isMember)
                    return new AdMembershipResult(true, $"{sam} уже состоит в группе {groupName}");
                if (!add && !isMember)
                    return new AdMembershipResult(true, $"{sam} не состоит в группе {groupName}");

                using var groupEntry = group.GetDirectoryEntry();
                groupEntry.Invoke("PutEx", add ? AdsPropertyAppend : AdsPropertyDelete, "member", new object[] { userDn });
                groupEntry.CommitChanges();

                _logger.LogInformation("AD: {Action} {User} {Direction} группы {Group}",
                    add ? "добавлен" : "удалён", sam, add ? "в состав" : "из состава", groupName);

                return new AdMembershipResult(true, add
                    ? $"{sam} добавлен в группу {groupName}"
                    : $"{sam} удалён из группы {groupName}");
            }
            catch (Exception ex)
            {
                var inner = ex is System.Reflection.TargetInvocationException { InnerException: not null } tie ? tie.InnerException : ex;
                bool accessDenied = inner is UnauthorizedAccessException
                                    || (inner is System.Runtime.InteropServices.COMException com
                                        && (com.HResult == HResultAccessDenied || com.HResult == HResultInsufficientRights
                                            || (com is DirectoryServicesCOMException ds && ds.ExtendedError == 8344)));
                _logger.LogError(inner, "Ошибка изменения состава группы {Group} ({User})", groupName, sam);
                return accessDenied
                    ? new AdMembershipResult(false, "Недостаточно прав учетной записи сервиса на изменение состава группы в Active Directory", Forbidden: true)
                    : new AdMembershipResult(false, $"Ошибка Active Directory: {inner.Message}");
            }
        }, cancellationToken).ConfigureAwait(false);

        if (result.Success)
            InvalidateGroupMembers(groupName);

        return result;
    }

    private void InvalidateGroupMembers(string groupName)
    {
        if (!_groupMembersCache.TryRemove(groupName, out _)) return;
        var cacheSnapshot = _groupMembersCache.ToDictionary(k => k.Key, v => v.Value.Details, StringComparer.OrdinalIgnoreCase);
        PersistentCacheHelper.SaveToDisk(AdCacheFileName, cacheSnapshot);
    }

    private static readonly char[] InvalidSamChars = ['"', '/', '\\', '[', ']', ':', ';', '|', '=', ',', '+', '*', '?', '<', '>', '@', '(', ')'];

    private static string NormalizeSam(string? sam)
    {
        string s = (sam ?? "").Trim();
        int slash = s.LastIndexOf('\\');
        if (slash >= 0) s = s[(slash + 1)..];
        int at = s.IndexOf('@');
        if (at > 0) s = s[..at];
        return s;
    }

    private static SearchResult? FindOne(DirectoryEntry root, string filter)
    {
        using var searcher = new DirectorySearcher(root)
        {
            Filter = filter,
            SearchScope = SearchScope.Subtree
        };
        searcher.PropertiesToLoad.Add("distinguishedName");
        return searcher.FindOne();
    }

    private static List<SearchResult> FindAll(DirectoryEntry root, string filter, int sizeLimit)
    {
        using var searcher = new DirectorySearcher(root)
        {
            Filter = filter,
            SearchScope = SearchScope.Subtree,
            SizeLimit = sizeLimit
        };
        searcher.PropertiesToLoad.Add("distinguishedName");
        using var results = searcher.FindAll();
        return results.Cast<SearchResult>().ToList();
    }

    private static bool IsEnabled(SearchResult r)
    {
        if (r.Properties.Contains("userAccountControl") && r.Properties["userAccountControl"].Count > 0
            && int.TryParse(r.Properties["userAccountControl"][0]?.ToString(), out int uac))
        {
            return (uac & 2) == 0;
        }
        return true;
    }

    private static string GetProp(SearchResult res, string propName)
    {
        if (res.Properties.Contains(propName) && res.Properties[propName].Count > 0)
        {
            return res.Properties[propName][0]?.ToString() ?? "";
        }
        return "";
    }

    private static string ExtractCnFromDn(string dn)
    {
        var parts = dn.Split(',');
        if (parts.Length > 0 && parts[0].StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            return parts[0][3..];
        }
        return dn;
    }

    private static string EscapeLdapFilter(string input)
    {
        return input
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}
