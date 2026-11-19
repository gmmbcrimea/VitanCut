using System.Collections.ObjectModel;
using System.ComponentModel;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

internal enum MaterialPickerRowKind { Category, Group, Material, Add }

internal sealed record MaterialPickerOption(MaterialChoice Choice, string Label, string Caption);

internal readonly record struct MaterialPickerRowKey(MaterialPickerRowKind Kind, string Category, string Item);

public sealed class MaterialPickerRow : INotifyPropertyChanged
{
    internal MaterialPickerRow(MaterialPickerRowKey key, string label, MaterialPickerOption? option)
    {
        Key = key;
        Label = label;
        Option = option;
    }

    internal MaterialPickerRowKey Key { get; }
    internal MaterialPickerOption? Option { get; }
    internal bool Expanded { get; private set; }
    public string Label { get; }
    public string Caption => Option?.Caption ?? "";
    public double CaptionHeight => Option is null ? 0 : 16;
    public double HeadingOpacity => Key.Kind switch
    {
        MaterialPickerRowKind.Category => 0.72,
        MaterialPickerRowKind.Group => 0.40,
        _ => 0
    };
    public double DividerOpacity => Key.Kind == MaterialPickerRowKind.Material ? 0.55 : 0;
    public double Indent => Key.Kind switch
    {
        MaterialPickerRowKind.Category => 0,
        MaterialPickerRowKind.Material when !string.IsNullOrWhiteSpace(Option?.Choice.Material.Group) => 26,
        _ => 12
    };
    public string Glyph => Key.Kind switch
    {
        MaterialPickerRowKind.Category or MaterialPickerRowKind.Group => Expanded ? "\uE70E" : "\uE70D",
        MaterialPickerRowKind.Add => "\uE710",
        _ => ""
    };
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void SetExpanded(bool expanded)
    {
        if (Expanded == expanded) return;
        Expanded = expanded;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Glyph)));
    }

    public override string ToString() => Caption.Length == 0 ? Label : $"{Label}, {Caption}";
}

internal sealed class MaterialPickerBrowse
{
    private readonly MaterialPickerOption[] _options;
    private readonly string[] _categories;
    private readonly string _addLabel;
    private readonly bool _directOnly;
    private readonly Dictionary<MaterialPickerRowKey, bool> _browseExpansion = [];
    private readonly Dictionary<MaterialPickerRowKey, bool> _searchExpansion = [];
    private string _query = "";
    private MaterialPickerOption[] _matches;
    private readonly ObservableCollection<MaterialPickerRow> _rows = [];
    internal ReadOnlyObservableCollection<MaterialPickerRow> Rows { get; }
    internal bool Searching => _query.Length != 0;
    internal int MatchCount => _matches.Length;

    internal MaterialPickerBrowse(IEnumerable<MaterialPickerOption> options, IEnumerable<string> categories, string addLabel,
        bool edgeOnly, string edgeCategory, bool legOnly = false)
    {
        _directOnly = edgeOnly || legOnly;
        _addLabel = addLabel;
        _options = options.Where(option => (!edgeOnly || EdgeBandingService.IsEdgeMaterial(option.Choice)) &&
                (!legOnly || LegDetails.IsMaterial(option.Choice)))
            .DistinctBy(option => (option.Choice.Type, option.Choice.Material.Id)).ToArray();
        _matches = _options;
        _categories = edgeOnly ? [edgeCategory] : categories.Concat(_options.Select(option => option.Choice.Type))
            .Distinct().OrderBy(category => category, StringComparer.CurrentCulture).ToArray();
        Rows = new ReadOnlyObservableCollection<MaterialPickerRow>(_rows);
        Rebuild();
    }

    internal void Filter(string query)
    {
        query = query.Trim();
        if (_query == query) return;
        _query = query;
        _searchExpansion.Clear();
        _matches = Searching ? _options.Where(option => UiTextSearch.Matches(query, option.Label, option.Choice.Type,
            option.Choice.Material.Group)).ToArray() : _options;
        Rebuild();
    }

    internal bool Toggle(MaterialPickerRow row)
    {
        if (!_rows.Contains(row) || row.Key.Kind is not (MaterialPickerRowKind.Category or MaterialPickerRowKind.Group))
            return false;
        var expanded = !row.Expanded;
        (Searching ? _searchExpansion : _browseExpansion)[row.Key] = expanded;
        Rebuild();
        return expanded;
    }

    private void Rebuild()
    {
        var sections = _matches.ToLookup(option => option.Choice.Type);
        var existing = _rows.ToDictionary(row => row.Key);
        var visible = new List<MaterialPickerRow>();
        MaterialPickerRow Add(MaterialPickerRowKind kind, string category, string item, string label, MaterialPickerOption? option = null)
        {
            var key = new MaterialPickerRowKey(kind, category, item);
            var row = existing.GetValueOrDefault(key) ?? new MaterialPickerRow(key, label, option);
            visible.Add(row);
            return row;
        }
        bool Open(MaterialPickerRow row)
        {
            var expanded = (Searching ? _searchExpansion : _browseExpansion).GetValueOrDefault(row.Key, Searching);
            row.SetExpanded(expanded);
            return expanded;
        }
        if (_directOnly)
        {
            foreach (var option in _matches)
                Add(MaterialPickerRowKind.Material, option.Choice.Type, option.Choice.Material.Id, option.Label, option);
        }
        else foreach (var category in _categories)
        {
            if (Searching && !sections.Contains(category)) continue;
            if (!Open(Add(MaterialPickerRowKind.Category, category, "", category))) continue;
            foreach (var group in sections[category].GroupBy(option => option.Choice.Material.Group.Trim(),
                StringComparer.CurrentCultureIgnoreCase).OrderBy(group => group.Key.Length == 0)
                .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (group.Key.Length > 0 && !Open(Add(MaterialPickerRowKind.Group, category, group.Key, group.Key))) continue;
                foreach (var option in group)
                    Add(MaterialPickerRowKind.Material, category, option.Choice.Material.Id, option.Label, option);
            }
            Add(MaterialPickerRowKind.Add, category, "", _addLabel);
        }

        // A toggle changes one contiguous range; keep its header and the other sections attached.
        var start = 0;
        while (start < _rows.Count && start < visible.Count && ReferenceEquals(_rows[start], visible[start])) start++;
        var oldEnd = _rows.Count;
        var newEnd = visible.Count;
        while (oldEnd > start && newEnd > start && ReferenceEquals(_rows[oldEnd - 1], visible[newEnd - 1]))
        {
            oldEnd--;
            newEnd--;
        }
        for (var i = oldEnd - 1; i >= start; i--) _rows.RemoveAt(i);
        for (var i = start; i < newEnd; i++) _rows.Insert(i, visible[i]);
    }
}
