using System.Text.Json;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Tests;

/// <summary>
/// The WildcatSnacks seed must match the printed 2026 menus (INDOOR, OUTDOOR, and VARSITY FOOTBALL CONCESSIONS).
/// Stands: Inside = indoor menu; Stadium and Fields = outdoor prices.
/// </summary>
public class WildcatSnacks2026MenuTests
{
    private const string Stadium = "football", Fields = "baseball", Inside = "inside";

    private static T Load<T>(string file) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "samples", "wildcatsnacks", file)), JsonStore.JsonOptions)!;

    private static readonly CatalogFile Catalog = Load<CatalogFile>("catalog.json");
    private static readonly PricingService Pricing = new(Catalog, Load<PricesFile>("prices.json"));
    private static readonly MenusFile Menus = Load<MenusFile>("menus.json");

    private static Item ItemNamed(string name, string? group = null) =>
        Catalog.Items.Single(i => i.Name == name &&
            (group is null ? i.SubCategoryId is null : Catalog.SubCategories.Single(s => s.Id == i.SubCategoryId).Name == group));

    // name, group, indoor price, outdoor price (Stadium and Fields)
    [Theory]
    [InlineData("Pepsi", "Soft Drinks", 2.50, 3.00)]
    [InlineData("Strawberry Lemonade", "Soft Drinks", 2.50, 3.00)]
    [InlineData("White (Cherry)", "Gatorade", 2.50, 3.00)]
    [InlineData("Regular", "Red Bull", 3.00, 3.00)]
    [InlineData("Diet", "Red Bull", 3.00, 3.00)]
    [InlineData("Water", null, 2.50, 2.50)]
    [InlineData("Coffee", null, 2.00, 2.00)]
    [InlineData("Hot Chocolate", null, 2.50, 2.50)]
    [InlineData("Hot Dog", null, 3.50, 3.50)]
    [InlineData("Nachos", null, 3.50, 3.50)]
    [InlineData("Pretzel", null, 2.50, 2.50)]
    [InlineData("Cup O Noodles", null, 3.00, 3.00)]
    [InlineData("Hamburger", null, 5.50, 5.50)]
    [InlineData("Cheeseburger", null, 6.00, 6.00)]
    [InlineData("Chick-fil-A Sandwich", null, 7.00, 7.00)]
    [InlineData("Cheese", "Pizza", 3.00, 3.00)]
    [InlineData("Pepperoni", "Pizza", 3.00, 3.00)]
    [InlineData("Chips", null, 1.00, 1.00)]
    [InlineData("Cookies", null, 2.00, 2.00)]
    [InlineData("Pickle", null, 1.50, 1.50)]
    [InlineData("Pickle Pop", null, 1.50, 1.50)]
    [InlineData("Popcorn", null, 1.00, 2.00)]
    [InlineData("Snickers", "Candy", 2.50, 2.50)]
    [InlineData("Airheads Extreme", "Candy", 2.50, 2.50)]
    public void Prices_match_the_printed_menus(string name, string? group, double indoor, double outdoor)
    {
        var item = ItemNamed(name, group);
        Assert.Equal((decimal)indoor, Pricing.Resolve(item.Id, Inside).Amount);
        Assert.Equal((decimal)outdoor, Pricing.Resolve(item.Id, Stadium).Amount);
        Assert.Equal((decimal)outdoor, Pricing.Resolve(item.Id, Fields).Amount);
    }

    [Fact]
    public void Every_item_has_a_price_at_every_stand()
    {
        foreach (var item in Catalog.Items)
            foreach (var stand in new[] { Stadium, Fields, Inside })
                Assert.True(Pricing.Resolve(item.Id, stand).HasPrice, $"{item.Name} has no price at {stand}");
    }

    private static HashSet<string> MenuItems(string name, string stand) =>
        Menus.Menus.Single(m => m.Name == name && m.StandId == stand).ItemIds
            .Select(id => Catalog.Items.Single(i => i.Id == id))
            .Select(i => i.SubCategoryId is null ? i.Name : $"{Catalog.SubCategories.Single(s => s.Id == i.SubCategoryId).Name}: {i.Name}")
            .ToHashSet();

    [Fact]
    public void Menus_are_assigned_to_the_right_stands()
    {
        Assert.Equal(new[] { "Basketball, Volleyball, and Wrestling" }, Menus.Menus.Where(m => m.StandId == Inside).Select(m => m.Name));
        Assert.Equal(new[] { "Outdoor Concessions", "Varsity Football" }, Menus.Menus.Where(m => m.StandId == Stadium).Select(m => m.Name).OrderBy(n => n));
        Assert.Equal(new[] { "Outdoor Concessions" }, Menus.Menus.Where(m => m.StandId == Fields).Select(m => m.Name));
    }

    [Fact]
    public void Each_menu_has_exactly_its_printed_differences()
    {
        var indoor = MenuItems("Basketball, Volleyball, and Wrestling", Inside);
        var outdoor = MenuItems("Outdoor Concessions", Fields);
        var varsity = MenuItems("Varsity Football", Stadium);

        Assert.Equal(outdoor, MenuItems("Outdoor Concessions", Stadium));

        // Indoor: Red Bull regular and diet; no grill, no Pickle Pop.
        Assert.Contains("Red Bull: Diet", indoor);
        Assert.DoesNotContain("Hamburger", indoor);
        Assert.DoesNotContain("Cheeseburger", indoor);
        Assert.DoesNotContain("Pickle Pop", indoor);

        // Outdoor: grill items and Pickle Pop; only regular Red Bull; no Chick-fil-A or pizza.
        Assert.Contains("Hamburger", outdoor);
        Assert.Contains("Pickle Pop", outdoor);
        Assert.DoesNotContain("Red Bull: Diet", outdoor);
        Assert.DoesNotContain("Chick-fil-A Sandwich", outdoor);
        Assert.DoesNotContain("Pizza: Cheese", outdoor);

        // Varsity: Chick-fil-A, pizza, Airheads Extreme instead of Airheads.
        Assert.Contains("Chick-fil-A Sandwich", varsity);
        Assert.Contains("Pizza: Pepperoni", varsity);
        Assert.Contains("Candy: Airheads Extreme", varsity);
        Assert.DoesNotContain("Candy: Airheads", varsity);
        Assert.Contains("Candy: Airheads", outdoor);

        Assert.Equal(10, indoor.Count(n => n.StartsWith("Soft Drinks:")));
        Assert.Equal(5, varsity.Count(n => n.StartsWith("Gatorade:")));
        Assert.Equal(18, varsity.Count(n => n.StartsWith("Candy:")));
    }
}
