using System.Globalization;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin;

public sealed class CalendarModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.ManageCalendar;

    public sealed class GameInput
    {
        public string? Id { get; set; }
        public string Date { get; set; } = "";
        public string Time { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Level { get; set; }
        public string? Sport { get; set; }
        public bool Home { get; set; } = true;
        public string? Location { get; set; }
    }

    public List<Game> Games { get; private set; } = new();
    public IReadOnlyList<Game> Preview { get; private set; } = Array.Empty<Game>();
    public string? EditId { get; private set; }

    public async Task OnGetAsync(string? edit)
    {
        EditId = edit;
        Games = Store.Read<CalendarFile>(DataFileNames.Calendar).Games.OrderBy(g => g.StartsAt).ToList();
        Preview = await Svc<CalendarService>().GetUpcomingAsync(Settings);
    }

    public string LocalDate(DateTime utc) => Time.ToLocal(utc).ToString("yyyy-MM-dd");
    public string LocalTime(DateTime utc) => Time.ToLocal(utc).ToString("HH:mm");
    public string Display(DateTime utc) => TimeService.FormatDate(Time.ToLocal(utc)) + " " + TimeService.FormatTime(Time.ToLocal(utc));

    public async Task<IActionResult> OnPostProviderAsync(string provider, string? icalUrl)
    {
        if (provider is not ("manual" or "ical")) throw new ValidationException("Unknown provider.");
        icalUrl = string.IsNullOrWhiteSpace(icalUrl) ? null : icalUrl.Trim();
        if (icalUrl is not null && (!Uri.TryCreate(icalUrl, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")))
            throw new ValidationException("The iCal URL must start with http:// or https://");
        if (provider == "ical" && icalUrl is null) throw new ValidationException("Enter the iCal feed URL.");
        object? before = null;
        await Store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s =>
        {
            before = new { s.Calendar.Provider, s.Calendar.IcalUrl };
            s.Calendar.Provider = provider;
            s.Calendar.IcalUrl = icalUrl;
        });
        await Audit.LogAsync("UpdateCalendarSource", "Settings", "calendar", "Calendar source", new { from = before, to = new { provider, icalUrl } });
        return Done("Calendar source saved.");
    }

    public async Task<IActionResult> OnPostSaveAsync(GameInput game)
    {
        if (string.IsNullOrWhiteSpace(game.Title)) throw new ValidationException("Enter the opponent or title.");
        if (!DateTime.TryParseExact($"{game.Date} {game.Time}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new ValidationException("Enter a valid date and time.");
        var startsAt = Time.ToUtc(local);
        var isNew = string.IsNullOrEmpty(game.Id);
        var id = isNew ? "game-" + Guid.NewGuid().ToString("n")[..10] : game.Id!;
        object? before = null;
        await Store.UpdateAsync<CalendarFile>(DataFileNames.Calendar, f =>
        {
            var g = isNew ? null : f.Games.FirstOrDefault(x => x.Id == id);
            if (!isNew && g is null) throw new NotFoundException("Game not found.");
            if (g is null) { g = new Game { Id = id }; f.Games.Add(g); }
            else before = new { g.StartsAt, g.Title, g.Level, g.Sport, g.Home, g.Location };
            g.StartsAt = startsAt;
            g.Title = game.Title.Trim();
            g.Level = (game.Level ?? "").Trim();
            g.Sport = (game.Sport ?? "").Trim();
            g.Home = game.Home;
            g.Location = (game.Location ?? "").Trim();
        });
        await Audit.LogAsync(isNew ? "CreateGame" : "UpdateGame", "Game", id, game.Title,
            new { from = before, to = new { startsAt, game.Title, game.Level, game.Sport, game.Home, game.Location } });
        return Done(isNew ? "Game added." : "Game updated.");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        Game? removed = null;
        await Store.UpdateAsync<CalendarFile>(DataFileNames.Calendar, f =>
        {
            removed = f.Games.FirstOrDefault(g => g.Id == id) ?? throw new NotFoundException("Game not found.");
            f.Games.Remove(removed);
        });
        await Audit.LogAsync("DeleteGame", "Game", id, removed!.Title, new { from = removed });
        return Done("Game removed.");
    }
}
