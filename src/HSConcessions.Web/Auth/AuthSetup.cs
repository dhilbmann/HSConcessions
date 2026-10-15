using System.Security.Claims;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace HSConcessions.Web.Auth;

public static class AuthSetup
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Well-known defaults for built-in providers; any field can be overridden in config.</summary>
    private static readonly Dictionary<string, OidcProviderOptions> KnownProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google"] = new() { DisplayName = "Google", Authority = "https://accounts.google.com", CallbackPath = "/signin-google" },
        ["Microsoft"] = new() { DisplayName = "Microsoft", Authority = "https://login.microsoftonline.com/common/v2.0", CallbackPath = "/signin-microsoft", MultiTenant = true },
    };

    /// <summary>Merges config over the known-provider defaults. Add a new OIDC provider by adding a config section.</summary>
    public static Dictionary<string, OidcProviderOptions> ResolveProviders(AuthOptions auth)
    {
        var result = new Dictionary<string, OidcProviderOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, cfg) in auth.Providers)
        {
            KnownProviders.TryGetValue(name, out var known);
            result[name] = new OidcProviderOptions
            {
                Enabled = cfg.Enabled,
                ClientId = cfg.ClientId,
                ClientSecret = cfg.ClientSecret,
                DisplayName = string.IsNullOrWhiteSpace(cfg.DisplayName) ? known?.DisplayName ?? name : cfg.DisplayName,
                Authority = string.IsNullOrWhiteSpace(cfg.Authority) ? known?.Authority ?? "" : cfg.Authority,
                CallbackPath = string.IsNullOrWhiteSpace(cfg.CallbackPath) ? known?.CallbackPath ?? "/signin-" + name.ToLowerInvariant() : cfg.CallbackPath,
                MultiTenant = cfg.MultiTenant || (known?.MultiTenant ?? false),
                Scopes = cfg.Scopes is { Count: > 0 } ? cfg.Scopes : new List<string> { "openid", "email", "profile" },
            };
        }
        return result;
    }

    public static void AddAppAuthentication(this WebApplicationBuilder builder, AuthOptions auth, InstanceOptions instance)
    {
        var isDev = builder.Environment.IsDevelopment();
        var authBuilder = builder.Services.AddAuthentication(o =>
        {
            o.DefaultScheme = CookieScheme;
            o.DefaultSignInScheme = CookieScheme;
            o.DefaultChallengeScheme = CookieScheme;
        });

        authBuilder.AddCookie(CookieScheme, o =>
        {
            o.Cookie.Name = $".{instance.Name}.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = isDev ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.SlidingExpiration = true;
            o.ExpireTimeSpan = TimeSpan.FromDays(Math.Max(1, auth.CookieDays));
            o.LoginPath = "/admin/login";
            o.LogoutPath = "/admin/logout";
            o.AccessDeniedPath = "/admin/forbidden";
            o.Events.OnRedirectToLogin = ctx => ApiAware(ctx, StatusCodes.Status401Unauthorized);
            o.Events.OnRedirectToAccessDenied = ctx => ApiAware(ctx, StatusCodes.Status403Forbidden);
            o.Events.OnValidatePrincipal = async ctx =>
            {
                // Deactivated or removed users lose access on their next request.
                var perms = ctx.HttpContext.RequestServices.GetRequiredService<PermissionService>();
                var email = ctx.Principal?.FindFirstValue(ClaimTypes.Email);
                if (perms.GetAccess(email) is null)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieScheme);
                }
            };
        });

        foreach (var (scheme, p) in ResolveProviders(auth))
        {
            if (!p.IsConfigured) continue;
            authBuilder.AddOpenIdConnect(scheme, p.DisplayName, o =>
            {
                o.SignInScheme = CookieScheme;
                o.Authority = p.Authority;
                o.ClientId = p.ClientId;
                o.ClientSecret = p.ClientSecret;
                o.CallbackPath = p.CallbackPath;
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.UsePkce = true;
                o.SaveTokens = false;
                o.MapInboundClaims = false;
                o.GetClaimsFromUserInfoEndpoint = true;
                o.Scope.Clear();
                foreach (var s in p.Scopes) o.Scope.Add(s);
                o.CorrelationCookie.SameSite = SameSiteMode.None;
                o.NonceCookie.SameSite = SameSiteMode.None;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                o.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
                if (p.MultiTenant)
                {
                    // The "common" endpoint issues tokens from each user's own tenant.
                    o.TokenValidationParameters.ValidateIssuer = false;
                }
                o.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = ctx => OnTokenValidated(ctx, scheme),
                    OnRemoteFailure = async ctx =>
                    {
                        var log = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<LoginService>>();
                        log.LogWarning("Remote sign-in failure via {Scheme}: {Message}", scheme, ctx.Failure?.Message);
                        var audit = ctx.HttpContext.RequestServices.GetRequiredService<AuditService>();
                        await audit.LogAsync("FailedLogin", "Auth", scheme, "Remote failure", new { reason = ctx.Failure?.Message }, "anonymous");
                        ctx.Response.Redirect("/admin/login?error=failed");
                        ctx.HandleResponse();
                    },
                };
            });
        }

        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        builder.Services.AddAuthorization();
    }

    private static Task ApiAware(RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    private static async Task OnTokenValidated(TokenValidatedContext ctx, string scheme)
    {
        var principal = ctx.Principal!;
        var email = principal.FindFirstValue("email");
        if (string.IsNullOrWhiteSpace(email) && scheme.Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            var upn = principal.FindFirstValue("preferred_username");
            if (upn is not null && upn.Contains('@')) email = upn;
        }
        var verified = principal.FindFirstValue("email_verified");
        var name = principal.FindFirstValue("name");

        var login = ctx.HttpContext.RequestServices.GetRequiredService<LoginService>();
        var result = string.Equals(verified, "false", StringComparison.OrdinalIgnoreCase)
            ? null
            : await login.CompleteAsync(email, name, scheme);
        if (result is null)
        {
            if (string.Equals(verified, "false", StringComparison.OrdinalIgnoreCase))
                await login.RecordFailureAsync(email, scheme, "Email not verified by provider");
            ctx.Response.Redirect("/admin/denied");
            ctx.HandleResponse();
            return;
        }
        ctx.Principal = result;
    }
}

/// <summary>The allowlist check shared by every sign-in path.</summary>
public sealed class LoginService(PermissionService perms, JsonStore store, AuditService audit, ILogger<LoginService> log)
{
    public async Task<ClaimsPrincipal?> CompleteAsync(string? email, string? displayName, string provider)
    {
        email = email?.Trim();
        var access = perms.GetAccess(email);
        if (access is null)
        {
            await RecordFailureAsync(email, provider, "Not on allowlist");
            return null;
        }

        await store.UpdateAsync<UsersFile>(DataFileNames.Users, f =>
        {
            var u = f.Users.FirstOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));
            if (u is null) return;
            u.LastLoginAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(displayName)) u.DisplayName = displayName;
        });
        await audit.LogAsync("Login", "User", access.Email, displayName ?? access.Email, new { provider }, access.Email);

        var claims = new List<Claim> { new(ClaimTypes.Email, access.Email), new(ClaimTypes.Name, displayName ?? access.Email), new("idp", provider) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSetup.CookieScheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    public async Task RecordFailureAsync(string? email, string provider, string reason)
    {
        log.LogWarning("Unauthorized sign-in attempt by {Email} via {Provider}: {Reason}", email ?? "(no email)", provider, reason);
        await audit.LogAsync("FailedLogin", "User", email ?? "(none)", email ?? "(none)", new { provider, reason }, email ?? "anonymous");
    }
}
