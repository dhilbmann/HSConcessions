using System.Net.Mail;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin;

public sealed class UsersModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.ManageUsers;

    public List<AppUser> Users { get; private set; } = new();
    public List<Stand> Stands { get; private set; } = new();
    public List<string> SystemAdmins => Svc<AuthOptions>().SystemAdmins;
    public bool IsSystemAdmin => Access?.IsSystemAdmin == true;

    public void OnGet()
    {
        Users = Store.Read<UsersFile>(DataFileNames.Users).Users
            .OrderByDescending(u => u.Active).ThenBy(u => u.Email, StringComparer.OrdinalIgnoreCase).ToList();
        Stands = Store.Read<StandsFile>(DataFileNames.Stands).Stands.Where(s => !s.Archived).OrderBy(s => s.SortOrder).ToList();
    }

    public string LastLogin(AppUser u) => u.LastLoginAt is { } t ? Time.FormatUtcDateTime(t) : "never";

    /// <summary>Only a System Admin may change users who hold (or would get) the Administrator role.</summary>
    public bool CanEditUser(AppUser u) => IsSystemAdmin || u.Roles.All(r => r.Role != Roles.Administrator);

    public async Task<IActionResult> OnPostAddAsync(string email, string? displayName, List<string>? roles)
    {
        email = (email ?? "").Trim();
        if (!IsValidEmail(email)) throw new ValidationException("Enter a valid email address.");
        var assignments = BuildRoles(roles, null);
        var me = Access!.Email;
        await Store.UpdateAsync<UsersFile>(DataFileNames.Users, f =>
        {
            var existing = f.Users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) throw new ValidationException("That email is already on the list. Edit or reactivate it below.");
            f.Users.Add(new AppUser
            {
                Email = email,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
                Active = true,
                Roles = assignments,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = me,
            });
        });
        await Audit.LogAsync("CreateUser", "User", email, email, new { roles = assignments.Select(r => r.Role) });
        return Done($"Added {email}.");
    }

    public async Task<IActionResult> OnPostRolesAsync(string email, List<string>? roles, string? displayName)
    {
        List<string> before = new();
        var after = new List<string>();
        await Store.UpdateAsync<UsersFile>(DataFileNames.Users, f =>
        {
            var u = Find(f, email);
            if (!CanEditUser(u)) throw new ValidationException("Only a System Admin can change an Administrator.");
            before = u.Roles.Select(r => r.Role).ToList();
            var form = Request.Form;
            u.Roles = BuildRoles(roles, role => form[$"stands-{role}"].Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList());
            if (displayName is not null) u.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            after = u.Roles.Select(r => r.Role).ToList();
        });
        await Audit.LogAsync("UpdateUserRoles", "User", email, email, new { from = before, to = after });
        return Done($"Updated {email}.");
    }

    public async Task<IActionResult> OnPostActiveAsync(string email, bool active)
    {
        await Store.UpdateAsync<UsersFile>(DataFileNames.Users, f =>
        {
            var u = Find(f, email);
            if (!CanEditUser(u)) throw new ValidationException("Only a System Admin can change an Administrator.");
            if (string.Equals(u.Email, Access!.Email, StringComparison.OrdinalIgnoreCase) && !active)
                throw new ValidationException("You can't deactivate yourself.");
            u.Active = active;
        });
        await Audit.LogAsync(active ? "ReactivateUser" : "DeactivateUser", "User", email, email, new { from = !active, to = active });
        return Done(active ? $"Reactivated {email}." : $"Deactivated {email}. Their record is kept for history.");
    }

    private AppUser Find(UsersFile f, string email) =>
        f.Users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)) ?? throw new NotFoundException("User not found.");

    private List<RoleAssignment> BuildRoles(List<string>? roles, Func<string, List<string>>? standsFor)
    {
        var list = new List<RoleAssignment>();
        foreach (var r in (roles ?? new()).Distinct())
        {
            if (!Models.Roles.IsValid(r)) continue;
            if (r == Models.Roles.Administrator && !IsSystemAdmin) throw new ValidationException("Only a System Admin can assign the Administrator role.");
            var stands = Features.StandScopedRoles && standsFor is not null ? standsFor(r) : new List<string>();
            list.Add(new RoleAssignment { Role = r, StandIds = stands });
        }
        return list;
    }

    private static bool IsValidEmail(string email)
    {
        if (email.Length is < 3 or > 254) return false;
        try { return new MailAddress(email).Address.Equals(email, StringComparison.OrdinalIgnoreCase); }
        catch (FormatException) { return false; }
    }
}
