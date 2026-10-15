using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin;

public sealed class StandSetupModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.EditSettings;

    public List<Stand> Stands { get; private set; } = new();

    public void OnGet() =>
        Stands = Store.Read<StandsFile>(DataFileNames.Stands).Stands.OrderBy(s => s.Archived).ThenBy(s => s.SortOrder).ToList();

    public async Task<IActionResult> OnPostAddAsync(string name, string slug)
    {
        name = (name ?? "").Trim();
        slug = (slug ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0 || name.Length > 40) throw new ValidationException("Stand name is required (40 characters max).");
        string id = slug;
        await Store.UpdateAsync<StandsFile>(DataFileNames.Stands, f =>
        {
            if (StandLogic.ValidateSlug(slug, null, f.Stands) is { } err) throw new ValidationException(err);
            if (f.Stands.Any(s => s.Id == id)) id = slug + "-" + Guid.NewGuid().ToString("n")[..4];
            f.Stands.Add(new Stand
            {
                Id = id, Name = name, Slug = slug,
                SortOrder = (f.Stands.Select(s => s.SortOrder).DefaultIfEmpty(0).Max() / 10 + 1) * 10,
            });
        });
        await Audit.LogAsync("CreateStand", "Stand", id, name, new { slug });
        return Done($"Added the {name} stand: /{slug} and /display/{slug} are live.");
    }

    public async Task<IActionResult> OnPostUpdateAsync(string id, string name, string slug, string? layout)
    {
        name = (name ?? "").Trim();
        slug = (slug ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0 || name.Length > 40) throw new ValidationException("Stand name is required (40 characters max).");
        object? before = null;
        await Store.UpdateAsync<StandsFile>(DataFileNames.Stands, f =>
        {
            var s = f.Stands.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Stand not found.");
            if (StandLogic.ValidateSlug(slug, id, f.Stands) is { } err) throw new ValidationException(err);
            before = new { s.Name, s.Slug, layout = s.DisplayLayout };
            s.Name = name;
            s.Slug = slug;
            s.DisplayLayout = DisplayLayouts.IsValid(layout) ? layout : null;
        });
        await Audit.LogAsync("UpdateStand", "Stand", id, name, new { from = before, to = new { name, slug, layout } });
        return Done("Stand updated. Re-print QR codes if the slug changed.");
    }

    public async Task<IActionResult> OnPostMoveAsync(string id, int dir)
    {
        await Store.UpdateAsync<StandsFile>(DataFileNames.Stands, f =>
        {
            var list = f.Stands.Where(s => !s.Archived).OrderBy(s => s.SortOrder).ToList();
            for (var i = 0; i < list.Count; i++) list[i].SortOrder = (i + 1) * 10;
            var idx = list.FindIndex(s => s.Id == id);
            var other = idx + Math.Sign(dir);
            if (idx < 0 || other < 0 || other >= list.Count) return;
            (list[idx].SortOrder, list[other].SortOrder) = (list[other].SortOrder, list[idx].SortOrder);
        });
        await Audit.LogAsync("ReorderStand", "Stand", id, id, new { direction = dir < 0 ? "up" : "down" });
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostArchiveAsync(string id, bool archived)
    {
        string name = id;
        await Store.UpdateAsync<StandsFile>(DataFileNames.Stands, f =>
        {
            var s = f.Stands.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Stand not found.");
            if (archived && s.State.IsOpen) throw new ValidationException("Close the stand before archiving it.");
            if (!archived && StandLogic.ValidateSlug(s.Slug, s.Id, f.Stands.Where(x => !x.Archived)) is { } err)
                throw new ValidationException("Can't restore: " + err);
            s.Archived = archived;
            name = s.Name;
        });
        await Audit.LogAsync(archived ? "Archive" : "Restore", "Stand", id, name, new { from = !archived, to = archived });
        return Done(archived ? $"Archived {name}." : $"Restored {name}.");
    }
}
