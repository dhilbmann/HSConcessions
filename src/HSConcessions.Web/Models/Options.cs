namespace HSConcessions.Web.Models;

public sealed class StorageOptions
{
    public string DataPath { get; set; } = "./data";
    public string ImagesPath { get; set; } = "./data/images";
    public string LogsPath { get; set; } = "./logs";
    public string BackupsPath { get; set; } = "./backups";
}

public sealed class InstanceOptions
{
    public string Name { get; set; } = "hsconcessions";
}

public sealed class SeedOptions
{
    public string Profile { get; set; } = "generic";
    /// <summary>Folder holding seed profiles. Defaults to "samples" next to the app binaries.</summary>
    public string? SamplesPath { get; set; }
}

/// <summary>
/// Public "try it" demo. Turns on the one-click sign-in buttons outside Development, shows a demo banner,
/// resets all data to the seed profile on a timer, and turns off functions that expose other visitors' data.
/// </summary>
public sealed class DemoOptions
{
    public bool Enabled { get; set; }
    /// <summary>How often everything is put back to the seed profile (minutes, at least 5).</summary>
    public int ResetIntervalMinutes { get; set; } = 1440;

    /// <summary>Functions nobody can use on the demo, because anyone can sign in as System Admin there.</summary>
    public static readonly string[] DisabledFunctions = { "DownloadLogs", "ManageBackups" };
}

public sealed class HostingOptions
{
    /// <summary>
    /// Trust X-Forwarded-* headers from any proxy. Needed on hosting platforms (Render, Fly.io, Azure, etc.)
    /// whose load balancer isn't on localhost; leave off behind a local Caddy.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }
    /// <summary>Public HTTPS port that plain-HTTP requests are redirected to.</summary>
    public int HttpsPort { get; set; } = 443;
}

public sealed class FeatureOptions
{
    public bool Events { get; set; }
    public bool OptionPricing { get; set; }
    public bool StandScopedRoles { get; set; }
}

public sealed class AuthOptions
{
    public List<string> SystemAdmins { get; set; } = new();
    public int CookieDays { get; set; } = 14;
    /// <summary>OIDC providers keyed by scheme name (e.g. "Google", "Microsoft").</summary>
    public Dictionary<string, OidcProviderOptions> Providers { get; set; } = new();
    public DevLoginOptions DevLogin { get; set; } = new();
}

public sealed class OidcProviderOptions
{
    public bool Enabled { get; set; }
    public string DisplayName { get; set; } = "";
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CallbackPath { get; set; } = "";
    /// <summary>Multi-tenant authorities (e.g. Microsoft "common") issue tokens from many issuers.</summary>
    public bool MultiTenant { get; set; }
    public List<string> Scopes { get; set; } = new() { "openid", "email", "profile" };

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Development-only email sign-in for local testing. Ignored outside the Development environment.</summary>
public sealed class DevLoginOptions
{
    public bool Enabled { get; set; }

    /// <summary>One-tap test accounts for the dev sign-in page. They exist only in config, never in users.json.</summary>
    public List<DevPersona> Personas { get; set; } = new();

    /// <summary>Dev sign-in and personas only ever work in the Development environment.</summary>
    public void RestrictTo(bool isDevelopment)
    {
        if (isDevelopment) return;
        Enabled = false;
        Personas.Clear();
    }

    public DevPersona? Find(string? email) =>
        Enabled && !string.IsNullOrWhiteSpace(email)
            ? Personas.FirstOrDefault(p => string.Equals(p.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
            : null;
}

public sealed class DevPersona
{
    public string Label { get; set; } = "";
    public string Email { get; set; } = "";
    public List<string> Roles { get; set; } = new();
}
