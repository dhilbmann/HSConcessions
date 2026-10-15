using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin;

public sealed class QrModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.EditSettings;

    public List<QrService.QrTarget> Targets { get; private set; } = new();

    public void OnGet() => Targets = Svc<QrService>().Targets();
}

/// <summary>Printable letter-size sign: logo, caption, QR, and the URL in text.</summary>
public sealed class QrSignModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.EditSettings;

    public QrService.QrTarget Target { get; private set; } = null!;
    public string Svg { get; private set; } = "";
    public SiteView Site { get; private set; } = null!;

    public IActionResult OnGet(string target)
    {
        var t = Svc<QrService>().Find(target);
        if (t is null) return NotFound();
        Target = t;
        Svg = QrService.Svg(t.Url);
        Site = PublicStateService.Site(Settings);
        return Page();
    }
}
