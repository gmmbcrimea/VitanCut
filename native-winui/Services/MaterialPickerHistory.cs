using System.Text.Json;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

internal sealed class MaterialPickerHistory(string path)
{
    private const int Capacity = 256;
    private List<MaterialKey>? _recent;

    internal static string NormalizeOrder(string? order) => order == "recent" ? "recent" : "alphabetical";

    internal MaterialChoice? Preferred(IReadOnlyList<MaterialChoice> choices, string? currentType = null, string? currentId = null)
    {
        var current = choices.FirstOrDefault(choice => choice.Type == currentType && choice.Material.Id == currentId);
        if (current is not null) return current;
        foreach (var key in Recent)
            if (choices.FirstOrDefault(choice => choice.Type == key.Type && choice.Material.Id == key.Id) is { } recent)
                return recent;
        return choices.FirstOrDefault();
    }

    internal MaterialChoice[] Sort(IEnumerable<MaterialChoice> choices, string? order)
    {
        var ranked = NormalizeOrder(order) == "recent"
            ? Recent.Select((key, index) => (key, index)).ToDictionary(item => item.key, item => item.index)
            : [];
        return choices.OrderBy(choice => ranked.GetValueOrDefault(new(choice.Type, choice.Material.Id), int.MaxValue))
            .ThenBy(choice => choice.Material.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(choice => choice.Type, StringComparer.Ordinal)
            .ThenBy(choice => choice.Material.Id, StringComparer.Ordinal).ToArray();
    }

    internal void Record(MaterialChoice choice)
    {
        var key = new MaterialKey(choice.Type, choice.Material.Id);
        if (string.IsNullOrWhiteSpace(key.Type) || string.IsNullOrWhiteSpace(key.Id)) return;
        var recent = Recent;
        if (recent.Count > 0 && recent[0] == key) return;
        recent.Remove(key);
        recent.Insert(0, key);
        if (recent.Count > Capacity) recent.RemoveRange(Capacity, recent.Count - Capacity);
        try { AtomicJsonFile.Write(path, JsonSerializer.Serialize(recent)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // An unavailable history file must not undo a successful material choice.
        }
    }

    private List<MaterialKey> Recent
    {
        get
        {
            if (_recent is not null) return _recent;
            try
            {
                _recent = (JsonSerializer.Deserialize<List<MaterialKey?>>(File.ReadAllText(path)) ?? [])
                    .OfType<MaterialKey>().Where(key => !string.IsNullOrWhiteSpace(key.Type) && !string.IsNullOrWhiteSpace(key.Id))
                    .Distinct().Take(Capacity).ToList();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { _recent = []; }
            return _recent;
        }
    }

    internal sealed record MaterialKey(string Type, string Id);
}
