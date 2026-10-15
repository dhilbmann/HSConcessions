using System.Globalization;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin;

/// <summary>Optional events (Features:Events). The only behavior is "Prepare stand for event".</summary>
public sealed class EventsModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.ManageEvents;

    public List<StandEvent> Events { get; private set; } = new();
    public List<Stand> Stands { get; private set; } = new();
    public List<PresetMenu> Menus { get; private set; } = new();

    public override async Task OnPageHandlerExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context,
        Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutionDelegate next)
    {
        if (!Features.Events) { context.Result = NotFound(); return; }
        await base.OnPageHandlerExecutionAsync(context, next);
    }

    public void OnGet()
    {
        Events = Store.Read<EventsFile>(DataFileNames.Events).Events.OrderBy(e => e.StartsAt).ToList();
        Stands = Store.Read<StandsFile>(DataFileNames.Stands).Stands.Where(s => !s.Archived).OrderBy(s => s.SortOrder).ToList();
        Menus = Store.Read<MenusFile>(DataFileNames.Menus).Menus.Where(m => !m.Archived).ToList();
    }

    public string When(DateTime? utc) => utc is { } u ? Time.FormatUtcDateTime(u) : "";

    public async Task<IActionResult> OnPostSaveAsync(string? id, string name, List<string>? standIds, string startsAt, string? menuId, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException("Event name is required.");
        if (!DateTime.TryParseExact(startsAt, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new ValidationException("Enter a valid start time.");
        var isNew = string.IsNullOrEmpty(id);
        var eventId = isNew ? NewId("evt", name) : id!;
        await Store.UpdateAsync<EventsFile>(DataFileNames.Events, f =>
        {
            var e = isNew ? new StandEvent { Id = eventId } : f.Events.FirstOrDefault(x => x.Id == eventId) ?? throw new NotFoundException("Event not found.");
            if (isNew) f.Events.Add(e);
            e.Name = name.Trim();
            e.StandIds = standIds ?? new();
            e.StartsAt = Time.ToUtc(local);
            e.MenuId = string.IsNullOrEmpty(menuId) ? null : menuId;
            e.Notes = (notes ?? "").Trim();
        });
        await Audit.LogAsync(isNew ? "Create" : "Update", "Event", eventId, name, new { standIds, startsAt, menuId });
        return Done("Event saved.");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        string name = id;
        await Store.UpdateAsync<EventsFile>(DataFileNames.Events, f =>
        {
            var e = f.Events.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Event not found.");
            name = e.Name;
            f.Events.Remove(e);
        });
        await Audit.LogAsync("Delete", "Event", id, name);
        return Done("Event removed.");
    }

    /// <summary>Applies the event's menu and sets the opening time from startsAt, for each of its stands.</summary>
    public async Task<IActionResult> OnPostPrepareAsync(string id)
    {
        var e = Store.Read<EventsFile>(DataFileNames.Events).Events.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Event not found.");
        var stands = Svc<StandService>();
        foreach (var standId in e.StandIds)
        {
            if (!Can(Functions.ApplyPresetMenu, standId) && e.MenuId is not null) return Forbid();
            if (!Can(Functions.SetStandTimes, standId)) return Forbid();
            if (e.MenuId is not null) await stands.ApplyMenuAsync(standId, e.MenuId);
            var current = stands.Get(standId);
            await stands.SetTimesAsync(standId, e.StartsAt, current.State.ClosingAt);
        }
        await Audit.LogAsync("PrepareEvent", "Event", e.Id, e.Name, new { e.StandIds, e.MenuId, e.StartsAt });
        return Done($"Prepared {e.StandIds.Count} stand(s) for {e.Name}.");
    }
}
