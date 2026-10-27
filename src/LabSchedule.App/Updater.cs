using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using LabSchedule.Core;
using Microsoft.Win32;

namespace LabSchedule;

/// <summary>
/// Checks GitHub for a newer release, downloads it and hands over to a small PowerShell script that
/// waits for this process to exit, installs the update (MSI upgrade, or file copy for the portable
/// build) and starts the new version.
/// </summary>
internal static class Updater
{
    /// <summary>Points the update check at another "latest release" URL (used for testing).</summary>
    public const string FeedEnvironmentVariable = "LAB_SCHEDULE_UPDATE_FEED";

    /// <summary>Registry key written by the installer (overridable so tests can use a separate test install).</summary>
    private static readonly string RegistryKey =
        Environment.GetEnvironmentVariable("LAB_SCHEDULE_REGISTRY_KEY") is { Length: > 0 } key ? key : @"Software\LabSchedule";

    // Declared before Http: static initialisers run in order and the client's User-Agent needs it.
    public static Version CurrentVersion { get; } =
        UpdateFeed.Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    private static readonly HttpClient Http = CreateClient();

    /// <summary>True when this copy was installed by the MSI (it records its folder in the registry).</summary>
    public static bool IsInstalledCopy
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            return key?.GetValue("InstallFolder") is string folder && SamePath(folder, AppContext.BaseDirectory);
        }
    }

    /// <summary>Automatic checks run for release builds, or when a test feed is configured.</summary>
    public static bool AutomaticChecksAllowed =>
#if DEBUG
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(FeedEnvironmentVariable));
#else
        true;
#endif

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"LabSchedule/{CurrentVersion}");
        return client;
    }

    /// <summary>The latest published release, or null when there is none.</summary>
    public static async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken cancellation)
    {
        var url = Environment.GetEnvironmentVariable(FeedEnvironmentVariable);
        using var request = new HttpRequestMessage(HttpMethod.Get, string.IsNullOrEmpty(url) ? UpdateFeed.LatestReleaseUrl : url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return UpdateFeed.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }

    /// <summary>Downloads the asset into %TEMP%, checking its size and SHA-256 when GitHub provides one.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, IProgress<double> progress, CancellationToken cancellation)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LabScheduleUpdate", release.Version.ToString());
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(asset.Name));
        var partial = target + ".download";

        using var response = await Http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;
        string hash;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
        await using (var file = File.Create(partial))
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 16];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
                sha.AppendData(buffer, 0, read);
                received += read;
                if (total > 0) progress.Report(received * 100.0 / total);
            }
            hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }

        if (asset.Size > 0 && new FileInfo(partial).Length != asset.Size) throw new IOException("下载的文件不完整，请重试");
        if (asset.Sha256 is not null && hash != asset.Sha256) throw new IOException("下载的文件校验失败，请重试");
        File.Move(partial, target, overwrite: true);
        return target;
    }

    /// <summary>Deletes downloaded updates that are already installed (or older); runs in the background.</summary>
    public static void CleanupDownloadsInBackground() => Task.Run(() =>
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "LabScheduleUpdate");
            if (!Directory.Exists(root)) return;
            foreach (var folder in Directory.GetDirectories(root))
            {
                if (UpdateFeed.ParseVersion(Path.GetFileName(folder)) is { } version && version <= CurrentVersion)
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Leftovers in %TEMP% are harmless; try again next time.
        }
    });

    /// <summary>True when the portable build may overwrite its own folder.</summary>
    public static bool CanWriteAppFolder()
    {
        try
        {
            var probe = Path.Combine(AppContext.BaseDirectory, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts the hand-over script. The caller must exit right afterwards; the script waits for that,
    /// installs the package and launches the new version (or the old one again if anything fails).
    /// </summary>
    public static void StartInstall(string package, bool installedCopy)
    {
        var folder = Path.GetDirectoryName(package)!;
        var source = package;
        if (!installedCopy)
        {
            var extracted = Path.Combine(folder, "extracted");
            if (Directory.Exists(extracted)) Directory.Delete(extracted, recursive: true);
            ZipFile.ExtractToDirectory(package, extracted);
            source = Directory.GetFiles(extracted, "LabSchedule.exe", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault() ?? throw new IOException("更新包里没有找到 LabSchedule.exe");
        }

        var script = Path.Combine(folder, "apply-update.ps1");
        File.WriteAllText(script, ApplyScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var appFolder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
            "-ProcessId", Environment.ProcessId.ToString(),
            "-Mode", installedCopy ? "msi" : "portable",
            "-Package", source,
            "-Target", appFolder,
            "-Log", Path.Combine(folder, "apply-update.log"),
            "-RegistryKey", RegistryKey
        })
        {
            start.ArgumentList.Add(argument);
        }
        Process.Start(start);
    }

    public static string Describe(Exception error) => error switch
    {
        TaskCanceledException or OperationCanceledException => "连接超时",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "GitHub 暂时拒绝了请求（可能是访问过于频繁），请稍后再试",
        HttpRequestException => "无法连接 GitHub，请检查网络",
        _ => error.Message
    };

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private const string ApplyScript = """
        param([int]$ProcessId, [string]$Mode, [string]$Package, [string]$Target, [string]$Log, [string]$RegistryKey = 'Software\LabSchedule')
        # Installs a downloaded update of 实验排班生成器 once the running copy has exited.
        $ErrorActionPreference = 'Stop'
        function Write-Log([string]$Text) { Add-Content -LiteralPath $Log -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $Text) -Encoding UTF8 }
        $exe = Join-Path $Target 'LabSchedule.exe'
        try {
            Write-Log "mode=$Mode package=$Package target=$Target"
            try { Wait-Process -Id $ProcessId -Timeout 120 -ErrorAction Stop } catch { }
            if ($Mode -eq 'msi') {
                $installer = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/i', ('"{0}"' -f $Package), '/passive', '/norestart') -Wait -PassThru
                Write-Log "msiexec exit code $($installer.ExitCode)"
                if ($installer.ExitCode -ne 0 -and $installer.ExitCode -ne 3010) { throw "msiexec exit code $($installer.ExitCode)" }
                $folder = (Get-ItemProperty -LiteralPath "HKCU:\$RegistryKey" -Name InstallFolder -ErrorAction SilentlyContinue).InstallFolder
                if ($folder) { $exe = Join-Path $folder 'LabSchedule.exe' }
            }
            else {
                & robocopy.exe $Package $Target /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
                Write-Log "robocopy exit code $LASTEXITCODE"
                if ($LASTEXITCODE -ge 8) { throw "robocopy exit code $LASTEXITCODE" }
            }
            Write-Log 'update installed'
        }
        catch {
            Write-Log "update failed: $_"
        }
        Start-Process -FilePath $exe
        """;
}
