using System.Security.Claims;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace HSConcessions.Web.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app, AuthOptions auth)
    {
        var providers = AuthSetup.ResolveProviders(auth);

        app.MapGet("/auth/challenge/{scheme}", (string scheme, string? returnUrl) =>
        {
            var match = providers.FirstOrDefault(p => p.Value.IsConfigured && string.Equals(p.Key, scheme, StringComparison.OrdinalIgnoreCase));
            if (match.Key is null) return Results.NotFound();
            return Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, new[] { match.Key });
        }).RequireRateLimiting("login");

        app.MapPost("/admin/logout", async (HttpContext ctx, IAntiforgery af, AuditService audit) =>
        {
            await af.ValidateRequestAsync(ctx);
            var email = ctx.User.FindFirstValue(ClaimTypes.Email);
            if (email is not null) await audit.LogAsync("Logout", "User", email, email, null, email);
            await ctx.SignOutAsync(AuthSetup.CookieScheme);
            return Results.Redirect("/admin/login?signedOut=1");
        });

        // One-click / email sign-in for Development and the public demo (Auth:DevLogin is cleared everywhere else at startup).
        // Goes through the same allowlist check as real sign-ins.
        if (auth.DevLogin.Enabled)
        {
            app.MapPost("/admin/dev-login", async (HttpContext ctx, IAntiforgery af, LoginService login) =>
            {
                await af.ValidateRequestAsync(ctx);
                var form = await ctx.Request.ReadFormAsync();
                var email = form["email"].ToString();
                var principal = await login.CompleteAsync(email, auth.DevLogin.Find(email)?.Label, "DevLogin");
                if (principal is null) return Results.Redirect("/admin/denied");
                await ctx.SignInAsync(AuthSetup.CookieScheme, principal, new AuthenticationProperties { IsPersistent = true });
                return Results.Redirect(SafeReturnUrl(form["returnUrl"]));
            }).RequireRateLimiting("login");
        }
    }

    public static string SafeReturnUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith("/admin", StringComparison.Ordinal) && !url.StartsWith("//") && !url.Contains('\\')
            ? url
            : "/admin";
}
