using System.Globalization;

namespace LabSchedule.Core;

/// <summary>A file (or sub-folder) in an item's attachment folder.</summary>
public sealed record AttachmentEntry(string Name, string FullPath, bool IsFolder, long Size, DateTime Modified);

/// <summary>Outcome of copying files into an attachment folder.</summary>
public sealed record AttachmentCopyResult(IReadOnlyList<string> Added, int Renamed, int Skipped);

/// <summary>
/// Per-item attachments. Each item that has any gets its own folder under <c>输出文件夹\附件</c>;
/// the folder name is stored in the schedule JSON (<c>attachment_folder</c>) and the files in it
/// are the attachment list, so files added in Explorer show up too.
/// </summary>
public static class Attachments
{
    public const string RootFolderName = "附件";

    private const int MaxFolderNameLength = 80;
    private static readonly char[] InvalidNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string RootDir(string outputDir) => Path.Combine(outputDir, RootFolderName);

    /// <summary>The attachment root inside the output folder chosen on the settings page.</summary>
    public static string DefaultRootDir => RootDir(ScheduleStore.DefaultOutputDir);

    /// <summary>A single, ordinary folder or file name (no path separators, "..", drive letters or reserved names).</summary>
    public static bool IsSafeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name == name.Trim() &&
        !name.EndsWith('.') &&
        name is not ("." or "..") &&
        name.IndexOfAny(InvalidNameChars) < 0 &&
        !name.Any(char.IsControl) &&
        !ReservedNames.Contains(Path.GetFileNameWithoutExtension(name));

    /// <summary>The item's attachment folder, or null when it has none (or the stored name is unsafe).</summary>
    public static string? FolderPath(string rootDir, ScheduleItem item) =>
        IsSafeName(item.AttachmentFolder) ? Path.Combine(rootDir, item.AttachmentFolder) : null;

