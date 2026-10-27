using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
    string? ReleaseUrl = null,
    string? ReleaseNotes = null)
{
    public bool CanInstall => Availability == UpdateAvailability.Available && !string.IsNullOrWhiteSpace(DownloadUrl);
}

public sealed record UpdateDownloadProgress(long DownloadedBytes, long? TotalBytes)
{
    public double? Percent => TotalBytes is > 0 ? DownloadedBytes * 100d / TotalBytes.Value : null;
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
    private readonly bool _enabled;
    private readonly string? _rollbackRoot;
    private readonly string? _temporaryRoot;

    public AppUpdateInfo? LastCheck { get; private set; }
    public bool CanRollback => File.Exists(Path.Combine(GetConfiguredRollbackDirectory(_applicationDirectory), "VitanCut.WinUI.exe"));

    public AppUpdateService()
        : this(AppPaths.ForCurrentProcess(AppContext.BaseDirectory))
    {
    }

    public AppUpdateService(AppPaths paths)
        : this(paths.ApplicationDirectory, GetApplicationVersion(), enabled: !paths.IsDevelopment,
            rollbackRoot: paths.IsDevelopment ? paths.RollbackDirectory : null,
            temporaryRoot: paths.IsDevelopment ? paths.TemporaryDirectory : null)
    {
    }

    internal AppUpdateService(string applicationDirectory, Version currentVersion, bool enabled = true,
        string? rollbackRoot = null, string? temporaryRoot = null)
    {
        _applicationDirectory = Path.GetFullPath(applicationDirectory);
        _currentVersion = currentVersion;
        _enabled = enabled;
        _rollbackRoot = rollbackRoot;
        _temporaryRoot = temporaryRoot;
    }

    public async Task<AppUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return Remember(new AppUpdateInfo(UpdateAvailability.Unavailable, _currentVersion, null,
                "Проверка обновлений отключена в изолированном режиме разработки."));
        try
        {
            using var response = await Client.GetAsync(
                $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases?per_page=100",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Remember(new AppUpdateInfo(
                    UpdateAvailability.Unavailable, _currentVersion, null,
                    "Не удалось проверить обновления. Проверьте подключение к GitHub."));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var releases = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            var releaseCandidate = releases?
                .Select(release => new { Release = release, IsVersion = TryParseVersion(release.TagName, out var version), Version = version })
                .Where(candidate => candidate.IsVersion && candidate.Release.Assets?.Any(IsPortableAsset) == true)
                .OrderByDescending(candidate => candidate.Version)
                .FirstOrDefault();
            if (releaseCandidate is null)
            {
                return Remember(new AppUpdateInfo(
                    UpdateAvailability.Unavailable, _currentVersion, null,
                    "В GitHub не найден релиз программы с portable-архивом."));
            }
            var release = releaseCandidate.Release;
            var latest = releaseCandidate.Version;

            if (latest <= _currentVersion)
            {
                return Remember(new AppUpdateInfo(
                    UpdateAvailability.UpToDate, _currentVersion, latest,
                    $"Установлена актуальная версия {FormatVersion(_currentVersion)}.",
                    ReleaseUrl: release.HtmlUrl));
            }

            var asset = release.Assets?
                .Where(IsPortableAsset)
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
                asset.BrowserDownloadUrl, release.HtmlUrl, SummarizeReleaseNotes(release.Body, release.TagName)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Remember(new AppUpdateInfo(UpdateAvailability.Unavailable, _currentVersion, null,
                "Не удалось подключиться к GitHub для проверки обновлений."));
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

    private static bool IsPortableAsset(GitHubAsset asset) =>
        asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
        asset.Name.Contains("portable", StringComparison.OrdinalIgnoreCase);

