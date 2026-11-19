using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VitanCut.WinUI.Services;
using Windows.Storage.Streams;

namespace VitanCut.WinUI.Controls;

public sealed class CounterpartyLabel : ContentControl
{
    private static readonly Dictionary<string, Task<BitmapImage?>> Logos = [];
    private readonly RichTextBlock _label = new() { IsTextSelectionEnabled = false, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly UiRequestGate _requests = new();

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CounterpartyLabel), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty CounterpartyProperty = DependencyProperty.Register(
        nameof(Counterparty), typeof(string), typeof(CounterpartyLabel), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty PrefixProperty = DependencyProperty.Register(
        nameof(Prefix), typeof(string), typeof(CounterpartyLabel), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(CounterpartyLabel), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty ShowLogoProperty = DependencyProperty.Register(
        nameof(ShowLogo), typeof(bool), typeof(CounterpartyLabel), new PropertyMetadata(true, Changed));
    public static readonly DependencyProperty MaxLinesProperty = DependencyProperty.Register(
        nameof(MaxLines), typeof(int), typeof(CounterpartyLabel), new PropertyMetadata(1, Changed));

    public string Text { get => (string?)GetValue(TextProperty) ?? ""; set => SetValue(TextProperty, value); }
    public string? Counterparty { get => (string?)GetValue(CounterpartyProperty); set => SetValue(CounterpartyProperty, value); }
    public string Prefix { get => (string?)GetValue(PrefixProperty) ?? ""; set => SetValue(PrefixProperty, value); }
    public string Suffix { get => (string?)GetValue(SuffixProperty) ?? ""; set => SetValue(SuffixProperty, value); }
    public bool ShowLogo { get => (bool)GetValue(ShowLogoProperty); set => SetValue(ShowLogoProperty, value); }
    public int MaxLines { get => (int)GetValue(MaxLinesProperty); set => SetValue(MaxLinesProperty, value); }

    public CounterpartyLabel()
    {
        IsTabStop = false;
        MinWidth = 0;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        Content = _label;
        Loaded += (_, _) =>
        {
            App.Catalog.CounterpartyLogosChanged += Refresh;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _requests.Begin();
            App.Catalog.CounterpartyLogosChanged -= Refresh;
        };
    }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is CounterpartyLabel { IsLoaded: true } label) label.Refresh();
    }

    private async void Refresh()
    {
        var request = _requests.Begin();
        var name = Counterparty ?? Text;
        var text = Text.Length == 0 ? name : Text;
        _label.MaxLines = MaxLines;
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = Prefix });
        var logo = ShowLogo ? App.Catalog.LogoFor(name) : "";
        var image = new Image { Width = FontSize * 1.2, Height = FontSize * 1.2,
            Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 5, 0), Visibility = Visibility.Collapsed };
        if (logo.Length > 0) paragraph.Inlines.Add(new InlineUIContainer { Child = image });
        paragraph.Inlines.Add(new Run { Text = text + Suffix });
        _label.Blocks.Clear();
        _label.Blocks.Add(paragraph);
        if (logo.Length == 0) return;
        if (!Logos.TryGetValue(logo, out var task))
        {
            if (Logos.Count >= 128) Logos.Clear();
            Logos[logo] = task = LoadLogoAsync(logo);
        }
        var bitmap = await task;
        if (!_requests.IsCurrent(request) || !IsLoaded) return;
        image.Source = bitmap;
        image.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static async Task<BitmapImage?> LoadLogoAsync(string logo)
    {
        try
        {
            if (ProductImageData.Read(logo) is not { } bytes) return null;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception error) when (error is ArgumentException or IOException or System.Runtime.InteropServices.COMException)
        { return null; }
    }
}
