using Microsoft.Extensions.Logging;

namespace IdiotProof.Hub;

/// <summary>
/// The Hub window's logger (IP-T578): the same single-line shape the simple console logger wrote
/// (<c>warn: Category[0] message</c>, exception below), but through <see cref="HubConsole"/>, so a
/// warning, error or critical line stands out in color on screen while the log file stays plain
/// text, and nothing is drawn while the window is hidden (IP-T577). Errors and critical entries are
/// also handed to <see cref="HubErrorTickets"/> (IP-T579).
/// </summary>
[ProviderAlias("HubConsole")]
public sealed class HubConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new HubConsoleLogger(categoryName);

    public void Dispose() { }

    private sealed class HubConsoleLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            var text = $"{Label(logLevel)}: {category}[{eventId.Id}] {message.ReplaceLineEndings(" ")}";
            if (exception is not null) text += Environment.NewLine + exception;
            HubConsole.Line(text, logLevel);

            if (logLevel >= LogLevel.Error && category != HubErrorTickets.Category)
                HubErrorTickets.Report(new HubError(logLevel, category, eventId, message, exception, OriginalTemplate(state)));
        }

        private static string Label(LogLevel level) => level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            _ => "crit",
        };

        /// <summary>The message template ("{Title} failed for {Symbol}") when the state carries one: the stable part of a log line.</summary>
        private static string? OriginalTemplate<TState>(TState state) =>
            state is IReadOnlyList<KeyValuePair<string, object?>> pairs
                ? pairs.FirstOrDefault(p => p.Key == "{OriginalFormat}").Value as string
                : null;
    }
}