    /// <summary>Replaces characters Windows does not allow in names and trims what it would strip.</summary>
    private static string SanitizeName(string name)
    {
        var chars = name.Select(ch => InvalidNameChars.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim().TrimEnd('.', ' ');
    }

    /// <summary>
    /// Folder name for an item's first attachment: "开始日期 名称", e.g. "2026-03-02 样品测试".
    /// Names used by other items get " (2)", " (3)"…; a folder already on disk that no item uses is
    /// reused, so attachments added before an undo, or before a schedule was saved, are found again.
    /// </summary>
    public static string SuggestFolderName(Schedule schedule, ScheduleItem item)
    {
        var name = SanitizeName(item.Name);
        if (name.Length == 0) name = SanitizeName(item.Id);
        if (name.Length == 0) name = "未命名";
        var baseName = ScheduleRules.ParseDate(item.StartDate) is not null ? $"{item.StartDate} {name}" : name;
        if (baseName.Length > MaxFolderNameLength)
        {
            var length = char.IsHighSurrogate(baseName[MaxFolderNameLength - 1]) ? MaxFolderNameLength - 1 : MaxFolderNameLength;
            baseName = baseName[..length].TrimEnd('.', ' ');
        }
        if (!IsSafeName(baseName)) baseName = "_" + baseName; // a reserved name such as "CON"

        var taken = schedule.Items
            .Where(other => !ReferenceEquals(other, item) && !string.IsNullOrEmpty(other.AttachmentFolder))
            .Select(other => other.AttachmentFolder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = baseName;
        for (var number = 2; taken.Contains(candidate); number++)
        {
            candidate = $"{baseName} ({number.ToString(CultureInfo.InvariantCulture)})";
        }
        return candidate;
    }

    /// <summary>Sub-folders first, then files, each sorted by name. A missing folder lists as empty.</summary>
    public static List<AttachmentEntry> List(string? folderPath)
    {
        if (folderPath is null || !Directory.Exists(folderPath)) return [];
        var directory = new DirectoryInfo(folderPath);
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        var folders = directory.EnumerateDirectories()
            .Where(info => !info.Attributes.HasFlag(FileAttributes.Hidden))
            .Select(info => new AttachmentEntry(info.Name, info.FullName, true, 0, info.LastWriteTime))
            .OrderBy(entry => entry.Name, comparer);
        var files = directory.EnumerateFiles()
            .Where(info => !info.Attributes.HasFlag(FileAttributes.Hidden) && !info.Attributes.HasFlag(FileAttributes.System))
            .Select(info => new AttachmentEntry(info.Name, info.FullName, false, info.Length, info.LastWriteTime))
            .OrderBy(entry => entry.Name, comparer);
        return [.. folders, .. files];
    }

    public static int Count(string? folderPath)
    {
        try
        {
            return List(folderPath).Count;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary><paramref name="fileName"/>, or "name (2).ext", "name (3).ext"… when that is taken.</summary>
    public static string UniqueName(string folderPath, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = fileName;
        for (var number = 2; File.Exists(Path.Combine(folderPath, candidate)) || Directory.Exists(Path.Combine(folderPath, candidate)); number++)
        {
            candidate = $"{stem} ({number.ToString(CultureInfo.InvariantCulture)}){extension}";
        }
        return candidate;
    }

    /// <summary>
    /// Copies files and folders into <paramref name="folderPath"/> (created if needed). Names already
    /// there get a number; sources that are already in the folder, or contain it, are skipped.
    /// </summary>
    public static AttachmentCopyResult Copy(string folderPath, IEnumerable<string> sources)
    {
        Directory.CreateDirectory(folderPath);
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        var added = new List<string>();
        var renamed = 0;
        var skipped = 0;
        foreach (var source in sources)
        {
            var fullSource = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            var isFolder = Directory.Exists(fullSource);
            var parent = Path.GetDirectoryName(fullSource);
            if ((!isFolder && !File.Exists(fullSource)) ||
                string.Equals(parent, target, StringComparison.OrdinalIgnoreCase) ||
                (isFolder && IsSameOrInside(target, fullSource)))
            {
                skipped++;
                continue;
            }
            var name = UniqueName(target, Path.GetFileName(fullSource));
            if (name != Path.GetFileName(fullSource)) renamed++;
            var destination = Path.Combine(target, name);
            if (isFolder) CopyFolder(fullSource, destination);
            else File.Copy(fullSource, destination);
            added.Add(name);
        }
        return new AttachmentCopyResult(added, renamed, skipped);
    }

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var folder in Directory.EnumerateDirectories(source))
        {
            CopyFolder(folder, Path.Combine(destination, Path.GetFileName(folder)));
        }
    }

    /// <summary>
    /// The full new name for a rename. Files keep their extension: <paramref name="newStem"/> is the
    /// part before it. Returns an error message instead when the name can't be used.
    /// </summary>
    public static (string? Name, string? Error) RenameTarget(AttachmentEntry entry, string newStem)
    {
        var stem = newStem.Trim().TrimEnd('.', ' ');
        if (stem.Length == 0) return (null, "名称不能为空");
        if (stem.IndexOfAny(InvalidNameChars) >= 0 || stem.Any(char.IsControl)) return (null, "名称不能包含 \\ / : * ? \" < > |");
        var name = entry.IsFolder ? stem : stem + Path.GetExtension(entry.Name);
        if (!IsSafeName(name)) return (null, "这个名称不能用作文件名");
        if (name == entry.Name) return (name, null);
        var folder = Path.GetDirectoryName(entry.FullPath) ?? "";
        var path = Path.Combine(folder, name);
        var caseOnly = string.Equals(name, entry.Name, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(path) || Directory.Exists(path))) return (null, $"已有名为“{name}”的附件");
        return (name, null);
    }

    /// <summary>Renames a file or folder in place; returns the new full path.</summary>
    public static string Rename(AttachmentEntry entry, string newName)
    {
        var path = Path.Combine(Path.GetDirectoryName(entry.FullPath) ?? "", newName);
        if (newName == entry.Name) return path;
        if (!entry.IsFolder)
        {
            File.Move(entry.FullPath, path);
        }
        else if (string.Equals(newName, entry.Name, StringComparison.OrdinalIgnoreCase))
        {
            // Directory.Move refuses a change of case only, so go through a temporary name.
            var temporary = Path.Combine(Path.GetDirectoryName(entry.FullPath) ?? "", $"{newName}.{Guid.NewGuid():N}");
            Directory.Move(entry.FullPath, temporary);
            Directory.Move(temporary, path);
        }
        else
        {
            Directory.Move(entry.FullPath, path);
        }
        return path;
    }

    /// <summary>File size the way Explorer shows it (1.2 MB).</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = ["KB", "MB", "GB", "TB"];
        if (bytes < 1024) return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = value < 10 ? "0.#" : "0";
        return $"{value.ToString(format, CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
