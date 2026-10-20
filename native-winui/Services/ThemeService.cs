using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System.Numerics;
using System.Runtime.CompilerServices;
using VitanCut.WinUI.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace VitanCut.WinUI.Services;

public static class ThemeService
{
    private static readonly ConditionalWeakTable<Window, Registration> Registrations = new();
    // Native elements outlive temporary .NET projection wrappers returned during traversal.
    private static readonly DependencyProperty OriginalPaddingProperty = DependencyProperty.RegisterAttached(
        "DensityOriginalPadding", typeof(Thickness), typeof(ThemeService), new PropertyMetadata(default(Thickness)));
    private static readonly DependencyProperty OriginalSpacingProperty = DependencyProperty.RegisterAttached(
        "DensityOriginalSpacing", typeof(double), typeof(ThemeService), new PropertyMetadata(0d));
    private static readonly DependencyProperty OriginalColumnSpacingProperty = DependencyProperty.RegisterAttached(
        "DensityOriginalColumnSpacing", typeof(double), typeof(ThemeService), new PropertyMetadata(0d));
    private static readonly DependencyProperty OriginalRowSpacingProperty = DependencyProperty.RegisterAttached(
        "DensityOriginalRowSpacing", typeof(double), typeof(ThemeService), new PropertyMetadata(0d));

