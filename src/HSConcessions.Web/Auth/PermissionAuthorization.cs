using System.Security.Claims;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HSConcessions.Web.Auth;

/// <summary>Policy "Perm:{Function}" requires an allowlisted user whose roles grant that function.</summary>
public sealed class PermissionRequirement(string function) : IAuthorizationRequirement
{
    public string Function { get; } = function;
}

public static class Perm
{
    public const string Prefix = "Perm:";
    public static string Policy(string function) => Prefix + function;
    public const string SystemAdminPolicy = "SystemAdmin";
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Perm.Prefix, StringComparison.Ordinal))
        {
            var policy = new AuthorizationPolicyBuilder(AuthSetup.CookieScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[Perm.Prefix.Length..]))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        if (policyName == Perm.SystemAdminPolicy)
        {
            var policy = new AuthorizationPolicyBuilder(AuthSetup.CookieScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(Perm.SystemAdminPolicy))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        return _fallback.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}

public sealed class PermissionHandler(PermissionService perms, IHttpContextAccessor http) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var access = CurrentUser.Get(http.HttpContext, perms, context.User);
        if (access is null) return Task.CompletedTask;
        if (requirement.Function == Perm.SystemAdminPolicy)
        {
            if (access.IsSystemAdmin) context.Succeed(requirement);
            return Task.CompletedTask;
        }
        if (perms.Has(access, requirement.Function)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Resolves (once per request) the signed-in user's access from users.json + System Admin config.</summary>
public static class CurrentUser
{
    private const string Key = "__userAccess";

    public static UserAccess? Get(HttpContext? ctx, PermissionService perms, ClaimsPrincipal? user = null)
    {
        if (ctx is null) return null;
        if (ctx.Items.TryGetValue(Key, out var cached)) return cached as UserAccess;
        var principal = user ?? ctx.User;
        var access = principal.Identity?.IsAuthenticated == true ? perms.GetAccess(principal.FindFirstValue(ClaimTypes.Email)) : null;
        ctx.Items[Key] = access;
        return access;
    }

    public static UserAccess? Get(HttpContext ctx) =>
        Get(ctx, ctx.RequestServices.GetRequiredService<PermissionService>());
}
