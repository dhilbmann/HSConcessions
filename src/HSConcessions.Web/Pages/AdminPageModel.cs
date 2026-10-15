using HSConcessions.Web.Auth;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HSConcessions.Web.Pages;

/// <summary>Base for admin pages: current user's access, permission helpers, flash messages.</summary>
public abstract class AdminPageModel : PageModel
{
    protected T Svc<T>() where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();
    protected JsonStore Store => Svc<JsonStore>();
    protected AuditService Audit => Svc<AuditService>();
    protected TimeService Time => Svc<TimeService>();
    public FeatureOptions Features => Svc<FeatureOptions>();

    public UserAccess? Access => CurrentUser.Get(HttpContext);

    public bool Can(string function, string? standId = null) => Svc<PermissionService>().Has(Access, function, standId);

    /// <summary>Function required to use the page at all (checked on every handler).</summary>
    protected virtual string? RequiredFunction => null;

    [TempData] public string? Flash { get; set; }
    [TempData] public string? FlashError { get; set; }

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (RequiredFunction is { } f && !Can(f))
        {
            context.Result = new ForbidResult();
            return;
        }
        var executed = await next();
        if (executed.Exception is ValidationException or NotFoundException or ImageValidationException && !executed.ExceptionHandled)
        {
            // Validation problems redirect back to the page with the message.
            FlashError = executed.Exception!.Message;
            executed.ExceptionHandled = true;
            var req = context.HttpContext.Request;
            executed.Result = new RedirectResult(req.PathBase + req.Path + req.QueryString);
        }
    }

    protected IActionResult Done(string message, object? routeValues = null)
    {
        Flash = message;
        return RedirectToPage(routeValues);
    }

    protected IActionResult Fail(string message, object? routeValues = null)
    {
        FlashError = message;
        return RedirectToPage(routeValues);
    }

    public string Money(decimal? amount) => amount is { } a ? PricingService.Format(a) : "";

    public SiteSettings Settings => _settings ??= Store.Read<SiteSettings>(DataFileNames.Settings);
    private SiteSettings? _settings;

    protected static string NewId(string prefix, string name)
    {
        var slug = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 24) slug = slug[..24].Trim('-');
        return $"{prefix}-{(slug.Length > 0 ? slug + "-" : "")}{Guid.NewGuid().ToString("n")[..6]}";
    }
}
