using LabSchedule.Core;

namespace LabSchedule;

/// <summary>One file (or folder) in the "附件" list of the editor.</summary>
public sealed class AttachmentRow(AttachmentEntry entry)
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic"
    };

    public AttachmentEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    /// <summary>"1.2 MB · 2026-10-30 14:05", or "文件夹 · …".</summary>
    public string Details => $"{(Entry.IsFolder ? "文件夹" : Attachments.FormatSize(Entry.Size))} · {Entry.Modified:yyyy-MM-dd HH:mm}";

    public string Glyph => Entry.IsFolder ? "\uE8B7" : ImageExtensions.Contains(Path.GetExtension(Entry.Name)) ? "\uE91B" : "\uE8A5";
}
