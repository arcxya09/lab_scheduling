using System.Text.Json;

namespace LabSchedule.Core;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Sha256);

public sealed record ReleaseInfo(string Tag, Version Version, string Title, string Notes, string PageUrl, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Reads GitHub's "latest release" response and picks the file to update with.</summary>
public static class UpdateFeed
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/arcxya09/lab_scheduling/releases/latest";

    /// <summary>"v3.1.0" → 3.1.0 (always three parts); null when the tag is not a version.</summary>
    public static Version? ParseVersion(string? tag)
    {
        var text = (tag ?? "").Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(['-', '+']);
        if (dash >= 0) text = text[..dash];
        if (!Version.TryParse(text, out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));

    /// <summary>Parses a GitHub release; drafts, pre-releases and non-version tags give null.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
        var tag = Str(root, "tag_name");
        var version = ParseVersion(tag);
        if (version is null) return null;

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in list.EnumerateArray())
            {
                var digest = Str(asset, "digest");
                assets.Add(new ReleaseAsset(
                    Str(asset, "name"),
                    Str(asset, "browser_download_url"),
                    asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
                    digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null));
            }
        }
        return new ReleaseInfo(tag, version, Str(root, "name"), Str(root, "body"), Str(root, "html_url"), assets);
    }

    public static bool IsNewer(ReleaseInfo release, Version current) => release.Version > Normalize(current);

    /// <summary>The MSI for installed copies, the portable zip otherwise.</summary>
    public static ReleaseAsset? PickAsset(ReleaseInfo release, bool installed, string architecture = "x64")
    {
        var suffix = installed ? $"-win-{architecture}-setup.msi" : $"-win-{architecture}-portable.zip";
        return release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
            asset.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
