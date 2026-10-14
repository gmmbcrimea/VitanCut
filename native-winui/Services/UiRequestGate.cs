namespace VitanCut.WinUI.Services;

internal sealed class UiRequestGate
{
    private long _version;

    public long Begin() => Interlocked.Increment(ref _version);

    public bool IsCurrent(long version) => version == Interlocked.Read(ref _version);
}
