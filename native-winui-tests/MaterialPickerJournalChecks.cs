using System.Text.Json;
using VitanCut.WinUI.Services;

internal static class MaterialPickerJournalChecks
{
    internal static void Run(Action<bool, string> check, string output)
    {
        var directory = Path.Combine(output, "material-picker-journal");
        var path = Path.Combine(directory, "material-picker-recent.json");
        var clock = new TestClock();
        var journal = new MaterialPickerJournal(directory, clock);
        journal.Record(4, "detail.add", 21);
        clock.Now += TimeSpan.FromSeconds(1);
        journal.Record(4, "picker.open", 21);
        clock.Now += TimeSpan.FromSeconds(179);
        journal.Record(4, "option.model-committed", 21, "matches");
        journal.Flush();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var events = document.RootElement.GetProperty("Events").EnumerateArray().ToArray();
        check(events.Select(e => e.GetProperty("Action").GetString()).SequenceEqual(["picker.open", "option.model-committed"]),
            "Material picker journal prunes events at the three-minute boundary");
        check(events.All(e => e.GetProperty("Editor").GetInt32() == 4 && e.GetProperty("Detail").GetInt32() == 21) &&
              events.Select(e => e.GetProperty("Sequence").GetInt64()).SequenceEqual([2L, 3L]),
            "Material picker journal preserves action order and session correlation");
        clock.Now += TimeSpan.FromMinutes(5);
        journal.Flush();
        check(File.ReadAllText(path) == document.RootElement.GetRawText(),
            "An idle editor preserves the last diagnostic snapshot for later inspection");
    }

    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
