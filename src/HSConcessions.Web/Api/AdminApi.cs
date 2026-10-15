using System.Globalization;
using System.IO.Compression;
using HSConcessions.Web.Auth;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Api;

public sealed record TimesRequest(string? Date, string? Opening, string? Closing);
public sealed record ApplyMenuRequest(string? MenuId);
public sealed record ItemPatchRequest(bool? Selling, string? Status);
public sealed record BulkRequest(string Scope, string TargetId, bool? Selling, string? Status);
public sealed record PriceRequest(string Level, string TargetId, string? StandId, decimal? Amount);
public sealed record PermissionRequest(string Function, string Role, bool Granted);

public static class AdminApi
{
    public static void MapAdminApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/admin")
            .RequireAuthorization(Perm.Policy(Functions.ViewDashboard))
            .AddEndpointFilter(AntiforgeryFilter)
            .AddEndpointFilter(ErrorFilter)
            .RequireRateLimiting("adminWrite");

        MapStands(api);
        MapPrices(api);
        MapPermissions(api);
        MapAudit(api);
        MapLogs(api);
        MapQr(api);
        MapBackups(api);
    }

    // ---------- filters ----------

    private static async ValueTask<object?> AntiforgeryFilter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
        {
            var af = http.RequestServices.GetRequiredService<IAntiforgery>();
            try { await af.ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException)
            {
                return Results.Json(new { error = "Your session expired. Reload the page and try again." }, statusCode: 400);
            }
        }
        return await next(ctx);
    }

    private static async ValueTask<object?> ErrorFilter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try { return await next(ctx); }
        catch (NotFoundException ex) { return Results.Json(new { error = ex.Message }, statusCode: 404); }
        catch (ValidationException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
        catch (ForbiddenException) { return Results.Json(new { error = "You do not have permission to do that." }, statusCode: 403); }
    }

    private static void Require(HttpContext http, string function, string? standId = null)
    {
        var perms = http.RequestServices.GetRequiredService<PermissionService>();
        if (!perms.Has(CurrentUser.Get(http), function, standId)) throw new ForbiddenException();
    }

    // ---------- stands ----------

    private static void MapStands(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/stands");

        g.MapGet("/{id}", (string id, StandService stands) => Results.Json(stands.Get(id), JsonStore.JsonOptions));

        g.MapPost("/{id}/open", async (string id, HttpContext http, StandService stands) =>
        {
            Require(http, Functions.OpenCloseStand, id);
            return Results.Json(StandSummary(await stands.OpenAsync(id)));
        });

        g.MapPost("/{id}/close", async (string id, HttpContext http, StandService stands) =>
        {
            Require(http, Functions.OpenCloseStand, id);
            return Results.Json(StandSummary(await stands.CloseAsync(id)));
        });

        g.MapPost("/{id}/times", async (string id, TimesRequest req, HttpContext http, StandService stands, TimeService time) =>
        {
            Require(http, Functions.SetStandTimes, id);
            var date = string.IsNullOrWhiteSpace(req.Date)
                ? DateOnly.FromDateTime(time.LocalNow)
                : DateOnly.ParseExact(req.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var opening = ParseLocalTime(date, req.Opening, time);
            var closing = ParseLocalTime(date, req.Closing, time);
            if (opening is { } o && closing is { } c && c <= o) closing = c.AddDays(1);
            var stand = await stands.SetTimesAsync(id, opening, closing);
            return Results.Json(StandSummary(stand, time));
        });

        g.MapPost("/{id}/apply-menu", async (string id, ApplyMenuRequest req, HttpContext http, StandService stands) =>
        {
            Require(http, Functions.ApplyPresetMenu, id);
            var stand = await stands.ApplyMenuAsync(id, string.IsNullOrWhiteSpace(req.MenuId) ? null : req.MenuId);
            return Results.Json(new { items = stand.State.Items, lastAppliedMenuId = stand.State.LastAppliedMenuId });
        });

        g.MapPatch("/{id}/items/{itemId}", async (string id, string itemId, ItemPatchRequest req, HttpContext http, StandService stands) =>
        {
            if (req.Selling is not null) Require(http, Functions.SetStandItems, id);
            if (req.Status is not null) Require(http, Functions.MarkItemStatus, id);
            if (req.Selling is null && req.Status is null) throw new ValidationException("Nothing to change.");
            var state = await stands.SetItemAsync(id, itemId, req.Selling, req.Status);
            return Results.Json(state, JsonStore.JsonOptions);
        });

        g.MapPost("/{id}/bulk", async (string id, BulkRequest req, HttpContext http, StandService stands) =>
        {
            if (req.Selling is not null) Require(http, Functions.SetStandItems, id);
            if (req.Status is not null) Require(http, Functions.MarkItemStatus, id);
            if (req.Selling is null && req.Status is null) throw new ValidationException("Nothing to change.");
            await stands.BulkAsync(id, req.Scope, req.TargetId, req.Selling, req.Status);
            return Results.Json(new { items = stands.Get(id).State.Items }, JsonStore.JsonOptions);
        });
    }

    private static DateTime? ParseLocalTime(DateOnly date, string? hhmm, TimeService time)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        if (DateTime.TryParseExact(hhmm, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var full))
            return time.ToUtc(full);
        if (!TimeOnly.TryParseExact(hhmm, new[] { "HH:mm", "H:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            throw new ValidationException("Times must be HH:mm.");
        return time.ToUtc(date.ToDateTime(t));
    }

    private static object StandSummary(Stand s, TimeService? time = null) => new
    {
        id = s.Id,
        isOpen = s.State.IsOpen,
        openingAt = s.State.OpeningAt,
        closingAt = s.State.ClosingAt,
        openingText = time?.FormatUtcTime(s.State.OpeningAt),
        closingText = time?.FormatUtcTime(s.State.ClosingAt),
        lastAppliedMenuId = s.State.LastAppliedMenuId,
    };

    // ---------- prices ----------

    private static void MapPrices(RouteGroupBuilder api)
    {
        api.MapGet("/prices", (HttpContext http, JsonStore store) =>
        {
            Require(http, Functions.SetPrices);
            return Results.Json(store.Read<PricesFile>(DataFileNames.Prices), JsonStore.JsonOptions);
        });

        // Set (amount) or clear (amount = null) a price at one level/target/stand.
        api.MapPut("/prices", async (PriceRequest req, HttpContext http, JsonStore store, AuditService audit) =>
        {
            Require(http, Functions.SetPrices);
            if (!PriceLevel.IsValid(req.Level)) throw new ValidationException("Invalid level.");
            if (req.Amount is < 0 or > 10000) throw new ValidationException("Price must be between $0 and $10,000.");
            var catalog = store.Read<CatalogFile>(DataFileNames.Catalog);
            var targetName = req.Level switch
            {
                PriceLevel.Item => catalog.Items.FirstOrDefault(i => i.Id == req.TargetId)?.Name,
                PriceLevel.SubCategory => catalog.SubCategories.FirstOrDefault(i => i.Id == req.TargetId)?.Name,
                _ => catalog.Categories.FirstOrDefault(i => i.Id == req.TargetId)?.Name,
            } ?? throw new NotFoundException("Catalog entry not found.");
            var standId = string.IsNullOrWhiteSpace(req.StandId) ? null : req.StandId;
            var stands = store.Read<StandsFile>(DataFileNames.Stands).Stands;
            if (standId is not null && stands.All(s => s.Id != standId)) throw new NotFoundException("Stand not found.");
            var amount = req.Amount is { } a ? Math.Round(a, 2) : (decimal?)null;

            decimal? before = null;
            await store.UpdateAsync<PricesFile>(DataFileNames.Prices, f =>
            {
                var existing = f.Prices.FirstOrDefault(p => p.Level == req.Level && p.TargetId == req.TargetId && (p.StandId ?? "") == (standId ?? ""));
                before = existing?.Amount;
                if (existing is not null) f.Prices.Remove(existing);
                if (amount is { } amt) f.Prices.Add(new PriceEntry { Level = req.Level, TargetId = req.TargetId, StandId = standId, Amount = amt });
            });
            var where = standId is null ? "Default" : stands.First(s => s.Id == standId).Name;
            await audit.LogAsync(amount is null ? "ClearPrice" : "SetPrice", "Price", $"{req.Level}/{req.TargetId}/{standId ?? "default"}",
                $"{targetName} @ {where}", new { from = before, to = amount });
            return Results.Json(new { ok = true });
        });
    }

    // ---------- permissions ----------

    private static void MapPermissions(RouteGroupBuilder api)
    {
        api.MapPut("/permissions", async (PermissionRequest req, HttpContext http, JsonStore store, AuditService audit) =>
        {
            Require(http, Functions.EditPermissionMatrix);
            var access = CurrentUser.Get(http)!;
            if (!Functions.IsKnown(req.Function) || !Roles.IsValid(req.Role)) throw new ValidationException("Unknown function or role.");
            if (Functions.IsLockedOn(req.Function, req.Role)) throw new ValidationException("That permission is locked to prevent lockout.");
            if (Functions.IsSystemAdminOnlyRow(req.Function) && !access.IsSystemAdmin) throw new ForbiddenException();

            bool before = false;
            await store.UpdateAsync<PermissionsFile>(DataFileNames.Permissions, f =>
            {
                if (!f.Matrix.TryGetValue(req.Function, out var roles)) f.Matrix[req.Function] = roles = new List<string>();
                before = roles.Contains(req.Role);
                roles.Remove(req.Role);
                if (req.Granted) roles.Add(req.Role);
            });
            var label = Functions.All.First(f => f.Key == req.Function).Label;
            await audit.LogAsync("SetPermission", "Permission", $"{req.Function}/{req.Role}", $"{label} — {req.Role}",
                new { from = before, to = req.Granted });
            return Results.Json(new { ok = true });
        });
    }

    // ---------- audit ----------

    private static AuditQuery ParseAuditQuery(HttpRequest r)
    {
        static DateOnly? D(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        return new AuditQuery
        {
            From = D(r.Query["from"]),
            To = D(r.Query["to"]),
            User = r.Query["user"],
            Action = r.Query["action"],
            Entity = r.Query["entity"],
        };
    }

    private static void MapAudit(RouteGroupBuilder api)
    {
        api.MapGet("/audit", (HttpContext http, AuditService audit, TimeService time, int? page) =>
        {
            Require(http, Functions.ViewAudit);
            var all = audit.Query(ParseAuditQuery(http.Request), time.Zone);
            const int size = 50;
            var p = Math.Max(1, page ?? 1);
            return Results.Json(new { total = all.Count, page = p, pageSize = size, records = all.Skip((p - 1) * size).Take(size) }, JsonStore.JsonOptions);
        });

        api.MapGet("/audit/export.csv", async (HttpContext http, AuditService audit, TimeService time) =>
        {
            Require(http, Functions.ViewAudit);
            var records = audit.Query(ParseAuditQuery(http.Request), time.Zone);
            await audit.LogAsync("ExportAudit", "Audit", "csv", "Audit CSV export", new { count = records.Count });
            var csv = AuditService.ToCsv(records, time.Zone);
            return Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(),
                "text/csv", $"audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        });
    }

    // ---------- logs ----------

    public static List<FileInfo> LogFiles(StorageOptions storage)
    {
        var dir = Path.GetFullPath(storage.LogsPath);
        if (!Directory.Exists(dir)) return new();
        return Directory.GetFiles(dir).Where(f => f.EndsWith(".log") || f.EndsWith(".jsonl"))
            .Select(f => new FileInfo(f)).OrderByDescending(f => f.Name).ToList();
    }

    private static void MapLogs(RouteGroupBuilder api)
    {
        api.MapGet("/logs", (HttpContext http, StorageOptions storage) =>
        {
            Require(http, Functions.DownloadLogs);
            return Results.Json(LogFiles(storage).Select(f => new { name = f.Name, size = f.Length, modified = f.LastWriteTimeUtc }));
        });

        api.MapGet("/logs/zip", async (HttpContext http, StorageOptions storage, AuditService audit, string? from, string? to) =>
        {
            Require(http, Functions.DownloadLogs);
            static DateOnly? D(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            var f = D(from) ?? DateOnly.MinValue;
            var t = D(to) ?? DateOnly.MaxValue;
            var files = LogFiles(storage).Where(fi =>
            {
                var name = Path.GetFileNameWithoutExtension(fi.Name);
                return name.Length >= 10 && DateOnly.TryParseExact(name[^10..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d >= f && d <= t;
            }).ToList();
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var fi in files)
                {
                    var entry = zip.CreateEntry(fi.Name, CompressionLevel.Optimal);
                    await using var es = entry.Open();
                    await using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    await fs.CopyToAsync(es);
                }
            }
            ms.Position = 0;
            await audit.LogAsync("DownloadLogs", "Logs", "zip", $"{from ?? "start"} to {to ?? "end"}", new { files = files.Count });
            return Results.File(ms, "application/zip", $"logs-{from ?? "all"}-{to ?? "all"}.zip");
        });

        api.MapGet("/logs/{file}", async (string file, HttpContext http, StorageOptions storage, AuditService audit) =>
        {
            Require(http, Functions.DownloadLogs);
            var fi = LogFiles(storage).FirstOrDefault(f => f.Name == file) ?? throw new NotFoundException("Log file not found.");
            await audit.LogAsync("DownloadLog", "Logs", fi.Name, fi.Name);
            var bytes = await ReadShared(fi.FullName);
            return Results.File(bytes, "text/plain", fi.Name);
        });
    }

    private static async Task<byte[]> ReadShared(string path)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        await fs.CopyToAsync(ms);
        return ms.ToArray();
    }

    // ---------- QR ----------

    private static void MapQr(RouteGroupBuilder api)
    {
        api.MapGet("/qr", (HttpContext http, QrService qr, string target, string? format) =>
        {
            Require(http, Functions.EditSettings);
            var t = qr.Find(target) ?? throw new NotFoundException("Unknown QR target.");
            return string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase)
                ? Results.File(System.Text.Encoding.UTF8.GetBytes(QrService.Svg(t.Url)), "image/svg+xml", $"qr-{t.Key}.svg")
                : Results.File(QrService.Png(t.Url), "image/png", $"qr-{t.Key}.png");
        });
    }

    // ---------- backups ----------

    private static void MapBackups(RouteGroupBuilder api)
    {
        api.MapGet("/backups", (HttpContext http, BackupService backups) =>
        {
            Require(http, Functions.ManageBackups);
            return Results.Json(backups.List());
        });

        api.MapPost("/backups", async (HttpContext http, BackupService backups, TimeService time, AuditService audit) =>
        {
            Require(http, Functions.ManageBackups);
            var name = await backups.CreateAsync(time.LocalNow, "manual-" + time.LocalNow.ToString("HHmmss", CultureInfo.InvariantCulture));
            await audit.LogAsync("CreateBackup", "Backup", name, name);
            return Results.Json(new { name });
        });

        api.MapGet("/backups/{file}", async (string file, HttpContext http, BackupService backups, AuditService audit) =>
        {
            Require(http, Functions.ManageBackups);
            var path = backups.PathFor(file) ?? throw new NotFoundException("Backup not found.");
            await audit.LogAsync("DownloadBackup", "Backup", file, file);
            return Results.File(path, "application/zip", file);
        });

        api.MapPost("/backups/{file}/restore", async (string file, [FromBody] RestoreRequest req, HttpContext http, BackupService backups, TimeService time, AuditService audit) =>
        {
            Require(http, Functions.ManageBackups);
            if (req.Confirm != "RESTORE") throw new ValidationException("Type RESTORE to confirm.");
            var safety = await backups.RestoreAsync(file, time.LocalNow);
            await audit.LogAsync("RestoreBackup", "Backup", file, file, new { preRestoreBackup = safety });
            return Results.Json(new { ok = true, preRestoreBackup = safety });
        });
    }

    public sealed record RestoreRequest(string? Confirm);
}

public sealed class ForbiddenException : Exception;
