using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

public enum StandPhase
{
    /// <summary>Closed with no opening time, or the opening time is further away than the "opening soon" window.</summary>
    Closed,
    /// <summary>Closed, opening time within the window.</summary>
    OpeningAt,
    /// <summary>Closed, opening time has passed but nobody has opened the stand yet.</summary>
    OpeningSoon,
    Open,
}

/// <summary>Pure stand state transitions (§7.5, §7.6, §8). Callers persist the result.</summary>
public static class StandLogic
{
    public static StandPhase Phase(Stand stand, DateTime nowUtc, int openingSoonMinutes)
    {
        var s = stand.State;
        if (s.IsOpen) return StandPhase.Open;
        if (s.OpeningAt is not { } opening) return StandPhase.Closed;
        var openingUtc = DateTime.SpecifyKind(opening, DateTimeKind.Utc);
        if (nowUtc >= openingUtc) return StandPhase.OpeningSoon;
        if (openingUtc - nowUtc <= TimeSpan.FromMinutes(openingSoonMinutes)) return StandPhase.OpeningAt;
        return StandPhase.Closed;
    }

    public static void Open(Stand stand) => stand.State.IsOpen = true;

    /// <summary>Closing resets every item, clears times, and keeps the last applied menu.</summary>
    public static void Close(Stand stand)
    {
        var s = stand.State;
        s.IsOpen = false;
        s.OpeningAt = null;
        s.ClosingAt = null;
        foreach (var key in s.Items.Keys.ToList())
            s.Items[key] = new StandItemState { Selling = false, Status = ItemStatus.Ok };
    }

    /// <summary>Selling exactly the menu's items, everything else off, all statuses reset to OK.</summary>
    public static void ApplyMenu(Stand stand, PresetMenu menu, CatalogFile catalog)
    {
        var menuItems = new HashSet<string>(menu.ItemIds);
        var s = stand.State;
        var ids = catalog.Items.Select(i => i.Id).Concat(s.Items.Keys).Distinct().ToList();
        s.Items = ids.ToDictionary(id => id, id => new StandItemState
        {
            Selling = menuItems.Contains(id) && catalog.Items.Any(i => i.Id == id && !i.Archived),
            Status = ItemStatus.Ok,
        });
        s.LastAppliedMenuId = menu.Id;
    }

    public static StandItemState GetItem(Stand stand, string itemId) =>
        stand.State.Items.TryGetValue(itemId, out var st) ? st : new StandItemState();

    public static (StandItemState before, StandItemState after) SetItem(Stand stand, string itemId, bool? selling, string? status)
    {
        var before = GetItem(stand, itemId);
        var after = new StandItemState
        {
            Selling = selling ?? before.Selling,
            Status = status ?? before.Status,
        };
        stand.State.Items[itemId] = after;
        return (new StandItemState { Selling = before.Selling, Status = before.Status }, after);
    }

    /// <summary>Items in a category or sub-category (non-archived) for bulk toggles.</summary>
    public static IEnumerable<Item> ItemsInScope(CatalogFile catalog, string scope, string targetId) => scope switch
    {
        "category" => catalog.Items.Where(i => !i.Archived && i.CategoryId == targetId),
        "subCategory" => catalog.Items.Where(i => !i.Archived && i.SubCategoryId == targetId),
        _ => Enumerable.Empty<Item>(),
    };

    /// <summary>On the menu at a stand = selling and not archived (and parents not archived).</summary>
    public static bool IsOnMenu(Stand stand, Item item, CatalogFile catalog)
    {
        if (item.Archived) return false;
        if (catalog.Categories.FirstOrDefault(c => c.Id == item.CategoryId) is not { Archived: false }) return false;
        if (item.SubCategoryId is not null &&
            catalog.SubCategories.FirstOrDefault(s => s.Id == item.SubCategoryId) is not { Archived: false }) return false;
        return GetItem(stand, item.Id).Selling;
    }

    public static readonly string[] ReservedSlugs =
        { "admin", "api", "display", "images", "css", "js", "img", "assets", "theme.css", "manifest.json", "fonts", "favicon.ico", "auth", "signin-google", "signin-microsoft", "index.html", "display.html" };

    public static string? ValidateSlug(string? slug, string? standId, IEnumerable<Stand> stands)
    {
        if (string.IsNullOrWhiteSpace(slug)) return "Slug is required.";
        if (slug.Length > 40) return "Slug is too long.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            return "Slug must be lowercase letters, digits, and single dashes.";
        if (ReservedSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase)) return $"\"{slug}\" is reserved.";
        if (stands.Any(s => s.Id != standId && string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase)))
            return "Another stand already uses that slug.";
        return null;
    }
}
