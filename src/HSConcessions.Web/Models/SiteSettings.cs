namespace HSConcessions.Web.Models;

/// <summary>settings.json — instance identity, branding, display text, and behavior. Defaults are the neutral generic values.</summary>
public sealed class SiteSettings : DataFile
{
    public string SiteBaseUrl { get; set; } = "https://example.com";
    public string SiteTitle { get; set; } = "HS Concessions";
    public string SiteShortName { get; set; } = "Concessions";
    public string SchoolName { get; set; } = "Your High School";
    public string MascotName { get; set; } = "Mascots";
    public string Tagline { get; set; } = "";
    public string TimeZone { get; set; } = "America/New_York";

    public BrandColors Colors { get; set; } = new();
    public BrandFonts Fonts { get; set; } = new();
    public BrandImages Images { get; set; } = new();
    public DisplayText Text { get; set; } = new();

    public int OpeningSoonMinutes { get; set; } = 60;
    public DisplayBehavior Display { get; set; } = new();
    public PublicBehavior Public { get; set; } = new();
    public CalendarSettings Calendar { get; set; } = new();
}

public sealed class BrandColors
{
    public string Primary { get; set; } = "#1F3A5F";
    public string Accent { get; set; } = "#F2A900";
    public string Dark { get; set; } = "#000000";
    public string Light { get; set; } = "#FFFFFF";
    public string Stamp { get; set; } = "#C8102E";
    /// <summary>Null or empty means "same as accent".</summary>
    public string? LowBadge { get; set; }
}

public sealed class BrandFonts
{
    public string Display { get; set; } = "Oswald";
    public string Body { get; set; } = "Inter";
}

/// <summary>Image references are file names under the images folder (content-hashed), or null.</summary>
public sealed class BrandImages
{
    public string? Logo { get; set; }
    public string? Mascot { get; set; }
    /// <summary>Base name of the favicon set (e.g. "fav-abc123"); files are {base}-16.png … and {base}.ico.</summary>
    public string? Favicon { get; set; }
    public bool FaviconUploaded { get; set; }
    public string? TvBackground { get; set; }
}

public sealed class DisplayText
{
    public string Closed { get; set; } = "Concessions Closed";
    public string OpeningAt { get; set; } = "Concessions Opening at {time}";
    public string OpeningSoon { get; set; } = "Opening Soon";
    public string SoldOutStamp { get; set; } = "SOLD OUT";
    public string LowBadge { get; set; } = "Going Fast!";
    public string SingleStandBanner { get; set; } = "Concessions are sold at the {stand} stand.";
    public string NoStandsOpen { get; set; } = "No concession stands are open right now.";
    public string Footer { get; set; } = "Payment accepted at the stand.";
}

public sealed class DisplayBehavior
{
    public int CategorySeconds { get; set; } = 12;
    public int RefreshSeconds { get; set; } = 120;
    /// <summary>TV layout: "rotate" (one category per screen), "single" (whole menu on one board), or "auto" (single when it fits readably).</summary>
    public string Layout { get; set; } = DisplayLayouts.Rotate;
    /// <summary>Show a sub-category as one card with its varieties listed (rotate layout).</summary>
    public bool GroupVarieties { get; set; } = true;
    /// <summary>Show the current time (server clock, instance time zone) in the TV header.</summary>
    public bool ShowClock { get; set; } = true;
    /// <summary>Show when the menu data last changed, small at the bottom of the TV.</summary>
    public bool ShowUpdatedTime { get; set; } = true;
}

public static class DisplayLayouts
{
    public const string Rotate = "rotate";
    public const string Single = "single";
    public const string Auto = "auto";

    public static readonly string[] All = { Rotate, Single, Auto };

    public static bool IsValid(string? v) => v is Rotate or Single or Auto;

    /// <summary>URL override, then the stand's own setting, then the site default.</summary>
    public static string Resolve(string? queryOverride, string? standLayout, string? siteLayout) =>
        IsValid(queryOverride) ? queryOverride! : IsValid(standLayout) ? standLayout! : IsValid(siteLayout) ? siteLayout! : Rotate;

    public static string Label(string v) => v switch
    {
        Single => "Single page (whole menu on one screen)",
        Auto => "Auto (single page if it fits, otherwise rotate)",
        _ => "Rotate (one category at a time)",
    };
}

public sealed class PublicBehavior
{
    public int RefreshSeconds { get; set; } = 120;
    public int UpcomingGamesCount { get; set; } = 5;
}

public sealed class CalendarSettings
{
    public string Provider { get; set; } = "manual";
    public string? IcalUrl { get; set; }
}
