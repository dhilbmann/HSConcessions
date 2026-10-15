using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin.Catalog;

public sealed class ArchiveModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.ArchiveCatalog;

    public CatalogFile Catalog { get; private set; } = new();

    public void OnGet() => Catalog = Store.Read<CatalogFile>(DataFileNames.Catalog);

    public async Task<IActionResult> OnPostRestoreAsync(string type, string id)
    {
        var name = await CatalogArchive.SetArchivedAsync(Store, type, id, false);
        await Audit.LogAsync("Restore", type, id, name, new { from = true, to = false });
        return Done($"Restored {name}.");
    }

    public string CategoryName(string id) => Catalog.Categories.FirstOrDefault(c => c.Id == id)?.Name ?? "?";
}