    internal static string? SummarizeReleaseNotes(string? body, string? tagName)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var lines = body.Split('\n', StringSplitOptions.TrimEntries);
        var version = tagName?.TrimStart('v', 'V');
        var headings = lines.Select((line, index) => (line, index))
            .Where(item => item.line.StartsWith('#'))
            .ToList();
        var versionHeadingPosition = string.IsNullOrWhiteSpace(version)
            ? -1
            : headings.FindIndex(item => item.line.Contains(version, StringComparison.OrdinalIgnoreCase));
        var versionHeading = versionHeadingPosition >= 0 ? headings[versionHeadingPosition].index : -1;
        if (versionHeading >= 0)
        {
            var nextHeading = headings.FirstOrDefault(item => item.index > versionHeading).index;
            lines = lines.Skip(versionHeading + 1).Take(nextHeading > versionHeading ? nextHeading - versionHeading - 1 : int.MaxValue).ToArray();
        }
        else if (headings.Count > 0)
        {
            var firstContentHeading = headings.FindIndex(item =>
                item.line.Contains("измен", StringComparison.OrdinalIgnoreCase) ||
                item.line.Contains("what's changed", StringComparison.OrdinalIgnoreCase) ||
                item.line.Contains("what’s changed", StringComparison.OrdinalIgnoreCase) ||
                item.line.Contains("what's new", StringComparison.OrdinalIgnoreCase) ||
                item.line.Contains("what’s new", StringComparison.OrdinalIgnoreCase));
            if (firstContentHeading >= 0)
            {
                var start = headings[firstContentHeading].index;
                var end = headings.Skip(firstContentHeading + 1).Select(item => item.index).FirstOrDefault(int.MaxValue);
                lines = lines.Skip(start + 1).Take(end == int.MaxValue ? int.MaxValue : end - start - 1).ToArray();
            }
        }
        var cleaned = lines
            .Select(line => line.TrimStart('#', ' ', '-', '*', '>').Trim())
            .Where(line => line.Length > 0 &&
                !line.StartsWith("Full Changelog", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("Полный список изменений", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("New Contributors", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("Новые участники", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var summary = string.Join(" · ", cleaned);
        return summary.Length <= 480 ? summary : summary[..477].TrimEnd() + "...";
    }

    public async Task<bool> InstallAsync(AppUpdateInfo update, IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled) return false;
        if (!update.CanInstall || string.IsNullOrWhiteSpace(update.DownloadUrl)) return false;
        if (!CanWriteTo(_applicationDirectory)) return false;

        var workDirectory = CreateWorkDirectory("VitanCut-update-");
        var archivePath = Path.Combine(workDirectory, "release.zip");
        var extractDirectory = Path.Combine(workDirectory, "files");
        Directory.CreateDirectory(extractDirectory);

        try
        {
            using var response = await Client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(archivePath))
            {
                var buffer = new byte[80 * 1024];
                long downloadedBytes = 0;
                int read;
                progress?.Report(new UpdateDownloadProgress(0, totalBytes));
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloadedBytes += read;
                    progress?.Report(new UpdateDownloadProgress(downloadedBytes, totalBytes));
                }
            }

            ExtractArchiveSafely(archivePath, extractDirectory);
            var executable = Directory.EnumerateFiles(extractDirectory, "VitanCut.WinUI.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (executable is null) return false;

            var sourceDirectory = Path.GetDirectoryName(executable)!;
            var launchedExe = Path.Combine(_applicationDirectory, Path.GetFileName(executable));
            var backupDirectory = GetConfiguredRollbackDirectory(_applicationDirectory);
            var script = BuildUpdateScript(sourceDirectory, _applicationDirectory, launchedExe, workDirectory, Environment.ProcessId, backupDirectory);
            var scriptPath = Path.Combine(workDirectory, "apply-update.ps1");
            File.WriteAllText(scriptPath, script, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            if (!await StartHelperAsync(scriptPath, workDirectory, cancellationToken))
                throw new IOException("Не удалось запустить установщик обновления.");
            return true;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            TryDelete(workDirectory);
            return false;
        }
    }

    public async Task<bool> RollbackAsync()
    {
        if (!_enabled) return false;
        var backupDirectory = GetConfiguredRollbackDirectory(_applicationDirectory);
        var backupExecutable = Path.Combine(backupDirectory, "VitanCut.WinUI.exe");
        if (!File.Exists(backupExecutable) || !CanWriteTo(_applicationDirectory)) return false;

        var workDirectory = CreateWorkDirectory("VitanCut-rollback-");
        try
        {
            Directory.CreateDirectory(workDirectory);
            var script = BuildUpdateScript(backupDirectory, _applicationDirectory,
                Path.Combine(_applicationDirectory, "VitanCut.WinUI.exe"), workDirectory, Environment.ProcessId, mirror: true);
            var scriptPath = Path.Combine(workDirectory, "rollback.ps1");
            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return await StartHelperAsync(scriptPath, workDirectory, CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            TryDelete(workDirectory);
            return false;
        }
    }

    private static async Task<bool> StartHelperAsync(string scriptPath, string workDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath })
            start.ArgumentList.Add(argument);
        using var helper = Process.Start(start);
        if (helper is null) return false;
        var ready = Path.Combine(workDirectory, "helper-ready");
        try
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (File.Exists(ready)) return true;
                if (helper.HasExited) return false;
                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException) { if (!helper.HasExited) helper.Kill(); throw; }
        // A late helper must not replace files after reporting a launch failure.
        if (!helper.HasExited) helper.Kill();
        return false;
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

    internal static string BuildUpdateScript(string source, string target, string executable, string workDirectory, int processId, string? backupDirectory = null, bool mirror = false) => string.Join(Environment.NewLine,
        "$ErrorActionPreference = 'Stop'",
        $"$source = '{EscapePowerShell(source)}'",
        $"$target = '{EscapePowerShell(target)}'",
        $"$executable = '{EscapePowerShell(executable)}'",
        $"$workDirectory = '{EscapePowerShell(workDirectory)}'",
        $"$backupDirectory = '{EscapePowerShell(backupDirectory ?? "")}'",
        $"$mirror = ${mirror.ToString().ToLowerInvariant()}",
        "$source = [IO.Path]::GetFullPath($source).TrimEnd('\\')",
        "$target = [IO.Path]::GetFullPath($target).TrimEnd('\\')",
        "if ($backupDirectory) { $backupDirectory = [IO.Path]::GetFullPath($backupDirectory).TrimEnd('\\') }",
        "$backupReady = $false; $copyStarted = $false",
        "foreach ($directory in @($workDirectory, $backupDirectory) | Where-Object { $_ }) {",
        "  $full = [IO.Path]::GetFullPath($directory).TrimEnd('\\')",
        "  if ($full -eq [IO.Path]::GetPathRoot($full).TrimEnd('\\') -or $full -eq $target -or $target.StartsWith($full + '\\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe updater working directory.' }",
        "}",
        "$logDirectory = Join-Path $env:LOCALAPPDATA 'VitanCut\\logs'",
        "$log = Join-Path $logDirectory 'updater.log'",
        "New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null",
        "function Write-UpdateLog([string]$message) { Add-Content -LiteralPath $log -Encoding UTF8 -Value (\"$(Get-Date -Format o) $message\") }",
        "",
        "try {",
        "  [IO.File]::WriteAllText((Join-Path $workDirectory 'helper-ready'), 'ready')",
        $"  Write-UpdateLog 'Waiting for application process {processId} to exit.'",
        $"  while (Get-Process -Id {processId} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}",
        "  if ($backupDirectory) {",
        "    Write-UpdateLog 'Saving current version for rollback.'",
        "    if (Test-Path -LiteralPath $backupDirectory) { Remove-Item -LiteralPath $backupDirectory -Recurse -Force }",
        "    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null",
        "    & robocopy $target $backupDirectory /E /XD (Join-Path $target 'data') /R:3 /W:1 /NFL /NDL /NJH /NJS /NC /NS | Out-File -LiteralPath $log -Encoding UTF8 -Append",
        "    if ($LASTEXITCODE -gt 7) { throw \"Could not save rollback copy (robocopy $LASTEXITCODE).\" }",
        "    $backupReady = $true",
        "  }",
        "  Write-UpdateLog 'Copying update files.'",
        "  $copyStarted = $true",
        "  if ($mirror) { & robocopy $source $target /MIR /XD 'data' /IS /IT /R:3 /W:1 /NFL /NDL /NJH /NJS /NC /NS | Out-File -LiteralPath $log -Encoding UTF8 -Append } else { & robocopy $source $target /E /XD (Join-Path $source 'data') (Join-Path $target 'data') /IS /IT /R:3 /W:1 /NFL /NDL /NJH /NJS /NC /NS | Out-File -LiteralPath $log -Encoding UTF8 -Append }",
        "  if ($LASTEXITCODE -gt 7) { throw \"robocopy failed with code $LASTEXITCODE\" }",
        "  if (-not (Test-Path -LiteralPath $executable)) { throw 'Updated executable was not found.' }",
        "  Write-UpdateLog 'Starting updated application.'",
        "  Start-Process -FilePath $executable -WorkingDirectory $target -WindowStyle Normal",
        "  Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue",
        "  Write-UpdateLog 'Update completed.'",
        "} catch {",
        "  Write-UpdateLog (\"Update failed: \" + $_.Exception.Message)",
        "  if ($backupReady -and $copyStarted) {",
        "    try {",
        "      Write-UpdateLog 'Restoring previous application files.'",
        "      & robocopy $backupDirectory $target /MIR /XD 'data' /IS /IT /R:3 /W:1 /NFL /NDL /NJH /NJS /NC /NS | Out-File -LiteralPath $log -Encoding UTF8 -Append",
        "      if ($LASTEXITCODE -gt 7) { throw \"rollback robocopy failed with code $LASTEXITCODE\" }",
        "      if (Test-Path -LiteralPath $executable) { Start-Process -FilePath $executable -WorkingDirectory $target -WindowStyle Normal }",
        "      Write-UpdateLog 'Previous application files restored.'",
        "    } catch { Write-UpdateLog (\"Rollback failed: \" + $_.Exception.Message) }",
        "  }",
        "  elseif (-not $copyStarted -and (Test-Path -LiteralPath $executable)) { Start-Process -FilePath $executable -WorkingDirectory $target -WindowStyle Normal }",
        "}");

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string GetRollbackDirectory(string applicationDirectory)
    {
        var normalized = Path.GetFullPath(applicationDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VitanCut", "rollback", key);
    }

    private string GetConfiguredRollbackDirectory(string applicationDirectory) =>
        _rollbackRoot is not null
            ? Path.Combine(_rollbackRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(applicationDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())))[..16])
            : GetRollbackDirectory(applicationDirectory);

    private string CreateWorkDirectory(string prefix)
    {
        var root = _temporaryRoot ?? Path.GetTempPath();
        var directory = Path.Combine(root, prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

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

        [JsonPropertyName("body")]
        public string? Body { get; init; }

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
