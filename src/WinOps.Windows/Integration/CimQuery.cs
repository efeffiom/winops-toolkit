using System.Globalization;
using System.Management;
using System.Runtime.Versioning;

namespace WinOps.Windows.Integration;

/// <summary>Small, disposable CIM/WMI helper returning plain dictionaries.</summary>
[SupportedOSPlatform("windows")]
public static class CimQuery
{
    public static IReadOnlyList<Dictionary<string, object?>> Query(string query, string namespaceName = "root\\cimv2", int timeoutMs = 15000)
    {
        var results = new List<Dictionary<string, object?>>();
        try
        {
            var scope = new ManagementScope(namespaceName);
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
            var options = new System.Management.EnumerationOptions { ReturnImmediately = true, Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            searcher.Options = options;
            foreach (var item in searcher.Get())
            {
                using var mo = (ManagementObject)item;
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in mo.Properties)
                {
                    try { row[property.Name] = property.Value; }
                    catch { row[property.Name] = null; }
                }
                results.Add(row);
            }
        }
        catch (ManagementException)
        {
            // Unavailable CIM classes are reported as missing data by callers.
        }
        catch (UnauthorizedAccessException)
        {
            // Callers distinguish unavailable data from zero values.
        }
        return results;
    }

    public static Dictionary<string, object?>? First(string query, string namespaceName = "root\\cimv2", int timeoutMs = 15000) =>
        Query(query, namespaceName, timeoutMs).FirstOrDefault();

    public static string Text(Dictionary<string, object?>? row, string property, string fallback = "Unavailable")
    {
        if (row is null || !row.TryGetValue(property, out var value) || value is null) return fallback;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text) || text.StartsWith("00000000", StringComparison.Ordinal) ? fallback : text.Trim();
    }

    public static long Number(Dictionary<string, object?>? row, string property, long fallback = 0)
    {
        if (row is null || !row.TryGetValue(property, out var value) || value is null) return fallback;
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public static double Double(Dictionary<string, object?>? row, string property, double fallback = 0)
    {
        if (row is null || !row.TryGetValue(property, out var value) || value is null) return fallback;
        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    public static bool Flag(Dictionary<string, object?>? row, string property) =>
        bool.TryParse(Text(row, property, "false"), out var result) && result;

    public static DateTimeOffset? Date(Dictionary<string, object?>? row, string property)
    {
        var raw = Text(row, property, string.Empty);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var managedDate = ManagementDateTimeConverter.ToDateTime(raw);
            return new DateTimeOffset(managedDate);
        }
        catch
        {
            try { return new DateTimeOffset(Convert.ToDateTime(raw, CultureInfo.InvariantCulture)); }
            catch { return null; }
        }
    }

    public static IReadOnlyList<string> Strings(Dictionary<string, object?>? row, string property)
    {
        if (row is null || !row.TryGetValue(property, out var value) || value is null) return [];
        if (value is Array array) return array.Cast<object?>().Where(x => x is not null).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)!).ToArray();
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text) ? [] : [text];
    }
}
