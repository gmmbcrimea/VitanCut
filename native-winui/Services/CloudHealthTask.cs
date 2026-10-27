using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace VitanCut.WinUI.Services;

internal sealed record TaskCommandResult(int ExitCode, string Output);

internal sealed class CloudHealthTask(AppPaths paths, string executable, string userId,
    Func<string[], Task<TaskCommandResult>>? runCommand = null)
{
    public const string CheckArgument = "--cloud-health-check";
    public const string RemoveArgument = "--remove-cloud-health-task";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    internal string TaskName => "VitanCut.CloudHealth-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..16];

    public static CloudHealthTask ForCurrentUser(AppPaths paths) => new(paths,
        Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable."),
        WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Windows user is unavailable."));

    internal string CreateXml(DateTime start) => new XDocument(new XElement(Ns + "Task", new XAttribute("version", "1.2"),
        new XElement(Ns + "RegistrationInfo", new XElement(Ns + "Description", "VitanCut: read-only cloud availability check every three days.")),
        new XElement(Ns + "Triggers", new XElement(Ns + "CalendarTrigger",
            new XElement(Ns + "StartBoundary", start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
            new XElement(Ns + "Enabled", true), new XElement(Ns + "ScheduleByDay", new XElement(Ns + "DaysInterval", 3)))),
        new XElement(Ns + "Principals", new XElement(Ns + "Principal", new XAttribute("id", "User"),
            new XElement(Ns + "UserId", userId), new XElement(Ns + "LogonType", "InteractiveToken"),
            new XElement(Ns + "RunLevel", "LeastPrivilege"))),
        new XElement(Ns + "Settings", new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
            new XElement(Ns + "DisallowStartIfOnBatteries", false), new XElement(Ns + "StopIfGoingOnBatteries", false),
            new XElement(Ns + "StartWhenAvailable", true), new XElement(Ns + "RunOnlyIfNetworkAvailable", true),
            new XElement(Ns + "Enabled", true), new XElement(Ns + "ExecutionTimeLimit", "PT2M"),
            new XElement(Ns + "RestartOnFailure", new XElement(Ns + "Interval", "PT30M"), new XElement(Ns + "Count", 3))),
        new XElement(Ns + "Actions", new XAttribute("Context", "User"), new XElement(Ns + "Exec",
            new XElement(Ns + "Command", executable), new XElement(Ns + "Arguments", CheckArgument),
            new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(executable)))))).ToString();

    internal bool IsCurrent(string xml)
    {
        try
        {
            var task = XDocument.Parse(xml).Root;
            var exec = task?.Element(Ns + "Actions")?.Element(Ns + "Exec");
            var settings = task?.Element(Ns + "Settings");
            var principal = task?.Element(Ns + "Principals")?.Element(Ns + "Principal");
            var trigger = task?.Element(Ns + "Triggers")?.Element(Ns + "CalendarTrigger");
            return string.Equals((string?)exec?.Element(Ns + "Command"), executable, StringComparison.OrdinalIgnoreCase)
                && (string?)exec?.Element(Ns + "Arguments") == CheckArgument
                && string.Equals((string?)exec?.Element(Ns + "WorkingDirectory"), Path.GetDirectoryName(executable), StringComparison.OrdinalIgnoreCase)
                && task?.Element(Ns + "Actions")?.Elements().Count() == 1
                && task?.Element(Ns + "Triggers")?.Elements().Count() == 1
                && (string?)principal?.Element(Ns + "UserId") == userId
                && (string?)principal?.Element(Ns + "LogonType") == "InteractiveToken"
                && (string?)principal?.Element(Ns + "RunLevel") == "LeastPrivilege"
                && (string?)trigger?.Element(Ns + "ScheduleByDay")?.Element(Ns + "DaysInterval") == "3"
                && (string?)trigger?.Element(Ns + "Enabled") == "true"
                && (string?)settings?.Element(Ns + "Enabled") == "true"
                && (string?)settings?.Element(Ns + "StartWhenAvailable") == "true"
                && (string?)settings?.Element(Ns + "RunOnlyIfNetworkAvailable") == "true"
                && (string?)settings?.Element(Ns + "MultipleInstancesPolicy") == "IgnoreNew"
                && (string?)settings?.Element(Ns + "ExecutionTimeLimit") == "PT2M";
        }
        catch (System.Xml.XmlException) { return false; }
    }

    public async Task<CloudHealthResult> EnsureAsync()
    {
        if (paths.IsDevelopment) return new(true, "Disabled in development.");
        try
        {
            var query = await RunAsync(["/Query", "/TN", TaskName, "/XML"]);
            if (query.ExitCode == 0 && IsCurrent(query.Output)) return new(true, "Task is registered.");
            var xmlPath = Path.Combine(paths.TemporaryDirectory, "cloud-health-task.xml");
            Directory.CreateDirectory(paths.TemporaryDirectory);
            try
            {
                await File.WriteAllTextAsync(xmlPath, CreateXml(DateTime.Now.AddDays(3)), Encoding.Unicode);
                var create = await RunAsync(["/Create", "/TN", TaskName, "/XML", xmlPath, "/F"]);
                if (create.ExitCode != 0) return new(false, "Task registration failed. Check Windows Task Scheduler permissions.");
            }
            finally { if (File.Exists(xmlPath)) File.Delete(xmlPath); }
            var start = await RunAsync(["/Run", "/TN", TaskName]);
            return new(start.ExitCode == 0, start.ExitCode == 0 ? "Task registered and started." : "Task registered, but its first run could not be started.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { return new(false, "Windows Task Scheduler is unavailable."); }
    }

    public async Task<int> RemoveAsync()
    {
        if (paths.IsDevelopment) return 0;
        var query = await RunAsync(["/Query", "/TN", TaskName, "/XML"]);
        if (query.ExitCode != 0) return 0;
        var definition = XDocument.Parse(query.Output);
        var command = (string?)definition.Root?.Element(Ns + "Actions")?.Element(Ns + "Exec")?.Element(Ns + "Command");
        if (!string.Equals(command, executable, StringComparison.OrdinalIgnoreCase)) return 0;
        return (await RunAsync(["/Delete", "/TN", TaskName, "/F"])).ExitCode;
    }

    private Task<TaskCommandResult> RunAsync(string[] arguments) => (runCommand ?? ExecuteAsync)(arguments);

    private static async Task<TaskCommandResult> ExecuteAsync(string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Task Scheduler could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); throw; }
        await errors;
        return new(process.ExitCode, await output);
    }
}
