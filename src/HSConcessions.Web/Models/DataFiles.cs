namespace HSConcessions.Web.Models;

/// <summary>Common header carried by every JSON data file.</summary>
public abstract class DataFile
{
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ---------- stands.json ----------

public sealed class StandsFile : DataFile
{
    public List<Stand> Stands { get; set; } = new();
}

public sealed class Stand
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
    /// <summary>TV layout override for this stand; null uses the site default.</summary>
    public string? DisplayLayout { get; set; }
    public StandState State { get; set; } = new();
}

public sealed class StandState
{
    public bool IsOpen { get; set; }
    public DateTime? OpeningAt { get; set; }
    public DateTime? ClosingAt { get; set; }
    public string? LastAppliedMenuId { get; set; }
    public Dictionary<string, StandItemState> Items { get; set; } = new();
}

public sealed class StandItemState
{
    public bool Selling { get; set; }
    public string Status { get; set; } = ItemStatus.Ok;
}

public static class ItemStatus
{
    public const string Ok = "ok";
    public const string Low = "low";
    public const string SoldOut = "soldOut";

    public static bool IsValid(string? s) => s is Ok or Low or SoldOut;
}

// ---------- catalog.json ----------

public sealed class CatalogFile : DataFile
{
    public List<Category> Categories { get; set; } = new();
    public List<SubCategory> SubCategories { get; set; } = new();
    public List<Item> Items { get; set; } = new();
}

public sealed class Category
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public string? ImageId { get; set; }
    public bool Archived { get; set; }
}

public sealed class SubCategory
{
    public string Id { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public string? ImageId { get; set; }
    public bool Archived { get; set; }
}

public sealed class Item
{
    public string Id { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public string? SubCategoryId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ImageId { get; set; }
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
    public List<ItemOption> Options { get; set; } = new();
}

public sealed class ItemOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceDelta { get; set; }
    public bool Archived { get; set; }
}

// ---------- prices.json ----------

public sealed class PricesFile : DataFile
{
    public List<PriceEntry> Prices { get; set; } = new();
}

public static class PriceLevel
{
    public const string Category = "category";
    public const string SubCategory = "subCategory";
    public const string Item = "item";

    public static bool IsValid(string? s) => s is Category or SubCategory or Item;
}

public sealed class PriceEntry
{
    public string Level { get; set; } = PriceLevel.Item;
    public string TargetId { get; set; } = "";
    public string? StandId { get; set; }
    public decimal Amount { get; set; }
}

// ---------- menus.json ----------

public sealed class MenusFile : DataFile
{
    public List<PresetMenu> Menus { get; set; } = new();
}

public sealed class PresetMenu
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string StandId { get; set; } = "";
    public List<string> ItemIds { get; set; } = new();
    public bool Archived { get; set; }
}

// ---------- users.json ----------

public sealed class UsersFile : DataFile
{
    public List<AppUser> Users { get; set; } = new();
}

public sealed class AppUser
{
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public bool Active { get; set; } = true;
    public List<RoleAssignment> Roles { get; set; } = new();
    public DateTime? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public sealed class RoleAssignment
{
    public string Role { get; set; } = "";
    public List<string>? StandIds { get; set; } = new();
}

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Pricing = "Pricing";
    public const string Manager = "Manager";

    public static readonly string[] All = { Administrator, Pricing, Manager };

    public static bool IsValid(string? r) => r is Administrator or Pricing or Manager;
}

// ---------- permissions.json ----------

public sealed class PermissionsFile : DataFile
{
    /// <summary>Function key → roles that are granted that function.</summary>
    public Dictionary<string, List<string>> Matrix { get; set; } = new();
}

// ---------- events.json ----------

public sealed class EventsFile : DataFile
{
    public List<StandEvent> Events { get; set; } = new();
}

public sealed class StandEvent
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> StandIds { get; set; } = new();
    public DateTime? StartsAt { get; set; }
    public string? MenuId { get; set; }
    public string Notes { get; set; } = "";
}

// ---------- calendar.json ----------

public sealed class CalendarFile : DataFile
{
    public List<Game> Games { get; set; } = new();
}

public sealed class Game
{
    public string Id { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public string Title { get; set; } = "";
    public string Level { get; set; } = "";
    public string Sport { get; set; } = "";
    public bool Home { get; set; } = true;
    public string Location { get; set; } = "";
}
