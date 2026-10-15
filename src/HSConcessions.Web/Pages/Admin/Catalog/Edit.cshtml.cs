using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin.Catalog;

public sealed class EditModel : AdminPageModel
{
    public sealed class OptionInput
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public decimal PriceDelta { get; set; }
        public bool Archived { get; set; }
    }

    public sealed class Input
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? CategoryId { get; set; }
        public string? SubCategoryId { get; set; }
        public List<OptionInput> Options { get; set; } = new();
    }

    [BindProperty(SupportsGet = true)] public string Type { get; set; } = "item";
    [BindProperty(SupportsGet = true)] public string? Id { get; set; }
    [BindProperty] public Input Form { get; set; } = new();

    public CatalogFile Catalog { get; private set; } = new();
    public string? ImageId { get; private set; }
    public bool IsNew => string.IsNullOrEmpty(Id);
    public bool CanEdit => Can(Functions.ManageCatalog);
    public bool CanPicture => Can(Functions.UploadPictures);
    public bool CanArchive => Can(Functions.ArchiveCatalog);
    public List<ItemOption> ArchivedOptions { get; private set; } = new();

    public string TypeLabel => Type switch { "category" => "Category", "subCategory" => "Sub-category", _ => "Item" };

    public IActionResult OnGet(string? categoryId)
    {
        if (Type is not ("category" or "subCategory" or "item")) return NotFound();
        if (!CanEdit && !CanPicture) return Forbid();
        Catalog = Store.Read<CatalogFile>(DataFileNames.Catalog);
        if (IsNew)
        {
            if (!CanEdit) return Forbid();
            Form.CategoryId = categoryId ?? Catalog.Categories.FirstOrDefault(c => !c.Archived)?.Id;
            return Page();
        }
        switch (Type)
        {
            case "category":
                var c = Catalog.Categories.FirstOrDefault(x => x.Id == Id);
                if (c is null) return NotFound();
                Form.Name = c.Name;
                ImageId = c.ImageId;
                break;
            case "subCategory":
                var s = Catalog.SubCategories.FirstOrDefault(x => x.Id == Id);
                if (s is null) return NotFound();
                Form.Name = s.Name;
                Form.Description = s.Description;
                Form.CategoryId = s.CategoryId;
                ImageId = s.ImageId;
                break;
            default:
                var i = Catalog.Items.FirstOrDefault(x => x.Id == Id);
                if (i is null) return NotFound();
                Form.Name = i.Name;
                Form.Description = i.Description;
                Form.CategoryId = i.CategoryId;
                Form.SubCategoryId = i.SubCategoryId;
                ImageId = i.ImageId;
                Form.Options = i.Options.Where(o => !o.Archived)
                    .Select(o => new OptionInput { Id = o.Id, Name = o.Name, PriceDelta = o.PriceDelta }).ToList();
                ArchivedOptions = i.Options.Where(o => o.Archived).ToList();
                break;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!CanEdit) return Forbid();
        var name = (Form.Name ?? "").Trim();
        if (name.Length == 0 || name.Length > 80) throw new ValidationException("Name is required (80 characters max).");
        var description = (Form.Description ?? "").Trim();
        if (description.Length > 300) throw new ValidationException("Description is too long (300 characters max).");

        object? before = null;
        string id = Id ?? "";
        await Store.UpdateAsync<CatalogFile>(DataFileNames.Catalog, f =>
        {
            switch (Type)
            {
                case "category":
                    if (IsNew)
                    {
                        id = NewId("cat", name);
                        f.Categories.Add(new Category { Id = id, Name = name, SortOrder = NextSort(f.Categories.Select(c => c.SortOrder)) });
                    }
                    else
                    {
                        var c = f.Categories.FirstOrDefault(x => x.Id == Id) ?? throw new NotFoundException("Category not found.");
                        before = new { c.Name };
                        c.Name = name;
                    }
                    break;
                case "subCategory":
                    var catId = Form.CategoryId;
                    if (f.Categories.All(c => c.Id != catId)) throw new ValidationException("Choose a category.");
                    if (IsNew)
                    {
                        id = NewId("sub", name);
                        f.SubCategories.Add(new SubCategory
                        {
                            Id = id, Name = name, Description = description, CategoryId = catId!,
                            SortOrder = NextSort(f.SubCategories.Where(s => s.CategoryId == catId).Select(s => s.SortOrder)
                                .Concat(f.Items.Where(i => i.CategoryId == catId && i.SubCategoryId == null).Select(i => i.SortOrder))),
                        });
                    }
                    else
                    {
                        var s = f.SubCategories.FirstOrDefault(x => x.Id == Id) ?? throw new NotFoundException("Sub-category not found.");
                        before = new { s.Name, s.Description, s.CategoryId };
                        s.Name = name;
                        s.Description = description;
                        if (s.CategoryId != catId)
                        {
                            s.CategoryId = catId!;
                            foreach (var it in f.Items.Where(i => i.SubCategoryId == s.Id)) it.CategoryId = catId!;
                        }
                    }
                    break;
                default:
                    var cid = Form.CategoryId;
                    var subId = string.IsNullOrEmpty(Form.SubCategoryId) ? null : Form.SubCategoryId;
                    if (f.Categories.All(c => c.Id != cid)) throw new ValidationException("Choose a category.");
                    if (subId is not null)
                    {
                        var sub = f.SubCategories.FirstOrDefault(s => s.Id == subId) ?? throw new ValidationException("Sub-category not found.");
                        if (sub.CategoryId != cid) throw new ValidationException("That sub-category belongs to a different category.");
                    }
                    Item item;
                    if (IsNew)
                    {
                        id = NewId("itm", name);
                        item = new Item
                        {
                            Id = id, CategoryId = cid!, SubCategoryId = subId,
                            SortOrder = NextSort(subId is null
                                ? f.Items.Where(i => i.CategoryId == cid && i.SubCategoryId == null).Select(i => i.SortOrder)
                                    .Concat(f.SubCategories.Where(s => s.CategoryId == cid).Select(s => s.SortOrder))
                                : f.Items.Where(i => i.SubCategoryId == subId).Select(i => i.SortOrder)),
                        };
                        f.Items.Add(item);
                    }
                    else
                    {
                        item = f.Items.FirstOrDefault(x => x.Id == Id) ?? throw new NotFoundException("Item not found.");
                        before = new { item.Name, item.Description, item.CategoryId, item.SubCategoryId, options = item.Options.Select(o => o.Name).ToList() };
                    }
                    item.Name = name;
                    item.Description = description;
                    item.CategoryId = cid!;
                    item.SubCategoryId = subId;
                    // Options: update existing, add new non-empty rows. Archived options are kept as-is.
                    foreach (var o in Form.Options)
                    {
                        var oname = (o.Name ?? "").Trim();
                        var existing = o.Id is null ? null : item.Options.FirstOrDefault(x => x.Id == o.Id);
                        if (existing is not null)
                        {
                            if (oname.Length == 0) continue;
                            existing.Name = oname;
                            existing.PriceDelta = Math.Round(o.PriceDelta, 2);
                            if (o.Archived) existing.Archived = true;
                        }
                        else if (oname.Length > 0)
                        {
                            item.Options.Add(new ItemOption { Id = NewId("opt", oname), Name = oname, PriceDelta = Math.Round(o.PriceDelta, 2) });
                        }
                    }
                    break;
            }
        });
        await Audit.LogAsync(IsNew ? "Create" : "Update", Type, id, name, new { from = before, to = new { name, description, Form.CategoryId, Form.SubCategoryId } });
        Flash = $"Saved {name}.";
        return Redirect($"/admin/catalog/edit/{Type}/{id}");
    }

    public async Task<IActionResult> OnPostPictureAsync(IFormFile? picture)
    {
        if (!CanPicture) return Forbid();
        if (picture is null || picture.Length == 0) throw new ValidationException("Choose a picture to upload.");
        if (picture.Length > ImageService.MaxUploadBytes) throw new ValidationException("Image is larger than 15 MB.");
        string imageId;
        await using (var s = picture.OpenReadStream())
            imageId = await Svc<ImageService>().SaveCatalogImageAsync(s, picture.ContentType);
        var (name, previous) = await SetImage(imageId);
        await Audit.LogAsync("SetPicture", Type, Id!, name, new { from = previous, to = imageId });
        Flash = "Picture updated.";
        return Redirect($"/admin/catalog/edit/{Type}/{Id}");
    }

    public async Task<IActionResult> OnPostRemovePictureAsync()
    {
        if (!CanPicture) return Forbid();
        var (name, previous) = await SetImage(null);
        await Audit.LogAsync("RemovePicture", Type, Id!, name, new { from = previous, to = (string?)null });
        Flash = "Picture removed.";
        return Redirect($"/admin/catalog/edit/{Type}/{Id}");
    }

    public async Task<IActionResult> OnPostArchiveOptionAsync(string optionId, bool archived)
    {
        if (!CanArchive) return Forbid();
        var name = await CatalogArchive.SetArchivedAsync(Store, "item", Id!, archived, optionId);
        await Audit.LogAsync(archived ? "Archive" : "Restore", "option", $"{Id}/{optionId}", name, new { from = !archived, to = archived });
        Flash = archived ? $"Archived option {name}." : $"Restored option {name}.";
        return Redirect($"/admin/catalog/edit/{Type}/{Id}");
    }

    private async Task<(string name, string? previous)> SetImage(string? imageId)
    {
        if (string.IsNullOrEmpty(Id)) throw new ValidationException("Save the entry first.");
        return await Store.UpdateAsync<CatalogFile, (string, string?)>(DataFileNames.Catalog, f =>
        {
            switch (Type)
            {
                case "category":
                    var c = f.Categories.FirstOrDefault(x => x.Id == Id) ?? throw new NotFoundException("Not found.");
                    var pc = c.ImageId; c.ImageId = imageId; return (c.Name, pc);
                case "subCategory":
                    var s = f.SubCategories.FirstOrDefault(x => x.Id == Id) ?? throw new NotFoundException("Not found.");
                    var ps = s.ImageId; s.ImageId = imageId; return (s.Name, ps);
                default:
                    var i = f.Items.FirstOrDefault(x => x.Id == Id) ?? throw new NotFoundException("Not found.");
                    var pi = i.ImageId; i.ImageId = imageId; return (i.Name, pi);
            }
        });
    }

    private static int NextSort(IEnumerable<int> existing) => (existing.DefaultIfEmpty(0).Max() / 10 + 1) * 10;
}
