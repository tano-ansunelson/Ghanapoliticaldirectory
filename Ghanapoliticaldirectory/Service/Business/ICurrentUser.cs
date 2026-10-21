namespace Ghanapoliticaldirectory.Business;

/// <summary>Who is making the request. The web project supplies this from the signed-in user.</summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string UserName { get; }
    string? IpAddress { get; }
    IReadOnlySet<string> Permissions { get; }
    bool IsAuthenticated => UserId is not null;
}

/// <summary>Used by seeders and background jobs. Holds no permissions, so it can never pass a workflow step.</summary>
public sealed class SystemUser : ICurrentUser
{
    public string? UserId => "system";
    public string UserName => "system";
    public string? IpAddress => null;
    public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
}

/// <summary>Switches. RequireDistinctUsers should stay true in production.</summary>
public sealed class WorkflowOptions
{
    /// <summary>The person who entered a record cannot also verify, approve or publish it.</summary>
    public bool RequireDistinctUsers { get; set; } = true;
}