    public static void Apply(Window window, AppPreferences preferences)
    {
        var registration = Registrations.GetValue(window, static owner => new Registration(owner));
        ApplySurfaces(preferences);
        ApplyAccent(preferences);
        if (window.Content is not FrameworkElement root) return;

        root.RequestedTheme = preferences.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        ApplyCaption(window, root.ActualTheme == ElementTheme.Dark);
        var acrylic = BackdropPolicy.UseAcrylic(preferences.AcrylicBackgroundEnabled || window is Views.StartupWindow,
            new UISettings().AdvancedEffectsEnabled, new AccessibilitySettings().HighContrast);
        if (acrylic)
        {
            if (window.SystemBackdrop is not DesktopAcrylicBackdrop) window.SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        else if (window.SystemBackdrop is not null) window.SystemBackdrop = null;
        if ((window is MainWindow || window is Views.StartupWindow) && root is Grid grid)
            grid.Background = acrylic ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                : new SolidColorBrush(((AcrylicBrush)Application.Current.Resources["GlassChromeBrush"]).FallbackColor);
        if (registration.NeedsLayout(preferences)) ApplyLayout(root, preferences);
        registration.ApplyReveal(window, preferences);
    }

    private sealed class Registration
    {
        private readonly UISettings _system = new();
        private CursorReveal? _reveal;
        private bool _layoutInitialized;
        private bool _compactMode;
        private bool _animationsEnabled;

        public bool NeedsLayout(AppPreferences preferences)
        {
            var changed = !_layoutInitialized || _compactMode != preferences.CompactMode || _animationsEnabled != preferences.AnimationsEnabled;
            _layoutInitialized = true;
            _compactMode = preferences.CompactMode;
            _animationsEnabled = preferences.AnimationsEnabled;
            return changed;
        }

        public void ApplyReveal(Window window, AppPreferences preferences)
        {
            var enabled = preferences.CursorRevealEnabled && _system.AdvancedEffectsEnabled && !new AccessibilitySettings().HighContrast;
            if (_reveal is null && enabled && window.Content is FrameworkElement { XamlRoot: not null } root)
                _reveal = new CursorReveal(window, root);
            _reveal?.Update(enabled);
        }
        public Registration(Window window)
        {
            void Refresh() => window.DispatcherQueue.TryEnqueue(() => Apply(window, App.Preferences.Current));
            EventHandler changed = (_, _) => Refresh();
            Windows.Foundation.TypedEventHandler<UISettings, object> colors = (_, _) => Refresh();
            App.Preferences.Changed += changed;
            _system.ColorValuesChanged += colors;
            _system.AdvancedEffectsEnabledChanged += colors;
            if (window.Content is FrameworkElement root)
            {
                root.Loaded += (_, _) => Apply(window, App.Preferences.Current);
                root.ActualThemeChanged += (_, _) => ApplyCaption(window, root.ActualTheme == ElementTheme.Dark);
            }
            window.Closed += (_, _) =>
            {
                _reveal?.Dispose();
                App.Preferences.Changed -= changed;
                _system.ColorValuesChanged -= colors;
                _system.AdvancedEffectsEnabledChanged -= colors;
            };
        }
    }

    private static void ApplyCaption(Window window, bool dark)
    {
        var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var bar = window.AppWindow.TitleBar;
        bar.ButtonForegroundColor = foreground;
        bar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(255, 170, 170, 170) : Color.FromArgb(255, 85, 85, 85);
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedForegroundColor = foreground;
        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(255, 55, 55, 55) : Color.FromArgb(255, 225, 225, 225);
        bar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(255, 65, 65, 65) : Color.FromArgb(255, 210, 210, 210);
    }

    private readonly record struct Metrics(Thickness Padding, double Spacing, double ColumnSpacing = 0, double RowSpacing = 0);
    private static Metrics OriginalMetrics(FrameworkElement element, Thickness padding, double spacing = 0, double columnSpacing = 0, double rowSpacing = 0)
    {
        if (element.ReadLocalValue(OriginalPaddingProperty) == DependencyProperty.UnsetValue)
        {
            element.SetValue(OriginalPaddingProperty, padding);
            element.SetValue(OriginalSpacingProperty, spacing);
            element.SetValue(OriginalColumnSpacingProperty, columnSpacing);
            element.SetValue(OriginalRowSpacingProperty, rowSpacing);
        }
        return new Metrics((Thickness)element.GetValue(OriginalPaddingProperty),
            (double)element.GetValue(OriginalSpacingProperty),
            (double)element.GetValue(OriginalColumnSpacingProperty),
            (double)element.GetValue(OriginalRowSpacingProperty));
    }
    private static Thickness DensePadding(Thickness padding) => new(
        UiLayout.CompactInset(padding.Left), UiLayout.CompactInset(padding.Top),
        UiLayout.CompactInset(padding.Right), UiLayout.CompactInset(padding.Bottom));
    public static void ApplyLayout(FrameworkElement root, AppPreferences preferences)
    {
        var compact = preferences.CompactMode;
        if (root is ScrollViewer scroll)
        {
            var original = OriginalMetrics(root, scroll.Padding);
            scroll.Padding = compact ? DensePadding(original.Padding) : original.Padding;
            if (scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
                scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            if (scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
                scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            if (scroll.Content is FrameworkElement scrollContent)
                ApplyLayout(scrollContent, preferences);
            return;
        }
        if (root is Button button)
        {
            if (button.Tag is "cut-tool" or "splitter" or "icon-tool") return;
            button.MinHeight = compact ? 32 : 40;
            button.Padding = compact ? new Thickness(8, 3, 8, 3) : new Thickness(16, 8, 16, 8);
            CommandAppearance.Decorate(button);
            if (button.Tag is "projects" or "customers" or "materials" or "catalog" or "settings")
            {
                button.Padding = new Thickness(4);
                button.Height = compact ? 60 : 76;
                return;
            }
            if (button.Content is FrameworkElement buttonContent)
                ApplyLayout(buttonContent, preferences);
            return;
        }
        if (root is TextBox text)
        {
            text.MinHeight = compact ? 32 : 40;
            text.Padding = compact ? new Thickness(8, 3, 8, 3) : new Thickness(12, 8, 12, 8);
            return;
        }
        if (root is NumberBox or ComboBox or ToggleSwitch or CheckBox or RadioButton)
        {
            if (root is Control control)
                control.MinHeight = compact ? 32 : 40;
            return;
        }
        if (root is Border { Shadow: ThemeShadow } elevated)
            elevated.Translation = new Vector3(0, 0, new UISettings().AdvancedEffectsEnabled && !new AccessibilitySettings().HighContrast ? 12 : 0);
        if (root is Border border)
        {
            var original = OriginalMetrics(root, border.Padding);
            border.Padding = compact ? DensePadding(original.Padding) : original.Padding;
        }
        if (root is Grid grid)
        {
            var original = OriginalMetrics(root, grid.Padding, columnSpacing: grid.ColumnSpacing, rowSpacing: grid.RowSpacing);
            grid.Padding = compact ? DensePadding(original.Padding) : original.Padding;
            grid.ColumnSpacing = compact ? UiLayout.CompactGap(original.ColumnSpacing) : original.ColumnSpacing;
            grid.RowSpacing = compact ? UiLayout.CompactGap(original.RowSpacing) : original.RowSpacing;
        }
        if (root is StackPanel stack)
        {
            var original = OriginalMetrics(root, stack.Padding, stack.Spacing);
            stack.Padding = compact ? DensePadding(original.Padding) : original.Padding;
            stack.Spacing = compact ? UiLayout.CompactGap(original.Spacing) : original.Spacing;
        }
        if (root is Panel panel)
            panel.ChildrenTransitions = preferences.AnimationsEnabled ? null : new TransitionCollection();
        if (root is ItemsControl items)
            items.ItemContainerTransitions = preferences.AnimationsEnabled ? null : new TransitionCollection();
        // Visit declared content as well as realized templates, including unopened tabs.
        if (root is Panel container)
        {
            foreach (var child in container.Children.OfType<FrameworkElement>()) ApplyLayout(child, preferences);
            return;
        }
        if (root is Border { Child: FrameworkElement borderChild })
        {
            ApplyLayout(borderChild, preferences);
            return;
        }
        if (root is ContentControl { Content: FrameworkElement content })
        {
            ApplyLayout(content, preferences);
            return;
        }
        // Native templates manage their own density and focus visuals.
        if (root is Control) return;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (VisualTreeHelper.GetChild(root, index) is FrameworkElement child) ApplyLayout(child, preferences);
    }

    private static void ApplySurfaces(AppPreferences preferences)
    {
        var system = new UISettings();
        var contrast = new AccessibilitySettings().HighContrast;
        var effects = system.AdvancedEffectsEnabled && !contrast;
        var transparent = BackdropPolicy.UseAcrylic(preferences.AcrylicBackgroundEnabled, effects, contrast);
        var dark = preferences.Theme == "dark" || (preferences.Theme == "auto" && system.GetColorValue(UIColorType.Background).R < 80);
        var background = system.GetColorValue(UIColorType.Background);
        var foreground = system.GetColorValue(UIColorType.Foreground);
        SetBrush("PageSurfaceBrush", dark ? (effects ? "#E0111418" : "#111418") : (effects ? "#D8CCD5DE" : "#CCD5DE"));
        SetBrush("PanelSurfaceBrush", dark ? (transparent ? "#602D3237" : "#2D3237") : (transparent ? "#70FFFFFF" : "#FFFFFF"));
        SetBrush("SidebarSurfaceBrush", dark ? (effects ? "#E820262C" : "#20262C") : (effects ? "#E8E3EAF0" : "#E3EAF0"));
        SetBrush("SectionSurfaceBrush", dark ? "#20262C" : "#EDF1F5");
        SetBrush("SoftPanelBrush", dark ? "#1C2227" : "#E5EBF0");
        SetBrush("PanelStrokeBrush", dark ? "#708EA0AF" : "#70808B96");
        SetBrush("AppTextBrush", dark ? "#F2F5F7" : "#172025");
        SetBrush("UiDangerBrush", dark ? "#FFB4AB" : "#B42318");
        SetBrush("UiSuccessBrush", dark ? "#7EDAC7" : "#087443");
        SetBrush("UiWarningTextBrush", dark ? "#F2C06C" : "#97600F");
        SetBrush("MetricAreaBrush", dark ? "#7EDAC7" : "#087E74");
        SetBrush("MetricMaterialBrush", dark ? "#B8C7D1" : "#63737D");
        SetBrush("MetricPayrollBrush", dark ? "#F2C06C" : "#97600F");
        foreach (var key in new[] { "ButtonBackground", "TextControlBackground" })
            SetBrush(key, dark ? "#F0404850" : "#FFFFFF");
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "TextControlBackgroundPointerOver" })
            SetBrush(key, dark ? "#F041494F" : "#FFFFFF");
        SetBrush("ButtonBackgroundPressed", dark ? "#FF272D32" : "#E5ECF0");
        SetBrush("TextControlBackgroundFocused", dark ? "#FF1D2226" : "#FFFFFF");
        foreach (var key in new[] { "ButtonBorderBrush", "TextControlBorderBrush" }) SetBrush(key, dark ? "#70D1E0E8" : "#70808B96");
        foreach (var key in new[] { "ButtonBorderBrushPointerOver", "TextControlBorderBrushPointerOver" }) SetBrush(key, dark ? "#80D1E0E8" : "#80808B96");
        SetGradient("GlassEdgeBrush", dark ? ["#A0DAEBF5", "#708EA0AF", "#508EA0AF", "#808EA0AF"] : ["#FFFFFFFF", "#80808B96", "#60808B96", "#C0FFFFFF"]);
        SetGradient("MetricSurfaceBrush", dark ? ["#F84A545D", "#F0374048"] : ["#FFF5F8FA", "#FFE0E8EF"]);
        SetGradient("PaneGlassBrush", dark ? (transparent ? ["#50383F46", "#40292E34"] : ["#F2383F46", "#F0292E34"])
            : (transparent ? ["#60FFFFFF", "#50F4F7FA"] : ["#FEFFFFFF", "#F0F4F7FA"]));
        var acrylic = (AcrylicBrush)Application.Current.Resources["GlassChromeBrush"];
        acrylic.TintColor = ParseColor(dark ? "#24292D" : "#F4F7F9");
        acrylic.FallbackColor = contrast ? background : acrylic.TintColor;
        acrylic.TintOpacity = dark ? .62 : .7;
        acrylic.TintLuminosityOpacity = dark ? .72 : .85;
        acrylic.AlwaysUseFallback = !transparent;
        if (contrast)
        {
            foreach (var key in new[] { "PageSurfaceBrush", "PanelSurfaceBrush", "SidebarSurfaceBrush", "SectionSurfaceBrush", "SoftPanelBrush", "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" }) SetColor(key, background);
            foreach (var key in new[] { "AppTextBrush", "UiDangerBrush", "UiSuccessBrush", "UiWarningTextBrush", "PanelStrokeBrush", "MetricAreaBrush", "MetricMaterialBrush", "MetricPayrollBrush", "ButtonBorderBrush", "TextControlBorderBrush", "ButtonBorderBrushPointerOver", "TextControlBorderBrushPointerOver" }) SetColor(key, foreground);
            foreach (var stop in ((LinearGradientBrush)Application.Current.Resources["GlassEdgeBrush"]).GradientStops) stop.Color = foreground;
            foreach (var stop in ((LinearGradientBrush)Application.Current.Resources["MetricSurfaceBrush"]).GradientStops) stop.Color = background;
            foreach (var stop in ((LinearGradientBrush)Application.Current.Resources["PaneGlassBrush"]).GradientStops) stop.Color = background;
        }
    }

    private static void ApplyAccent(AppPreferences preferences)
    {
        var systemSettings = new UISettings();
        var option = AccentPalette.Find(preferences.AccentColor);
        var color = option.Id == "system" ? systemSettings.GetColorValue(UIColorType.Accent)
            : Color.FromArgb(255, option.R, option.G, option.B);
        if (new AccessibilitySettings().HighContrast) color = systemSettings.GetColorValue(UIColorType.Foreground);
        var accentSurface = (LinearGradientBrush)Application.Current.Resources["AccentSurfaceBrush"];
        accentSurface.GradientStops[0].Color = color;
        accentSurface.GradientStops[1].Color = new AccessibilitySettings().HighContrast
            ? color : Color.FromArgb(255, (byte)(color.R * .78), (byte)(color.G * .78), (byte)(color.B * .78));
        SetColor("AppOnAccentBrush", AccentPalette.UseLightForeground(color.R, color.G, color.B) ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black);
        var hover = Color.FromArgb(255, (byte)(color.R * .85), (byte)(color.G * .85), (byte)(color.B * .85));

        if (Application.Current.Resources["AppAccentBrush"] is SolidColorBrush accentBrush) accentBrush.Color = color;
        var dark = preferences.Theme == "dark" || (preferences.Theme == "auto" && systemSettings.GetColorValue(UIColorType.Background).R < 80);
        var textColor = dark ? Color.FromArgb(255, (byte)(color.R + (255 - color.R) * .5), (byte)(color.G + (255 - color.G) * .5), (byte)(color.B + (255 - color.B) * .5)) : color;
        if (!dark && !AccentPalette.UseLightForeground(color.R, color.G, color.B))
            textColor = Color.FromArgb(255, (byte)(color.R * .55), (byte)(color.G * .55), (byte)(color.B * .55));
        if (new AccessibilitySettings().HighContrast) textColor = color;
        SetColor("AppAccentTextBrush", textColor);
        SetColor("AppSelectionBrush", Color.FromArgb(38, color.R, color.G, color.B));
        SetColor("AppWarningBrush", dark ? Color.FromArgb(255, 65, 51, 23) : Color.FromArgb(255, 255, 244, 206));
        // These brushes exist in App.xaml before native templates are instantiated.
        // Mutating their color updates existing controls without replacing resources.
        foreach (var key in new[] { "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed", "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchStrokeOnPressed", "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed", "ListViewItemSelectionIndicatorBrush", "ListViewItemSelectionIndicatorPointerOverBrush", "ListViewItemSelectionIndicatorPressedBrush", "ComboBoxItemSelectionIndicatorBrush", "TextControlBorderBrushFocused" })
            SetColor(key, color);
        foreach (var key in new[] { "ListViewItemBackgroundSelected", "ListViewItemBackgroundSelectedPointerOver", "ListViewItemBackgroundSelectedPressed" })
            SetColor(key, Color.FromArgb(38, color.R, color.G, color.B));
        Application.Current.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(color);
        Application.Current.Resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(hover);
        Application.Current.Resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(hover);
        Application.Current.Resources["AccentFillColorPrimaryBrush"] = new SolidColorBrush(color);
        Application.Current.Resources["SystemAccentColor"] = color;
        Application.Current.Resources["SystemAccentColorLight1"] = color;
        Application.Current.Resources["SystemAccentColorLight2"] = color;
        Application.Current.Resources["SystemAccentColorLight3"] = color;
        Application.Current.Resources["SystemAccentColorDark1"] = color;
        Application.Current.Resources["SystemAccentColorDark2"] = color;
        Application.Current.Resources["SystemAccentColorDark3"] = color;
        Application.Current.Resources["SystemControlHighlightAccentBrush"] = new SolidColorBrush(color);
    }

    private static void SetBrush(string key, string hex)
    {
        if (Application.Current.Resources[key] is not SolidColorBrush brush) return;
        brush.Color = ParseColor(hex);
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        var alpha = hex.Length == 8 ? Convert.ToByte(hex[..2], 16) : (byte)255;
        if (hex.Length == 8) hex = hex[2..];
        return Color.FromArgb(alpha, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16));
    }

    private static void SetGradient(string key, string[] colors)
    {
        var brush = (LinearGradientBrush)Application.Current.Resources[key];
        for (var i = 0; i < colors.Length; i++) brush.GradientStops[i].Color = ParseColor(colors[i]);
    }

    private static void SetColor(string key, Color color)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush) brush.Color = color;
        else Application.Current.Resources[key] = new SolidColorBrush(color);
    }
}
