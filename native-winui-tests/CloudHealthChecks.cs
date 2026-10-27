using System.Net;
using System.Xml.Linq;
using VitanCut.WinUI.Services;

internal static class CloudHealthChecks
{
    public static async Task RunAsync(Action<bool, string> check, string output)
    {
        var paths = AppPaths.ForDatabaseFile(Path.Combine(output, "cloud-health", "database.json"), isDevelopment: false);
        var executable = Path.Combine(output, "Программа & test", "VitanCut.WinUI.exe");
        var commands = new List<string[]>();
        string? xml = null;
        var exists = false;
        var task = new CloudHealthTask(paths, executable, "S-1-5-21-test", async arguments =>
        {
            commands.Add(arguments);
            if (arguments[0] == "/Query") return new(exists ? 0 : 1, xml ?? "");
            if (arguments[0] == "/Create")
            {
                xml = await File.ReadAllTextAsync(arguments[Array.IndexOf(arguments, "/XML") + 1]);
                exists = true;
            }
            return new(0, "");
        });
        check((await task.EnsureAsync()).Succeeded && commands.Select(args => args[0]).SequenceEqual(["/Query", "/Create", "/Run"]),
            "Startup registers a missing cloud task and starts its first run");
        var ns = XNamespace.Get("http://schemas.microsoft.com/windows/2004/02/mit/task");
        var root = XDocument.Parse(xml!).Root!;
        await File.WriteAllTextAsync(Path.Combine(output, "cloud-health-validation.xml"), xml!);
        check(task.IsCurrent(xml!) && root.Descendants(ns + "DaysInterval").Single().Value == "3" &&
            root.Descendants(ns + "Command").Single().Value == executable &&
            root.Descendants(ns + "LogonType").Single().Value == "InteractiveToken" &&
            root.Descendants(ns + "RunLevel").Single().Value == "LeastPrivilege" && !xml!.Contains("Password"),
            "Scheduled task has a three-day interval, escaped paths, current-user identity and no credentials");
        check(root.Descendants(ns + "StartWhenAvailable").Single().Value == "true" &&
            root.Descendants(ns + "RunOnlyIfNetworkAvailable").Single().Value == "true" &&
            root.Descendants(ns + "MultipleInstancesPolicy").Single().Value == "IgnoreNew",
            "Scheduled task catches up missed runs and avoids duplicate workers");
        check(!Directory.EnumerateFiles(paths.TemporaryDirectory).Any() && !File.Exists(paths.DatabasePath),
            "Task registration removes temporary XML without creating or loading the application database");
        commands.Clear();
        await task.EnsureAsync();
        check(commands.Count == 1 && commands[0][0] == "/Query", "Existing current task is not recreated or restarted on every launch");
        check(!task.IsCurrent("broken xml") && !task.IsCurrent(xml!.Replace("<DaysInterval>3</DaysInterval>", "<DaysInterval>1</DaysInterval>")) &&
            !task.IsCurrent(xml.Replace(executable.Replace("&", "&amp;"), "other.exe")),
            "Invalid, obsolete or wrong-executable task definitions need repair");
        commands.Clear();
        xml = xml!.Replace("<Enabled>true</Enabled>", "<Enabled>false</Enabled>");
        await task.EnsureAsync();
        check(commands.Select(args => args[0]).SequenceEqual(["/Query", "/Create", "/Run"]), "Disabled background task is repaired at startup");
        commands.Clear();
        check(await task.RemoveAsync() == 0 && commands.Last()[0] == "/Delete", "Uninstall removes the current user's managed task");
        var disabled = new CloudHealthTask(AppPaths.ForDatabaseFile(Path.Combine(output, "isolated-health.json")), executable, "test",
            _ => throw new Exception("Development must not call Task Scheduler"));
        check((await disabled.EnsureAsync()).Succeeded && await disabled.RemoveAsync() == 0,
            "Isolated development cannot register, run or remove Windows tasks");
        var deniedCommands = new List<string[]>();
        var denied = new CloudHealthTask(paths, executable, "test", arguments =>
        {
            deniedCommands.Add(arguments);
            return Task.FromResult(new TaskCommandResult(1, "denied"));
        });
        check(!(await denied.EnsureAsync()).Succeeded && !deniedCommands.Any(args => args[0] == "/Run"),
            "Registration denial returns a nonfatal error and cannot run an unregistered task");

        using var http = new HttpClient(new ProbeHandler(request =>
        {
            check(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/rest/v1/rpc/vitan_cloud_health" &&
                request.Headers.GetValues("apikey").Single() == "public-test-key" && request.Headers.Authorization is null && request.Content is null,
                "Cloud health only calls the read-only RPC using a publishable key, not a user session or database payload");
            return new(HttpStatusCode.OK) { Content = new StringContent("true") };
        }));
        check((await CloudHealthProbe.CheckAsync(http, "https://cloud.test", "public-test-key")).Succeeded, "Read-only database health succeeds on a true response");
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.ServiceUnavailable })
        {
            using var failure = new HttpClient(new ProbeHandler(_ => new(status) { Content = new StringContent("sensitive server detail") }));
            var result = await CloudHealthProbe.CheckAsync(failure, "https://cloud.test", "test");
            check(!result.Succeeded && !result.Message.Contains("sensitive"), "Cloud errors never report success or expose server bodies: " + status);
        }
        using var timeout = new HttpClient(new ProbeHandler(_ => throw new TaskCanceledException()));
        check(!(await CloudHealthProbe.CheckAsync(timeout, "https://cloud.test", "test")).Succeeded, "Cloud probe timeout is handled without a false success");
        using var badPayload = new HttpClient(new ProbeHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent("{}") }));
        check(!(await CloudHealthProbe.CheckAsync(badPayload, "https://cloud.test", "test")).Succeeded, "Unexpected successful HTTP payload is not a healthy database result");
        using var offline = new HttpClient(new ProbeHandler(_ => throw new HttpRequestException()));
        check(!(await CloudHealthProbe.CheckAsync(offline, "https://cloud.test", "test")).Succeeded, "Offline background probe fails without changing synchronization state");
        check(await CloudHealthProbe.RunAsync(AppPaths.ForDatabaseFile(Path.Combine(output, "dev-probe.json"))) == 0,
            "Development headless probe exits before accessing the production endpoint");
    }

    private sealed class ProbeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
