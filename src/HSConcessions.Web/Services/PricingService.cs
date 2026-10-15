using System.Globalization;
using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

/// <summary>Where an effective price came from.</summary>
public sealed record PriceResolution(decimal? Amount, string? Level, string? TargetId, string? StandId, string SourceName)
{
    public bool HasPrice => Amount.HasValue;
    public static readonly PriceResolution None = new(null, null, null, null, "No price");
}

/// <summary>
/// Resolves an item's price at a stand. First match wins:
/// item@stand, item default, sub-category@stand, sub-category default, category@stand, category default, none.
/// </summary>
public sealed class PricingService
{
    private readonly CatalogFile _catalog;
    private readonly PricesFile _prices;
    private readonly Dictionary<string, string> _standNames;

    public PricingService(CatalogFile catalog, PricesFile prices, IEnumerable<Stand>? stands = null)
    {
        _catalog = catalog;
        _prices = prices;
        _standNames = (stands ?? Enumerable.Empty<Stand>()).ToDictionary(s => s.Id, s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    public PriceResolution Resolve(string itemId, string? standId)
    {
        var item = _catalog.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return PriceResolution.None;

        var steps = new List<(string level, string? targetId)>
        {
            (PriceLevel.Item, item.Id),
            (PriceLevel.SubCategory, item.SubCategoryId),
            (PriceLevel.Category, item.CategoryId),
        };

        foreach (var (level, targetId) in steps)
        {
            if (string.IsNullOrEmpty(targetId)) continue;
            if (!string.IsNullOrEmpty(standId))
            {
                var perStand = Find(level, targetId, standId);
                if (perStand is not null) return Describe(perStand);
            }
            var def = Find(level, targetId, null);
            if (def is not null) return Describe(def);
        }
        return PriceResolution.None;
    }

    /// <summary>The price explicitly set at exactly this level/target/stand, if any.</summary>
    public decimal? Explicit(string level, string targetId, string? standId) => Find(level, targetId, standId)?.Amount;

    /// <summary>The price a category or sub-category cell would show (its own, else inherited from its parent).</summary>
    public PriceResolution ResolveGroup(string level, string targetId, string? standId)
    {
        var chain = new List<(string level, string id)> { (level, targetId) };
        if (level == PriceLevel.SubCategory)
        {
            var sub = _catalog.SubCategories.FirstOrDefault(s => s.Id == targetId);
            if (sub is not null) chain.Add((PriceLevel.Category, sub.CategoryId));
        }
        foreach (var (lvl, id) in chain)
        {
            if (!string.IsNullOrEmpty(standId))
            {
                var perStand = Find(lvl, id, standId);
                if (perStand is not null) return Describe(perStand);
            }
            var def = Find(lvl, id, null);
            if (def is not null) return Describe(def);
        }
        return PriceResolution.None;
    }

    private PriceEntry? Find(string level, string targetId, string? standId) =>
        _prices.Prices.FirstOrDefault(p =>
            p.Level == level &&
            p.TargetId == targetId &&
            string.Equals(p.StandId ?? "", standId ?? "", StringComparison.OrdinalIgnoreCase));

    private PriceResolution Describe(PriceEntry p)
    {
        var name = p.Level switch
        {
            PriceLevel.Item => _catalog.Items.FirstOrDefault(i => i.Id == p.TargetId)?.Name,
            PriceLevel.SubCategory => _catalog.SubCategories.FirstOrDefault(s => s.Id == p.TargetId)?.Name,
            PriceLevel.Category => _catalog.Categories.FirstOrDefault(c => c.Id == p.TargetId)?.Name,
            _ => null,
        } ?? p.TargetId;
        var where = p.StandId is null ? "default" : (_standNames.TryGetValue(p.StandId, out var sn) ? sn : p.StandId);
        var source = p.StandId is null ? $"{name} (default)" : $"{name} @ {where}";
        return new PriceResolution(p.Amount, p.Level, p.TargetId, p.StandId, source);
    }

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static string Format(decimal amount) => amount.ToString("C2", Us);

    public static string FormatDelta(decimal delta) =>
        delta >= 0 ? "+" + Format(delta) : "-" + Format(-delta);
}
