using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = Path.GetFullPath(args[0]);
var keys = new HashSet<string>(StringComparer.Ordinal);
bool Russian(string text) => Regex.IsMatch(text, "[А-Яа-яЁё]");
string FormatKey(InterpolatedStringExpressionSyntax expression)
{
    var builder = new StringBuilder();
    var index = 0;
    foreach (var part in expression.Contents)
        if (part is InterpolatedStringTextSyntax text) builder.Append(text.TextToken.ValueText);
        else if (part is InterpolationSyntax item) builder.Append('{').Append(index++).Append(item.AlignmentClause?.ToString()).Append(item.FormatClause?.ToString()).Append('}');
    return builder.ToString();
}
foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "native-winui"), "*.cs", SearchOption.AllDirectories)
    .Where(path => !Regex.IsMatch(path, @"[\\/](bin|obj)[\\/]")))
{
    var source = File.ReadAllText(path);
    var tree = CSharpSyntaxTree.ParseText(source).GetRoot();
    foreach (var value in tree.DescendantNodes().OfType<LiteralExpressionSyntax>())
        if (value.IsKind(SyntaxKind.StringLiteralExpression) && Russian(value.Token.ValueText)) keys.Add(value.Token.ValueText);
    foreach (var value in tree.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>())
        if (Russian(value.ToString())) keys.Add(FormatKey(value));
    var relative = Path.GetRelativePath(Path.Combine(root, "native-winui"), path).Replace('\\', '/');
    if (!(relative == "MainWindow.xaml.cs" || relative.StartsWith("Views/") || relative.StartsWith("Controllers/") ||
        relative is "Services/NotificationCenter.cs" or "Services/CommandAppearance.cs" or "Services/AccentPicker.cs")) continue;
    var updated = new Rewriter().Visit(tree)!.ToFullString();
    if (updated != source && !updated.Contains("using VitanCut.WinUI.Services;") && !updated.Contains("namespace VitanCut.WinUI.Services;"))
        updated = "using VitanCut.WinUI.Services;\n" + updated;
    if (updated != source) File.WriteAllText(path, updated, new UTF8Encoding(false));
}
var uiProperties = new HashSet<string> { "Text", "Content", "Header", "PlaceholderText", "Label", "OnContent", "OffContent", "AutomationProperties.Name", "ToolTipService.ToolTip" };
foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "native-winui"), "*.xaml", SearchOption.AllDirectories)
    .Where(path => !Regex.IsMatch(path, @"[\\/](bin|obj)[\\/]")))
{
    var source = File.ReadAllText(path);
    var changed = Regex.Replace(source, @"<[A-Za-z][^<>]*?>", match =>
    {
        if (match.Value.StartsWith("<Window ") || match.Value.StartsWith("<Window\r") || match.Value.StartsWith("<Window\n")) return match.Value;
        var names = new List<string>();
        foreach (Match attribute in Regex.Matches(match.Value, "(?<name>[A-Za-z][A-Za-z0-9_.:-]*)=\"(?<text>[^\"]*)\""))
        {
            var value = System.Net.WebUtility.HtmlDecode(attribute.Groups["text"].Value);
            if (uiProperties.Contains(attribute.Groups["name"].Value) && Russian(value) && !value.StartsWith('{'))
            { names.Add(attribute.Groups["name"].Value); keys.Add(value); }
        }
        if (names.Count == 0 || match.Value.Contains("l10n:UiLocalization.Properties=")) return match.Value;
        var index = match.Value.EndsWith("/>") ? match.Value.Length - 2 : match.Value.Length - 1;
        return match.Value.Insert(index, $" l10n:UiLocalization.Properties=\"{string.Join(',', names)}\"");
    });
    if (changed != source)
    {
        if (!changed.Contains("xmlns:l10n="))
            changed = Regex.Replace(changed, "xmlns:x=\"[^\"]+\"", match => match.Value + "\n    xmlns:l10n=\"using:VitanCut.WinUI.Services\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        File.WriteAllText(path, changed, new UTF8Encoding(false));
    }
}
var output = Path.Combine(root, "native-winui", "Localization");
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "source.json"), JsonSerializer.Serialize(keys.Order().ToArray(), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"Collected {keys.Count} source messages");

sealed class Rewriter : CSharpSyntaxRewriter
{
    private static readonly HashSet<string> Properties = ["Text", "Header", "Content", "PlaceholderText", "Title", "Label", "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText", "OnContent", "OffContent"];
    private static readonly HashSet<string> Calls = ["ShowMessageAsync", "ConfirmDeleteAsync", "ConfirmDelete", "CreateDialog", "ShowSuccess", "Publish", "SetName", "SetToolTip", "ConfirmDeleteAsync", "ShowError"];
    private static ExpressionSyntax Localize(ExpressionSyntax node)
    {
        if (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) && Regex.IsMatch(literal.Token.ValueText, "[А-Яа-яЁё]"))
            return SyntaxFactory.ParseExpression("UiText.Get(" + node.WithoutTrivia() + ")").WithTriviaFrom(node);
        if (node is InterpolatedStringExpressionSyntax && Regex.IsMatch(node.ToString(), "[А-Яа-яЁё]"))
            return SyntaxFactory.ParseExpression("UiText.Format(" + node.WithoutTrivia() + ")").WithTriviaFrom(node);
        if (node is ConditionalExpressionSyntax conditional)
            return conditional.WithWhenTrue(Localize(conditional.WhenTrue)).WithWhenFalse(Localize(conditional.WhenFalse));
        if (node is SwitchExpressionSyntax choice)
            return choice.WithArms(SyntaxFactory.SeparatedList(choice.Arms.Select(arm => arm.WithExpression(Localize(arm.Expression)))));
        if (node is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
            return binary.WithLeft(Localize(binary.Left)).WithRight(Localize(binary.Right));
        return node;
    }
    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        var result = (AssignmentExpressionSyntax)base.VisitAssignmentExpression(node)!;
        var name = result.Left is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text : result.Left.ToString();
        return Properties.Contains(name) ? result.WithRight(Localize(result.Right)) : result;
    }
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var result = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        var name = result.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text : result.Expression.ToString();
        if (!Calls.Contains(name) || result.ArgumentList.Arguments.Count == 0) return result;
        return result.WithArgumentList(result.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(
            result.ArgumentList.Arguments.Select(argument => argument.WithExpression(Localize(argument.Expression))))));
    }
}
