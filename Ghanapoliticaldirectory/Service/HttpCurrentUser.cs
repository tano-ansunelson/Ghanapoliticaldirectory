
using Ghanapoliticaldirectory.Business;
using Ghanapoliticaldirectory.Domain;
//using Ghanapoliticaldirectory.Service.Business;
using System.Security.Claims;

namespace Ghanapoliticaldirectory.Services;

/// <summary>
/// The signed-in user for the current request. Permissions come from the role claims on the cookie,
/// mapped through <see cref="RolePermissions"/>; the services re-check them on every sensitive action.
/// </summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    private IReadOnlySet<string>? _permissions;

    public HttpCurrentUser(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? Principal => _http.HttpContext?.User;
    private bool Signed => Principal?.Identity?.IsAuthenticated == true;

    public string? UserId => Signed ? Principal!.FindFirstValue(ClaimTypes.NameIdentifier) : null;
    public string UserName => Signed ? (Principal!.FindFirstValue("display_name") ?? Principal.Identity!.Name ?? "unknown") : "anonymous";
    public string? IpAddress => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public IReadOnlySet<string> Permissions => _permissions ??= Signed
        ? RolePermissions.For(Principal!.FindAll(ClaimTypes.Role).Select(c => c.Value))
        : new HashSet<string>();
}

/// <summary>Authorization policy names: one per permission.</summary>
public static class Policies
{
    public static string For(string permission) => "perm:" + permission;
}
