using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin.Menus;

public sealed class EditModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.ManageMenus;

    public PresetMenu Menu { get; private set; } = new();
    public CatalogFile Catalog { get; private set; } = new();
    public List<Stand> Stands { get; private set; } = new();

    public IActionResult OnGet(string id)
    {
        var menu = Store.Read<MenusFile>(DataFileNames.Menus).Menus.FirstOrDefault(m => m.Id == id);
        if (menu is null) return NotFound();
        Menu = menu;
        Catalog = Store.Read<CatalogFile>(DataFileNames.Catalog);
        Stands = Store.Read<StandsFile>(DataFileNames.Stands).Stands.Where(s => !s.Archived).OrderBy(s => s.SortOrder).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id, List<string>? itemIds, string? standId)
    {
        var catalog = Store.Read<CatalogFile>(DataFileNames.Catalog);
        var valid = (itemIds ?? new()).Where(i => catalog.Items.Any(x => x.Id == i)).Distinct().ToList();
        List<string> before = new();
        string name = id;
        string? beforeStand = null;
        await Store.UpdateAsync<MenusFile>(DataFileNames.Menus, f =>
        {
            var m = f.Menus.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Menu not found.");
            before = m.ItemIds.ToList();
            beforeStand = m.StandId;
            name = m.Name;
            // Keep archived items that were on the menu, so restoring them restores the menu too.
            var archivedKept = m.ItemIds.Where(i => catalog.Items.FirstOrDefault(x => x.Id == i)?.Archived == true);
            m.ItemIds = valid.Concat(archivedKept).Distinct().ToList();
            if (!string.IsNullOrEmpty(standId)) m.StandId = standId;
        });
        await Audit.LogAsync("UpdateMenuItems", "Menu", id, name, new
        {
            added = valid.Except(before).ToList(),
            removed = before.Except(valid).ToList(),
            stand = beforeStand != standId && standId is not null ? new { from = beforeStand, to = standId } : null,
        });
        Flash = $"Saved {name} ({valid.Count} items).";
        return Redirect($"/admin/menus/{id}");
    }
}
