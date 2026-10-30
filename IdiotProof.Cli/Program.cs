using System.Text;
using IdiotProof.Cli;

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

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // First Ctrl+C stops waiting (cleanly, with a message); a second one kills the process.
    if (cts.IsCancellationRequested) return;
    e.Cancel = true;
    cts.Cancel();
};

return await new CliApp(CliEnvironment.FromConsole()).RunAsync(args, cts.Token);
