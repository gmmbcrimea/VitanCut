using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VitanCut.WinUI.Services;

public static class UiLocalization
{
    public static readonly DependencyProperty PropertiesProperty = DependencyProperty.RegisterAttached(
        "Properties", typeof(string), typeof(UiLocalization), new PropertyMetadata("", OnPropertiesChanged));
    private sealed record SourceText(string Source, string Translation);
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, SourceText>> Sources = new();
    public static string GetProperties(DependencyObject target) => (string)target.GetValue(PropertiesProperty);
    public static void SetProperties(DependencyObject target, string value) => target.SetValue(PropertiesProperty, value);

    private static void OnPropertiesChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        ApplyElement(target);
        if (target is FrameworkElement element) element.Loaded += (_, _) => ApplyElement(element);
    }

    private static void ApplyElement(DependencyObject target)
    {
        var sources = Sources.GetValue(target, _ => new());
        foreach (var name in GetProperties(target).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var property = name switch
            {
                "AutomationProperties.Name" => AutomationProperties.NameProperty,
                "ToolTipService.ToolTip" => ToolTipService.ToolTipProperty,
                _ => target.GetType().GetProperty(name + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null) as DependencyProperty
            };
            if (property is null) continue;
            if (target.GetValue(property) is not string text) continue;
            if (!sources.ContainsKey(property)) sources.Add(property, new SourceText(text, text));
            var source = sources[property];
            if (text != source.Source && text != source.Translation) continue;
            var translated = UiText.Get(source.Source);
            target.SetValue(property, translated);
            sources[property] = source with { Translation = translated };
        }
    }

    public static void Apply(DependencyObject? root)
    {
        if (root is null) return;
        ApplyElement(root);
        if (root is Panel panel) { foreach (var child in panel.Children) Apply(child); return; }
        if (root is Border border) { Apply(border.Child); return; }
        if (root is ContentControl content && content.Content is DependencyObject childContent) Apply(childContent);
        if (root is ItemsControl items) foreach (var item in items.Items.OfType<DependencyObject>()) Apply(item);
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) Apply(VisualTreeHelper.GetChild(root, index));
    }
}
