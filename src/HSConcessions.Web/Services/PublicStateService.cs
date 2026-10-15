using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

public sealed record SiteView(string Title, string ShortName, string School, string Mascot, string Tagline, string Footer,
    string LogoUrl, string ClosedImageUrl, string TvBackgroundUrl, string TimeZone);

public sealed record TextView(string LowBadge, string SoldOutStamp, string NoStandsOpen, string Closed, string OpeningSoon);

public sealed record OptionView(string Name, string? DeltaText);

public sealed record ItemView(string Id, string Name, string Description, string ImageUrl, string ThumbUrl,
    decimal? Price, string PriceText, bool ShowPrice, string Status, IReadOnlyList<OptionView> Options, string? GroupName);

public sealed record GroupView(string Kind, string Id, string Name, string Description, string ImageUrl, string? PriceText,
    IReadOnlyList<ItemView> Items);

public sealed record CategoryView(string Id, string Name, string ImageUrl, IReadOnlyList<GroupView> Groups);

public sealed record StandView(string Id, string Name, string Slug, int SortOrder, bool IsOpen, string Phase,
    string Message, string? Notice, string? OpenUntil, string? Banner, IReadOnlyList<CategoryView> Categories);

public sealed record GameView(string Date, string Time, string Title, string Level, string Sport, bool Home, string Location);

public sealed record PublicState(DateTime ServerTime, SiteView Site, TextView Text, int RefreshSeconds,
    IReadOnlyList<StandView> Stands, IReadOnlyList<string> Notices, IReadOnlyList<GameView> UpcomingGames);

public sealed record DisplayState(DateTime ServerTime, SiteView Site, TextView Text, int CategorySeconds, int RefreshSeconds,
    string Layout, bool GroupVarieties, bool ShowClock, bool ShowUpdatedTime, DateTime DataUpdatedAt, string DataUpdatedText,
    StandView Stand);

/// <summary>Builds the public JSON for the phone site and TV display. Never exposes users, audit, or archived entries.</summary>
public sealed class PublicStateService(JsonStore store, CalendarService calendar, FeatureOptions features)
{
    public const string PlaceholderLogo = "/img/placeholder-logo.svg";

    public async Task<PublicState> BuildPublicAsync(CancellationToken ct = default)
    {
        var ctx = Load();
        var stands = ctx.Stands.Stands.Where(s => !s.Archived).OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => BuildStand(ctx, s, includeSoldOut: false))
            .ToList();

        var notices = stands.Where(s => s.Notice is not null).Select(s => s.Notice!).ToList();
        var zone = TimeService.ZoneFor(ctx.Settings.TimeZone);
        var games = (await calendar.GetUpcomingAsync(ctx.Settings, ct)).Select(g =>
        {
            var local = TimeService.ToLocal(g.StartsAt, zone);
            return new GameView(TimeService.FormatDate(local), TimeService.FormatTime(local), g.Title, g.Level, g.Sport, g.Home, g.Location);
        }).ToList();

