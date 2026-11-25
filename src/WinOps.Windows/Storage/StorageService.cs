using System.Runtime.Versioning;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.Windows.Storage;

[SupportedOSPlatform("windows")]
public sealed class StorageService : IStorageService
{
    private readonly ActionSafetyEngine _safety;

    private static readonly Dictionary<string, (string Path, string Reason, RiskLevel Risk)> SuggestionRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user-temp"] = (Path.GetTempPath(), "Application temporary files created for the current user.", RiskLevel.Level4Destructive),
        ["windows-temp"] = (@"C:\Windows\Temp", "Windows temporary files. Skip files currently in use.", RiskLevel.Level4Destructive),
        ["update-cache"] = (@"C:\Windows\SoftwareDistribution\Download", "Downloaded Windows Update payloads that can be re-downloaded.", RiskLevel.Level4Destructive),
        ["thumbnails"] = (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer", "thumbcache_*.db"), "Explorer thumbnail cache files.", RiskLevel.Level4Destructive)
    };

    private static readonly string[] CriticalDirectories =
    [
        @"C:\Windows\System32", @"C:\Windows\SysWOW64", @"C:\Windows\WinSxS", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\All Users"
    ];

    public StorageService(ActionSafetyEngine safety) => _safety = safety;

    public async Task<IReadOnlyList<PhysicalDiskInfo>> GetPhysicalDisksAsync(CancellationToken ct = default) =>
        await Task.Run(() => CimQuery.Query("SELECT DeviceID,Model,MediaType,InterfaceType,Size,Status,OperationalStatus FROM Win32_DiskDrive")
            .Select(row => new PhysicalDiskInfo(
                CimQuery.Text(row, "DeviceID", string.Empty),
                CimQuery.Text(row, "Model", "Unknown"),
                CimQuery.Text(row, "MediaType", "Unknown"),
                CimQuery.Text(row, "InterfaceType", "Unknown"),
                CimQuery.Number(row, "Size"),
                CimQuery.Text(row, "Status", "Unknown"),
                CimQuery.Number(row, "OperationalStatus") switch { 0 => "Unknown", 1 => "Other", 2 => "OK", 3 => "Degraded", 4 => "Stressed", 5 => "Predictive failure", _ => "Unknown" }))
            .ToList(), ct);

