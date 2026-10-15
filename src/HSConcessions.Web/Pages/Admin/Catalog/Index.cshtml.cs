using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin.Catalog;

public sealed class IndexModel : AdminPageModel
{
    public CatalogFile Catalog { get; private set; } = new();

    public bool CanEdit => Can(Functions.ManageCatalog);
    public bool CanArchive => Can(Functions.ArchiveCatalog);

    public IActionResult OnGet()
    {
        if (!CanEdit && !CanArchive && !Can(Functions.UploadPictures)) return Forbid();
        Catalog = Store.Read<CatalogFile>(DataFileNames.Catalog);
        return Page();
    }

    public IEnumerable<Category> Categories => Catalog.Categories.Where(c => !c.Archived).OrderBy(c => c.SortOrder).ThenBy(c => c.Name);
    public IEnumerable<SubCategory> SubsOf(string catId) => Catalog.SubCategories.Where(s => s.CategoryId == catId && !s.Archived).OrderBy(s => s.SortOrder).ThenBy(s => s.Name);
    public IEnumerable<Item> DirectItemsOf(string catId) => Catalog.Items.Where(i => i.CategoryId == catId && i.SubCategoryId is null && !i.Archived).OrderBy(i => i.SortOrder).ThenBy(i => i.Name);
    public IEnumerable<Item> ItemsOf(string subId) => Catalog.Items.Where(i => i.SubCategoryId == subId && !i.Archived).OrderBy(i => i.SortOrder).ThenBy(i => i.Name);

    /// <summary>Swap sort order with the neighbour among siblings.</summary>
    public async Task<IActionResult> OnPostMoveAsync(string type, string id, int dir)
    {
        if (!CanEdit) return Forbid();
        string name = id;
        await Store.UpdateAsync<CatalogFile>(DataFileNames.Catalog, f =>
        {
            switch (type)
            {
                case "category":
                    Move(f.Categories.Where(c => !c.Archived).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList(), c => c.Id == id, c => c.SortOrder, (c, v) => c.SortOrder = v, dir);
                    name = f.Categories.First(c => c.Id == id).Name;
                    break;
                case "subCategory":
                    var sub = f.SubCategories.First(s => s.Id == id);
                    name = sub.Name;
                    // Sub-categories and direct items share one ordering inside a category.
                    MoveMixed(f, sub.CategoryId, id, dir);
                    break;
                case "item":
                    var item = f.Items.First(i => i.Id == id);
                    name = item.Name;
                    if (item.SubCategoryId is null) MoveMixed(f, item.CategoryId, id, dir);
                    else Move(f.Items.Where(i => i.SubCategoryId == item.SubCategoryId && !i.Archived).OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToList(), i => i.Id == id, i => i.SortOrder, (i, v) => i.SortOrder = v, dir);
                    break;
                default:
                    throw new ValidationException("Unknown type.");
            }
        });
        await Audit.LogAsync("ReorderCatalog", type, id, name, new { direction = dir < 0 ? "up" : "down" });
        return RedirectToPage(null, null, null, "e-" + id);
    }

    private static void Move<T>(List<T> list, Func<T, bool> match, Func<T, int> get, Action<T, int> set, int dir)
    {
        for (var i = 0; i < list.Count; i++) set(list[i], (i + 1) * 10);
        var idx = list.FindIndex(x => match(x));
        var other = idx + Math.Sign(dir);
        if (idx < 0 || other < 0 || other >= list.Count) return;
        var a = get(list[idx]);
        set(list[idx], get(list[other]));
        set(list[other], a);
    }

    private static void MoveMixed(CatalogFile f, string catId, string id, int dir)
    {
        var entries = f.SubCategories.Where(s => s.CategoryId == catId && !s.Archived)
            .Select(s => (id: s.Id, sort: s.SortOrder, name: s.Name, set: (Action<int>)(v => s.SortOrder = v)))
            .Concat(f.Items.Where(i => i.CategoryId == catId && i.SubCategoryId is null && !i.Archived)
                .Select(i => (id: i.Id, sort: i.SortOrder, name: i.Name, set: (Action<int>)(v => i.SortOrder = v))))
            .OrderBy(e => e.sort).ThenBy(e => e.name).ToList();
        var order = entries.Select(e => e.id).ToList();
        var idx = order.IndexOf(id);
        var other = idx + Math.Sign(dir);
        if (idx >= 0 && other >= 0 && other < order.Count) (order[idx], order[other]) = (order[other], order[idx]);
        foreach (var e in entries) e.set((order.IndexOf(e.id) + 1) * 10);
    }

    public async Task<IActionResult> OnPostArchiveAsync(string type, string id)
    {
        if (!CanArchive) return Forbid();
        var name = await CatalogArchive.SetArchivedAsync(Store, type, id, true);
        await Audit.LogAsync("Archive", type, id, name, new { from = false, to = true });
        return Done($"Archived {name}. Restore it from the Archive view.");
    }
}

public static class CatalogArchive
{
    public static async Task<string> SetArchivedAsync(JsonStore store, string type, string id, bool archived, string? optionId = null)
    {
        return await store.UpdateAsync<CatalogFile, string>(DataFileNames.Catalog, f =>
        {
            switch (type)
            {
                case "category":
                    var c = f.Categories.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Category not found.");
                    c.Archived = archived;
                    return c.Name;
                case "subCategory":
                    var s = f.SubCategories.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Sub-category not found.");
                    s.Archived = archived;
                    return s.Name;
                case "item":
                    var i = f.Items.FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("Item not found.");
                    if (optionId is not null)
                    {
                        var o = i.Options.FirstOrDefault(x => x.Id == optionId) ?? throw new NotFoundException("Option not found.");
                        o.Archived = archived;
                        return $"{i.Name}: {o.Name}";
                    }
                    i.Archived = archived;
                    return i.Name;
                default:
                    throw new ValidationException("Unknown type.");
            }
        });
    }
}
