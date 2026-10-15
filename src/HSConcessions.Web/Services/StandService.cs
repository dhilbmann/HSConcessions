using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

public sealed class NotFoundException(string message) : Exception(message);
public sealed class ValidationException(string message) : Exception(message);

/// <summary>Game-day stand operations: persisted to stands.json and audited.</summary>
public sealed class StandService(JsonStore store, AuditService audit)
{
    public Stand Get(string standId) =>
        store.Read<StandsFile>(DataFileNames.Stands).Stands.FirstOrDefault(s => s.Id == standId && !s.Archived)
        ?? throw new NotFoundException("Stand not found.");

    private async Task<Stand> Mutate(string standId, Action<Stand> action) =>
        await store.UpdateAsync<StandsFile, Stand>(DataFileNames.Stands, f =>
        {
            var stand = f.Stands.FirstOrDefault(s => s.Id == standId && !s.Archived) ?? throw new NotFoundException("Stand not found.");
            action(stand);
            return stand;
        });

    public async Task<Stand> OpenAsync(string standId)
    {
        var wasOpen = false;
        var stand = await Mutate(standId, s => { wasOpen = s.State.IsOpen; StandLogic.Open(s); });
        await audit.LogAsync("OpenStand", "Stand", stand.Id, stand.Name, new { from = wasOpen ? "open" : "closed", to = "open" });
        return stand;
    }

    public async Task<Stand> CloseAsync(string standId)
    {
        object? before = null;
        var stand = await Mutate(standId, s =>
        {
            before = new
            {
                isOpen = s.State.IsOpen,
                openingAt = s.State.OpeningAt,
                closingAt = s.State.ClosingAt,
                selling = s.State.Items.Count(i => i.Value.Selling),
            };
            StandLogic.Close(s);
        });
        await audit.LogAsync("CloseStand", "Stand", stand.Id, stand.Name, new { from = before, to = "closed (reset)" });
        return stand;
    }

    public async Task<Stand> SetTimesAsync(string standId, DateTime? openingUtc, DateTime? closingUtc)
    {
        object? before = null;
        var stand = await Mutate(standId, s =>
        {
            before = new { openingAt = s.State.OpeningAt, closingAt = s.State.ClosingAt };
            s.State.OpeningAt = openingUtc;
            s.State.ClosingAt = closingUtc;
        });
        await audit.LogAsync("SetStandTimes", "Stand", stand.Id, stand.Name,
            new { from = before, to = new { openingAt = openingUtc, closingAt = closingUtc } });
        return stand;
    }

    public async Task<Stand> ApplyMenuAsync(string standId, string? menuId)
    {
        var catalog = store.Read<CatalogFile>(DataFileNames.Catalog);
        var menus = store.Read<MenusFile>(DataFileNames.Menus);
        string? previous = null;
        PresetMenu? menu = null;
        var stand = await Mutate(standId, s =>
        {
            var id = menuId ?? s.State.LastAppliedMenuId ?? throw new ValidationException("No menu has been applied to this stand yet.");
            menu = menus.Menus.FirstOrDefault(m => m.Id == id && !m.Archived) ?? throw new NotFoundException("Menu not found.");
            previous = s.State.LastAppliedMenuId;
            StandLogic.ApplyMenu(s, menu, catalog);
        });
        await audit.LogAsync(menuId is null ? "ReapplyMenu" : "ApplyMenu", "Stand", stand.Id, $"{menu!.Name} @ {stand.Name}",
            new { from = previous, to = menu.Id, items = menu.ItemIds.Count });
        return stand;
    }

    public async Task<StandItemState> SetItemAsync(string standId, string itemId, bool? selling, string? status)
    {
        if (status is not null && !ItemStatus.IsValid(status)) throw new ValidationException("Invalid status.");
        var catalog = store.Read<CatalogFile>(DataFileNames.Catalog);
        var item = catalog.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new NotFoundException("Item not found.");
        StandItemState before = new(), after = new();
        var stand = await Mutate(standId, s => (before, after) = StandLogic.SetItem(s, itemId, selling, status));

        if (selling is not null && selling != before.Selling)
            await audit.LogAsync("SetItemSelling", "StandItem", $"{stand.Id}/{item.Id}", $"{item.Name} @ {stand.Name}",
                new { from = before.Selling, to = after.Selling });
        if (status is not null && status != before.Status)
            await audit.LogAsync("SetItemStatus", "StandItem", $"{stand.Id}/{item.Id}", $"{item.Name} @ {stand.Name}",
                new { from = before.Status, to = after.Status });
        return after;
    }

    public async Task<int> BulkAsync(string standId, string scope, string targetId, bool? selling, string? status)
    {
        if (scope is not ("category" or "subCategory")) throw new ValidationException("Scope must be category or subCategory.");
        if (status is not null && !ItemStatus.IsValid(status)) throw new ValidationException("Invalid status.");
        var catalog = store.Read<CatalogFile>(DataFileNames.Catalog);
        var items = StandLogic.ItemsInScope(catalog, scope, targetId).ToList();
        var name = scope == "category"
            ? catalog.Categories.FirstOrDefault(c => c.Id == targetId)?.Name
            : catalog.SubCategories.FirstOrDefault(c => c.Id == targetId)?.Name;
        if (name is null) throw new NotFoundException("Category not found.");
        var stand = await Mutate(standId, s =>
        {
            foreach (var item in items) StandLogic.SetItem(s, item.Id, selling, status);
        });
        await audit.LogAsync("BulkSetItems", "Stand", stand.Id, $"{name} @ {stand.Name}",
            new { scope, targetId, selling, status, count = items.Count });
        return items.Count;
    }
}
