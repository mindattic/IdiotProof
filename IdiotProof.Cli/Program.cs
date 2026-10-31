using System.Text;
using IdiotProof.Cli;
using IdiotProof.Core.Hosting;

// ── idiotproof ───────────────────────────────────────────────────────────────
//
// The command-line front door to IdiotProof (docs/agent/IDIOTPROOF_PROTOCOL.md):
//
//   idiotproof <noun> <verb> [arguments] [--options] [--json] [--user <guid>] [--confirm <code>]
//
// Help, version, `agent bootstrap`, `hub status|start|stop|logs|version` and `users use|whoami`
// run here; everything else is POSTed to the Hub (127.0.0.1:5910, IDIOTPROOF_HUB_URL to override).
// Exit codes: 0 ok, 1 failed / Hub not running, 2 usage, 3 blocked by a gate, 4 confirmation required.
// Any argument value "-" is read from stdin. Times are Eastern (ET).

try
{
    var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    Console.OutputEncoding = utf8;
    Console.InputEncoding = utf8;
}
catch (IOException) { /* no console (a service, a test host) */ }

// Docs: idiotproof --export-commands [docs/CLI_COMMANDS.md] (from the catalog built into this CLI).
if (args.Length > 0 && args[0] == "--export-commands")
{
    var outPath = args.Length > 1 ? args[1] : Path.Combine("docs", "CLI_COMMANDS.md");
    var catalog = IdiotProof.Operations.OperationCatalog.FromAssemblies(typeof(IdiotProof.Operations.OperationCatalog).Assembly);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    File.WriteAllText(outPath, CommandDocs.Render(catalog));
    Console.Error.WriteLine($"[export-commands] wrote {catalog.Operations.Count} commands to {Path.GetFullPath(outPath)}");
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // First Ctrl+C stops waiting (cleanly, with a message); a second one kills the process.
    if (cts.IsCancellationRequested) return;
    e.Cancel = true;
    cts.Cancel();
};

// Errors only (IP-T23): CLI-side failures and crashes go to MindAttic.Log at
// %APPDATA%\MindAttic\Logging\IdiotProof.Cli\MindAttic.Log.db, opened only when one happens.
// The Hub already logs every command it runs; arguments are never logged here.
IdiotProofLog.Start(IdiotProofLog.Cli);
try
{
    return await new CliApp(CliEnvironment.FromConsole()).RunAsync(args, cts.Token);
}
catch (Exception ex)
{
    // Logged here (with the command's name) rather than by the unhandled-exception hook.
    IdiotProofLog.Write(Serilog.Events.LogEventLevel.Fatal, ex, "Cli", "idiotproof {Command:l} crashed", CliApp.CommandName(args));
    Console.Error.WriteLine(ex);
    return CliExitCodes.Failed;
}
finally
{
    IdiotProofLog.Stop();
}
