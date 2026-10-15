using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin;

public sealed class StandModel : AdminPageModel
{
    public sealed record Row(Item Item, StandItemState State, string PriceText, bool NoPrice);
    public sealed record Group(SubCategory? Sub, List<Row> Rows);
    public sealed record Cat(Category Category, List<Group> Groups);

    public Stand Stand { get; private set; } = new();
    public List<Cat> Categories { get; } = new();
    public List<PresetMenu> Menus { get; private set; } = new();
    public PresetMenu? LastMenu { get; private set; }
    public string OpeningValue { get; private set; } = "";
    public string ClosingValue { get; private set; } = "";
    public string DateValue { get; private set; } = "";
    public string? PhaseMessage { get; private set; }

    public bool CanOpenClose => Can(Functions.OpenCloseStand, Stand.Id);
    public bool CanTimes => Can(Functions.SetStandTimes, Stand.Id);
    public bool CanSelling => Can(Functions.SetStandItems, Stand.Id);
    public bool CanStatus => Can(Functions.MarkItemStatus, Stand.Id);
    public bool CanApply => Can(Functions.ApplyPresetMenu, Stand.Id);

    public IActionResult OnGet(string id)
    {
        var stand = Store.Read<StandsFile>(DataFileNames.Stands).Stands.FirstOrDefault(s => s.Id == id && !s.Archived);
        if (stand is null) return NotFound();
        Stand = stand;
        if (!(CanOpenClose || CanTimes || CanSelling || CanStatus || CanApply)) return Forbid();

        var catalog = Store.Read<CatalogFile>(DataFileNames.Catalog);
        var stands = Store.Read<StandsFile>(DataFileNames.Stands);
        var pricing = new PricingService(catalog, Store.Read<PricesFile>(DataFileNames.Prices), stands.Stands);

        Row MakeRow(Item i)
        {
            var price = pricing.Resolve(i.Id, stand.Id);
            return new Row(i, StandLogic.GetItem(stand, i.Id), price.Amount is { } a ? PricingService.Format(a) : "No price", !price.HasPrice);
        }

        foreach (var cat in catalog.Categories.Where(c => !c.Archived).OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            var groups = new List<(int sort, Group g)>();
            var direct = catalog.Items.Where(i => i.CategoryId == cat.Id && i.SubCategoryId is null && !i.Archived)
                .OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Select(MakeRow).ToList();
            if (direct.Count > 0) groups.Add((int.MinValue, new Group(null, direct)));
            foreach (var sub in catalog.SubCategories.Where(s => s.CategoryId == cat.Id && !s.Archived).OrderBy(s => s.SortOrder).ThenBy(s => s.Name))
            {
                var rows = catalog.Items.Where(i => i.SubCategoryId == sub.Id && !i.Archived)
                    .OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Select(MakeRow).ToList();
                if (rows.Count > 0) groups.Add((sub.SortOrder, new Group(sub, rows)));
            }
            if (groups.Count > 0) Categories.Add(new Cat(cat, groups.OrderBy(g => g.sort).Select(g => g.g).ToList()));
        }

        Menus = Store.Read<MenusFile>(DataFileNames.Menus).Menus.Where(m => !m.Archived && m.StandId == stand.Id).OrderBy(m => m.Name).ToList();
        LastMenu = Store.Read<MenusFile>(DataFileNames.Menus).Menus.FirstOrDefault(m => m.Id == stand.State.LastAppliedMenuId && !m.Archived);

        DateValue = (stand.State.OpeningAt is { } od ? Time.ToLocal(od) : Time.LocalNow).ToString("yyyy-MM-dd");
        OpeningValue = stand.State.OpeningAt is { } o ? Time.ToLocal(o).ToString("HH:mm") : "";
        ClosingValue = stand.State.ClosingAt is { } c ? Time.ToLocal(c).ToString("HH:mm") : "";

        var phase = StandLogic.Phase(stand, DateTime.UtcNow, Settings.OpeningSoonMinutes);
        PhaseMessage = phase switch
        {
            StandPhase.OpeningAt => TextTemplates.Fill(Settings.Text.OpeningAt, Settings, stand.Name, Time.FormatUtcTime(stand.State.OpeningAt)),
            StandPhase.OpeningSoon => TextTemplates.Fill(Settings.Text.OpeningSoon, Settings, stand.Name),
            StandPhase.Closed => TextTemplates.Fill(Settings.Text.Closed, Settings, stand.Name),
            _ => null,
        };
        return Page();
    }
}
