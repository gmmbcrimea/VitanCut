namespace VitanCut.WinUI.Services;

public sealed class MaterialSelectionModel
{
    private readonly List<string> _orderedIds = [];
    private readonly HashSet<string> _selectedIds = new(StringComparer.OrdinalIgnoreCase);
    private string? _anchorId;

    public IReadOnlySet<string> SelectedIds => _selectedIds;

    public void SetVisibleOrder(IEnumerable<string> ids)
    {
        _orderedIds.Clear();
        _orderedIds.AddRange(ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase));
        _selectedIds.IntersectWith(_orderedIds);
        if (_anchorId is not null && !_orderedIds.Contains(_anchorId, StringComparer.OrdinalIgnoreCase))
            _anchorId = null;
    }

    public void Reset(IEnumerable<string> ids)
    {
        _selectedIds.Clear();
        _anchorId = null;
        SetVisibleOrder(ids);
    }

    public void ReplaceSelection(IEnumerable<string> ids)
    {
        _selectedIds.Clear();
        _selectedIds.UnionWith(ids.Where(id => _orderedIds.Contains(id, StringComparer.OrdinalIgnoreCase)));
        _anchorId = _orderedIds.FirstOrDefault(_selectedIds.Contains);
    }

    public void Apply(string id, bool control, bool shift)
    {
        var index = _orderedIds.FindIndex(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        if (control)
        {
            if (!_selectedIds.Add(id)) _selectedIds.Remove(id);
            _anchorId = id;
            return;
        }

        if (shift && _anchorId is not null)
        {
            var anchorIndex = _orderedIds.FindIndex(item => string.Equals(item, _anchorId, StringComparison.OrdinalIgnoreCase));
            if (anchorIndex >= 0)
            {
                _selectedIds.Clear();
                foreach (var item in _orderedIds.Skip(Math.Min(index, anchorIndex)).Take(Math.Abs(index - anchorIndex) + 1))
                    _selectedIds.Add(item);
                return;
            }
        }

        _selectedIds.Clear();
        _selectedIds.Add(id);
        _anchorId = id;
    }

    public string? Move(string focusedId, int delta, bool control, bool shift)
    {
        var index = _orderedIds.FindIndex(id => string.Equals(id, focusedId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        var target = _orderedIds[(int)Math.Clamp((long)index + delta, 0, _orderedIds.Count - 1)];
        if (control && !shift) return target;
        if (shift) _anchorId ??= focusedId;
        var previous = control && shift ? _selectedIds.ToArray() : null;
        Apply(target, control: false, shift);
        if (previous is not null) _selectedIds.UnionWith(previous);
        return target;
    }
}
