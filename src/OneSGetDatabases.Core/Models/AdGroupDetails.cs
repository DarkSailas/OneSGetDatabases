namespace OneSGetDatabases.Core.Models;

public record AdGroupMember
{
    public string SamAccountName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Department { get; init; } = "";
    public string Email { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool IsGroup { get; init; } = false;
}

public record AdGroupDetails
{
    public string GroupName { get; init; } = "";
    public string Description { get; init; } = "";
    public int MemberCount => Members.Count;
    public List<AdGroupMember> Members { get; init; } = [];
    public string? Error { get; init; }
}

public record AdMembershipResult(bool Success, string Message, bool NotFound = false, bool Forbidden = false);

public record AdMemberChangeRequest(string SamAccountName);
