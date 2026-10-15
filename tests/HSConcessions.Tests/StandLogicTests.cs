using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Tests;

public class StandLogicTests
{
    private static CatalogFile Catalog() => new()
    {
        Categories = { new Category { Id = "cat-food", Name = "Food" } },
        Items =
        {
            new Item { Id = "itm-hotdog", CategoryId = "cat-food", Name = "Hot Dog" },
            new Item { Id = "itm-cfa", CategoryId = "cat-food", Name = "Chick-fil-A Sandwich" },
            new Item { Id = "itm-nachos", CategoryId = "cat-food", Name = "Nachos" },
            new Item { Id = "itm-old", CategoryId = "cat-food", Name = "Old", Archived = true },
        },
    };

    private static readonly PresetMenu Varsity = new() { Id = "menu-varsity", StandId = "football", ItemIds = { "itm-hotdog", "itm-cfa", "itm-nachos" } };
    private static readonly PresetMenu Jv = new() { Id = "menu-jv", StandId = "football", ItemIds = { "itm-hotdog", "itm-nachos", "itm-old" } };

    [Fact]
    public void Apply_menu_sells_exactly_its_items_and_resets_status()
    {
        var stand = new Stand { Id = "football" };
        StandLogic.ApplyMenu(stand, Varsity, Catalog());
        StandLogic.SetItem(stand, "itm-nachos", null, ItemStatus.SoldOut);

        StandLogic.ApplyMenu(stand, Jv, Catalog());
        Assert.True(stand.State.Items["itm-hotdog"].Selling);
        Assert.False(stand.State.Items["itm-cfa"].Selling);
        Assert.Equal(ItemStatus.Ok, stand.State.Items["itm-nachos"].Status);
        Assert.False(stand.State.Items["itm-old"].Selling); // archived items never sell
        Assert.Equal("menu-jv", stand.State.LastAppliedMenuId);
    }

    [Fact]
    public void Items_can_be_toggled_after_a_preset()
    {
        var stand = new Stand { Id = "football" };
        StandLogic.ApplyMenu(stand, Jv, Catalog());
        StandLogic.SetItem(stand, "itm-cfa", true, null);
        Assert.True(StandLogic.IsOnMenu(stand, Catalog().Items.First(i => i.Id == "itm-cfa"), Catalog()));
    }

    [Fact]
    public void Close_resets_items_and_times_but_keeps_last_menu()
    {
        var stand = new Stand { Id = "football" };
        StandLogic.ApplyMenu(stand, Varsity, Catalog());
        StandLogic.Open(stand);
        stand.State.OpeningAt = DateTime.UtcNow;
        stand.State.ClosingAt = DateTime.UtcNow.AddHours(3);
        StandLogic.SetItem(stand, "itm-nachos", null, ItemStatus.Low);

        StandLogic.Close(stand);

        Assert.False(stand.State.IsOpen);
        Assert.Null(stand.State.OpeningAt);
        Assert.Null(stand.State.ClosingAt);
        Assert.All(stand.State.Items.Values, s => { Assert.False(s.Selling); Assert.Equal(ItemStatus.Ok, s.Status); });
        Assert.Equal("menu-varsity", stand.State.LastAppliedMenuId);
    }

    [Fact]
    public void Phases_follow_the_lifecycle_table()
    {
        var now = new DateTime(2026, 10, 16, 21, 0, 0, DateTimeKind.Utc);
        var stand = new Stand();
        Assert.Equal(StandPhase.Closed, StandLogic.Phase(stand, now, 60));

        stand.State.OpeningAt = now.AddMinutes(61);
        Assert.Equal(StandPhase.Closed, StandLogic.Phase(stand, now, 60));

        stand.State.OpeningAt = now.AddMinutes(59);
        Assert.Equal(StandPhase.OpeningAt, StandLogic.Phase(stand, now, 60));

        stand.State.OpeningAt = now.AddMinutes(-1);
        Assert.Equal(StandPhase.OpeningSoon, StandLogic.Phase(stand, now, 60));

        StandLogic.Open(stand);
        Assert.Equal(StandPhase.Open, StandLogic.Phase(stand, now, 60));
    }

    [Fact]
    public void Times_never_open_a_stand_automatically()
    {
        var stand = new Stand();
        stand.State.OpeningAt = DateTime.UtcNow.AddHours(-2);
        Assert.False(stand.State.IsOpen);
        Assert.Equal(StandPhase.OpeningSoon, StandLogic.Phase(stand, DateTime.UtcNow, 60));
    }

    [Fact]
    public void Archived_parent_category_hides_items()
    {
        var catalog = Catalog();
        var stand = new Stand();
        StandLogic.ApplyMenu(stand, Varsity, catalog);
        catalog.Categories[0].Archived = true;
        Assert.False(StandLogic.IsOnMenu(stand, catalog.Items[0], catalog));
    }

    [Theory]
    [InlineData("softball", null)]
    [InlineData("admin", "reserved")]
    [InlineData("theme.css", "lowercase")]
    [InlineData("Football", "lowercase")]
    [InlineData("foot ball", "lowercase")]
    [InlineData("football", "already")]
    public void Slug_validation(string slug, string? errorContains)
    {
        var stands = new[] { new Stand { Id = "football", Slug = "football" } };
        var err = StandLogic.ValidateSlug(slug, null, stands);
        if (errorContains is null) Assert.Null(err);
        else Assert.Contains(errorContains, err, StringComparison.OrdinalIgnoreCase);
    }
}
