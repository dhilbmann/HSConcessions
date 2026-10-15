using System.Globalization;
using System.Text;
using HSConcessions.Web.Models;
using Microsoft.Extensions.Caching.Memory;

namespace HSConcessions.Web.Services;

public interface ICalendarProvider
{
    string Key { get; }
    Task<IReadOnlyList<Game>> GetUpcomingAsync(SiteSettings settings, DateTime nowUtc, int count, CancellationToken ct = default);
}

/// <summary>Games entered by admins in calendar.json.</summary>
public sealed class ManualCalendarProvider(JsonStore store) : ICalendarProvider
{
    public string Key => "manual";

    public Task<IReadOnlyList<Game>> GetUpcomingAsync(SiteSettings settings, DateTime nowUtc, int count, CancellationToken ct = default)
    {
        var games = store.Read<CalendarFile>(DataFileNames.Calendar).Games
            .Where(g => g.StartsAt >= nowUtc.AddHours(-3))
            .OrderBy(g => g.StartsAt)
            .Take(count)
            .ToList();
        return Task.FromResult<IReadOnlyList<Game>>(games);
    }
}

/// <summary>Reads an iCal feed URL from settings, cached server-side for 30 minutes.</summary>
public sealed class ICalFeedCalendarProvider(IHttpClientFactory httpFactory, IMemoryCache cache, ILogger<ICalFeedCalendarProvider> log) : ICalendarProvider
{
    public string Key => "ical";

    public async Task<IReadOnlyList<Game>> GetUpcomingAsync(SiteSettings settings, DateTime nowUtc, int count, CancellationToken ct = default)
    {
        var url = settings.Calendar.IcalUrl;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Array.Empty<Game>();

        var zone = TimeService.ZoneFor(settings.TimeZone);
        var all = await cache.GetOrCreateAsync("ical:" + url, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            try
            {
                var client = httpFactory.CreateClient("ical");
                var text = await client.GetStringAsync(uri, ct);
                return Parse(text, zone);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "iCal feed fetch failed");
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return new List<Game>();
            }
        }) ?? new List<Game>();

        return all.Where(g => g.StartsAt >= nowUtc.AddHours(-3)).OrderBy(g => g.StartsAt).Take(count).ToList();
    }

    /// <summary>Minimal VEVENT parser: DTSTART, SUMMARY, LOCATION, CATEGORIES, UID.</summary>
    public static List<Game> Parse(string ics, TimeZoneInfo defaultZone)
    {
        var lines = Unfold(ics);
        var games = new List<Game>();
        Dictionary<string, (string param, string value)>? ev = null;
        foreach (var line in lines)
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { ev = new(StringComparer.OrdinalIgnoreCase); continue; }
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (ev is not null && ev.TryGetValue("DTSTART", out var dt) && TryParseDate(dt.param, dt.value, defaultZone, out var start))
                {
                    var summary = Unescape(ev.GetValueOrDefault("SUMMARY").value ?? "");
                    var location = Unescape(ev.GetValueOrDefault("LOCATION").value ?? "");
                    var sport = Unescape(ev.GetValueOrDefault("CATEGORIES").value ?? "");
                    games.Add(new Game
                    {
                        Id = ev.GetValueOrDefault("UID").value ?? Guid.NewGuid().ToString("n"),
                        StartsAt = start,
                        Title = summary,
                        Location = location,
                        Sport = sport,
                        Home = !summary.Contains(" at ", StringComparison.OrdinalIgnoreCase) && !summary.StartsWith("@"),
                    });
                }
                ev = null;
                continue;
            }
            if (ev is null) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var head = line[..colon];
            var value = line[(colon + 1)..];
            var semi = head.IndexOf(';');
            var name = semi < 0 ? head : head[..semi];
            var param = semi < 0 ? "" : head[(semi + 1)..];
            ev[name] = (param, value);
        }
        return games;
    }

    private static List<string> Unfold(string ics)
    {
        var result = new List<string>();
        foreach (var raw in ics.Replace("\r\n", "\n").Split('\n'))
        {
            if ((raw.StartsWith(' ') || raw.StartsWith('\t')) && result.Count > 0) result[^1] += raw[1..];
            else if (raw.Length > 0) result.Add(raw);
        }
        return result;
    }

    private static string Unescape(string s) =>
        s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    private static bool TryParseDate(string param, string value, TimeZoneInfo defaultZone, out DateTime utc)
    {
        utc = default;
        var parms = param.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
        if (value.EndsWith('Z') && DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var z))
        {
            utc = DateTime.SpecifyKind(z, DateTimeKind.Utc);
            return true;
        }
        var zone = parms.TryGetValue("TZID", out var tzid) ? TimeService.ZoneFor(tzid.Trim('"')) : defaultZone;
        if (DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) ||
            DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
        {
            utc = TimeService.ToUtc(local, zone);
            return true;
        }
        return false;
    }
}

public sealed class CalendarService(IEnumerable<ICalendarProvider> providers)
{
    public Task<IReadOnlyList<Game>> GetUpcomingAsync(SiteSettings settings, CancellationToken ct = default)
    {
        var provider = providers.FirstOrDefault(p => p.Key == settings.Calendar.Provider)
                       ?? providers.First(p => p.Key == "manual");
        return provider.GetUpcomingAsync(settings, DateTime.UtcNow, Math.Clamp(settings.Public.UpcomingGamesCount, 0, 50), ct);
    }
}
