using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Web.Pages.Admin;

public sealed class PricingModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.SetPrices;

    public sealed record Cell(string Level, string TargetId, string? StandId, decimal? Explicit, PriceResolution Effective);
    public sealed record GridRow(string Kind, string Name, string TargetId, List<Cell> Cells);

    public List<Stand> Stands { get; private set; } = new();
    public List<GridRow> Rows { get; } = new();
    public int MissingCount { get; private set; }

    public void OnGet()
    {
        var catalog = Store.Read<CatalogFile>(DataFileNames.Catalog);
        var standsFile = Store.Read<StandsFile>(DataFileNames.Stands);
        Stands = standsFile.Stands.Where(s => !s.Archived).OrderBy(s => s.SortOrder).ToList();
        var pricing = new PricingService(catalog, Store.Read<PricesFile>(DataFileNames.Prices), standsFile.Stands);
        var columns = new List<string?> { null };
        columns.AddRange(Stands.Select(s => (string?)s.Id));

        List<Cell> GroupCells(string level, string id) =>
            columns.Select(sid => new Cell(level, id, sid, pricing.Explicit(level, id, sid), pricing.ResolveGroup(level, id, sid))).ToList();

        List<Cell> ItemCells(Item item)
        {
            var cells = columns.Select(sid => new Cell(PriceLevel.Item, item.Id, sid, pricing.Explicit(PriceLevel.Item, item.Id, sid), pricing.Resolve(item.Id, sid))).ToList();
            // An item is "missing" if any stand column has no resolvable price.
            if (cells.Skip(1).Any(c => !c.Effective.HasPrice) || (Stands.Count == 0 && !cells[0].Effective.HasPrice)) MissingCount++;
            return cells;
        }

        foreach (var cat in catalog.Categories.Where(c => !c.Archived).OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            Rows.Add(new GridRow("category", cat.Name, cat.Id, GroupCells(PriceLevel.Category, cat.Id)));
            var entries = catalog.SubCategories.Where(s => s.CategoryId == cat.Id && !s.Archived)
                .Select(s => (sort: s.SortOrder, name: s.Name, sub: (SubCategory?)s, item: (Item?)null))
                .Concat(catalog.Items.Where(i => i.CategoryId == cat.Id && i.SubCategoryId == null && !i.Archived)
                    .Select(i => (sort: i.SortOrder, name: i.Name, sub: (SubCategory?)null, item: (Item?)i)))
                .OrderBy(e => e.sort).ThenBy(e => e.name);
            foreach (var e in entries)
            {
                if (e.item is { } item)
                {
                    Rows.Add(new GridRow("item", item.Name, item.Id, ItemCells(item)));
                }
                else if (e.sub is { } sub)
                {
                    Rows.Add(new GridRow("subCategory", sub.Name, sub.Id, GroupCells(PriceLevel.SubCategory, sub.Id)));
                    foreach (var si in catalog.Items.Where(i => i.SubCategoryId == sub.Id && !i.Archived).OrderBy(i => i.SortOrder).ThenBy(i => i.Name))
                        Rows.Add(new GridRow("subItem", si.Name, si.Id, ItemCells(si)));
                }
            }
        }
    }
}
