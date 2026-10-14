using System.Diagnostics;
using System.Text.Json;

namespace VitanCut.WinUI.Services;

internal enum UiWork { Materials, Catalog, ProductRows, CuttingReport, DetailingReport, ImagePreview, ProductRowData, ProductRowControls, ProductRowLayout }
internal sealed record UiWorkSample(string Stage, double ElapsedMilliseconds, long? ManagedAllocatedBytes);

internal sealed class UiPerformanceLog(string directory, bool enabled)
{
    private const int Capacity = 64;
    private static readonly int SamplesPerStage = Math.Max(1, Capacity / Enum.GetValues<UiWork>().Length);
    private readonly Queue<UiWorkSample> _samples = new();
    private readonly object _gate = new();

    public IDisposable Measure(UiWork stage, bool asynchronous = false) =>
        enabled ? new Measurement(this, stage, asynchronous) : NoMeasurement.Instance;

    private void Add(UiWorkSample sample)
    {
        lock (_gate)
        {
            if (_samples.Count(item => item.Stage == sample.Stage) == SamplesPerStage)
            {
                var remaining = _samples.ToArray();
                _samples.Clear();
                var removed = false;
                foreach (var item in remaining)
                {
                    if (!removed && item.Stage == sample.Stage) { removed = true; continue; }
                    _samples.Enqueue(item);
                }
            }
            if (_samples.Count == Capacity) _samples.Dequeue();
            _samples.Enqueue(sample);
        }
    }

    public void Flush()
    {
        if (!enabled) return;
        UiWorkSample[] samples;
        lock (_gate) samples = _samples.ToArray();
        if (samples.Length == 0) return;
        try { AtomicJsonFile.Write(Path.Combine(directory, "ui-performance.json"), JsonSerializer.Serialize(samples)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private sealed class Measurement(UiPerformanceLog owner, UiWork stage, bool asynchronous) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly long _allocated = GC.GetAllocatedBytesForCurrentThread();
        private bool _completed;

        public void Dispose()
        {
            if (_completed) return;
            _completed = true;
            var elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            // Async elapsed includes waits; interleaved UI work makes thread-allocation deltas misleading.
            long? allocated = !asynchronous && Environment.CurrentManagedThreadId == _thread
                ? Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _allocated) : null;
            owner.Add(new UiWorkSample(stage.ToString(), elapsed, allocated));
        }
    }

    private sealed class NoMeasurement : IDisposable
    {
        public static readonly NoMeasurement Instance = new();
        public void Dispose() { }
    }
}
