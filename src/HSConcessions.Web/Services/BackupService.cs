using System.Globalization;
using System.IO.Compression;
using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

/// <summary>Zips data/ (JSON + images) to backups/, keeps 30 days, and restores.</summary>
public sealed class BackupService
{
    private readonly JsonStore _store;
    private readonly StorageOptions _storage;
    private readonly string _backupsPath;
    private readonly string _prefix;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupService(JsonStore store, StorageOptions storage, InstanceOptions instance)
    {
        _store = store;
        _storage = storage;
        _backupsPath = Path.GetFullPath(storage.BackupsPath);
        Directory.CreateDirectory(_backupsPath);
        _prefix = $"{instance.Name}-backup-";
    }

    public string BackupsPath => _backupsPath;

    public sealed record BackupInfo(string Name, long Size, DateTime CreatedUtc);

    public List<BackupInfo> List() =>
        Directory.GetFiles(_backupsPath, _prefix + "*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupInfo(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();

    /// <summary>Resolves a backup name to a path inside the backups folder, or null.</summary>
    public string? PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || !name.StartsWith(_prefix) || !name.EndsWith(".zip")) return null;
        var path = Path.Combine(_backupsPath, name);
        return File.Exists(path) ? path : null;
    }

    public async Task<string> CreateAsync(DateTime localDate, string? suffix = null)
    {
        await _gate.WaitAsync();
        try
        {
            var name = $"{_prefix}{localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{(suffix is null ? "" : "-" + suffix)}.zip";
            var path = Path.Combine(_backupsPath, name);
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                var dataRoot = _store.DataPath;
                foreach (var file in Directory.GetFiles(dataRoot, "*.json"))
                    zip.CreateEntryFromFile(file, Path.GetFileName(file));
                var imagesRoot = Path.GetFullPath(_storage.ImagesPath);
                if (Directory.Exists(imagesRoot))
                    foreach (var file in Directory.GetFiles(imagesRoot).Where(f => !f.EndsWith(".tmp")))
                        zip.CreateEntryFromFile(file, "images/" + Path.GetFileName(file));
            }
            File.Move(tmp, path, true);
            return name;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Takes a pre-restore backup, then replaces the JSON files and adds images from the archive.</summary>
    public async Task<string> RestoreAsync(string name, DateTime localNow)
    {
        var path = PathFor(name) ?? throw new FileNotFoundException("Backup not found.");
        var safety = await CreateAsync(localNow, "pre-restore-" + localNow.ToString("HHmmss", CultureInfo.InvariantCulture));

        await using var locks = await _store.LockFilesAsync(DataFileNames.All);
        using var zip = ZipFile.OpenRead(path);
        var imagesRoot = Path.GetFullPath(_storage.ImagesPath);
        Directory.CreateDirectory(imagesRoot);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            if (entry.FullName.StartsWith("images/"))
            {
                var fileName = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(fileName)) continue;
                entry.ExtractToFile(Path.Combine(imagesRoot, fileName), overwrite: true);
            }
            else if (DataFileNames.All.Contains(entry.FullName))
            {
                var target = _store.PathFor(entry.FullName);
                var tmp = target + ".tmp";
                entry.ExtractToFile(tmp, overwrite: true);
                File.Move(tmp, target, true);
            }
        }
        return safety;
    }

    public int Prune(int keepDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-keepDays);
        var removed = 0;
        foreach (var f in Directory.GetFiles(_backupsPath, _prefix + "*.zip"))
        {
            if (File.GetLastWriteTimeUtc(f) < cutoff)
            {
                File.Delete(f);
                removed++;
            }
        }
        return removed;
    }
}

/// <summary>Runs the daily backup at 3:00 AM local time and prunes backups older than 30 days.</summary>
public sealed class BackupHostedService(BackupService backups, TimeService time, ILogger<BackupHostedService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = time.LocalNow;
            var next = now.Date.AddHours(3);
            if (next <= now) next = next.AddDays(1);
            try { await Task.Delay(next - now, stoppingToken); }
            catch (TaskCanceledException) { return; }
            try
            {
                var name = await backups.CreateAsync(time.LocalNow);
                var pruned = backups.Prune(30);
                log.LogInformation("Daily backup {Name} created; {Pruned} old backups removed", name, pruned);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Daily backup failed");
            }
        }
    }
}

/// <summary>Deletes app and audit log files older than 90 days, once a day.</summary>
public sealed class RetentionService(StorageOptions storage, ILogger<RetentionService> log) : BackgroundService
{
    public const int KeepDays = 90;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = Prune(Path.GetFullPath(storage.LogsPath), DateTime.UtcNow, KeepDays);
                if (removed > 0) log.LogInformation("Retention removed {Count} log files older than {Days} days", removed, KeepDays);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Log retention failed");
            }
            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    /// <summary>Uses the date in the file name (…-YYYY-MM-DD.log/.jsonl), falling back to last write time.</summary>
    public static int Prune(string logsPath, DateTime nowUtc, int keepDays)
    {
        if (!Directory.Exists(logsPath)) return 0;
        var cutoff = DateOnly.FromDateTime(nowUtc.AddDays(-keepDays));
        var removed = 0;
        foreach (var file in Directory.GetFiles(logsPath).Where(f => f.EndsWith(".log") || f.EndsWith(".jsonl")))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            DateOnly date;
            if (name.Length >= 10 && DateOnly.TryParseExact(name[^10..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                date = d;
            else
                date = DateOnly.FromDateTime(File.GetLastWriteTimeUtc(file));
            if (date < cutoff)
            {
                try { File.Delete(file); removed++; }
                catch (IOException) { /* in use; try again tomorrow */ }
            }
        }
        return removed;
    }
}
