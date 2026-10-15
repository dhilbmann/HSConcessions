using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Tests;

public class PricingServiceTests
{
    private static CatalogFile Catalog() => new()
    {
        Categories = { new Category { Id = "cat-bev", Name = "Beverages" }, new Category { Id = "cat-food", Name = "Food" } },
        SubCategories = { new SubCategory { Id = "sub-soda", CategoryId = "cat-bev", Name = "Soda" } },
        Items =
        {
            new Item { Id = "itm-pepsi", CategoryId = "cat-bev", SubCategoryId = "sub-soda", Name = "Pepsi" },
            new Item { Id = "itm-water", CategoryId = "cat-bev", Name = "Water" },
            new Item { Id = "itm-hotdog", CategoryId = "cat-food", Name = "Hot Dog" },
        },
    };

    private static readonly Stand[] Stands = { new() { Id = "football", Name = "Football" }, new() { Id = "baseball", Name = "Baseball" } };

    private static PriceEntry P(string level, string target, decimal amount, string? stand = null) =>
        new() { Level = level, TargetId = target, StandId = stand, Amount = amount };

    /// <summary>Every level set; removing them one at a time walks down the resolution order.</summary>
    [Fact]
    public void Resolution_follows_all_seven_steps_in_order()
    {
        var prices = new PricesFile
        {
            Prices =
            {
                P(PriceLevel.Item, "itm-pepsi", 1.01m, "baseball"),     // 1
                P(PriceLevel.Item, "itm-pepsi", 1.02m),                 // 2
                P(PriceLevel.SubCategory, "sub-soda", 1.03m, "baseball"), // 3
                P(PriceLevel.SubCategory, "sub-soda", 1.04m),           // 4
                P(PriceLevel.Category, "cat-bev", 1.05m, "baseball"),   // 5
                P(PriceLevel.Category, "cat-bev", 1.06m),               // 6
            },
        };
        var expected = new[] { 1.01m, 1.02m, 1.03m, 1.04m, 1.05m, 1.06m };
        for (var step = 0; step < expected.Length; step++)
        {
            var r = new PricingService(Catalog(), prices, Stands).Resolve("itm-pepsi", "baseball");
            Assert.Equal(expected[step], r.Amount);
            prices.Prices.RemoveAt(0);
        }
        // 7: nothing left → no price
        var none = new PricingService(Catalog(), prices, Stands).Resolve("itm-pepsi", "baseball");
        Assert.Null(none.Amount);
        Assert.False(none.HasPrice);
    }

    [Fact]
    public void Step1_item_price_for_stand_beats_item_default()
    {
        var prices = new PricesFile { Prices = { P(PriceLevel.Item, "itm-pepsi", 2m), P(PriceLevel.Item, "itm-pepsi", 3m, "baseball") } };
        var svc = new PricingService(Catalog(), prices, Stands);
        Assert.Equal(3m, svc.Resolve("itm-pepsi", "baseball").Amount);
        Assert.Equal(2m, svc.Resolve("itm-pepsi", "football").Amount);
    }

    [Fact]
    public void Step2_item_default_beats_subcategory_stand_price()
    {
        var prices = new PricesFile { Prices = { P(PriceLevel.SubCategory, "sub-soda", 9m, "baseball"), P(PriceLevel.Item, "itm-pepsi", 2m) } };
        Assert.Equal(2m, new PricingService(Catalog(), prices, Stands).Resolve("itm-pepsi", "baseball").Amount);
    }

    [Fact]
    public void Step3_and_4_subcategory_stand_then_default()
    {
        var prices = new PricesFile { Prices = { P(PriceLevel.SubCategory, "sub-soda", 2.50m), P(PriceLevel.SubCategory, "sub-soda", 3.00m, "baseball") } };
        var svc = new PricingService(Catalog(), prices, Stands);
        Assert.Equal(2.50m, svc.Resolve("itm-pepsi", "football").Amount);
        Assert.Equal(3.00m, svc.Resolve("itm-pepsi", "baseball").Amount);
        Assert.Equal("Soda @ Baseball", svc.Resolve("itm-pepsi", "baseball").SourceName);
    }

    [Fact]
    public void Step5_and_6_category_stand_then_default()
    {
        var prices = new PricesFile { Prices = { P(PriceLevel.Category, "cat-bev", 2m), P(PriceLevel.Category, "cat-bev", 2.25m, "football") } };
        var svc = new PricingService(Catalog(), prices, Stands);
        Assert.Equal(2.25m, svc.Resolve("itm-water", "football").Amount);
        Assert.Equal(2m, svc.Resolve("itm-water", "baseball").Amount);
        Assert.Equal(2.25m, svc.Resolve("itm-pepsi", "football").Amount); // falls through the empty sub-category
    }

    [Fact]
    public void Step7_no_price_when_nothing_matches()
    {
        var prices = new PricesFile { Prices = { P(PriceLevel.Category, "cat-bev", 2m) } };
        var r = new PricingService(Catalog(), prices, Stands).Resolve("itm-hotdog", "football");
        Assert.False(r.HasPrice);
    }

    [Fact]
    public void Another_stands_override_is_ignored()
    {
        var prices = new PricesFile { Prices = { P(PriceLevel.Item, "itm-water", 5m, "baseball"), P(PriceLevel.Category, "cat-bev", 2m) } };
        Assert.Equal(2m, new PricingService(Catalog(), prices, Stands).Resolve("itm-water", "football").Amount);
    }

    [Fact]
    public void Sample_soda_prices_match_acceptance_criteria()
    {
        var prices = new PricesFile { Prices = { P(PriceLevel.SubCategory, "sub-soda", 2.50m), P(PriceLevel.SubCategory, "sub-soda", 3.00m, "baseball") } };
        var svc = new PricingService(Catalog(), prices, Stands);
        Assert.Equal("$2.50", PricingService.Format(svc.Resolve("itm-pepsi", "football").Amount!.Value));
        Assert.Equal("$3.00", PricingService.Format(svc.Resolve("itm-pepsi", "baseball").Amount!.Value));
    }
}
