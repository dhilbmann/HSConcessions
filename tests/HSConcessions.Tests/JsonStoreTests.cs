using System.Text.Json;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Tests;

public sealed class JsonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hsc-tests-" + Guid.NewGuid().ToString("n"));
    private readonly JsonStore _store;

    public JsonStoreTests() => _store = new JsonStore(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static StandsFile Sample(string name) => new() { Stands = { new Stand { Id = "s1", Name = name, Slug = "s1" } } };

    [Fact]
    public async Task Write_then_read_round_trips()
    {
        await _store.WriteAsync(DataFileNames.Stands, Sample("Football"));
        Assert.Equal("Football", _store.Read<StandsFile>(DataFileNames.Stands).Stands[0].Name);
        Assert.False(File.Exists(_store.PathFor(DataFileNames.Stands) + ".tmp"));
    }

    [Fact]
    public async Task A_crash_while_writing_the_temp_file_leaves_the_original_intact()
    {
        await _store.WriteAsync(DataFileNames.Stands, Sample("Original"));
        var path = _store.PathFor(DataFileNames.Stands);

        // Simulate the process dying mid-write: a truncated temp file is left behind and the rename never happened.
        var full = JsonSerializer.Serialize(Sample("New"), JsonStore.JsonOptions);
        await File.WriteAllTextAsync(path + ".tmp", full[..(full.Length / 2)]);

        Assert.Equal("Original", _store.Read<StandsFile>(DataFileNames.Stands).Stands[0].Name);

        // The next write simply overwrites the stale temp file.
        await _store.UpdateAsync<StandsFile>(DataFileNames.Stands, f => f.Stands[0].Name = "Next");
        Assert.Equal("Next", _store.Read<StandsFile>(DataFileNames.Stands).Stands[0].Name);
    }

    [Fact]
    public async Task A_failure_during_serialization_never_touches_the_original()
    {
        await _store.WriteAsync(DataFileNames.Stands, Sample("Original"));
        var path = _store.PathFor(DataFileNames.Stands);
        var before = await File.ReadAllBytesAsync(path);

        // Serialization throws partway through writing the temp file.
        Assert.ThrowsAny<Exception>(() => JsonStore.WriteAtomic(path, new Exploding()));

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Equal("Original", _store.Read<StandsFile>(DataFileNames.Stands).Stands[0].Name);
    }

    [Fact]
    public async Task A_failing_mutation_writes_nothing()
    {
        await _store.WriteAsync(DataFileNames.Stands, Sample("Original"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync<StandsFile>(DataFileNames.Stands, f =>
            {
                f.Stands[0].Name = "Half-done";
                throw new InvalidOperationException("boom");
            }));
        Assert.Equal("Original", _store.Read<StandsFile>(DataFileNames.Stands).Stands[0].Name);
    }

    [Fact]
    public async Task Concurrent_read_modify_write_cycles_do_not_lose_updates()
    {
        await _store.WriteAsync(DataFileNames.Prices, new PricesFile());
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
            _store.UpdateAsync<PricesFile>(DataFileNames.Prices, f =>
                f.Prices.Add(new PriceEntry { Level = PriceLevel.Item, TargetId = "itm-" + i, Amount = i }))));
        await Task.WhenAll(tasks);
        Assert.Equal(50, _store.Read<PricesFile>(DataFileNames.Prices).Prices.Count);
    }

    [Fact]
    public async Task Readers_always_see_a_complete_file_while_writes_happen()
    {
        await _store.WriteAsync(DataFileNames.Stands, Sample("v0"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var writer = Task.Run(async () =>
        {
            var n = 0;
            while (!cts.IsCancellationRequested)
                await _store.UpdateAsync<StandsFile>(DataFileNames.Stands, f => f.Stands[0].Name = "v" + ++n);
        });
        var reads = 0;
        while (!cts.IsCancellationRequested)
        {
            var f = _store.Read<StandsFile>(DataFileNames.Stands); // throws if a half-written file is ever observed
            Assert.StartsWith("v", f.Stands[0].Name);
            reads++;
        }
        await writer;
        Assert.True(reads > 0);
    }

    [Fact]
    public async Task Updates_set_updatedAt_and_keep_version()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        await _store.UpdateAsync<StandsFile>(DataFileNames.Stands, f => f.Stands.Add(new Stand { Id = "x" }));
        var file = _store.Read<StandsFile>(DataFileNames.Stands);
        Assert.Equal(1, file.Version);
        Assert.True(file.UpdatedAt >= before);
    }

    /// <summary>Serializes a few bytes, then throws — like a crash in the middle of writing.</summary>
    private sealed class Exploding
    {
        public string First { get; set; } = new('x', 10_000);
        public string Second => throw new IOException("disk went away");
    }
}
