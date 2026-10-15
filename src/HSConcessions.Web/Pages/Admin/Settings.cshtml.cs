using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin;

public sealed class SettingsModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.EditSettings;

    public sealed class Input
    {
        public string SiteBaseUrl { get; set; } = "";
        public string SiteTitle { get; set; } = "";
        public string SiteShortName { get; set; } = "";
        public string SchoolName { get; set; } = "";
        public string MascotName { get; set; } = "";
        public string? Tagline { get; set; }
        public string TimeZone { get; set; } = "";
        public int OpeningSoonMinutes { get; set; }
        public int CategorySeconds { get; set; }
        public int DisplayRefreshSeconds { get; set; }
        public int PublicRefreshSeconds { get; set; }
        public int UpcomingGamesCount { get; set; }
        public string DisplayLayout { get; set; } = DisplayLayouts.Rotate;
        public bool GroupVarieties { get; set; }
        public bool ShowClock { get; set; }
        public bool ShowUpdatedTime { get; set; }
    }

    [BindProperty] public Input Form { get; set; } = new();

    public void OnGet()
    {
        var s = Settings;
        Form = new Input
        {
            SiteBaseUrl = s.SiteBaseUrl, SiteTitle = s.SiteTitle, SiteShortName = s.SiteShortName, SchoolName = s.SchoolName,
            MascotName = s.MascotName, Tagline = s.Tagline, TimeZone = s.TimeZone,
            OpeningSoonMinutes = s.OpeningSoonMinutes, CategorySeconds = s.Display.CategorySeconds,
            DisplayRefreshSeconds = s.Display.RefreshSeconds, PublicRefreshSeconds = s.Public.RefreshSeconds,
            UpcomingGamesCount = s.Public.UpcomingGamesCount,
            DisplayLayout = s.Display.Layout, GroupVarieties = s.Display.GroupVarieties,
            ShowClock = s.Display.ShowClock, ShowUpdatedTime = s.Display.ShowUpdatedTime,
        };
    }

    public static IEnumerable<string> TimeZones() =>
        new[] { "America/New_York", "America/Chicago", "America/Denver", "America/Phoenix", "America/Los_Angeles", "America/Anchorage", "Pacific/Honolulu" };

    public async Task<IActionResult> OnPostAsync()
    {
        var f = Form;
        if (!Uri.TryCreate(f.SiteBaseUrl?.Trim(), UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https"))
            throw new ValidationException("Site base URL must be a full URL like https://example.com");
        if (string.IsNullOrWhiteSpace(f.SiteTitle) || string.IsNullOrWhiteSpace(f.SiteShortName))
            throw new ValidationException("Site title and short name are required.");
        try { TimeZoneInfo.FindSystemTimeZoneById(f.TimeZone); }
        catch { throw new ValidationException("Unknown time zone. Use an IANA name like America/New_York."); }

        SiteSettings? before = null;
        await Store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s =>
        {
            before = System.Text.Json.JsonSerializer.Deserialize<SiteSettings>(System.Text.Json.JsonSerializer.Serialize(s, JsonStore.JsonOptions), JsonStore.JsonOptions);
            s.SiteBaseUrl = baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            s.SiteTitle = f.SiteTitle.Trim();
            s.SiteShortName = f.SiteShortName.Trim();
            s.SchoolName = (f.SchoolName ?? "").Trim();
            s.MascotName = (f.MascotName ?? "").Trim();
            s.Tagline = (f.Tagline ?? "").Trim();
            s.TimeZone = f.TimeZone.Trim();
            s.OpeningSoonMinutes = Math.Clamp(f.OpeningSoonMinutes, 1, 24 * 60);
            s.Display.CategorySeconds = Math.Clamp(f.CategorySeconds, 3, 600);
            s.Display.RefreshSeconds = Math.Clamp(f.DisplayRefreshSeconds, 15, 3600);
            s.Public.RefreshSeconds = Math.Clamp(f.PublicRefreshSeconds, 15, 3600);
            s.Public.UpcomingGamesCount = Math.Clamp(f.UpcomingGamesCount, 0, 50);
            s.Display.Layout = DisplayLayouts.IsValid(f.DisplayLayout) ? f.DisplayLayout : DisplayLayouts.Rotate;
            s.Display.GroupVarieties = f.GroupVarieties;
            s.Display.ShowClock = f.ShowClock;
            s.Display.ShowUpdatedTime = f.ShowUpdatedTime;
        });
        var after = Store.Read<SiteSettings>(DataFileNames.Settings);
        await Audit.LogAsync("UpdateSettings", "Settings", "settings", "Site settings", SettingsDiff.Diff(before!, after));
        return Done("Settings saved. Phones and TVs pick them up on their next refresh.");
    }
}

/// <summary>Field-level before/after for settings audits.</summary>
public static class SettingsDiff
{
    public static object Diff(SiteSettings before, SiteSettings after)
    {
        var a = System.Text.Json.JsonSerializer.SerializeToNode(before, JsonStore.JsonOptions)!.AsObject();
        var b = System.Text.Json.JsonSerializer.SerializeToNode(after, JsonStore.JsonOptions)!.AsObject();
        var changes = new Dictionary<string, object?>();
        Walk("", a, b, changes);
        return changes;
    }

    private static void Walk(string prefix, System.Text.Json.Nodes.JsonObject a, System.Text.Json.Nodes.JsonObject b, Dictionary<string, object?> changes)
    {
        foreach (var (key, bv) in b)
        {
            if (key is "updatedAt" or "version") continue;
            a.TryGetPropertyValue(key, out var av);
            var path = prefix + key;
            if (av is System.Text.Json.Nodes.JsonObject ao && bv is System.Text.Json.Nodes.JsonObject bo) { Walk(path + ".", ao, bo, changes); continue; }
            var sa = av?.ToJsonString();
            var sb = bv?.ToJsonString();
            if (sa != sb) changes[path] = new { from = av?.ToString(), to = bv?.ToString() };
        }
    }
}
