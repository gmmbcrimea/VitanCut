using System.Reflection;
using System.Text.Json;

namespace VitanCut.WinUI.Services;

internal sealed class MaterialPickerJournal
{
    private const int Capacity = 4096;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(3);
    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly Queue<Entry> _entries = new();
    private readonly Timer _timer;
    private long _sequence;
    private bool _pending;
    private static readonly string? Build = typeof(MaterialPickerJournal).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    internal MaterialPickerJournal(string directory, TimeProvider? clock = null)
    {
        _path = Path.Combine(directory, "material-picker-recent.json");
        _clock = clock ?? TimeProvider.System;
        _timer = new Timer(_ => Flush());
    }

    internal void Record(int editor, string action, int? detail = null, string? outcome = null, int? value = null)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            while (_entries.TryPeek(out var first) && (now - first.At >= Window || _entries.Count >= Capacity))
                _entries.Dequeue();
            _entries.Enqueue(new(++_sequence, now, editor, action, detail, outcome, value));
            if (_pending) return;
            _pending = true;
            _timer.Change(350, Timeout.Infinite);
        }
    }

    internal void Flush()
    {
        lock (_writeGate)
        {
            Entry[] entries;
            lock (_gate)
            {
                if (!_pending) return;
                _pending = false;
                entries = _entries.ToArray();
            }
            try
            {
                AtomicJsonFile.Write(_path, JsonSerializer.Serialize(new
                {
                    Format = 1, Build, Process = Environment.ProcessId, Events = entries
                }));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Entry(long Sequence, DateTimeOffset At, int Editor, string Action, int? Detail, string? Outcome, int? Value);
}
