using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class PayrollChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var language = UiText.Language;
        try
        {
            UiText.Language = "ru";
            var areaPayroll = new Payroll { Note = "Сборка мебели", Mode = "area", Rate = 500.25 };
            var row = PayrollPresentation.Create(areaPayroll, 3.62);
            check(row.Title == areaPayroll.Note && row.Calculation.Contains("500,25") && row.Calculation.Contains("3,62") &&
                row.Total == "1 810,91 руб.", "Area payroll row shows comment, decimal rate, area and calculated total");
            var updated = PayrollPresentation.Create(areaPayroll, 10);
            check(updated.Total == "5 002,5 руб." && areaPayroll.Rate == 500.25,
                "Payroll row recalculates against project area without changing the saved rate");
            var fixedPayroll = new Payroll { Note = "Доставка", Mode = "fixed", Amount = 1000.75, Rate = 900 };
            var fixedRow = PayrollPresentation.Create(fixedPayroll, 100);
            check(fixedRow.Title == "Доставка" && fixedRow.Calculation == "Фиксированная сумма" && fixedRow.Total == "1 000,75 руб.",
                "Fixed payroll row shows its actual amount independently of area or an old rate");
            check(PayrollPresentation.Create(new Payroll(), 0).Title == "ЗП" && PayrollPresentation.Create(areaPayroll, 0).Total == "0 руб.",
                "Blank payroll comments have a fallback and an empty project gives zero area payroll");
            var state = new AppState();
            state.Load(Path.Combine(output, "payroll-presentation.json"));
            var material = new Material { Name = "ЛДСП", Unit = "m2", Cost = 100 };
            state.Database.Materials["ЛДСП"] = [material];
            var project = new Project { Products = [new Product { Qty = 2, Details = [new Detail {
                Type = "ЛДСП", MaterialId = material.Id, LengthExpr = "1000", WidthExpr = "500", Qty = 3 }] }],
                Payrolls = [areaPayroll, fixedPayroll] };
            var totals = Calculator.Project(state, project);
            check(totals.Area == 3 && totals.Payroll == areaPayroll.Rate * 3 + fixedPayroll.Amount &&
                PayrollPresentation.Create(areaPayroll, totals.Area).Total == "1 500,75 руб.",
                "Payroll rows use the same area, including product/detail quantities, as project totals");
            foreach (var selectedLanguage in new[] { "en", "de", "es", "zh-Hans" })
            {
                UiText.Language = selectedLanguage;
                check(PayrollPresentation.Create(areaPayroll, 3).Total == UiText.Format($"{Calculator.Number(1500.75)} руб.") &&
                    UiText.Get("Ставка за м²") != "Ставка за м²", "Payroll amounts and rate header support interface language: " + selectedLanguage);
            }
        }
        finally { UiText.Language = language; }
    }
}
