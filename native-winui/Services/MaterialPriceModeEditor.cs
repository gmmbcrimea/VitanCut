using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

// Keeps the amount in its original price basis until missing dimensions are entered.
internal sealed class MaterialPriceModeEditor
{
    private readonly NumberBox _cost, _length, _width, _roll;
    private readonly TextBlock _error;
    private string _unit = "pc", _mode = "unit";
    private string? _pendingFrom, _pendingTo;
    private double _pendingAmount;
    private bool _loading, _applying;

    public MaterialPriceModeEditor(NumberBox cost, NumberBox length, NumberBox width, NumberBox roll, TextBlock error)
    {
        (_cost, _length, _width, _roll, _error) = (cost, length, width, roll, error);
        length.ValueChanged += (_, _) => CompletePending();
        width.ValueChanged += (_, _) => CompletePending();
        roll.ValueChanged += (_, _) => CompletePending();
    }

    public void BeginLoad() { _loading = true; Reset(_unit, _mode); }
    public void EndLoad(string unit, string mode) { Reset(unit, mode); _loading = false; }

    public void Reset(string unit, string mode = "unit")
    {
        (_unit, _mode) = (unit, mode);
        _pendingFrom = _pendingTo = null;
        _cost.IsEnabled = true;
        _error.Visibility = Visibility.Collapsed;
    }

    public void ChangeMode(string mode)
    {
        if (_loading || _applying || mode == _pendingTo || _pendingFrom is null && mode == _mode) return;
        if (_pendingFrom is null)
        {
            _pendingFrom = _mode;
            _pendingAmount = NumberBoxInput.Read(_cost);
        }
        _pendingTo = mode;
        CompletePending();
    }

    public void EnsureReady()
    {
        CompletePending();
        if (_pendingFrom is not null) throw new InvalidOperationException(_error.Text);
    }

    private void CompletePending()
    {
        if (_loading || _applying || _pendingFrom is null || _pendingTo is null) return;
        _applying = true;
        try
        {
            var material = new Material { Unit = _unit, SheetLength = NumberBoxInput.Read(_length),
                SheetWidth = NumberBoxInput.Read(_width), RollLength = NumberBoxInput.Read(_roll) };
            _cost.Value = MaterialPricing.ConvertPrice(material, _pendingFrom, _pendingTo, _pendingAmount);
            _mode = _pendingTo;
            _pendingFrom = _pendingTo = null;
            _cost.IsEnabled = true;
            _error.Visibility = Visibility.Collapsed;
        }
        catch (InvalidOperationException error)
        {
            _cost.IsEnabled = false;
            _error.Text = UiText.Get(error.Message);
            _error.Visibility = Visibility.Visible;
        }
        finally { _applying = false; }
    }
}
