namespace VitanCut.WinUI.Models;

public sealed class Database
{
    public List<string> Counterparties { get; set; } = ["Частный заказчик", "Дилер"];
    public List<string> MaterialGroups { get; set; } = ["ЛДСП", "Фурнитура", "Кромка", "Прочее"];
    public Dictionary<string, List<Material>> Materials { get; set; } = SeedMaterials();
    public Dictionary<string, List<Product>> Catalog { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
    public AppPreferences Preferences { get; set; } = new();

    private static Dictionary<string, List<Material>> SeedMaterials() => new()
    {
        ["ЛДСП"] =
        [
            new() { Name = "ЛДСП белый 16 мм", Cost = 850, Unit = "m2", SheetLength = 2750, SheetWidth = 1830 },
            new() { Name = "ЛДСП графит 18 мм", Cost = 1180, Unit = "m2", SheetLength = 2800, SheetWidth = 2070 }
        ],
        ["Фурнитура"] =
        [
            new() { Name = "Петля с доводчиком", Cost = 110, Unit = "pc" },
            new() { Name = "Направляющие 450 мм", Cost = 640, Unit = "pc" }
        ],
        ["Кромка"] =
        [
            new() { Name = "Кромка ПВХ 2 мм белая", Cost = 28, Unit = "lm" },
            new() { Name = "Кромка ПВХ 1 мм графит", Cost = 24, Unit = "lm" }
        ],
        ["Прочее"] =
        [
            new() { Name = "Упаковка", Cost = 250, Unit = "pc" }
        ]
    };
}

public sealed class Project
{
    public string Id { get; set; } = Ids.NewId();
    public string Name { get; set; } = "Новый проект";
    public string Counterparty { get; set; } = "Частный заказчик";
    public string Address { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<Product> Products { get; set; } = [];
    public List<Payroll> Payrolls { get; set; } = [];
    public List<CutLayoutOverride> CutLayout { get; set; } = [];
    public List<SavedCutPlan> CutPlans { get; set; } = [];
    public string ActiveCutPlanId { get; set; } = "";
    public List<SavedCutPlan> AdditionalCutPlans { get; set; } = [];
    public string ActiveAdditionalCutPlanId { get; set; } = "";
}

public sealed class SavedCutPlan
{
    public string Id { get; set; } = Ids.NewId();
    public string Name { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public bool Automatic { get; set; }
    public string InputSignature { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public List<CutLayoutOverride> Layout { get; set; } = [];
    public Dictionary<string, int> SheetCounts { get; set; } = [];
}

public sealed class CutLayoutOverride
{
    public string MaterialId { get; set; } = "";
    public int SheetNumber { get; set; }
    public string InstanceId { get; set; } = "";
    public double BaseLength { get; set; }
    public double BaseWidth { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Length { get; set; }
    public double Width { get; set; }
    public bool Rotated { get; set; }
}
public sealed class AppPreferences
{
    public string Theme { get; set; } = "auto";
    public string AccentColor { get; set; } = "system";
    public bool CompactMode { get; set; }
    public bool AnimationsEnabled { get; set; } = true;
    public bool CursorRevealEnabled { get; set; } = true;
    public string CutPalette { get; set; } = "blue";
    public string CutExportFormat { get; set; } = "pdf";
    public string DetailingExportFormat { get; set; } = "pdf";
    public bool DetailingIncludeImages { get; set; } = true;
    public double CutTrim { get; set; } = 10;
    public double CutGap { get; set; } = 6;
    public double CutUsefulRemainder { get; set; } = 200;
    public bool RotationDefaultsMigrated { get; set; }
    public bool SystemThemeDefaultMigrated { get; set; }
    public int ThemeDefaultsVersion { get; set; }
}

public sealed class Product
{
    public string Id { get; set; } = Ids.NewId();
    public string Name { get; set; } = "Новое изделие";
    public string Image { get; set; } = "";
    public double Length { get; set; } = 600;
    public double Depth { get; set; } = 500;
    public double Height { get; set; } = 720;
    public double Qty { get; set; } = 1;
    public bool HasLegs { get; set; }
    public double LegHeight { get; set; } = 100;
    public List<FixedSize> FixedSizes { get; set; } = [];
    public List<Detail> Details { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public string SizeText => $"{Length:0.##} × {Depth:0.##} × {Height:0.##} мм · {Qty:0.##} шт.";

    public override string ToString() => $"{Name} · {Length:0} x {Depth:0} x {Height:0} · {Qty:0} шт.";
}

public sealed class Detail
{
    public string Id { get; set; } = Ids.NewId();
    public string Name { get; set; } = "Новая деталь";
    public double Qty { get; set; } = 1;
    public string Type { get; set; } = "ЛДСП";
    public string MaterialId { get; set; } = "";
    public string LengthExpr { get; set; } = "длина изделия";
    public string WidthExpr { get; set; } = "глубина изделия";
    public bool AllowRotation { get; set; } = true;

    public override string ToString() => $"{Name} · {LengthExpr} x {WidthExpr} · {Qty:0} шт.";
}

public sealed class Material
{
    public string Id { get; set; } = Ids.NewId();
    public string Name { get; set; } = "Новый материал";
    public double Cost { get; set; }
    public string Unit { get; set; } = "pc";
    public double SheetLength { get; set; }
    public double SheetWidth { get; set; }
    public bool TextureDirection { get; set; }
}

public sealed class FixedSize
{
    public string Id { get; set; } = Ids.NewId();
    public string Name { get; set; } = "";
    public double Value { get; set; }
}

public sealed class Payroll
{
    public string Id { get; set; } = Ids.NewId();
    public string Mode { get; set; } = "fixed";
    public double Amount { get; set; }
    public double Rate { get; set; }
    public string Scope { get; set; } = "all-dsp";
    public string Note { get; set; } = "";
}

public sealed record MaterialChoice(string Type, Material Material)
{
    public override string ToString() => Material.Name;

    private static string UnitLabel(string unit) => unit switch
    {
        "m2" => "м²",
        "lm" => "пог. м",
        _ => "шт."
    };
}

public sealed record CustomerProductItem(string Counterparty, Project? Project, Product Product)
{
    public string ProductName => Product.Name;
    public string ProjectName => Project?.Name ?? "Каталог";
    public string SizeText => $"{Product.Length:0} x {Product.Depth:0} x {Product.Height:0} · {Product.Qty:0} шт.";

    public override string ToString() => $"{Product.Name} · {ProjectName} · {SizeText}";
}

public sealed record DetailCalc(double Length, double Width, double Qty, double Area, double RawArea, double Usage, double Cost, Material? Material, string Error = "");
public sealed record Totals(double Area, double RawArea, double ProductCost, double Payroll, double Cost);

public static class Ids
{
    public static string NewId() => Guid.NewGuid().ToString("N")[..12];
}
