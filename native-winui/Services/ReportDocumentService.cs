using System.Text;
using System.Globalization;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VitanCut.WinUI.Services;

/// <summary>
/// Generates portable PDF and editable XLSX documents from cutting and detailing reports.
/// </summary>
public static class ReportDocumentService
{
    static ReportDocumentService() => QuestPDF.Settings.License = LicenseType.Community;

    public static void SavePdf(CutReport report, string path) => CreateCutPdf(report).GeneratePdf(path);
    public static void SavePdf(DetailingReport report, string path) => CreateDetailingPdf(report).GeneratePdf(path);
    public static void SaveXlsx(CutReport report, string path) { using var book = CreateCutWorkbook(report); book.SaveAs(path); }
    public static void SaveXlsx(DetailingReport report, string path) { using var book = CreateDetailingWorkbook(report); book.SaveAs(path); }

    private static Document CreateCutPdf(CutReport report) => Document.Create(document =>
    {
        var sheets = report.Groups.SelectMany(group => group.Sheets.Select(sheet => (group, sheet))).ToList();
        if (sheets.Count == 0)
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.Content().Text("В раскрое нет листовых деталей.");
            });
        }

        foreach (var (group, sheet) in sheets)
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontSize(8));
                page.Header().Column(column =>
                {
                    column.Item().Text("Карта кроя").FontSize(20).SemiBold();
                    column.Item().Text($"{report.ProjectName} · {group.MaterialName} · Лист {sheet.Number}").FontColor(Colors.Grey.Darken1);
                });
                page.Content().PaddingTop(10).ScaleToFit().Column(column =>
                {
                    column.Item().Text($"{N(sheet.SheetLength)} × {N(sheet.SheetWidth)} мм · использование {sheet.Efficiency:P0}").SemiBold();
                    column.Item().Height(330).PaddingTop(8).AlignCenter().Svg(CutSheetSvg(sheet, report.Palette)).FitArea();
                    column.Item().PaddingTop(8).Text("Легенда").FontSize(11).SemiBold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(28);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1.4f);
                        });
                        Header(table, "№", "Изделие", "Деталь", "Размер, мм");
                        foreach (var item in sheet.Placements.GroupBy(part => part.DisplayNumber).OrderBy(item => item.Key))
                        {
                            var first = item.First();
                            var quantity = item.Count() > 1 ? $" × {item.Count()}" : "";
                            Row(table, first.DisplayNumber.ToString(), first.ProductName, first.DetailName + quantity, $"{N(first.BaseLength)} × {N(first.BaseWidth)}");
                        }
                    });
                });
                page.Footer().AlignCenter().Text(text => { text.Span("Расчёт мебели · "); text.CurrentPageNumber(); });
            });
        }
        if (report.Unplaced.Count > 0)
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontSize(9));
                page.Header().Text("Не размещено в раскрое").FontSize(18).SemiBold();
                page.Content().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(columns => { columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn(2); });
                    Header(table, "Изделие", "Деталь", "Материал", "Причина");
                    foreach (var item in report.Unplaced) Row(table, item.ProductName, item.DetailName, item.MaterialName, item.Reason);
                });
            });
        }
    });
    private static Document CreateDetailingPdf(DetailingReport report) => Document.Create(document => document.Page(page =>
    {
        page.Size(PageSizes.A4.Landscape());
        page.Margin(24);
        page.DefaultTextStyle(style => style.FontSize(9));
        page.Header().Column(column =>
        {
            column.Item().Text("Деталировка").FontSize(20).SemiBold();
            column.Item().Text($"{report.ProjectName} · {report.Counterparty} · {report.Address}").FontColor(Colors.Grey.Darken1);
        });
        page.Content().PaddingTop(14).Column(column =>
        {
            foreach (var product in report.Products)
            {
                column.Item().PaddingTop(8).Text(product.Name).FontSize(13).SemiBold();
                column.Item().Text($"{N(product.Length)} × {N(product.Depth)} × {N(product.Height)} мм · {N(product.Qty)} шт.");
                if (report.IncludeImages && ProductImageData.Read(product.Image) is { } image)
                    column.Item().Height(100).Image(image).FitArea();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2); columns.RelativeColumn(2); columns.RelativeColumn();
                        columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn();
                    });
                    Header(table, "Деталь", "Материал", "Кол-во", "Длина", "Ширина", "Квадратура", "Стоимость, руб.");
                    foreach (var row in product.Rows)
                        Row(table, row.DetailName + (row.RotationLocked ? " (без поворота)" : ""), row.MaterialName, N(row.Qty), Dimension(row.Length), Dimension(row.Width), row.Area > 0 ? $"{N(row.Area)} м²" : "—", Money(row.Cost));
                });
                foreach (var row in product.Rows.Where(row => row.HasIssue))
                    column.Item().PaddingTop(3).Text($"{row.DetailName}: {row.Issue}").FontColor(Colors.Red.Darken2);
            }
        });
        page.Footer().AlignCenter().Text(text => { text.Span("Расчёт мебели · "); text.CurrentPageNumber(); });
    }));
    private static XLWorkbook CreateCutWorkbook(CutReport report)
    {
        var book = new XLWorkbook();
        var index = 0;
        foreach (var group in report.Groups)
        foreach (var cutSheet in group.Sheets)
        {
            var sheet = book.AddWorksheet($"Лист {++index}");
            sheet.Range("A1:F1").Merge().Value = $"{report.ProjectName} · {group.MaterialName} · Лист {cutSheet.Number}";
            sheet.Cell("A1").Style.Font.SetBold().Font.SetFontSize(14);
            sheet.Range("A2:F2").Merge().Value = $"{N(cutSheet.SheetLength)} × {N(cutSheet.SheetWidth)} мм · использование {cutSheet.Efficiency:P0}";
            sheet.Columns(1, 6).Width = 20;
            sheet.Rows(3, 21).Height = 16;
            var pictureDocument = Document.Create(document => document.Page(page =>
            {
                page.Size(780, 370);
                page.Content().Svg(CutSheetSvg(cutSheet, report.Palette)).FitArea();
            }));
            using var imageStream = new MemoryStream(pictureDocument.GenerateImages().First());
            var picture = sheet.AddPicture(imageStream).MoveTo(sheet.Cell("A3"));
            picture.WithSize(780, 370);
            var row = 23;
            WriteHeaders(sheet, row++, "№", "Изделие", "Деталь", "Длина, мм", "Ширина, мм", "Кол-во");
            foreach (var parts in cutSheet.Placements.GroupBy(part => part.DisplayNumber).OrderBy(parts => parts.Key))
            {
                var part = parts.First();
                sheet.Cell(row, 1).Value = part.DisplayNumber;
                sheet.Cell(row, 2).Value = part.ProductName;
                sheet.Cell(row, 3).Value = part.DetailName;
                sheet.Cell(row, 4).Value = part.BaseLength;
                sheet.Cell(row, 5).Value = part.BaseWidth;
                sheet.Cell(row, 6).Value = parts.Count();
                row++;
            }
            sheet.Range(23, 1, row - 1, 6).Style.Alignment.WrapText = true;
            sheet.Rows(23, row - 1).AdjustToContents();
            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
            sheet.PageSetup.FitToPages(1, 1);
            sheet.PageSetup.PrintAreas.Add(1, 1, row - 1, 6);
        }
        if (index == 0) book.AddWorksheet("Раскрой").Cell("A1").Value = "В раскрое нет листовых деталей.";
        if (report.Unplaced.Count > 0)
        {
            var unplaced = book.AddWorksheet("Не размещено");
            WriteHeaders(unplaced, 1, "Материал", "Изделие", "Деталь", "Длина", "Ширина", "Причина");
            var unplacedRow = 2;
            foreach (var item in report.Unplaced)
            {
                unplaced.Cell(unplacedRow, 1).Value = item.MaterialName;
                unplaced.Cell(unplacedRow, 2).Value = item.ProductName;
                unplaced.Cell(unplacedRow, 3).Value = item.DetailName;
                unplaced.Cell(unplacedRow, 4).Value = item.Length;
                unplaced.Cell(unplacedRow, 5).Value = item.Width;
                unplaced.Cell(unplacedRow, 6).Value = item.Reason;
                unplacedRow++;
            }
            unplaced.Columns().AdjustToContents();
        }
        return book;
    }
    private static XLWorkbook CreateDetailingWorkbook(DetailingReport report)
    {
        var book = NewWorkbook("Деталировка", report.ProjectName, report.Counterparty);
        var sheet = book.Worksheet(1);
        var row = 5;
        WriteHeaders(sheet, row++, "Изделие", "Деталь", "Тип", "Материал", "Кол-во", "Длина", "Ширина", "Квадратура, м²", "Стоимость, руб.");
        foreach (var product in report.Products)
        {
            if (report.IncludeImages && ProductImageData.Read(product.Image) is { } bytes)
            {
                using var stream = new MemoryStream(bytes);
                var picture = sheet.AddPicture(stream).MoveTo(sheet.Cell(row, 1));
                var scale = Math.Min(180.0 / picture.Width, 100.0 / picture.Height);
                picture.WithSize((int)(picture.Width * scale), (int)(picture.Height * scale));
                sheet.Row(row).Height = 80;
                sheet.Cell(row, 2).Value = product.Name;
                row++;
            }
            foreach (var detail in product.Rows)
            {
                sheet.Cell(row, 1).Value = product.Name;
                sheet.Cell(row, 2).Value = detail.DetailName + (detail.RotationLocked ? " (без поворота)" : "");
                sheet.Cell(row, 3).Value = detail.Type;
                sheet.Cell(row, 4).Value = detail.MaterialName;
                sheet.Cell(row, 5).Value = detail.Qty;
                if (detail.Length > 0) sheet.Cell(row, 6).Value = detail.Length;
                if (detail.Width > 0) sheet.Cell(row, 7).Value = detail.Width;
                if (detail.Area > 0) sheet.Cell(row, 8).Value = detail.Area;
                sheet.Cell(row, 9).Value = detail.Cost;
                if (detail.HasIssue) sheet.Cell(row, 2).CreateComment().AddText(detail.Issue);
                row++;
            }
        }
        sheet.Column(8).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(9).Style.NumberFormat.Format = "#,##0.00";
        FinalizeSheet(sheet, row - 1, 9);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(5, 5);
        return book;
    }
    private static XLWorkbook NewWorkbook(string sheetName, string project, string customer)
    {
        var book = new XLWorkbook();
        var sheet = book.AddWorksheet(sheetName);
        sheet.Cell(1, 1).Value = sheetName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Cell(2, 1).Value = $"Проект: {project}";
        sheet.Cell(3, 1).Value = $"Заказчик: {customer}";
        return book;
    }

    private static void WriteHeaders(IXLWorksheet sheet, int row, params string[] headers)
    {
        for (var column = 0; column < headers.Length; column++) sheet.Cell(row, column + 1).Value = headers[column];
        var range = sheet.Range(row, 1, row, headers.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("DCEBFA");
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void FinalizeSheet(IXLWorksheet sheet, int lastRow, int lastColumn)
    {
        if (lastRow >= 5) sheet.Range(4, 1, lastRow, lastColumn).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(4);
    }

    private static void Header(TableDescriptor table, params string[] values)
    {
        table.Header(header =>
        {
            foreach (var value in values)
                header.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text(value).SemiBold();
        });
    }

    private static void Row(TableDescriptor table, params string[] values)
    {
        foreach (var value in values)
            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(value);
    }

    private static string N(double value) => Calculator.Number(value);
    private static string Money(double value) => Calculator.Money(value);
    private static string Dimension(double value) => value > 0 ? N(value) : "—";

    private static string CutSheetSvg(CutSheet sheet, string palette)
    {
        var xml = new StringBuilder();
        xml.AppendFormat(CultureInfo.InvariantCulture, @"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 {0} {1}"" preserveAspectRatio=""xMidYMid meet"">", sheet.SheetLength, sheet.SheetWidth);
        xml.AppendFormat(CultureInfo.InvariantCulture, @"<rect x=""0"" y=""0"" width=""{0}"" height=""{1}"" fill=""#FFFFFF"" stroke=""#475569"" stroke-width=""8""/>", sheet.SheetLength, sheet.SheetWidth);
        foreach (var part in sheet.Placements)
        {
            var fill = CutColors.Fill(palette, part.DisplayNumber);
            xml.AppendFormat(CultureInfo.InvariantCulture, @"<rect x=""{0}"" y=""{1}"" width=""{2}"" height=""{3}"" fill=""{4}"" stroke=""{5}"" stroke-width=""5""/>", part.X, part.Y, part.Length, part.Width, fill, CutColors.Stroke(palette));
            var font = Math.Max(36, Math.Min(part.Length, part.Width) / 5);
            xml.AppendFormat(CultureInfo.InvariantCulture, @"<text x=""{0}"" y=""{1}"" text-anchor=""middle"" dominant-baseline=""middle"" font-family=""Arial"" font-size=""{2}"" fill=""#111827"">{3}</text>", part.X + part.Length / 2, part.Y + part.Width / 2, font, part.DisplayNumber);
        }
        xml.Append("</svg>");
        return xml.ToString();
    }
}
