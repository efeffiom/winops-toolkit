using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;
using System.Xml.Linq;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;

namespace WinOps.Windows.Diagnostics;

[SupportedOSPlatform("windows")]
public sealed class EventLogService : IEventLogService
{
    public async Task<IReadOnlyList<string>> GetLogsAsync(CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            try { return EventLogSession.GlobalSession.GetLogNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch { return new List<string>(); }
        }, ct);

    public async Task<IReadOnlyList<EventLogEntryInfo>> QueryAsync(
        string logName,
        string? level,
        long? eventId,
        string? provider,
        DateTimeOffset? from,
        string? keyword,
        int maxEvents,
        CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            var result = new List<EventLogEntryInfo>();
            var xpath = BuildXPath(level, eventId, provider, from);
            try
            {
                var query = new EventLogQuery(logName, PathType.LogName, xpath);
                using var reader = new EventLogReader(query);
                EventRecord? record;
                while ((record = reader.ReadEvent()) is not null && result.Count < Math.Clamp(maxEvents, 1, 5000))
                {
                    using (record)
                    {
                        ct.ThrowIfCancellationRequested();
                        var message = Safe(() => FormatMessage(record));
                        if (!string.IsNullOrWhiteSpace(keyword) && !message.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                        result.Add(new EventLogEntryInfo(
                            logName,
                            record.Id,
                            record.ProviderName ?? string.Empty,
                            record.LevelDisplayName ?? "Unknown",
                            record.TimeCreated ?? DateTimeOffset.MinValue,
                            message,
                            Safe(() => record.ToXml())));
                    }
                }
            }
            catch (EventLogNotFoundException) { /* log not present */ }
            catch (UnauthorizedAccessException) { /* log requires elevation */ }
            return result;
        }, ct);

    private static string BuildXPath(string? level, long? eventId, string? provider, DateTimeOffset? from)
    {
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(level))
        {
            var levels = level.ToLowerInvariant() switch
            {
                "critical" => "1",
                "error" => "2",
                "warning" => "3",
                "information" => "4",
                "verbose" => "5",
                _ => string.Empty
            };
            if (!string.IsNullOrEmpty(levels)) conditions.Add($"Level={levels}");
        }
        if (eventId.HasValue) conditions.Add($"EventID={eventId.Value}");
        if (!string.IsNullOrWhiteSpace(provider)) conditions.Add($"Provider[@Name='{Escape(provider)}']");
        if (from.HasValue) conditions.Add($"TimeCreated[@SystemTime>='{from.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}']");
        return conditions.Count == 0 ? "*" : $"*[System[{string.Join(" and ", conditions)}]]";
    }

    private static string FormatMessage(EventRecord record)
    {
        var xml = record.ToXml();
        if (string.IsNullOrWhiteSpace(xml)) return string.Empty;
        var document = XDocument.Parse(xml);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var values = document.Descendants(ns + "Data").Select(e => e.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        if (values.Length == 0) return document.Root?.Value ?? string.Empty;
        return string.Join(" | ", values);
    }

    private static string Escape(string value) => value.Replace("'", "&apos;");
    private static string Safe(Func<string?> action) { try { return action() ?? string.Empty; } catch { return string.Empty; } }
}
