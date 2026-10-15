using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HSConcessions.Tests;

public sealed class DemoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hsc-demo-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private sealed class Env(string root) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Demo";
    }

    [Fact]
    public async Task Demo_reset_puts_every_file_and_image_back_to_the_seed_profile()
    {
        var store = new JsonStore(Path.Combine(_dir, "data"));
        var images = new ImageService(new StorageOptions { ImagesPath = Path.Combine(_dir, "data", "images") });
        var seed = new SeedService(store, new SeedOptions { Profile = "wildcatsnacks", SamplesPath = Path.Combine(TestPaths.RepoRoot, "samples") },
            images, new Env(Path.Combine(TestPaths.RepoRoot, "src", "HSConcessions.Web")), NullLogger<SeedService>.Instance);

        await seed.EnsureSeededAsync();
        var seededPrices = File.ReadAllText(store.PathFor(DataFileNames.Prices));

        // A visitor vandalizes the demo.
        await store.UpdateAsync<PricesFile>(DataFileNames.Prices, p => p.Prices.Clear());
        await store.UpdateAsync<UsersFile>(DataFileNames.Users, u => u.Users.Add(new AppUser { Email = "visitor@example.org" }));
        File.WriteAllText(Path.Combine(images.ImagesPath, "img-uploaded.webp"), "x");

        await seed.ResetToProfileAsync();

        Assert.Equal(seededPrices, File.ReadAllText(store.PathFor(DataFileNames.Prices)));
        Assert.Empty(store.Read<UsersFile>(DataFileNames.Users).Users);
        Assert.False(File.Exists(Path.Combine(images.ImagesPath, "img-uploaded.webp")));
        var logo = store.Read<SiteSettings>(DataFileNames.Settings).Images.Logo!;
        Assert.True(File.Exists(Path.Combine(images.ImagesPath, logo)));
        Assert.Empty(Directory.GetFiles(store.DataPath, "*.tmp"));
    }

    [Fact]
    public void Demo_turns_off_log_downloads_and_backups_even_for_system_admins()
    {
        var store = new JsonStore(Path.Combine(_dir, "data"));
        JsonStore.WriteAtomic(store.PathFor(DataFileNames.Permissions), Functions.DefaultMatrix());
        var auth = new AuthOptions { SystemAdmins = { "admin@demo.test" } };
        var demo = new PermissionService(store, auth, new FeatureOptions(), new DemoOptions { Enabled = true });
        var normal = new PermissionService(store, auth, new FeatureOptions());
        var admin = demo.GetAccess("admin@demo.test");

        Assert.False(demo.Has(admin, Functions.DownloadLogs));
        Assert.False(demo.Has(admin, Functions.ManageBackups));
        Assert.True(demo.Has(admin, Functions.SetPrices));
        Assert.True(normal.Has(admin, Functions.ManageBackups));
    }
}
