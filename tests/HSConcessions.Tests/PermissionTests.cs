using System.Text.Json;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Tests;

public class PermissionTests
{
    private static UserAccess User(params string[] roles) =>
        new("pat@example.org", null, false, roles.Select(r => new RoleAssignment { Role = r }).ToList());

    [Fact]
    public void Union_of_manager_and_pricing_grants_both_sets()
    {
        var matrix = Functions.DefaultMatrix();
        var both = User(Roles.Manager, Roles.Pricing);
        Assert.True(PermissionService.Evaluate(both, Functions.OpenCloseStand, matrix, false, null));
        Assert.True(PermissionService.Evaluate(both, Functions.SetPrices, matrix, false, null));
        Assert.False(PermissionService.Evaluate(both, Functions.ManageUsers, matrix, false, null));

        var managerOnly = User(Roles.Manager);
        Assert.False(PermissionService.Evaluate(managerOnly, Functions.SetPrices, matrix, false, null));
    }

    [Fact]
    public void Roles_never_deny()
    {
        var matrix = Functions.DefaultMatrix();
        // Administrator has no stand functions, but adding it must not remove Manager's.
        var user = User(Roles.Manager, Roles.Administrator);
        Assert.True(PermissionService.Evaluate(user, Functions.MarkItemStatus, matrix, false, null));
    }

    [Fact]
    public void Unchecking_a_function_removes_access_immediately()
    {
        var matrix = Functions.DefaultMatrix();
        var user = User(Roles.Pricing);
        Assert.True(PermissionService.Evaluate(user, Functions.SetPrices, matrix, false, null));
        matrix.Matrix[Functions.SetPrices].Remove(Roles.Pricing);
        Assert.False(PermissionService.Evaluate(user, Functions.SetPrices, matrix, false, null));
    }

    [Fact]
    public void Manage_users_is_locked_on_for_administrator()
    {
        var matrix = Functions.DefaultMatrix();
        matrix.Matrix[Functions.ManageUsers].Clear();
        Assert.True(PermissionService.Evaluate(User(Roles.Administrator), Functions.ManageUsers, matrix, false, null));
    }

    [Fact]
    public void System_admin_has_everything()
    {
        var sys = new UserAccess("admin@example.org", null, true, new List<RoleAssignment>());
        var empty = new PermissionsFile();
        foreach (var f in Functions.All)
            Assert.True(PermissionService.Evaluate(sys, f.Key, empty, false, null));
    }

    [Fact]
    public void Edit_matrix_and_backups_default_to_system_admin_only()
    {
        var matrix = Functions.DefaultMatrix();
        var everyone = User(Roles.Administrator, Roles.Pricing, Roles.Manager);
        Assert.False(PermissionService.Evaluate(everyone, Functions.EditPermissionMatrix, matrix, false, null));
        Assert.False(PermissionService.Evaluate(everyone, Functions.ManageBackups, matrix, false, null));
    }

    [Fact]
    public void Stand_scoping_is_enforced_only_when_enabled()
    {
        var matrix = Functions.DefaultMatrix();
        var scoped = new UserAccess("pat@example.org", null, false,
            new List<RoleAssignment> { new() { Role = Roles.Manager, StandIds = new List<string> { "football" } } });
        Assert.True(PermissionService.Evaluate(scoped, Functions.OpenCloseStand, matrix, false, "baseball"));
        Assert.True(PermissionService.Evaluate(scoped, Functions.OpenCloseStand, matrix, true, "football"));
        Assert.False(PermissionService.Evaluate(scoped, Functions.OpenCloseStand, matrix, true, "baseball"));
    }

    private static (PermissionService svc, string dir) WithPersonas(bool development)
    {
        var dir = Path.Combine(Path.GetTempPath(), "hsc-perm-" + Guid.NewGuid().ToString("n"));
        var auth = new AuthOptions();
        auth.DevLogin.Enabled = true;
        auth.DevLogin.Personas.Add(new DevPersona { Label = "Pricing + Manager", Email = "pm@localhost.test", Roles = { Roles.Pricing, Roles.Manager } });
        auth.DevLogin.RestrictTo(development);
        var store = new JsonStore(dir);
        JsonStore.WriteAtomic(store.PathFor(DataFileNames.Permissions), Functions.DefaultMatrix());
        return (new PermissionService(store, auth, new FeatureOptions()), dir);
    }

    [Fact]
    public void Dev_personas_sign_in_with_their_configured_roles_in_development()
    {
        var (svc, dir) = WithPersonas(development: true);
        try
        {
            var access = svc.GetAccess("PM@localhost.test");
            Assert.NotNull(access);
            Assert.True(svc.Has(access, Functions.SetPrices));
            Assert.True(svc.Has(access, Functions.OpenCloseStand));
            Assert.False(svc.Has(access, Functions.ManageUsers));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Dev_personas_do_not_exist_outside_development()
    {
        var (svc, dir) = WithPersonas(development: false);
        try { Assert.Null(svc.GetAccess("pm@localhost.test")); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Null_user_has_nothing()
    {
        Assert.False(PermissionService.Evaluate(null, Functions.ViewDashboard, Functions.DefaultMatrix(), false, null));
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("wildcatsnacks")]
    public void Seed_permissions_match_the_default_matrix(string profile)
    {
        var path = Path.Combine(TestPaths.RepoRoot, "samples", profile, "permissions.json");
        var seed = JsonSerializer.Deserialize<PermissionsFile>(File.ReadAllText(path), JsonStore.JsonOptions)!;
        var defaults = Functions.DefaultMatrix();
        Assert.Equal(defaults.Matrix.Keys.OrderBy(k => k), seed.Matrix.Keys.OrderBy(k => k));
        foreach (var (fn, roles) in defaults.Matrix)
            Assert.Equal(roles.OrderBy(r => r), seed.Matrix[fn].OrderBy(r => r));
    }
}
