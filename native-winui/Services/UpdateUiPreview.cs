namespace VitanCut.WinUI.Services;

internal sealed record UpdateUiPreview(AppUpdateInfo Info, bool Checking = false,
    bool Downloading = false, double? Percent = null, bool Restarting = false)
{
    public static UpdateUiPreview? Create(bool development, string? scenario, Version current)
    {
        if (!development) return null;
        var latest = new Version(current.Major, current.Minor + 1, 0);
        var available = new AppUpdateInfo(UpdateAvailability.Available, current, latest,
            $"Доступна версия {AppUpdateService.FormatVersion(latest)}. Установлена {AppUpdateService.FormatVersion(current)}.",
            DownloadUrl: "https://updates.example.test/VitanCut-portable.zip",
            ReleaseNotes: "Улучшена читаемость таблиц и сохранение фокуса при выборе материала.\nИсправлено повторное уменьшение отступов в компактном режиме.");
        return scenario switch
        {
            "current" => new(new AppUpdateInfo(UpdateAvailability.UpToDate, current, current,
                $"Установлена актуальная версия {AppUpdateService.FormatVersion(current)}.")),
            "available" => new(available),
            "error" => new(new AppUpdateInfo(UpdateAvailability.Unavailable, current, null,
                "Нет соединения. Не удалось проверить обновления. Попробуйте позже.")),
            "checking" => new(new AppUpdateInfo(UpdateAvailability.Unavailable, current, null,
                "Проверяем наличие новой версии..."), Checking: true),
            "downloading" => new(available, Downloading: true, Percent: 45),
            "restarting" => new(available, Downloading: true, Percent: 100, Restarting: true),
            _ => null
        };
    }
}
