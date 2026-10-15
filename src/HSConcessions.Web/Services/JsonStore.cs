using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

/// <summary>
/// JSON-file persistence. Every read goes to disk so a change is visible on the very next request.
/// Writes are atomic (temp file + flush + rename) and each read-modify-write cycle holds a per-file lock.
/// Last write wins.
/// </summary>
public sealed class JsonStore
{
    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public JsonStore(string dataPath)
    {
        DataPath = Path.GetFullPath(dataPath);
        Directory.CreateDirectory(DataPath);
    }

    public string DataPath { get; }

    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string PathFor(string fileName) => Path.Combine(DataPath, fileName);

    public T Read<T>(string fileName) where T : DataFile, new()
    {
        var path = PathFor(fileName);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return new T();
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<T>(fs, JsonOptions) ?? new T();
            }
            catch (IOException) when (attempt < 5)
            {
                // A rename may be replacing the file right now (mostly on Windows); retry briefly.
                Thread.Sleep(20);
            }
        }
    }

    public Task<T> UpdateAsync<T>(string fileName, Action<T> mutate) where T : DataFile, new()
        => UpdateAsync<T, T>(fileName, f => { mutate(f); return f; });

    /// <summary>Read-modify-write under the file's lock. If <paramref name="mutate"/> throws, nothing is written.</summary>
    public async Task<TResult> UpdateAsync<T, TResult>(string fileName, Func<T, TResult> mutate) where T : DataFile, new()
    {
        var gate = LockFor(fileName);
        await gate.WaitAsync();
        try
        {
            var data = Read<T>(fileName);
            var result = mutate(data);
            data.UpdatedAt = DateTime.UtcNow;
            WriteAtomic(PathFor(fileName), data);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WriteAsync<T>(string fileName, T data) where T : DataFile
    {
        var gate = LockFor(fileName);
        await gate.WaitAsync();
        try
        {
            data.UpdatedAt = DateTime.UtcNow;
            WriteAtomic(PathFor(fileName), data);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Holds the locks of the given files (in a stable order) — used while restoring a backup.</summary>
    public async Task<IAsyncDisposable> LockFilesAsync(IEnumerable<string> fileNames)
    {
        var gates = fileNames.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(LockFor)
            .ToList();
        foreach (var g in gates) await g.WaitAsync();
        return new Releaser(gates);
    }

    /// <summary>Serialize to file.tmp, flush to disk, then rename over the target.</summary>
    public static void WriteAtomic<T>(string path, T data)
    {
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(fs, data, JsonOptions);
            fs.Flush(flushToDisk: true);
        }
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(tmp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(20);
            }
            catch (UnauthorizedAccessException) when (attempt < 10)
            {
                Thread.Sleep(20);
            }
        }
    }

    private SemaphoreSlim LockFor(string fileName) => _locks.GetOrAdd(fileName, _ => new SemaphoreSlim(1, 1));

    private sealed class Releaser(List<SemaphoreSlim> gates) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            foreach (var g in gates) g.Release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Names of the data files.</summary>
public static class DataFileNames
{
    public const string Settings = "settings.json";
    public const string Stands = "stands.json";
    public const string Catalog = "catalog.json";
    public const string Prices = "prices.json";
    public const string Menus = "menus.json";
    public const string Users = "users.json";
    public const string Permissions = "permissions.json";
    public const string Events = "events.json";
    public const string Calendar = "calendar.json";

    public static readonly string[] All = { Settings, Stands, Catalog, Prices, Menus, Users, Permissions, Events, Calendar };
}
