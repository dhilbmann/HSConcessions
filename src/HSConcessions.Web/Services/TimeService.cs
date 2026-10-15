using System.Globalization;
using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

/// <summary>Converts between stored UTC and the instance time zone, and formats times for display.</summary>
public sealed class TimeService
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private readonly JsonStore _store;

    public TimeService(JsonStore store) => _store = store;

    public TimeZoneInfo Zone => ZoneFor(_store.Read<SiteSettings>(DataFileNames.Settings).TimeZone);

    public static TimeZoneInfo ZoneFor(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "America/New_York" : id); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
    }

    public DateTime ToLocal(DateTime utc) => ToLocal(utc, Zone);

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    public DateTime ToUtc(DateTime local) => ToUtc(local, Zone);

    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);

    public DateTime LocalNow => ToLocal(DateTime.UtcNow);

    /// <summary>"6:00 PM"</summary>
    public static string FormatTime(DateTime local) => local.ToString("h:mm tt", Us);

    /// <summary>"Fri, Oct 16"</summary>
    public static string FormatDate(DateTime local) => local.ToString("ddd, MMM d", Us);

    /// <summary>"Oct 16, 2026 6:00 PM"</summary>
    public static string FormatDateTime(DateTime local) => local.ToString("MMM d, yyyy h:mm tt", Us);

    public string FormatUtcTime(DateTime? utc) => utc is { } u ? FormatTime(ToLocal(u)) : "";
    public string FormatUtcDateTime(DateTime? utc) => utc is { } u ? FormatDateTime(ToLocal(u)) : "";
}

/// <summary>Fills {time}, {stand}, {school}, {mascot}, {site} tokens in display text templates.</summary>
public static class TextTemplates
{
    public static string Fill(string? template, SiteSettings settings, string? stand = null, string? time = null)
    {
        if (string.IsNullOrEmpty(template)) return "";
        return template
            .Replace("{time}", time ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{stand}", stand ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{school}", settings.SchoolName, StringComparison.OrdinalIgnoreCase)
            .Replace("{mascot}", settings.MascotName, StringComparison.OrdinalIgnoreCase)
            .Replace("{site}", settings.SiteTitle, StringComparison.OrdinalIgnoreCase);
    }
}
