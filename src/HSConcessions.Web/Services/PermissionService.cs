using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

/// <summary>The functions (rows) of the permission matrix.</summary>
public static class Functions
{
    public const string ViewDashboard = "ViewDashboard";
    public const string OpenCloseStand = "OpenCloseStand";
    public const string SetStandTimes = "SetStandTimes";
    public const string SetStandItems = "SetStandItems";
    public const string MarkItemStatus = "MarkItemStatus";
    public const string ApplyPresetMenu = "ApplyPresetMenu";
    public const string ManageMenus = "ManageMenus";
    public const string ManageCatalog = "ManageCatalog";
    public const string UploadPictures = "UploadPictures";
    public const string ArchiveCatalog = "ArchiveCatalog";
    public const string SetPrices = "SetPrices";
    public const string ManageEvents = "ManageEvents";
    public const string ManageCalendar = "ManageCalendar";
    public const string ManageUsers = "ManageUsers";
    public const string ViewAudit = "ViewAudit";
    public const string DownloadLogs = "DownloadLogs";
    public const string EditSettings = "EditSettings";
    public const string ManageBackups = "ManageBackups";
    public const string EditPermissionMatrix = "EditPermissionMatrix";

    public sealed record FunctionInfo(string Key, string Label, string[] DefaultRoles, bool StandScoped = false);

    /// <summary>Rows in display order, with the default matrix (§5.2).</summary>
    public static readonly IReadOnlyList<FunctionInfo> All = new FunctionInfo[]
    {
        new(ViewDashboard, "View admin dashboard", new[] { Roles.Administrator, Roles.Pricing, Roles.Manager }),
        new(OpenCloseStand, "Open / close a stand", new[] { Roles.Manager }, true),
        new(SetStandTimes, "Set stand opening / closing time", new[] { Roles.Manager }, true),
        new(SetStandItems, "Set which items are selling at a stand", new[] { Roles.Manager }, true),
        new(MarkItemStatus, "Mark items Low / Sold Out", new[] { Roles.Manager }, true),
        new(ApplyPresetMenu, "Apply a preset menu to a stand", new[] { Roles.Pricing }, true),
        new(ManageMenus, "Create / edit / delete preset menus", new[] { Roles.Pricing }),
        new(ManageCatalog, "Add / edit categories, sub-categories, items, options", new[] { Roles.Pricing }),
        new(UploadPictures, "Upload / replace item pictures", new[] { Roles.Pricing }),
        new(ArchiveCatalog, "Archive / restore catalog entries", new[] { Roles.Pricing }),
        new(SetPrices, "Set prices (default and per-stand)", new[] { Roles.Pricing }),
        new(ManageEvents, "Manage events (only when Events are enabled)", new[] { Roles.Pricing }),
        new(ManageCalendar, "Manage game calendar", new[] { Roles.Administrator }),
        new(ManageUsers, "Manage users (add/remove, assign Pricing/Manager)", new[] { Roles.Administrator }),
        new(ViewAudit, "View audit log", new[] { Roles.Administrator }),
        new(DownloadLogs, "Download audit and application logs", new[] { Roles.Administrator }),
        new(EditSettings, "Edit site settings and generate QR codes", new[] { Roles.Administrator }),
        new(ManageBackups, "Download / restore backups", Array.Empty<string>()),
        new(EditPermissionMatrix, "Edit permission matrix", Array.Empty<string>()),
    };

    public static bool IsKnown(string key) => All.Any(f => f.Key == key);

    /// <summary>Cells that are locked on (cannot be unchecked) to prevent lockout.</summary>
    public static bool IsLockedOn(string function, string role) =>
        function == ManageUsers && role == Roles.Administrator;

    /// <summary>Rows that only a System Admin may change.</summary>
    public static bool IsSystemAdminOnlyRow(string function) => function == EditPermissionMatrix;

    public static PermissionsFile DefaultMatrix()
    {
        var file = new PermissionsFile();
        foreach (var f in All) file.Matrix[f.Key] = f.DefaultRoles.ToList();
        return file;
    }
}

/// <summary>Who the current user is, as far as permissions are concerned.</summary>
public sealed record UserAccess(string Email, string? DisplayName, bool IsSystemAdmin, IReadOnlyList<RoleAssignment> Roles)
{
    public bool HasRole(string role) => IsSystemAdmin || Roles.Any(r => r.Role == role);
}

/// <summary>
/// Permission checks: System Admin has everything; otherwise permissions are the union of the user's roles.
/// </summary>
public sealed class PermissionService
{
    private readonly JsonStore _store;
    private readonly AuthOptions _auth;
    private readonly FeatureOptions _features;

    private readonly DemoOptions _demo;

    public PermissionService(JsonStore store, AuthOptions auth, FeatureOptions features, DemoOptions? demo = null)
    {
        _demo = demo ?? new DemoOptions();
        _store = store;
        _auth = auth;
        _features = features;
    }

    public bool IsSystemAdmin(string? email) =>
        !string.IsNullOrWhiteSpace(email) &&
        _auth.SystemAdmins.Any(a => string.Equals(a.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the user's access if they are allowlisted (active user or System Admin), else null.</summary>
    public UserAccess? GetAccess(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var users = _store.Read<UsersFile>(DataFileNames.Users);
        var user = users.Users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        var sys = IsSystemAdmin(email);
        if (sys) return new UserAccess(email, user?.DisplayName ?? _auth.DevLogin.Find(email)?.Label, true, user?.Roles ?? new List<RoleAssignment>());
        if (user is null && _auth.DevLogin.Find(email) is { } persona)
        {
            // Development-only test account: roles come from config; permissions still come from the matrix.
            var roles = persona.Roles.Where(Roles.IsValid).Select(r => new RoleAssignment { Role = r }).ToList();
            return new UserAccess(persona.Email, persona.Label, false, roles);
        }
        if (user is null || !user.Active) return null;
        return new UserAccess(user.Email, user.DisplayName, false, user.Roles);
    }

    public bool Has(UserAccess? access, string function, string? standId = null)
        => Has(access, function, _store.Read<PermissionsFile>(DataFileNames.Permissions), standId);

    public bool Has(UserAccess? access, string function, PermissionsFile matrix, string? standId = null)
        => !(_demo.Enabled && DemoOptions.DisabledFunctions.Contains(function))
           && Evaluate(access, function, matrix, _features.StandScopedRoles, standId);

    /// <summary>Pure permission evaluation (unit-tested).</summary>
    public static bool Evaluate(UserAccess? access, string function, PermissionsFile matrix, bool standScoped, string? standId)
    {
        if (access is null) return false;
        if (access.IsSystemAdmin) return true;

        matrix.Matrix.TryGetValue(function, out var granted);
        granted ??= new List<string>();

        var info = Functions.All.FirstOrDefault(f => f.Key == function);
        foreach (var assignment in access.Roles)
        {
            var allowed = granted.Contains(assignment.Role) || Functions.IsLockedOn(function, assignment.Role);
            if (!allowed) continue;
            if (standScoped && info?.StandScoped == true && standId is not null
                && assignment.StandIds is { Count: > 0 }
                && !assignment.StandIds.Contains(standId, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            return true;
        }
        return false;
    }

    public HashSet<string> AllGranted(UserAccess? access)
    {
        var matrix = _store.Read<PermissionsFile>(DataFileNames.Permissions);
        return Functions.All.Where(f => Has(access, f.Key, matrix)).Select(f => f.Key).ToHashSet();
    }
}
