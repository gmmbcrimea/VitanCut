using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public sealed record PayrollRow(string Title, string Calculation, string Total);

public static class PayrollPresentation
{
    public static PayrollRow Create(Payroll payroll, double area)
    {
        var fixedAmount = payroll.Mode == "fixed";
        var amount = fixedAmount ? payroll.Amount : payroll.Rate * area;
        var calculation = fixedAmount ? UiText.Get("Фиксированная сумма")
            : $"{UiText.Format($"{Calculator.Number(payroll.Rate)} руб.")} / {UiText.Get("м²")} × {UiText.Format($"{Calculator.Number(area)} м²")}";
        return new(string.IsNullOrWhiteSpace(payroll.Note) ? UiText.Get("ЗП") : payroll.Note,
            calculation, UiText.Format($"{Calculator.Number(amount)} руб."));
    }
}
