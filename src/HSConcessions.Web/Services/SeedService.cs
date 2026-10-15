using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

/// <summary>
/// Copies a seed profile from samples/ into the data folder: on first run (empty data folder), and on a timer
/// for the public demo (see <see cref="DemoResetService"/>).
/// </summary>
public sealed class SeedService(JsonStore store, SeedOptions seed, ImageService images, IWebHostEnvironment env, ILogger<SeedService> log)
{
    public string SamplesPath => Path.GetFullPath(seed.SamplesPath ?? Path.Combine(AppContext.BaseDirectory, "samples"));

    public async Task EnsureSeededAsync()
    {
        var dataDir = store.DataPath;
        var hasData = Directory.Exists(dataDir) && Directory.EnumerateFiles(dataDir, "*.json").Any();
        if (hasData) return;
        log.LogInformation("Seeding empty data folder {DataPath} from profile {Profile}", dataDir, ProfileName);
        await CopyProfileAsync();
    }

    /// <summary>Replaces all data and images with the seed profile. Used by the demo reset.</summary>
    public async Task ResetToProfileAsync()
    {
        log.LogInformation("Resetting data folder {DataPath} to profile {Profile}", store.DataPath, ProfileName);
        await CopyProfileAsync();
    }

    private string ProfileName => string.IsNullOrWhiteSpace(seed.Profile) ? "generic" : seed.Profile.Trim();

    private async Task CopyProfileAsync()
    {
        var profile = ProfileName;
        if (profile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || profile.Contains(".."))
            throw new InvalidOperationException($"Invalid Seed:Profile '{profile}'.");
        var source = Path.Combine(SamplesPath, profile);
        if (!Directory.Exists(source))
            throw new InvalidOperationException($"Seed profile folder not found: {source}");

        var dataDir = store.DataPath;
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(images.ImagesPath);

        // Hold every file's lock so no request sees a mix of old and new data; each file is replaced atomically.
        await using (await store.LockFilesAsync(DataFileNames.All))
        {
            foreach (var name in DataFileNames.All)
            {
                var from = Path.Combine(source, name);
                var to = store.PathFor(name);
                if (File.Exists(from)) ReplaceAtomic(from, to);
                else if (File.Exists(to)) File.Delete(to);
            }

            foreach (var file in Directory.GetFiles(images.ImagesPath))
            {
                try { File.Delete(file); } catch (IOException) { /* in use; harmless leftover */ }
            }
            var sourceImages = Path.Combine(source, "images");
            if (Directory.Exists(sourceImages))
                foreach (var file in Directory.GetFiles(sourceImages))
                    ReplaceAtomic(file, Path.Combine(images.ImagesPath, Path.GetFileName(file)));
        }

        // Make sure every file exists, even if the profile omitted one.
        EnsureFile<SiteSettings>(DataFileNames.Settings);
        EnsureFile<StandsFile>(DataFileNames.Stands);
        EnsureFile<CatalogFile>(DataFileNames.Catalog);
        EnsureFile<PricesFile>(DataFileNames.Prices);
        EnsureFile<MenusFile>(DataFileNames.Menus);
        EnsureFile<UsersFile>(DataFileNames.Users);
        EnsureFile<EventsFile>(DataFileNames.Events);
        EnsureFile<CalendarFile>(DataFileNames.Calendar);
        if (!File.Exists(store.PathFor(DataFileNames.Permissions)))
            await store.WriteAsync(DataFileNames.Permissions, Functions.DefaultMatrix());

        // Favicons from the seeded logo (or the bundled placeholder).
        var settings = store.Read<SiteSettings>(DataFileNames.Settings);
        if (settings.Images.Favicon is null || !File.Exists(Path.Combine(images.ImagesPath, settings.Images.Favicon + ".ico")))
        {
            var logoPath = settings.Images.Logo is { } logo
                ? Path.Combine(images.ImagesPath, logo)
                : Path.Combine(env.WebRootPath, "img", "placeholder-logo.svg");
            if (File.Exists(logoPath))
            {
                try
                {
                    var fav = await images.GenerateFaviconsFromFileAsync(logoPath);
                    await store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s => s.Images.Favicon = fav);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Could not generate favicons from {Logo}", logoPath);
                }
            }
        }
    }

    private static void ReplaceAtomic(string from, string to)
    {
        var tmp = to + ".tmp";
        File.Copy(from, tmp, overwrite: true);
        File.Move(tmp, to, overwrite: true);
    }

    private void EnsureFile<T>(string name) where T : DataFile, new()
    {
        var path = store.PathFor(name);
        if (!File.Exists(path)) JsonStore.WriteAtomic(path, new T());
    }
}

/// <summary>Public demo: puts everything back to the seed profile on a fixed interval.</summary>
public sealed class DemoResetService(SeedService seed, DemoOptions demo, ILogger<DemoResetService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(5, demo.ResetIntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(interval, stoppingToken); }
            catch (TaskCanceledException) { return; }
            try { await seed.ResetToProfileAsync(); }
            catch (Exception ex) { log.LogError(ex, "Demo reset failed"); }
        }
    }
}
