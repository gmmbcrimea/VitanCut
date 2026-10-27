using System.Text.Json;

namespace VitanCut.WinUI.Services;

internal sealed record CloudHealthResult(bool Succeeded, string Message);

internal static class CloudHealthProbe
{
    internal static async Task<CloudHealthResult> CheckAsync(HttpClient http, string projectUrl, string publishableKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, projectUrl.TrimEnd('/') + "/rest/v1/rpc/vitan_cloud_health");
            request.Headers.Add("apikey", publishableKey);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, $"Cloud health request failed (HTTP {(int)response.StatusCode}). Check connectivity and the cloud health migration.");
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            return payload.Trim() == "true" ? new(true, "Cloud database is reachable. User data was not read or synchronized.")
                : new(false, "Unexpected cloud health response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, "Cloud health request timed out."); }
        catch (HttpRequestException) { return new(false, "Cloud database is unreachable."); }
    }

    public static async Task<int> RunAsync(AppPaths paths)
    {
        if (paths.IsDevelopment) return 0;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var result = await CheckAsync(http, CloudSyncService.DefaultProjectUrl, CloudSyncService.DefaultPublishableKey);
        AtomicJsonFile.Write(Path.Combine(paths.LogsDirectory, "cloud-health.json"),
            JsonSerializer.Serialize(new { CheckedAtUtc = DateTimeOffset.UtcNow, result.Succeeded, result.Message }));
        return result.Succeeded ? 0 : 1;
    }
}
