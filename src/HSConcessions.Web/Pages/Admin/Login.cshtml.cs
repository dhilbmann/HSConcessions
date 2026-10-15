using HSConcessions.Web.Auth;
using HSConcessions.Web.Models;

namespace HSConcessions.Web.Pages.Admin;

public sealed class LoginModel(AuthOptions auth, DemoOptions demo) : AdminPageModel
{
    public Dictionary<string, OidcProviderOptions> Providers { get; private set; } = new();
    public bool DevLogin => auth.DevLogin.Enabled;
    public bool IsDemo => demo.Enabled;
    public string? DevEmailHint => DevLogin ? auth.SystemAdmins.FirstOrDefault() : null;
    public List<DevPersona> Personas => DevLogin ? auth.DevLogin.Personas : new();
    public bool IsSystemAdmin(string email) => auth.SystemAdmins.Any(a => string.Equals(a, email, StringComparison.OrdinalIgnoreCase));
    public string ReturnUrl { get; private set; } = "/admin";
    public string? Error { get; private set; }
    public bool SignedOut { get; private set; }

    public void OnGet(string? returnUrl, string? error, string? signedOut)
    {
        Providers = AuthSetup.ResolveProviders(auth).Where(p => p.Value.IsConfigured)
            .ToDictionary(p => p.Key, p => p.Value);
        ReturnUrl = AuthEndpoints.SafeReturnUrl(returnUrl);
        Error = error;
        SignedOut = signedOut is not null;
    }
}
