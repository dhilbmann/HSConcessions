using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

public sealed class AuditRecord
{
    public DateTime Timestamp { get; set; }
    public string User { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityName { get; set; } = "";
    public JsonElement? Details { get; set; }
    public string? Ip { get; set; }
}

public sealed class AuditQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? User { get; set; }
    public string? Action { get; set; }
    public string? Entity { get; set; }
}

/// <summary>Appends audit records to logs/{instance}-audit-YYYY-MM-DD.jsonl (UTC) and queries them.</summary>
public sealed class AuditService
{
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    // JSON Lines must not start with a byte-order mark, or the day's first record can't be parsed.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _logsPath;
    private readonly string _prefix;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditService> _log;

    private readonly bool _recordIp;

    public AuditService(StorageOptions storage, InstanceOptions instance, IHttpContextAccessor http, ILogger<AuditService> log, DemoOptions? demo = null)
    {
        _recordIp = demo?.Enabled != true;
        _logsPath = Path.GetFullPath(storage.LogsPath);
        Directory.CreateDirectory(_logsPath);
        _prefix = $"{instance.Name}-audit-";
        _http = http;
        _log = log;
    }

    public string FilePrefix => _prefix;

    public async Task LogAsync(string action, string entityType, string entityId, string entityName, object? details = null, string? userOverride = null)
    {
        var ctx = _http.HttpContext;
        var record = new AuditRecord
        {
            Timestamp = DateTime.UtcNow,
            User = userOverride ?? ctx?.User.FindFirstValue(ClaimTypes.Email) ?? "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EntityName = entityName,
            Details = details is null ? null : JsonSerializer.SerializeToElement(details, LineOptions),
            Ip = _recordIp ? ctx?.Connection.RemoteIpAddress?.ToString() : null,
        };
        var line = JsonSerializer.Serialize(record, LineOptions) + "\n";
        var path = Path.Combine(_logsPath, $"{_prefix}{record.Timestamp:yyyy-MM-dd}.jsonl");
        await _gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(path, line, Utf8NoBom);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to write audit record {Action}", action);
        }
        finally
        {
            _gate.Release();
        }
        _log.LogInformation("AUDIT {User} {Action} {EntityType} {EntityId}", record.User, action, entityType, entityId);
    }

    /// <summary>Newest first. Date filters are local (instance time zone) dates.</summary>
    public List<AuditRecord> Query(AuditQuery q, TimeZoneInfo zone)
    {
        var results = new List<AuditRecord>();
        if (!Directory.Exists(_logsPath)) return results;

        foreach (var file in Directory.GetFiles(_logsPath, _prefix + "*.jsonl").OrderByDescending(f => f))
        {
            var datePart = Path.GetFileNameWithoutExtension(file)[_prefix.Length..];
            if (DateOnly.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
            {
                // A local date can span two UTC files, so widen by a day on each side.
                if (q.From is { } f && fileDate < f.AddDays(-1)) continue;
                if (q.To is { } t && fileDate > t.AddDays(1)) continue;
            }
            List<string> lines;
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                lines = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            catch (IOException) { continue; }

            foreach (var line in lines)
            {
                AuditRecord? r;
                try { r = JsonSerializer.Deserialize<AuditRecord>(line.TrimStart('﻿'), LineOptions); }
                catch (JsonException) { continue; }
                if (r is null) continue;
                var localDate = DateOnly.FromDateTime(TimeService.ToLocal(r.Timestamp, zone));
                if (q.From is { } from && localDate < from) continue;
                if (q.To is { } to && localDate > to) continue;
                if (!string.IsNullOrWhiteSpace(q.User) && !r.User.Contains(q.User.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(q.Action) && !r.Action.Equals(q.Action.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(q.Entity) &&
                    !r.EntityType.Contains(q.Entity.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    !r.EntityId.Contains(q.Entity.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    !r.EntityName.Contains(q.Entity.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                results.Add(r);
            }
        }
        return results.OrderByDescending(r => r.Timestamp).ToList();
    }

    public static string ToCsv(IEnumerable<AuditRecord> records, TimeZoneInfo zone)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Time (local),User,Action,Entity Type,Entity Id,Entity Name,Details,IP");
        foreach (var r in records)
        {
            var local = TimeService.ToLocal(r.Timestamp, zone);
            sb.AppendLine(string.Join(",",
                Csv(local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Csv(r.User), Csv(r.Action), Csv(r.EntityType), Csv(r.EntityId), Csv(r.EntityName),
                Csv(r.Details?.GetRawText() ?? ""), Csv(r.Ip ?? "")));
        }
        return sb.ToString();
    }

    private static string Csv(string s)
    {
        // Guard against spreadsheet formula injection.
        if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
