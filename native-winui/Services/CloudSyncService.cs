using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public sealed class CloudSyncService
{
    private const string DefaultProjectUrl = "https://oihkaipfyjpoekdzewzn.supabase.co";
    private const string DefaultPublishableKey = "sb_publishable_6IS_guUveBXMRS2RMuHHgw_ESWRBE0f";
    private const string SettingsFileName = "vitan-cut-cloud.json";
    private const string WorkspaceName = "Cutlist";
    private const string BaselineFileName = "vitan-cut-cloud-baseline.json";
    private const string SessionFileName = "vitan-cut-cloud-session.dat";
    private const int RecoveryCopiesLimit = 5;
    private readonly AppState state;
    private readonly HttpClient _http;
    private readonly string _projectUrl;
    private readonly string _publishableKey;
    private readonly bool _allowIsolatedTransport;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private int _operationInProgress;
    private CancellationToken _activeOperationToken;
    private CloudSyncSettings _settings = new();
    private string _settingsPath = "";
    private string _baselinePath = "";
    private string _sessionPath = "";
    private string _accessToken = "";
    private string _refreshToken = "";

    public CloudSyncService(AppState state)
        : this(state, new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, DefaultProjectUrl, DefaultPublishableKey, allowIsolatedTransport: false)
    {
    }

    internal CloudSyncService(AppState state, HttpClient http, string projectUrl, string publishableKey)
        : this(state, http, projectUrl, publishableKey, allowIsolatedTransport: true)
    {
        if (!Uri.TryCreate(projectUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) || uri.IsLoopback))
            throw new ArgumentException("Изолированный transport разрешён только для тестового адреса.", nameof(projectUrl));
    }

    private CloudSyncService(AppState state, HttpClient http, string projectUrl, string publishableKey, bool allowIsolatedTransport)
    {
        this.state = state;
        _http = http;
        _projectUrl = projectUrl.TrimEnd('/');
        _publishableKey = publishableKey;
        _allowIsolatedTransport = allowIsolatedTransport;
    }

    public bool IsEnabled => state.Paths is { IsDevelopment: false } || _allowIsolatedTransport;
    public bool IsOperationInProgress => Volatile.Read(ref _operationInProgress) != 0;

    public string Status { get; private set; } = "Облачная синхронизация не подключена.";
    public bool IsSignedIn => !string.IsNullOrWhiteSpace(_accessToken);
    public bool HasError { get; private set; }
    public bool HasStoredSession => !string.IsNullOrWhiteSpace(_refreshToken);
    public bool HasUnsyncedLocalChanges => !string.IsNullOrWhiteSpace(_settings.WorkspaceId) &&
        (string.IsNullOrWhiteSpace(_settings.LastSnapshotHash) ||
         !string.Equals(SnapshotHash(), _settings.LastSnapshotHash, StringComparison.Ordinal));
    public bool ShouldDownloadInitialSnapshot => IsSignedIn && LoadBaseline() is null &&
        state.Database.Projects.Count == 0 && state.Database.Catalog.Count == 0;

    public void Load()
    {
        if (state.Paths?.IsDevelopment == true)
        {
            _settingsPath = state.Paths.CloudSettingsPath;
            _baselinePath = state.Paths.CloudBaselinePath;
            _sessionPath = state.Paths.CloudSessionPath;
            _settings = new CloudSyncSettings();
            _accessToken = "";
            _refreshToken = "";
            Status = "Облачная синхронизация отключена в изолированном режиме разработки.";
            HasError = false;
            return;
        }
        _settingsPath = Path.Combine(Path.GetDirectoryName(state.DatabasePath)!, SettingsFileName);
        _baselinePath = Path.Combine(Path.GetDirectoryName(state.DatabasePath)!, BaselineFileName);
        _sessionPath = Path.Combine(Path.GetDirectoryName(state.DatabasePath)!, SessionFileName);
        try { AtomicJsonPairFile.Recover(_settingsPath + ".pair-journal.json", _settingsPath, _baselinePath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
        {
            Status = "Не удалось восстановить состояние облачной синхронизации. Локальная база не изменена.";
            HasError = true;
            return;
        }
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

    public Task<CloudSyncResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default) =>
        SerializeAsync(() => SignInCoreAsync(email, password), cancellationToken);

    private async Task<CloudSyncResult> SignInCoreAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return Fail("Введите email и пароль пользователя Supabase.");

        _accessToken = "";
        var response = await SendAsync(HttpMethod.Post, "/auth/v1/token?grant_type=password", new { email = email.Trim(), password }, false);
        if (!response.Succeeded) return response;
        try
        {
            using var document = JsonDocument.Parse(response.Payload!);
            UpdateSession(document.RootElement);
            if (!IsSignedIn || string.IsNullOrWhiteSpace(_refreshToken)) return Fail("Supabase не вернул сеанс входа.");
            SaveRefreshToken();
            var workspaceResult = await EnsureWorkspaceAsync();
            if (!workspaceResult.Succeeded) return DisconnectAfterFailure(workspaceResult);
            var syncResult = await VerifyAndSynchronizeAsync();
            return syncResult.Succeeded
                ? Ok("Облачная синхронизация подключена и проверена.") with { DatabaseChanged = syncResult.DatabaseChanged }
                : DisconnectAfterFailure(syncResult);
        }
        catch (JsonException) { return Fail("Supabase вернул неполный ответ авторизации."); }
    }

    public Task<CloudSyncResult> RestoreSessionAsync(CancellationToken cancellationToken = default) =>
        SerializeAsync(RestoreSessionCoreAsync, cancellationToken);

    private async Task<CloudSyncResult> RestoreSessionCoreAsync()
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
            var workspaceResult = await EnsureWorkspaceAsync();
            if (!workspaceResult.Succeeded) return DisconnectAfterFailure(workspaceResult);
            var syncResult = await VerifyAndSynchronizeAsync();
            return syncResult.Succeeded
                ? Ok("Облачная синхронизация подключена и проверена.") with { DatabaseChanged = syncResult.DatabaseChanged }
                : DisconnectAfterFailure(syncResult);
        }
        catch (JsonException) { return Fail("Supabase вернул неполный ответ восстановления сеанса."); }
    }

    private Task<CloudSyncResult> VerifyAndSynchronizeAsync()
    {
        if (ShouldDownloadInitialSnapshot) return DownloadCoreAsync(discardLocalChanges: true);
        return LoadBaseline() is null ? PublishImportedDatabaseCoreAsync() : PublishCoreAsync();
    }

    private CloudSyncResult DisconnectAfterFailure(CloudSyncResult result)
    {
        _accessToken = "";
        return Fail(result.Message);
    }

    public CloudSyncResult RequireSignInAfterSyncFailure(CloudSyncResult result) =>
        result.IsConflict ? result : DisconnectAfterFailure(result);

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

    public Task<CloudSyncResult> DownloadAsync(bool discardLocalChanges = false, CancellationToken cancellationToken = default) =>
        SerializeAsync(() => DownloadCoreAsync(discardLocalChanges), cancellationToken);

    private async Task<CloudSyncResult> DownloadCoreAsync(bool discardLocalChanges)
    {
        var localRevision = state.Revision;
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
            if (state.Revision != localRevision)
                return new CloudSyncResult(false, "Локальная база изменилась во время загрузки; новые изменения сохранены.", null, true);
            _settings.LastRevision = snapshot.GetProperty("revision").GetInt64();
            _settings.LastSnapshotHash = SnapshotHash(payload);
            state.ReplaceFromCloudSnapshot(payload, databaseJson => SaveCloudCheckpoint(payload, databaseJson));
            return Ok(recoveryPath is null
                ? "Облачная база загружена в локальную рабочую копию."
                : $"Облачная база загружена. Локальная версия сохранена: {recoveryPath}") with { DatabaseChanged = true };
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            return Fail($"Не удалось применить облачную базу. {error.Message}");
        }
    }

    public Task<CloudSyncResult> DownloadInitialSnapshotAsync(CancellationToken cancellationToken = default) =>
        DownloadAsync(discardLocalChanges: true, cancellationToken);

    public Task<CloudSyncResult> PublishImportedDatabaseAsync(CancellationToken cancellationToken = default) =>
        SerializeAsync(PublishImportedDatabaseCoreAsync, cancellationToken);

    private async Task<CloudSyncResult> PublishImportedDatabaseCoreAsync()
    {
        if (!IsSignedIn) return Fail("Сначала войдите в Supabase.");
        if (!Guid.TryParse(_settings.WorkspaceId, out var workspaceId))
            return Fail("Войдите в Supabase, чтобы подключить пространство Cutlist.");

        var snapshotResponse = await SendAsync(HttpMethod.Get,
            $"/rest/v1/workspace_snapshots?workspace_id=eq.{workspaceId:D}&select=revision,payload", null, true);
        if (!snapshotResponse.Succeeded) return snapshotResponse;

        try
        {
            using var document = JsonDocument.Parse(snapshotResponse.Payload!);
            if (document.RootElement.GetArrayLength() != 1) return Fail("Снимок рабочего пространства не найден.");
            var snapshot = document.RootElement[0];
            _settings.LastRevision = snapshot.GetProperty("revision").GetInt64();
            _settings.LastSnapshotHash = "";
            SaveCloudCheckpoint(snapshot.GetProperty("payload").GetRawText());
            return await PublishCoreAsync();
        }
        catch (JsonException)
        {
            return Fail("Supabase вернул неверный снимок рабочего пространства.");
        }
    }

    public Task<CloudSyncResult> PublishAsync(CancellationToken cancellationToken = default) =>
        SerializeAsync(PublishCoreAsync, cancellationToken);

    private async Task<CloudSyncResult> PublishCoreAsync()
    {
        if (!IsSignedIn) return Fail("Сначала войдите в Supabase.");
        if (!Guid.TryParse(_settings.WorkspaceId, out var workspaceId)) return Fail("Войдите в Supabase, чтобы подключить пространство Cutlist.");
        var baselineJson = LoadBaseline();
        if (baselineJson is null)
            return Fail("Для адресной синхронизации один раз загрузите облачную базу. Локальная работа останется без изменений.");

        var localRevision = state.Revision;
        Database localSnapshot;
        try { localSnapshot = CloudEntityMerge.DeserializeDatabase(state.CreateCloudSnapshot()); }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            return Fail($"Не удалось зафиксировать локальную версию для синхронизации. {error.Message}");
        }

        var snapshotResponse = await SendAsync(HttpMethod.Get,
            $"/rest/v1/workspace_snapshots?workspace_id=eq.{workspaceId:D}&select=revision", null, true);
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
            remote = baseline;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            return Fail($"Не удалось подготовить адресную синхронизацию. {error.Message}");
        }

        // A full snapshot is only needed when another computer has published since our baseline.
        if (remoteRevision != _settings.LastRevision)
        {
            var remoteResponse = await SendAsync(HttpMethod.Get,
                $"/rest/v1/workspace_snapshots?workspace_id=eq.{workspaceId:D}&select=payload", null, true);
            if (!remoteResponse.Succeeded) return remoteResponse;
            try
            {
                using var document = JsonDocument.Parse(remoteResponse.Payload!);
                if (document.RootElement.GetArrayLength() != 1) return Fail("Снимок рабочего пространства не найден.");
                remote = CloudEntityMerge.DeserializeDatabase(document.RootElement[0].GetProperty("payload").GetRawText());
            }
            catch (Exception error) when (error is JsonException or InvalidDataException)
            {
                return Fail($"Не удалось получить изменения из облака. {error.Message}");
            }
        }

        var merge = CloudEntityMerge.Merge(baseline, localSnapshot, remote);
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
        var revisions = await ReadEntityRevisionsAsync(workspaceId);
        if (revisions.Error is not null) return revisions.Error;
        var changes = remoteEntities.Keys.Union(mergedEntities.Keys)
            .Where(key => !remoteEntities.TryGetValue(key, out var remoteEntity) || !mergedEntities.TryGetValue(key, out var mergedEntity) || remoteEntity.Payload != mergedEntity.Payload)
            .Select(key => new
            {
                entity_type = key.Type,
                entity_id = key.Id,
                payload = mergedEntities.TryGetValue(key, out var entity) ? JsonDocument.Parse(entity.Payload).RootElement.Clone() : JsonDocument.Parse("{}").RootElement.Clone(),
                expected_revision = revisions.Values!.TryGetValue(key, out var revision) ? revision : 0L,
                deleted = !mergedEntities.ContainsKey(key)
            }).ToArray();

        var mergedJson = CloudEntityMerge.SerializeDatabase(merge.Database);
        var databaseChanged = !SameEntities(CloudEntityMerge.Snapshot(localSnapshot), mergedEntities);
        if (changes.Length == 0)
        {
            var hasConcurrentEdits = state.Revision != localRevision;
            _settings.LastRevision = remoteRevision;
            _settings.LastSnapshotHash = SnapshotHash(mergedJson);
            if (databaseChanged && !hasConcurrentEdits)
                state.ReplaceFromCloudSnapshot(mergedJson, databaseJson => SaveCloudCheckpoint(mergedJson, databaseJson));
            else SaveCloudCheckpoint(mergedJson);
            return Ok(hasConcurrentEdits
                ? "Синхронизация завершена; новые локальные изменения будут опубликованы следующим циклом."
                : "Локальная база объединена с актуальной облачной версией.") with { DatabaseChanged = databaseChanged && !hasConcurrentEdits };
        }

        var response = await SendAsync(HttpMethod.Post, "/rest/v1/rpc/sync_vitan_entity_changes", new
        {
            p_workspace_id = workspaceId,
            p_expected_snapshot_revision = remoteRevision,
            p_changes = changes
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
            var hasConcurrentEdits = state.Revision != localRevision;
            _settings.LastSnapshotHash = SnapshotHash(mergedJson);
            if (databaseChanged && !hasConcurrentEdits)
                state.ReplaceFromCloudSnapshot(mergedJson, databaseJson => SaveCloudCheckpoint(mergedJson, databaseJson));
            else SaveCloudCheckpoint(mergedJson);
            return Ok(hasConcurrentEdits
                ? "Снимок опубликован; новые локальные изменения остались в программе и будут отправлены следующим циклом."
                : "Локальные изменения объединены и опубликованы в облаке.") with { DatabaseChanged = databaseChanged && !hasConcurrentEdits };
        }
        catch (JsonException) { return Fail("Supabase вернул неверный ответ публикации."); }
    }

    public Task<CloudHistoryResult> GetHistoryAsync(CancellationToken cancellationToken = default) =>
        SerializeAsync(GetHistoryCoreAsync, cancellationToken);

    private async Task<CloudHistoryResult> GetHistoryCoreAsync()
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

    public Task<CloudSyncResult> RestoreHistoryAsync(long revision, CancellationToken cancellationToken = default) =>
        SerializeAsync(() => RestoreHistoryCoreAsync(revision), cancellationToken);

    private async Task<CloudSyncResult> RestoreHistoryCoreAsync(long revision)
    {
        var localRevision = state.Revision;
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

            if (state.Revision != localRevision)
                return new CloudSyncResult(false, "Локальная база изменилась во время восстановления. Повторите действие.", null, true);

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

    private async Task<(Dictionary<CloudEntityKey, long>? Values, CloudSyncResult? Error)> ReadEntityRevisionsAsync(Guid workspaceId)
    {
        const int pageSize = 1000;
        var result = new Dictionary<CloudEntityKey, long>();
        var start = 0;
        while (true)
        {
            var response = await SendAsync(HttpMethod.Get,
                $"/rest/v1/workspace_entities?workspace_id=eq.{workspaceId:D}&select=entity_type,entity_id,revision&order=entity_type.asc,entity_id.asc",
                null, true, $"{start}-{start + pageSize - 1}");
            if (!response.Succeeded) return (null, response);
            if (!TryParseEntityRevisions(response.Payload!, out var page))
                return (null, Fail("Supabase вернул неверный список версий объектов."));
            foreach (var pair in page)
                if (!result.TryAdd(pair.Key, pair.Value))
                    return (null, Fail("Supabase вернул повторяющийся объект в списке версий."));
            if (page.Count < pageSize) return (result, null);
            start = checked(start + pageSize);
        }
    }

    private async Task<CloudSyncResult> SendAsync(HttpMethod method, string path, object? body, bool withAuth, string? range = null)
    {
        if (!IsEnabled) return Fail("Облачная синхронизация отключена в изолированном режиме разработки.");
        try
        {
            var response = await SendRequestWithRetryAsync(method, path, body, withAuth, range);
            if (response.StatusCode == HttpStatusCode.Unauthorized && withAuth && !string.IsNullOrWhiteSpace(_refreshToken))
            {
                var refresh = await SendRequestAsync(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token",
                    new { refresh_token = _refreshToken }, withAuth: false, range: null);
                if ((int)refresh.StatusCode is >= 200 and < 300)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(refresh.Payload);
                        UpdateSession(document.RootElement);
                        if (!IsSignedIn || string.IsNullOrWhiteSpace(_refreshToken))
                            return Fail("Сеанс Supabase истёк. Войдите снова в настройках.");
                        SaveRefreshToken();
                        response = await SendRequestWithRetryAsync(method, path, body, withAuth: true, range);
                    }
                    catch (JsonException) { return Fail("Не удалось обновить сеанс Supabase. Войдите снова в настройках."); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
                    {
                        return Fail("Не удалось сохранить обновлённый сеанс Supabase.");
                    }
                }
                else
                {
                    _accessToken = "";
                    return Fail(DescribeFailure(refresh.StatusCode, refresh.Payload));
                }
            }

            if ((int)response.StatusCode is >= 200 and < 300) return new CloudSyncResult(true, "", response.Payload);
            if (response.Payload.Contains("revision_conflict", StringComparison.OrdinalIgnoreCase))
                return new CloudSyncResult(false, "Облачная база изменилась на другом компьютере. Сначала загрузите её и разрешите конфликт.", response.Payload, true);
            return Fail(DescribeFailure(response.StatusCode, response.Payload));
        }
        catch (HttpRequestException) { return Fail("Нет соединения с Supabase. Проверьте интернет и попробуйте снова."); }
        catch (OperationCanceledException) when (_activeOperationToken.IsCancellationRequested) { return Fail("Синхронизация отменена. Локальные данные не изменены."); }
        catch (TaskCanceledException) { return Fail("Supabase не ответил вовремя. Попробуйте ещё раз."); }
    }

    private async Task<(HttpStatusCode StatusCode, string Payload, TimeSpan? RetryAfter)> SendRequestAsync(
        HttpMethod method, string path, object? body, bool withAuth, string? range)
    {
        using var request = new HttpRequestMessage(method, _projectUrl + path);
        request.Headers.Add("apikey", _publishableKey);
        if (range is not null)
        {
            request.Headers.TryAddWithoutValidation("Range-Unit", "items");
            request.Headers.TryAddWithoutValidation("Range", range);
        }
        if (withAuth) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, _activeOperationToken);
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
            retryAfter = retryDate - DateTimeOffset.UtcNow;
        return (response.StatusCode, await response.Content.ReadAsStringAsync(_activeOperationToken), retryAfter);
    }

    private async Task<(HttpStatusCode StatusCode, string Payload, TimeSpan? RetryAfter)> SendRequestWithRetryAsync(
        HttpMethod method, string path, object? body, bool withAuth, string? range)
    {
        const int maxAttempts = 3;
        TimeSpan? retryAfter = null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await SendRequestAsync(method, path, body, withAuth, range);
                if (method != HttpMethod.Get || !IsTransient(response.StatusCode) || attempt >= maxAttempts)
                    return response;
                retryAfter = response.RetryAfter;
            }
            catch (HttpRequestException) when (method == HttpMethod.Get && attempt < maxAttempts) { }
            catch (TaskCanceledException) when (method == HttpMethod.Get &&
                                                !_activeOperationToken.IsCancellationRequested && attempt < maxAttempts) { }

            var delay = TimeSpan.FromMilliseconds(150 * attempt);
            if (retryAfter is { } serverDelay && serverDelay > delay)
                delay = serverDelay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : serverDelay;
            await Task.Delay(delay, _activeOperationToken);
            retryAfter = null;
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 || (int)statusCode >= 500;

    private async Task<T> SerializeAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        var previousToken = _activeOperationToken;
        _activeOperationToken = cancellationToken;
        Volatile.Write(ref _operationInProgress, 1);
        try { return await operation(); }
        finally
        {
            _activeOperationToken = previousToken;
            Volatile.Write(ref _operationInProgress, 0);
            _operationGate.Release();
        }
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
            SaveCloudCheckpoint(state.CreateCloudSnapshot());
            return new CloudSyncResult(true, message, payload, DatabaseChanged: true);
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

    private static bool TryParseEntityRevisions(string payload, out Dictionary<CloudEntityKey, long> revisions)
    {
        revisions = [];
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var key = new CloudEntityKey(row.GetProperty("entity_type").GetString() ?? "", row.GetProperty("entity_id").GetString() ?? "");
                if (string.IsNullOrWhiteSpace(key.Type) || string.IsNullOrWhiteSpace(key.Id) ||
                    !revisions.TryAdd(key, row.GetProperty("revision").GetInt64())) return false;
            }
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            revisions = [];
            return false;
        }
    }

    private string? LoadBaseline()
    {
        try { return File.Exists(_baselinePath) ? File.ReadAllText(_baselinePath) : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private void SaveCloudCheckpoint(string baselineJson, string? databaseJson = null)
    {
        var previousSettings = File.Exists(_settingsPath)
            ? JsonSerializer.Deserialize<CloudSyncSettings>(File.ReadAllText(_settingsPath)) ?? new CloudSyncSettings()
            : new CloudSyncSettings();
        try
        {
            databaseJson ??= state.CreateCloudSnapshot();
            if (File.Exists(state.DatabasePath))
                AtomicJsonFile.Write(state.DatabasePath + ".bak", File.ReadAllText(state.DatabasePath));
            AtomicJsonPairFile.Write(_settingsPath, JsonSerializer.Serialize(_settings), _baselinePath, baselineJson,
                state.DatabasePath, databaseJson);
        }
        catch
        {
            _settings = previousSettings;
            throw;
        }
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

    private CloudSyncResult Ok(string message) { Status = message; HasError = false; return new CloudSyncResult(true, message); }
    private CloudSyncResult Fail(string message) { Status = message; HasError = true; return new CloudSyncResult(false, message); }
    private string SnapshotHash() => SnapshotHash(state.CreateCloudSnapshot());
    private static bool SameEntities(IReadOnlyDictionary<CloudEntityKey, CloudEntity> left, IReadOnlyDictionary<CloudEntityKey, CloudEntity> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var entity) && entity.Payload == pair.Value.Payload);
    private static string SnapshotHash(string json)
    {
        var entities = CloudEntityMerge.Snapshot(CloudEntityMerge.DeserializeDatabase(json));
        var canonical = JsonSerializer.Serialize(entities.Values
            .OrderBy(entity => entity.Key.Type, StringComparer.Ordinal)
            .ThenBy(entity => entity.Key.Id, StringComparer.Ordinal)
            .Select(entity => new { entity.Key.Type, entity.Key.Id, entity.Payload }));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string DescribeFailure(HttpStatusCode statusCode, string payload)
    {
        var details = payload;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                details = string.Join(" ", new[] { "error_code", "code", "msg", "message", "error" }
                    .Where(name => document.RootElement.TryGetProperty(name, out _))
                    .Select(name => document.RootElement.GetProperty(name).ToString()));
            }
        }
        catch (JsonException) { }

        var normalized = details.ToLowerInvariant();
        if (normalized.Contains("invalid_credentials") || normalized.Contains("invalid login credentials") ||
            normalized.Contains("user not found") || normalized.Contains("invalid password"))
            return "Неверный email или пароль.";
        if (normalized.Contains("email_not_confirmed") || normalized.Contains("email not confirmed"))
            return "Подтвердите email в письме от Supabase, затем войдите снова.";
        if (normalized.Contains("refresh_token") || normalized.Contains("jwt expired") || normalized.Contains("invalid jwt"))
            return "Сеанс входа истёк. Войдите в Supabase снова.";
        if (normalized.Contains("revision_conflict") || normalized.Contains("entity_revision_conflict"))
            return "Облачная база изменилась на другом компьютере. Сначала загрузите её и разрешите конфликт.";
        if (normalized.Contains("workspace_access_denied") || normalized.Contains("permission denied") || normalized.Contains("row-level security"))
            return "У этой учётной записи нет доступа к облачной базе Cutlist.";
        if (normalized.Contains("workspace_not_found"))
            return "Облачное пространство Cutlist не найдено. Войдите снова.";

        return statusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "Не удалось обработать запрос. Проверьте введённые данные.",
            HttpStatusCode.Unauthorized => "Сеанс входа истёк. Войдите в Supabase снова.",
            HttpStatusCode.Forbidden => "У этой учётной записи нет доступа к облачной базе Cutlist.",
            HttpStatusCode.NotFound => "Запрошенные облачные данные не найдены.",
            HttpStatusCode.RequestTimeout => "Supabase не ответил вовремя. Попробуйте ещё раз.",
            (HttpStatusCode)429 => "Слишком много запросов к Supabase. Подождите немного и повторите попытку.",
            _ when (int)statusCode >= 500 => "Сервис Supabase временно недоступен. Попробуйте позже.",
            _ => "Не удалось выполнить запрос к Supabase. Попробуйте ещё раз."
        };
    }

    private void SaveSettings()
    {
        AtomicJsonFile.Write(_settingsPath, JsonSerializer.Serialize(_settings));
    }
}

public sealed class CloudSyncSettings
{
    public string WorkspaceId { get; set; } = "";
    public long LastRevision { get; set; }
    public string LastSnapshotHash { get; set; } = "";
}

public sealed record CloudSyncResult(bool Succeeded, string Message, string? Payload = null, bool IsConflict = false, bool DatabaseChanged = false);
public sealed record CloudSnapshotVersion(long Revision, DateTimeOffset SavedAt)
{
    public string Label => $"Версия {Revision} · {SavedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
}

public sealed record CloudHistoryResult(bool Succeeded, string Message, IReadOnlyList<CloudSnapshotVersion> Versions)
{
    public static CloudHistoryResult Ok(IReadOnlyList<CloudSnapshotVersion> versions) => new(true, "", versions);
    public static CloudHistoryResult Fail(string message) => new(false, message, []);
}
