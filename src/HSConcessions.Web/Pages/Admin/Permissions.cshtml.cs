using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Web.Pages.Admin;

public sealed class PermissionsModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.EditPermissionMatrix;

    public PermissionsFile Matrix { get; private set; } = new();

    public void OnGet() => Matrix = Store.Read<PermissionsFile>(DataFileNames.Permissions);

    public bool IsChecked(string function, string role) =>
        Functions.IsLockedOn(function, role) || (Matrix.Matrix.TryGetValue(function, out var roles) && roles.Contains(role));

    public bool IsDisabled(string function, string role) =>
        Functions.IsLockedOn(function, role) || (Functions.IsSystemAdminOnlyRow(function) && Access?.IsSystemAdmin != true);
}
