namespace VitanCut.WinUI.Models;
public readonly record struct CounterpartyEdit(string Name, string Logo);

public readonly record struct ProjectEdit(string Name, string Counterparty, string Address);
public readonly record struct PayrollEdit(string Mode, double Amount, double Rate, string Note);
