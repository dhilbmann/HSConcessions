using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin.Menus;

public sealed class IndexModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.ManageMenus;

    public List<PresetMenu> Menus { get; private set; } = new();
    public List<Stand> Stands { get; private set; } = new();
    public bool ShowArchived { get; private set; }

    public void OnGet(bool archived = false)
    {
        ShowArchived = archived;
        Menus = Store.Read<MenusFile>(DataFileNames.Menus).Menus.Where(m => m.Archived == archived).ToList();
        Stands = Store.Read<StandsFile>(DataFileNames.Stands).Stands.Where(s => !s.Archived).OrderBy(s => s.SortOrder).ToList();
    }

    public string StandName(string id) => Stands.FirstOrDefault(s => s.Id == id)?.Name ?? id;

    public async Task<IActionResult> OnPostCreateAsync(string name, string standId)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 80) throw new ValidationException("Menu name is required.");
        if (Store.Read<StandsFile>(DataFileNames.Stands).Stands.All(s => s.Id != standId)) throw new ValidationException("Choose a stand.");
        var id = NewId("menu", name);
        await Store.UpdateAsync<MenusFile>(DataFileNames.Menus, f => f.Menus.Add(new PresetMenu { Id = id, Name = name, StandId = standId }));
        await Audit.LogAsync("Create", "Menu", id, name, new { standId });
        return Redirect($"/admin/menus/{id}");
    }

    public async Task<IActionResult> OnPostDuplicateAsync(string id, string? name)
    {
        PresetMenu? copy = null;
        await Store.UpdateAsync<MenusFile>(DataFileNames.Menus, f =>
        {
            var src = f.Menus.FirstOrDefault(m => m.Id == id) ?? throw new NotFoundException("Menu not found.");
            var newName = string.IsNullOrWhiteSpace(name) ? src.Name + " (copy)" : name.Trim();
            copy = new PresetMenu { Id = NewId("menu", newName), Name = newName, StandId = src.StandId, ItemIds = src.ItemIds.ToList() };
            f.Menus.Add(copy);
        });
        await Audit.LogAsync("Duplicate", "Menu", copy!.Id, copy.Name, new { from = id });
        return Done($"Created {copy.Name}.");
    }

    public async Task<IActionResult> OnPostRenameAsync(string id, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 80) throw new ValidationException("Menu name is required.");
        string? before = null;
        await Store.UpdateAsync<MenusFile>(DataFileNames.Menus, f =>
        {
            var m = f.Menus.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Menu not found.");
            before = m.Name;
            m.Name = name;
        });
        await Audit.LogAsync("Rename", "Menu", id, name, new { from = before, to = name });
        return Done("Renamed.");
    }

    public async Task<IActionResult> OnPostArchiveAsync(string id, bool archived)
    {
        string name = id;
        await Store.UpdateAsync<MenusFile>(DataFileNames.Menus, f =>
        {
            var m = f.Menus.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Menu not found.");
            m.Archived = archived;
            name = m.Name;
        });
        await Audit.LogAsync(archived ? "Archive" : "Restore", "Menu", id, name, new { from = !archived, to = archived });
        return Done(archived ? $"Archived {name}." : $"Restored {name}.");
    }
}
