using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VitanCut.WinUI.Services;

public enum UpdateAvailability
{
    UpToDate,
    Available,
    Unavailable
}

public sealed record AppUpdateInfo(
    UpdateAvailability Availability,
    Version CurrentVersion,
    Version? LatestVersion,
    string Message,
    string? DownloadUrl = null,
    string? ReleaseUrl = null)
{
    public bool CanInstall => Availability == UpdateAvailability.Available && !string.IsNullOrWhiteSpace(DownloadUrl);
}

/// <summary>Checks the public GitHub Release feed and replaces a portable installation after exit.</summary>
public sealed class AppUpdateService
{
    // Keep the feed location in one place so a future repository move does not affect the UI.
    public const string RepositoryOwner = "gmmbcrimea";
    public const string RepositoryName = "VitanCut";
    public static string ReleasesPageUrl => $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases";

    private static readonly HttpClient Client = CreateClient();
    private readonly string _applicationDirectory;
    private readonly Version _currentVersion;

    public AppUpdateInfo? LastCheck { get; private set; }

    public AppUpdateService()
        : this(AppContext.BaseDirectory, GetApplicationVersion())
    {
    }

    internal AppUpdateService(string applicationDirectory, Version currentVersion)
    {
        _applicationDirectory = Path.GetFullPath(applicationDirectory);
        _currentVersion = currentVersion;
    }

    public async Task<AppUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync(
                $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Remember(new AppUpdateInfo(
                    UpdateAvailability.Unavailable, _currentVersion, null,
                    "Не удалось проверить обновления. Проверьте подключение к GitHub."));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            if (release is null || !TryParseVersion(release.TagName, out var latest))
            {
                return Remember(new AppUpdateInfo(
                    UpdateAvailability.Unavailable, _currentVersion, null,
                    "В последнем релизе GitHub не указан корректный номер версии."));
            }

            if (latest <= _currentVersion)
            {
                return Remember(new AppUpdateInfo(
                    UpdateAvailability.UpToDate, _currentVersion, latest,
                    $"Установлена актуальная версия {FormatVersion(_currentVersion)}.",
                    ReleaseUrl: release.HtmlUrl));
            }

            var asset = release.Assets?
                .Where(item => item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                               item.Name.Contains("portable", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Size)
                .FirstOrDefault();
            if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            {
                return Remember(new AppUpdateInfo(
                    UpdateAvailability.Unavailable, _currentVersion, latest,
                    "Новая версия найдена, но portable-архив пока не прикреплён к релизу GitHub.",
                    ReleaseUrl: release.HtmlUrl));
            }

            return Remember(new AppUpdateInfo(
                UpdateAvailability.Available, _currentVersion, latest,
                $"Доступна версия {FormatVersion(latest)}. Установлена {FormatVersion(_currentVersion)}.",
                asset.BrowserDownloadUrl, release.HtmlUrl));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Remember(new AppUpdateInfo(UpdateAvailability.Unavailable, _currentVersion, null,
                "Не удалось подключиться к GitHub для проверки обновлений."));
        }
        catch (JsonException)
        {
            return Remember(new AppUpdateInfo(UpdateAvailability.Unavailable, _currentVersion, null,
                "GitHub вернул некорректные данные о релизе."));
        }
    }

    public async Task<bool> InstallAsync(AppUpdateInfo update, CancellationToken cancellationToken = default)
    {
        if (!update.CanInstall || string.IsNullOrWhiteSpace(update.DownloadUrl)) return false;
        if (!CanWriteTo(_applicationDirectory)) return false;

        var workDirectory = Path.Combine(Path.GetTempPath(), "VitanCut-update-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(workDirectory, "release.zip");
        var extractDirectory = Path.Combine(workDirectory, "files");
        Directory.CreateDirectory(extractDirectory);

        try
        {
            using var response = await Client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(archivePath))
                await input.CopyToAsync(output, cancellationToken);

            ExtractArchiveSafely(archivePath, extractDirectory);
            var executable = Directory.EnumerateFiles(extractDirectory, "VitanCut.WinUI.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (executable is null) return false;

            var sourceDirectory = Path.GetDirectoryName(executable)!;
            var launchedExe = Path.Combine(_applicationDirectory, Path.GetFileName(executable));
            var scriptPath = Path.Combine(workDirectory, "apply-update.cmd");
            File.WriteAllText(scriptPath, BuildUpdateScript(sourceDirectory, _applicationDirectory, launchedExe, workDirectory));
            Process.Start(new ProcessStartInfo(scriptPath) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            return true;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            TryDelete(workDirectory);
            return false;
        }
    }

    internal static bool TryParseVersion(string? tag, out Version version)
    {
        var value = tag?.Trim() ?? "";
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var separator = value.IndexOfAny(['-', '+']);
        if (separator >= 0) value = value[..separator];
        return Version.TryParse(value, out version!);
    }

    internal static string FormatVersion(Version version) => version.Build >= 0
        ? $"{version.Major}.{version.Minor}.{version.Build}"
        : $"{version.Major}.{version.Minor}";

    private AppUpdateInfo Remember(AppUpdateInfo update)
    {
        LastCheck = update;
        return update;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VitanCut", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static Version GetApplicationVersion() => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    private static bool CanWriteTo(string directory)
    {
        try
        {
            var path = Path.Combine(directory, ".vitancut-write-test-" + Guid.NewGuid().ToString("N"));
            using (File.Create(path)) { }
            File.Delete(path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void ExtractArchiveSafely(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var path = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Архив обновления содержит недопустимый путь.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: true);
        }
    }

    private static string BuildUpdateScript(string source, string target, string executable, string workDirectory) => $"""
@echo off
timeout /t 2 /nobreak >nul
robocopy "{source}" "{target}" /E /IS /IT /NFL /NDL /NJH /NJS /NC /NS >nul
start "" "{executable}"
rd /s /q "{workDirectory}"
""";

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; init; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
    }
}
