namespace VitanCut.WinUI.Services;

internal enum CloudUiPhase { Disabled, SignedOut, Connecting, Synchronizing, Ready, Attention }

internal sealed record CloudUiSnapshot(bool Enabled, bool SignedIn, bool Busy, bool Error, string Status)
{
    public CloudUiPresentation Presentation => CloudUiPresentation.Create(Enabled, SignedIn, Busy, Error);

    public static CloudUiSnapshot? Preview(bool development, string? scenario) => !development ? null : scenario switch
    {
        "signed-out" => new(true, false, false, false, "Войдите для синхронизации."),
        "connecting" => new(true, false, true, false, "Подключение к облаку..."),
        "synchronizing" => new(true, true, true, false, "Синхронизация выполняется..."),
        "ready" => new(true, true, false, false, "Изменения сохранены в облаке."),
        "offline" => new(true, true, false, true, "Нет соединения. Данные сохранены на ПК и будут отправлены после восстановления связи."),
        "expired" => new(true, false, false, true, "Сеанс истёк. Войдите снова, чтобы продолжить синхронизацию."),
        _ => null
    };
}

internal sealed record CloudUiPresentation(CloudUiPhase Phase, bool SignedIn)
{
    public bool IsBusy => Phase is CloudUiPhase.Connecting or CloudUiPhase.Synchronizing;
    public bool IsHealthy => Phase == CloudUiPhase.Ready;
    public bool ShowLogin => Phase != CloudUiPhase.Disabled && !SignedIn;
    public bool ShowBadge => Phase != CloudUiPhase.Disabled && SignedIn;
    public bool ShowStatus => Phase is CloudUiPhase.Disabled or CloudUiPhase.Attention || (IsBusy && !ShowBadge);
    public bool ControlsEnabled => Phase != CloudUiPhase.Disabled && !IsBusy;
    public string Title => Phase switch
    {
        CloudUiPhase.Connecting => "Подключение к облаку...",
        CloudUiPhase.Synchronizing => "Синхронизация выполняется...",
        CloudUiPhase.Ready => "Облачная синхронизация работает",
        CloudUiPhase.Attention => "Синхронизация требует внимания",
        CloudUiPhase.Disabled => "Облачные функции отключены",
        _ => "Войдите для синхронизации"
    };

    public static CloudUiPresentation Create(bool enabled, bool signedIn, bool busy, bool error) => new(
        !enabled ? CloudUiPhase.Disabled : busy ? signedIn ? CloudUiPhase.Synchronizing : CloudUiPhase.Connecting
        : error ? CloudUiPhase.Attention : signedIn ? CloudUiPhase.Ready : CloudUiPhase.SignedOut, signedIn);
}