        return new PublicState(DateTime.UtcNow, Site(ctx.Settings), Text(ctx.Settings),
            Math.Clamp(ctx.Settings.Public.RefreshSeconds, 15, 3600), stands, notices, games);
    }

    /// <summary>Display data for one stand (by id or slug, case-insensitive), including sold-out items.</summary>
    public DisplayState? BuildDisplay(string standKey, string? layoutOverride = null)
    {
        var ctx = Load();
        var stand = ctx.Stands.Stands.FirstOrDefault(s => !s.Archived &&
            (string.Equals(s.Id, standKey, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Slug, standKey, StringComparison.OrdinalIgnoreCase)));
        if (stand is null) return null;
        return new DisplayState(DateTime.UtcNow, Site(ctx.Settings), Text(ctx.Settings),
            Math.Clamp(ctx.Settings.Display.CategorySeconds, 3, 600),
            Math.Clamp(ctx.Settings.Display.RefreshSeconds, 15, 3600),
            DisplayLayouts.Resolve(layoutOverride, stand.DisplayLayout, ctx.Settings.Display.Layout),
            ctx.Settings.Display.GroupVarieties,
            ctx.Settings.Display.ShowClock,
            ctx.Settings.Display.ShowUpdatedTime,
            ctx.DataUpdatedAt,
            UpdatedText(ctx.DataUpdatedAt, TimeService.ZoneFor(ctx.Settings.TimeZone), DateTime.UtcNow),
            BuildStand(ctx, stand, includeSoldOut: true));
    }

    private sealed record Ctx(SiteSettings Settings, StandsFile Stands, CatalogFile Catalog, PricingService Pricing, DateTime DataUpdatedAt);

    /// <summary>"Menu updated 7:42 PM" today, otherwise "Menu updated Oct 14, 7:42 PM".</summary>
    public static string UpdatedText(DateTime updatedUtc, TimeZoneInfo zone, DateTime nowUtc)
    {
        var local = TimeService.ToLocal(updatedUtc, zone);
        var today = TimeService.ToLocal(nowUtc, zone).Date;
        var us = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        return "Menu updated " + (local.Date == today ? local.ToString("h:mm tt", us) : local.ToString("MMM d, h:mm tt", us));
    }

    private Ctx Load()
    {
        var settings = store.Read<SiteSettings>(DataFileNames.Settings);
        var stands = store.Read<StandsFile>(DataFileNames.Stands);
        var catalog = store.Read<CatalogFile>(DataFileNames.Catalog);
        var prices = store.Read<PricesFile>(DataFileNames.Prices);
        // When the menu data last changed: the newest of the files that feed the menu.
        var updated = new[] { settings.UpdatedAt, stands.UpdatedAt, catalog.UpdatedAt, prices.UpdatedAt }.Max();
        return new Ctx(settings, stands, catalog, new PricingService(catalog, prices, stands.Stands), DateTime.SpecifyKind(updated, DateTimeKind.Utc));
    }

    public static SiteView Site(SiteSettings s)
    {
        var logo = string.IsNullOrEmpty(s.Images.Logo) ? PlaceholderLogo : ImageService.FileUrl(s.Images.Logo);
        var closed = string.IsNullOrEmpty(s.Images.Mascot) ? logo : ImageService.FileUrl(s.Images.Mascot);
        return new SiteView(s.SiteTitle, s.SiteShortName, s.SchoolName, s.MascotName,
            TextTemplates.Fill(s.Tagline, s), TextTemplates.Fill(s.Text.Footer, s),
            logo, closed, ImageService.FileUrl(s.Images.TvBackground), s.TimeZone);
    }

    private static TextView Text(SiteSettings s) => new(
        TextTemplates.Fill(s.Text.LowBadge, s),
        TextTemplates.Fill(s.Text.SoldOutStamp, s),
        TextTemplates.Fill(s.Text.NoStandsOpen, s),
        TextTemplates.Fill(s.Text.Closed, s),
        TextTemplates.Fill(s.Text.OpeningSoon, s));

    private StandView BuildStand(Ctx ctx, Stand stand, bool includeSoldOut)
    {
        var s = ctx.Settings;
        var zone = TimeService.ZoneFor(s.TimeZone);
        var phase = StandLogic.Phase(stand, DateTime.UtcNow, s.OpeningSoonMinutes);
        var openingText = stand.State.OpeningAt is { } o ? TimeService.FormatTime(TimeService.ToLocal(o, zone)) : null;
        var closingText = stand.State.ClosingAt is { } c ? TimeService.FormatTime(TimeService.ToLocal(c, zone)) : null;

        var message = phase switch
        {
            StandPhase.Open => "",
            StandPhase.OpeningAt => TextTemplates.Fill(s.Text.OpeningAt, s, stand.Name, openingText),
            StandPhase.OpeningSoon => TextTemplates.Fill(s.Text.OpeningSoon, s, stand.Name),
            _ => TextTemplates.Fill(s.Text.Closed, s, stand.Name),
        };
        var notice = phase switch
        {
            StandPhase.OpeningAt => $"{stand.Name} stand opens at {openingText}",
            StandPhase.OpeningSoon => $"{stand.Name} stand opening soon",
            _ => null,
        };
        var openUntil = phase == StandPhase.Open && closingText is not null ? $"Open until {closingText}" : null;
        var banner = TextTemplates.Fill(s.Text.SingleStandBanner, s, stand.Name);
        var categories = stand.State.IsOpen ? BuildMenu(ctx, stand, includeSoldOut) : new List<CategoryView>();

        return new StandView(stand.Id, stand.Name, stand.Slug, stand.SortOrder, stand.State.IsOpen,
            PhaseName(phase), message, notice, openUntil, banner, categories);
    }

    public static string PhaseName(StandPhase p) => p switch
    {
        StandPhase.Open => "open",
        StandPhase.OpeningAt => "openingAt",
        StandPhase.OpeningSoon => "openingSoon",
        _ => "closed",
    };

    private List<CategoryView> BuildMenu(Ctx ctx, Stand stand, bool includeSoldOut)
    {
        var catalog = ctx.Catalog;
        var result = new List<CategoryView>();

        ItemView? ToView(Item item, string? groupName)
        {
            if (!StandLogic.IsOnMenu(stand, item, catalog)) return null;
            var state = StandLogic.GetItem(stand, item.Id);
            if (!includeSoldOut && state.Status == ItemStatus.SoldOut) return null;
            var price = ctx.Pricing.Resolve(item.Id, stand.Id).Amount;
            var options = item.Options.Where(o => !o.Archived)
                .Select(o => new OptionView(o.Name, features.OptionPricing && o.PriceDelta != 0 ? PricingService.FormatDelta(o.PriceDelta) : null))
                .ToList();
            return new ItemView(item.Id, item.Name, item.Description, ImageService.CatalogUrl(item.ImageId), ImageService.ThumbUrl(item.ImageId),
                price, price is { } p ? PricingService.Format(p) : "", true, state.Status, options, groupName);
        }

        foreach (var cat in catalog.Categories.Where(c => !c.Archived).OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            var entries = new List<(int sort, string name, GroupView group)>();

            foreach (var item in catalog.Items.Where(i => i.CategoryId == cat.Id && i.SubCategoryId is null))
            {
                var view = ToView(item, null);
                if (view is not null)
                    entries.Add((item.SortOrder, item.Name, new GroupView("item", item.Id, item.Name, item.Description, view.ImageUrl, null, new[] { view })));
            }

            foreach (var sub in catalog.SubCategories.Where(sc => sc.CategoryId == cat.Id && !sc.Archived))
            {
                var items = catalog.Items.Where(i => i.SubCategoryId == sub.Id)
                    .OrderBy(i => i.SortOrder).ThenBy(i => i.Name)
                    .Select(i => ToView(i, sub.Name)).Where(v => v is not null).Cast<ItemView>().ToList();
                if (items.Count == 0) continue;

                // Show a shared price once on the header; varieties that differ show theirs inline.
                var common = items.Where(i => i.Price.HasValue).GroupBy(i => i.Price!.Value)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();
                string? groupPrice = null;
                if (common is not null && (common.Count() >= 2 || items.Count == 1))
                {
                    groupPrice = PricingService.Format(common.Key);
                    items = items.Select(i => i with { ShowPrice = i.Price != common.Key }).ToList();
                }
                entries.Add((sub.SortOrder, sub.Name, new GroupView("sub", sub.Id, sub.Name, sub.Description, ImageService.CatalogUrl(sub.ImageId), groupPrice, items)));
            }

            if (entries.Count == 0) continue;
            result.Add(new CategoryView(cat.Id, cat.Name, ImageService.ThumbUrl(cat.ImageId),
                entries.OrderBy(e => e.sort).ThenBy(e => e.name).Select(e => e.group).ToList()));
        }
        return result;
    }
}
