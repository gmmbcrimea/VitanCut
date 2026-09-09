using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public sealed class CloudSyncService(AppState state)
{
    private const string DefaultProjectUrl = "https://oihkaipfyjpoekdzewzn.supabase.co";
    private const string DefaultPublishableKey = "sb_publishable_6IS_guUveBXMRS2RMuHHgw_ESWRBE0f";
    private const string SettingsFileName = "vitan-cut-cloud.json";
    private const string WorkspaceName = "Cutlist";
    private const string BaselineFileName = "vitan-cut-cloud-baseline.json";
    private const string SessionFileName = "vitan-cut-cloud-session.dat";
    private const int RecoveryCopiesLimit = 5;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private CloudSyncSettings _settings = new();
    private string _settingsPath = "";
    private string _baselinePath = "";
    private string _sessionPath = "";
    private string _accessToken = "";
    private string _refreshToken = "";

    public string Status { get; private set; } = "Облачная синхронизация не подключена.";
    public bool IsSignedIn => !string.IsNullOrWhiteSpace(_accessToken);
    public bool HasStoredSession => !string.IsNullOrWhiteSpace(_refreshToken);
    public bool HasUnsyncedLocalChanges => !string.IsNullOrWhiteSpace(_settings.WorkspaceId) &&
        (string.IsNullOrWhiteSpace(_settings.LastSnapshotHash) ||
         !string.Equals(SnapshotHash(), _settings.LastSnapshotHash, StringComparison.Ordinal));

    public void Load()
    {
        _settingsPath = Path.Combine(Path.GetDirectoryName(state.DatabasePath)!, SettingsFileName);
        _baselinePath = Path.Combine(Path.GetDirectoryName(state.DatabasePath)!, BaselineFileName);
        _sessionPath = Path.Combine(Path.GetDirectoryName(state.DatabasePath)!, SessionFileName);
        _refreshToken = LoadRefreshToken();
        try
        {
            _settings = File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<CloudSyncSettings>(File.ReadAllText(_settingsPath)) ?? new CloudSyncSettings()
                : new CloudSyncSettings();
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            _settings = new CloudSyncSettings();
            Status = "Не удалось прочитать настройки облачной синхронизации.";
        }

        Status = string.IsNullOrWhiteSpace(_settings.WorkspaceId)
            ? "Войдите в Supabase, чтобы подключить пространство Cutlist."
            : "Пространство Cutlist настроено. Войдите для синхронизации.";
    }

    public async Task<CloudSyncResult> SignInAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return Fail("Введите email и пароль пользователя Supabase.");

        var response = await SendAsync(HttpMethod.Post, "/auth/v1/token?grant_type=password", new { email = email.Trim(), password }, false);
        if (!response.Succeeded) return response;
        try
        {
            using var document = JsonDocument.Parse(response.Payload!);
            UpdateSession(document.RootElement);
            if (!IsSignedIn || string.IsNullOrWhiteSpace(_refreshToken)) return Fail("Supabase не вернул сеанс входа.");
            SaveRefreshToken();
            return await EnsureWorkspaceAsync();
        }
        catch (JsonException) { return Fail("Supabase вернул неполный ответ авторизации."); }
    }

    public async Task<CloudSyncResult> RestoreSessionAsync()
    {
        if (string.IsNullOrWhiteSpace(_refreshToken)) return Fail("Облачная синхронизация не подключена. Войдите в Supabase в настройках.");
        var response = await SendAsync(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token", new { refresh_token = _refreshToken }, false);
        if (!response.Succeeded) return Fail("Не удалось восстановить сеанс Supabase. Проверьте подключение или войдите заново в настройках.");
        try
        {
            using var document = JsonDocument.Parse(response.Payload!);
            UpdateSession(document.RootElement);
            if (!IsSignedIn) return Fail("Supabase не вернул действующий сеанс.");
            SaveRefreshToken();
            return await EnsureWorkspaceAsync();
        }
        catch (JsonException) { return Fail("Supabase вернул неполный ответ восстановления сеанса."); }
    }

    private async Task<CloudSyncResult> EnsureWorkspaceAsync()
    {
        var path = $"/rest/v1/workspaces?name=eq.{Uri.EscapeDataString(WorkspaceName)}&select=id&limit=1";
        var response = await SendAsync(HttpMethod.Get, path, null, true);
        if (!response.Succeeded) return response;

        try
        {
            using var document = JsonDocument.Parse(response.Payload!);
            if (document.RootElement.GetArrayLength() == 1)
            {
                SetWorkspaceId(document.RootElement[0].GetProperty("id").GetGuid().ToString());
                return Ok("Вход выполнен. Подключено пространство Cutlist.");
            }
        }
        catch (JsonException) { return Fail("Supabase вернул неверный список рабочих пространств."); }

        return await CreateDefaultWorkspaceAsync();
    }

    private async Task<CloudSyncResult> CreateDefaultWorkspaceAsync()
    {
        var payload = JsonDocument.Parse(state.CreateCloudSnapshot()).RootElement.Clone();
        var response = await SendAsync(HttpMethod.Post, "/rest/v1/rpc/create_vitan_workspace", new { p_name = WorkspaceName, p_payload = payload }, true);
        if (!response.Succeeded) return response;
        return ApplyWorkspaceResponse(response.Payload!, "Создано пространство Cutlist.");
    }

    public async Task<CloudSyncResult> DownloadAsync(bool discardLocalChanges = false)
    {
        if (!IsSignedIn) return Fail("Сначала войдите в Supabase.");
        if (!Guid.TryParse(_settings.WorkspaceId, out var workspaceId)) return Fail("Войдите в Supabase, чтобы подключить пространство Cutlist.");
        if (!discardLocalChanges && HasUnsyncedLocalChanges)
            return new CloudSyncResult(false, "Локальная база содержит несинхронизированные изменения. Подтвердите замену или сначала опубликуйте их.", null, true);

        string? recoveryPath = null;
        if (discardLocalChanges && HasUnsyncedLocalChanges)
        {
            recoveryPath = TrySaveRecoveryCopy("before-cloud-download");
            if (recoveryPath is null)
                return Fail("Не удалось создать локальную резервную копию. Загрузка облачной версии отменена.");
        }

        var path = $"/rest/v1/workspace_snapshots?workspace_id=eq.{workspaceId:D}&select=revision,payload";
        var response = await SendAsync(HttpMethod.Get, path, null, true);
        if (!response.Succeeded) return response;
        try
        {
            using var document = JsonDocument.Parse(response.Payload!);
            if (document.RootElement.GetArrayLength() != 1) return Fail("Снимок рабочего пространства не найден.");
            var snapshot = document.RootElement[0];
            var payload = snapshot.GetProperty("payload").GetRawText();
            state.ReplaceFromCloudSnapshot(payload);
            _settings.LastRevision = snapshot.GetProperty("revision").GetInt64();
            _settings.LastSnapshotHash = SnapshotHash();
            SaveSettings();
            SaveBaseline(payload);
            return Ok(recoveryPath is null
                ? "Облачная база загружена в локальную рабочую копию."
                : $"Облачная база загружена. Локальная версия сохранена: {recoveryPath}");
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            return Fail($"Не удалось применить облачную базу. {error.Message}");
        }
    }

    public async Task<CloudSyncResult> PublishAsync()
    {
        if (!IsSignedIn) return Fail("Сначала войдите в Supabase.");
        if (!Guid.TryParse(_settings.WorkspaceId, out var workspaceId)) return Fail("Войдите в Supabase, чтобы подключить пространство Cutlist.");
        var baselineJson = LoadBaseline();
        if (baselineJson is null)
            return Fail("Для адресной синхронизации один раз загрузите облачную базу. Локальная работа останется без изменений.");

        var snapshotResponse = await SendAsync(HttpMethod.Get,
            $"/rest/v1/workspace_snapshots?workspace_id=eq.{workspaceId:D}&select=revision,payload", null, true);
        if (!snapshotResponse.Succeeded) return snapshotResponse;

        Database baseline;
        Database remote;
        long remoteRevision;
        try
        {
            using var document = JsonDocument.Parse(snapshotResponse.Payload!);
            if (document.RootElement.GetArrayLength() != 1) return Fail("Снимок рабочего пространства не найден.");
            var snapshot = document.RootElement[0];
            remoteRevision = snapshot.GetProperty("revision").GetInt64();
            baseline = CloudEntityMerge.DeserializeDatabase(baselineJson);
            remote = CloudEntityMerge.DeserializeDatabase(snapshot.GetProperty("payload").GetRawText());
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            return Fail($"Не удалось подготовить адресную синхронизацию. {error.Message}");
        }

        var merge = CloudEntityMerge.Merge(baseline, state.Database, remote);
        if (merge.Conflicts.Count > 0)
        {
            var recoveryPath = TrySaveRecoveryCopy("entity-conflict");
            var names = string.Join(", ", merge.Conflicts.Take(3).Select(conflict => conflict.DisplayName));
            var suffix = merge.Conflicts.Count > 3 ? $" и ещё {merge.Conflicts.Count - 3}" : "";
            return new CloudSyncResult(false,
                $"Конфликт изменений: {names}{suffix}. Локальная работа сохранена: {recoveryPath ?? "резервную копию создать не удалось"}.", null, true);
        }

        var remoteEntities = CloudEntityMerge.Snapshot(remote);
        var mergedEntities = CloudEntityMerge.Snapshot(merge.Database);
        var revisionsResponse = await SendAsync(HttpMethod.Get,
            $"/rest/v1/workspace_entities?workspace_id=eq.{workspaceId:D}&select=entity_type,entity_id,revision", null, true);
        if (!revisionsResponse.Succeeded) return revisionsResponse;
        var revisions = ParseEntityRevisions(revisionsResponse.Payload!);
        var changes = remoteEntities.Keys.Union(mergedEntities.Keys)
            .Where(key => !remoteEntities.TryGetValue(key, out var remoteEntity) || !mergedEntities.TryGetValue(key, out var mergedEntity) || remoteEntity.Payload != mergedEntity.Payload)
            .Select(key => new
            {
                entity_type = key.Type,
                entity_id = key.Id,
                payload = mergedEntities.TryGetValue(key, out var entity) ? JsonDocument.Parse(entity.Payload).RootElement.Clone() : JsonDocument.Parse("{}").RootElement.Clone(),
                expected_revision = revisions.TryGetValue(key, out var revision) ? revision : 0L,
                deleted = !mergedEntities.ContainsKey(key)
            }).ToArray();

        var mergedJson = CloudEntityMerge.SerializeDatabase(merge.Database);
        if (changes.Length == 0)
        {
            state.ReplaceFromCloudSnapshot(mergedJson);
            _settings.LastRevision = remoteRevision;
            _settings.LastSnapshotHash = SnapshotHash();
            SaveSettings();
            SaveBaseline(mergedJson);
            return Ok("Локальная база объединена с актуальной облачной версией.");
        }

        var payload = JsonDocument.Parse(mergedJson).RootElement.Clone();
        var response = await SendAsync(HttpMethod.Post, "/rest/v1/rpc/sync_vitan_entities", new
        {
            p_workspace_id = workspaceId,
            p_expected_snapshot_revision = remoteRevision,
            p_changes = changes,
            p_snapshot_payload = payload
        }, true);
        if (!response.Succeeded)
        {
            if (!response.IsConflict) return response;
            var recoveryPath = TrySaveRecoveryCopy("publish-conflict");
            var message = recoveryPath is null
                ? "Облачная база изменилась на другом компьютере. Локальная работа осталась в программе; не загружайте облачную версию, пока не сохраните её отдельно."
                : $"Облачная база изменилась на другом компьютере. Локальная работа сохранена отдельно: {recoveryPath}";
            Status = message;
            return new CloudSyncResult(false, message, response.Payload, true);
        }
        try
        {
            using var document = JsonDocument.Parse(response.Payload!);
            _settings.LastRevision = document.RootElement[0].GetProperty("revision").GetInt64();
            state.ReplaceFromCloudSnapshot(mergedJson);
            _settings.LastSnapshotHash = SnapshotHash();
            SaveSettings();
            SaveBaseline(mergedJson);
            return Ok("Локальные изменения объединены и опубликованы в облаке.");
        }
        catch (JsonException) { return Fail("Supabase вернул неверный ответ публикации."); }
    }

    public async Task<CloudHistoryResult> GetHistoryAsync()
    {
        if (!IsSignedIn) return CloudHistoryResult.Fail("Сначала войдите в Supabase.");
        if (!Guid.TryParse(_settings.WorkspaceId, out var workspaceId))
            return CloudHistoryResult.Fail("Войдите в Supabase, чтобы подключить пространство Cutlist.");

        var path = $"/rest/v1/workspace_snapshot_history?workspace_id=eq.{workspaceId:D}&select=revision,saved_at&order=revision.desc&limit=6";
        var response = await SendAsync(HttpMethod.Get, path, null, true);
        if (!response.Succeeded) return CloudHistoryResult.Fail(response.Message);

        try
        {
            using var document = JsonDocument.Parse(response.Payload!);
            var versions = document.RootElement.EnumerateArray()
                .Select(row => new CloudSnapshotVersion(
                    row.GetProperty("revision").GetInt64(),
                    row.GetProperty("saved_at").GetDateTimeOffset()))
                .OrderByDescending(version => version.Revision)
                .ToArray();
            return versions.Length == 0
                ? CloudHistoryResult.Fail("В облаке пока нет сохранённых версий.")
                : CloudHistoryResult.Ok(versions);
        }
        catch (JsonException) { return CloudHistoryResult.Fail("Supabase вернул неверную историю версий."); }
    }

    public async Task<CloudSyncResult> RestoreHistoryAsync(long revision)
    {
        if (!IsSignedIn) return Fail("Сначала войдите в Supabase.");
        if (!Guid.TryParse(_settings.WorkspaceId, out var workspaceId))
            return Fail("Войдите в Supabase, чтобы подключить пространство Cutlist.");

        var current = await SendAsync(HttpMethod.Get,
            $"/rest/v1/workspace_snapshots?workspace_id=eq.{workspaceId:D}&select=revision", null, true);
        if (!current.Succeeded) return current;

        var target = await SendAsync(HttpMethod.Get,
            $"/rest/v1/workspace_snapshot_history?workspace_id=eq.{workspaceId:D}&revision=eq.{revision}&select=payload", null, true);
        if (!target.Succeeded) return target;

        try
        {
            using var currentDocument = JsonDocument.Parse(current.Payload!);
            using var targetDocument = JsonDocument.Parse(target.Payload!);
            if (currentDocument.RootElement.GetArrayLength() != 1 || targetDocument.RootElement.GetArrayLength() != 1)
                return Fail("Выбранная версия больше недоступна в облачной истории.");

            var recoveryPath = TrySaveRecoveryCopy("before-history-restore");
            if (recoveryPath is null) return Fail("Не удалось создать локальную резервную копию. Восстановление отменено.");

            state.ReplaceFromCloudSnapshot(targetDocument.RootElement[0].GetProperty("payload").GetRawText());
            _settings.LastRevision = currentDocument.RootElement[0].GetProperty("revision").GetInt64();
            _settings.LastSnapshotHash = "";
            SaveSettings();
            return Ok($"Версия {revision} восстановлена в локальную рабочую копию. Предыдущая локальная версия: {recoveryPath}. Проверьте данные и опубликуйте их отдельной новой версией.");
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            return Fail($"Не удалось восстановить версию. {error.Message}");
        }
    }

    private async Task<CloudSyncResult> SendAsync(HttpMethod method, string path, object? body, bool withAuth)
    {
        try
        {
            using var request = new HttpRequestMessage(method, DefaultProjectUrl + path);
            request.Headers.Add("apikey", DefaultPublishableKey);
            if (withAuth) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) return new CloudSyncResult(true, "", payload);
            if (payload.Contains("revision_conflict", StringComparison.OrdinalIgnoreCase))
                return new CloudSyncResult(false, "Облачная база изменилась на другом компьютере. Сначала загрузите её и разрешите конфликт.", payload, true);
            return Fail($"Supabase вернул {(int)response.StatusCode}: {ExtractMessage(payload)}");
        }
        catch (HttpRequestException error) { return Fail($"Нет связи с Supabase. {error.Message}"); }
        catch (TaskCanceledException) { return Fail("Supabase не ответил вовремя."); }
    }

    private CloudSyncResult ApplyWorkspaceResponse(string payload, string message)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var row = document.RootElement[0];
            SetWorkspaceId(row.GetProperty("workspace_id").GetGuid().ToString(), save: false);
            _settings.LastRevision = row.GetProperty("revision").GetInt64();
            _settings.LastSnapshotHash = SnapshotHash();
            SaveSettings();
            SaveBaseline(state.CreateCloudSnapshot());
            return new CloudSyncResult(true, message, payload);
        }
        catch (JsonException) { return Fail("Supabase вернул неверный ответ рабочего пространства."); }
    }

    private void SetWorkspaceId(string workspaceId, bool save = true)
    {
        var changed = !string.Equals(_settings.WorkspaceId, workspaceId, StringComparison.Ordinal);
        _settings.WorkspaceId = workspaceId;
        if (changed)
        {
            _settings.LastRevision = 0;
            _settings.LastSnapshotHash = "";
        }
        if (save) SaveSettings();
    }

    private string? TrySaveRecoveryCopy(string reason)
    {
        try
        {
            var folder = Path.Combine(Path.GetDirectoryName(state.DatabasePath)!, "cloud-recovery");
            Directory.CreateDirectory(folder);
            var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{reason}.json";
            var path = Path.Combine(folder, fileName);
            File.WriteAllText(path, state.CreateCloudSnapshot());

            foreach (var oldCopy in new DirectoryInfo(folder).GetFiles("*.json")
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .Skip(RecoveryCopiesLimit))
                oldCopy.Delete();

            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private Dictionary<CloudEntityKey, long> ParseEntityRevisions(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.EnumerateArray().ToDictionary(
                row => new CloudEntityKey(row.GetProperty("entity_type").GetString() ?? "", row.GetProperty("entity_id").GetString() ?? ""),
                row => row.GetProperty("revision").GetInt64());
        }
        catch (JsonException) { return []; }
    }

    private string? LoadBaseline()
    {
        try { return File.Exists(_baselinePath) ? File.ReadAllText(_baselinePath) : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private void SaveBaseline(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_baselinePath)!);
        File.WriteAllText(_baselinePath, json);
    }

    private void UpdateSession(JsonElement response)
    {
        _accessToken = response.TryGetProperty("access_token", out var access) ? access.GetString() ?? "" : "";
        if (response.TryGetProperty("refresh_token", out var refresh) && !string.IsNullOrWhiteSpace(refresh.GetString()))
            _refreshToken = refresh.GetString()!;
    }

    private string LoadRefreshToken()
    {
        if (!OperatingSystem.IsWindows()) return "";
        try
        {
            if (!File.Exists(_sessionPath)) return "";
            var encrypted = File.ReadAllBytes(_sessionPath);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return "";
        }
    }

    private void SaveRefreshToken()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Защищённое хранилище доступно только в Windows.");
        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(_refreshToken), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_sessionPath, encrypted);
    }

    private CloudSyncResult Ok(string message) { Status = message; return new CloudSyncResult(true, message); }
    private CloudSyncResult Fail(string message) { Status = message; return new CloudSyncResult(false, message); }
    private string SnapshotHash() => SnapshotHash(state.CreateCloudSnapshot());
    private static string SnapshotHash(string json) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static string ExtractMessage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("message", out var message)
                ? message.GetString() ?? payload
                : payload;
        }
        catch (JsonException) { return string.IsNullOrWhiteSpace(payload) ? "пустой ответ" : payload; }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings));
    }
}

public sealed class CloudSyncSettings
{
    public string WorkspaceId { get; set; } = "";
    public long LastRevision { get; set; }
    public string LastSnapshotHash { get; set; } = "";
}

public sealed record CloudSyncResult(bool Succeeded, string Message, string? Payload = null, bool IsConflict = false);
public sealed record CloudSnapshotVersion(long Revision, DateTimeOffset SavedAt)
{
    public string Label => $"Версия {Revision} · {SavedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
}

public sealed record CloudHistoryResult(bool Succeeded, string Message, IReadOnlyList<CloudSnapshotVersion> Versions)
{
    public static CloudHistoryResult Ok(IReadOnlyList<CloudSnapshotVersion> versions) => new(true, "", versions);
    public static CloudHistoryResult Fail(string message) => new(false, message, []);
}
