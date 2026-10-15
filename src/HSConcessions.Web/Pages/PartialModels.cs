using HSConcessions.Web.Models;

namespace HSConcessions.Web.Pages;

public sealed record CatalogActionsModel(string Type, string Id, string Name, bool CanEdit, bool CanArchive);

public sealed record CatalogItemRowModel(Item Item, bool CanEdit, bool CanArchive);
