using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Web.Pages.Admin;

public sealed class IndexModel : AdminPageModel
{
    public sealed record StandCard(Stand Stand, string? Opening, string? Closing, string? Message, int Selling, int Low, int SoldOut);

    public List<StandCard> Cards { get; private set; } = new();

    public bool CanControl(string standId) =>
        Can(Functions.OpenCloseStand, standId) || Can(Functions.SetStandItems, standId) ||
        Can(Functions.MarkItemStatus, standId) || Can(Functions.ApplyPresetMenu, standId) || Can(Functions.SetStandTimes, standId);

    public void OnGet()
    {
        var catalog = Store.Read<CatalogFile>(DataFileNames.Catalog);
        var settings = Settings;
        foreach (var s in Store.Read<StandsFile>(DataFileNames.Stands).Stands.Where(s => !s.Archived).OrderBy(s => s.SortOrder))
        {
            var onMenu = catalog.Items.Where(i => StandLogic.IsOnMenu(s, i, catalog)).Select(i => StandLogic.GetItem(s, i.Id)).ToList();
            var phase = StandLogic.Phase(s, DateTime.UtcNow, settings.OpeningSoonMinutes);
            var opening = s.State.OpeningAt is { } o ? Time.FormatUtcTime(o) : null;
            var message = phase switch
            {
                StandPhase.Open => null,
                StandPhase.OpeningAt => TextTemplates.Fill(settings.Text.OpeningAt, settings, s.Name, opening),
                StandPhase.OpeningSoon => TextTemplates.Fill(settings.Text.OpeningSoon, settings, s.Name),
                _ => TextTemplates.Fill(settings.Text.Closed, settings, s.Name),
            };
            Cards.Add(new StandCard(s, opening, s.State.ClosingAt is { } c ? Time.FormatUtcTime(c) : null, message,
                onMenu.Count, onMenu.Count(x => x.Status == ItemStatus.Low), onMenu.Count(x => x.Status == ItemStatus.SoldOut)));
        }
    }
}
