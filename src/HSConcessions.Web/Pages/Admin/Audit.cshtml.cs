using System.Globalization;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin;

public sealed class AuditModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.ViewAudit;
    public const int PageSize = 50;

    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "user")] public string? UserFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? Action { get; set; }
    [BindProperty(SupportsGet = true)] public string? Entity { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public List<AuditRecord> Records { get; private set; } = new();
    public int Total { get; private set; }
    public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);

    public void OnGet()
    {
        if (string.IsNullOrEmpty(From) && string.IsNullOrEmpty(To))
            From = Time.LocalNow.AddDays(-7).ToString("yyyy-MM-dd");
        var all = Svc<AuditService>().Query(new AuditQuery { From = D(From), To = D(To), User = UserFilter, Action = Action, Entity = Entity }, Time.Zone);
        Total = all.Count;
        PageNumber = Math.Clamp(PageNumber, 1, Pages);
        Records = all.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
    }

    private static DateOnly? D(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public string Query(int? page = null) =>
        string.Join("&", new[] { ("from", From), ("to", To), ("user", UserFilter), ("action", Action), ("entity", Entity), ("p", page?.ToString()) }
            .Where(p => !string.IsNullOrEmpty(p.Item2))
            .Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2!)}"));

    public string LocalTime(DateTime utc) => Time.ToLocal(utc).ToString("MMM d, h:mm:ss tt", CultureInfo.GetCultureInfo("en-US"));

    public static readonly string[] ActionTypes =
    {
        "Login", "FailedLogin", "Logout", "OpenStand", "CloseStand", "SetStandTimes", "ApplyMenu", "ReapplyMenu", "SetItemSelling", "SetItemStatus",
        "BulkSetItems", "SetPrice", "ClearPrice", "Create", "Update", "Archive", "Restore", "ReorderCatalog", "SetPicture", "RemovePicture",
        "CreateUser", "UpdateUserRoles", "DeactivateUser", "ReactivateUser", "SetPermission", "UpdateSettings", "UpdateBranding",
        "CreateGame", "UpdateGame", "DeleteGame", "UpdateMenuItems", "Duplicate", "Rename", "DownloadLog", "DownloadLogs", "ExportAudit",
        "CreateBackup", "DownloadBackup", "RestoreBackup", "CreateStand", "UpdateStand",
    };
}
