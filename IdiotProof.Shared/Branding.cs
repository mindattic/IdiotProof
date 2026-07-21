namespace IdiotProof.Shared;

/// <summary>
/// Shared brand assets (ASCII banner, etc.) so any host — Monitor console,
/// tooling — prints the same identity. Kept in IdiotProof.Shared so there's a
/// single source of truth rather than a copy per entry point.
/// </summary>
public static class Branding
{
    /// <summary>
    /// The IdiotProof ASCII wordmark, printed by the Monitor on startup.
    /// Raw string literal — the '\' and '$' glyphs are stored verbatim.
    /// </summary>
    public const string AsciiBanner = """
 /$$$$$$       /$$ /$$             /$$     /$$$$$$$                                /$$$$$$
|_  $$_/      | $$|__/            | $$    | $$__  $$                              /$$__  $$
  | $$    /$$$$$$$ /$$  /$$$$$$  /$$$$$$  | $$  \ $$ /$$$$$$   /$$$$$$   /$$$$$$ | $$  \__/
  | $$   /$$__  $$| $$ /$$__  $$|_  $$_/  | $$$$$$$//$$__  $$ /$$__  $$ /$$__  $$| $$$$
  | $$  | $$  | $$| $$| $$  \ $$  | $$    | $$____/| $$  \__/| $$  \ $$| $$  \ $$| $$_/
  | $$  | $$  | $$| $$| $$  | $$  | $$ /$$| $$     | $$      | $$  | $$| $$  | $$| $$
 /$$$$$$|  $$$$$$$| $$|  $$$$$$/  |  $$$$/| $$     | $$      |  $$$$$$/|  $$$$$$/| $$
|______/ \_______/|__/ \______/    \___/  |__/     |__/       \______/  \______/ |__/
""";
}
