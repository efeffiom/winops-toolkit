using System.Globalization;
using System.IO;
using System.Reflection;

namespace WinOps.App.Helpers;

public static class CsvExport
{
    public static string Write<T>(IEnumerable<T> rows, string fileName)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinOps Toolkit", "Exports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{fileName}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();
        using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        writer.WriteLine(string.Join(',', properties.Select(p => Cell(p.Name))));
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(',', properties.Select(p =>
            {
                try { return Cell(Convert.ToString(p.GetValue(row), CultureInfo.InvariantCulture) ?? string.Empty); }
                catch { return Cell(string.Empty); }
            })));
        }
        return path;
    }

    private static string Cell(string value) => "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
}