    public async Task<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            var encryption = CimQuery.Query("SELECT DriveLetter,ProtectionStatus FROM Win32_EncryptableVolume", @"root\cimv2\security\microsoftvolumeencryption");
            var health = CimQuery.Query("SELECT DriveLetter,HealthStatus FROM MSFT_Volume", @"root\Microsoft\Windows\Storage");
            var result = new List<VolumeInfo>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    var letter = drive.Name.TrimEnd('\\');
                    var encryptRow = encryption.FirstOrDefault(r => CimQuery.Text(r, "DriveLetter", string.Empty).Equals(letter, StringComparison.OrdinalIgnoreCase));
                    var healthRow = health.FirstOrDefault(r => CimQuery.Text(r, "DriveLetter", string.Empty).Equals(letter, StringComparison.OrdinalIgnoreCase));
                    result.Add(new VolumeInfo(
                        drive.Name,
                        string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "No label" : drive.VolumeLabel,
                        drive.DriveFormat,
                        drive.TotalSize,
                        drive.AvailableFreeSpace,
                        CimQuery.Number(healthRow, "HealthStatus", -1) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unavailable" },
                        encryptRow is null ? "Not encrypted / unknown" : CimQuery.Number(encryptRow, "ProtectionStatus") == 1 ? "BitLocker on" : "BitLocker off"));
                }
                catch { /* removable or inaccessible drives are skipped */ }
            }
            return result;
        }, ct);

    public async Task<IReadOnlyList<FolderSizeEntry>> AnalyzeFolderAsync(string path, int topFolders, IProgress<OperationProgress>? progress = null, CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            if (!Directory.Exists(path)) return [];
            var groups = new Dictionary<string, FolderSizeEntry>(StringComparer.OrdinalIgnoreCase);
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            long totalBytes = 0;
            var count = 0;
            foreach (var file in SafeEnumerateFiles(path, options))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(path, file);
                var root = relative.Contains(Path.DirectorySeparatorChar) ? relative.Split(Path.DirectorySeparatorChar)[0] : "(root)";
                long size;
                try { size = new FileInfo(file).Length; } catch { continue; }
                if (!groups.TryGetValue(root, out var entry))
                {
                    entry = new FolderSizeEntry(root, 0, 0, 0);
                    groups[root] = entry;
                }
                groups[root] = entry with { SizeBytes = entry.SizeBytes + size, FileCount = entry.FileCount + 1 };
                totalBytes += size;
                count++;
                if (count % 200 == 0)
                    progress?.Report(new OperationProgress("Analyzing folders", Math.Min(95, count / 1000), $"{count:N0} files, {FormatBytes(totalBytes)}"));
            }
            progress?.Report(new OperationProgress("Analysis complete", 100, $"{count:N0} files"));
            return groups.Values.OrderByDescending(e => e.SizeBytes).Take(Math.Clamp(topFolders, 1, 500)).ToList();
        }, ct);

    public async Task<IReadOnlyList<LargeFileInfo>> FindLargeFilesAsync(string path, long minimumBytes, int maxResults, IProgress<OperationProgress>? progress = null, CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            if (!Directory.Exists(path)) return [];
            var files = new List<LargeFileInfo>();
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            var count = 0;
            foreach (var file in SafeEnumerateFiles(path, options))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length >= minimumBytes)
                        files.Add(new LargeFileInfo(file, info.Length, info.LastWriteTimeUtc));
                }
                catch { /* file may have been removed */ }
                count++;
                if (count % 500 == 0) progress?.Report(new OperationProgress("Searching for large files", Math.Min(95, count / 1000), $"{files.Count} matches"));
            }
            return files.OrderByDescending(f => f.SizeBytes).Take(Math.Clamp(maxResults, 1, 1000)).ToList();
        }, ct);

    public async Task<IReadOnlyList<CleanupSuggestion>> GetCleanupSuggestionsAsync(CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            var suggestions = new List<CleanupSuggestion>();
            foreach (var (id, (path, reason, risk)) in SuggestionRoots)
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(path)) continue;
                long size = 0;
                var count = 0;
                try
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
                    foreach (var file in SafeEnumerateFiles(path, options))
                    {
                        if (++count > 10000) break;
                        try { size += new FileInfo(file).Length; } catch { /* skip */ }
                    }
                }
                catch { /* skip inaccessible root */ }
                if (count == 0) continue;
                suggestions.Add(new CleanupSuggestion(id, Prettify(id), path, size, reason, risk, CriticalDirectories));
            }
            return suggestions;
        }, ct);

    public Task<ActionExecutionResult> CleanupAsync(
        string suggestionId,
        IReadOnlyList<string> approvedPaths,
        Func<ActionDefinition, Task<bool>> confirmAsync,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!SuggestionRoots.TryGetValue(suggestionId, out var suggestion))
            return Task.FromResult(ActionExecutionResult.Fail("Unknown cleanup suggestion.", "Invalid suggestion ID"));

        var validPaths = approvedPaths.Where(p => IsUnderRoot(p, suggestion.Path) && !IsCritical(p)).ToList();
        if (validPaths.Count == 0)
            return Task.FromResult(ActionExecutionResult.Fail("No approved files were inside the selected cleanup location. Critical directories are excluded.", "Validation failed"));

        return _safety.ExecuteAsync(
            "storage.cleanup",
            async token =>
            {
                var deleted = 0;
                long reclaimed = 0;
                var errors = new List<string>();
                for (var i = 0; i < validPaths.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var file = validPaths[i];
                    try
                    {
                        var size = new FileInfo(file).Length;
                        File.Delete(file);
                        deleted++;
                        reclaimed += size;
                    }
                    catch (Exception ex) { errors.Add($"{file}: {ex.Message}"); }
                    if (i % 20 == 0) progress?.Report(new OperationProgress("Deleting approved files", (i * 100) / validPaths.Count, $"{deleted} deleted"));
                }
                var message = $"Deleted {deleted} approved file(s), reclaimed {FormatBytes(reclaimed)}.";
                return errors.Count == 0
                    ? ActionExecutionResult.Ok(message, message, $"Delete approved files under {suggestion.Path}", verified: deleted > 0)
                    : ActionExecutionResult.Fail(message + $" {errors.Count} file(s) could not be deleted.", string.Join(Environment.NewLine, errors), message, "Delete approved files", -1);
            },
            confirmAsync,
            progress,
            ct);
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path, EnumerationOptions options)
    {
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            string[] files;
            string[] folders;
            try
            {
                files = Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly);
                folders = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch { continue; }
            foreach (var file in files) yield return file;
            foreach (var folder in folders)
            {
                try
                {
                    var info = new FileInfo(folder);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue; // never traverse junctions/symlinks
                }
                catch { continue; }
                stack.Push(folder);
            }
        }
    }

    private static bool IsUnderRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(root);
            return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && !full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsCritical(string path) =>
        CriticalDirectories.Any(dir => IsUnderRoot(path, dir));

    private static string Prettify(string id) =>
        string.Join(' ', id.Split('-').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
