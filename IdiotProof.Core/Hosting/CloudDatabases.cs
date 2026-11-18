namespace IdiotProof.Core.Hosting;

/// <summary>
/// The Azure SQL databases IdiotProof lives in, and the connection strings used when nothing else is configured
/// (IP-T545). There is no local SQL Server fallback any more: with no environment variable and no configuration
/// entry, every host, the design-time factory and every research run lands on these, so nothing is ever saved to a
/// local instance by accident.
/// <para>
/// The strings carry no secret. <c>Authentication=Active Directory Default</c> signs in as whoever is signed in on
/// the machine (<c>az login</c>, Visual Studio, a managed identity), and the server accepts Entra logins only.
/// Unattended processes (the Hub service, scheduled jobs) get service-principal strings through the environment
/// variables <c>ConnectionStrings__IdiotProof</c>, <c>ConnectionStrings__IdiotProofHistory</c> and
/// <c>ConnectionStrings__IdiotProofResearch</c>, which win over these.
/// </para>
/// </summary>
public static class CloudDatabases
{
    public const string Server = "sql-mindattic-f65744.database.windows.net";

    /// <summary>The trading database: strategies, orders, the diary, the leader lease.</summary>
    public const string MainName = "IdiotProof";

    /// <summary>The permanent market history (minute bars, daily bars, news, filings).</summary>
    public const string HistoryName = "IdiotProof_History";

    /// <summary>Every research study's database starts with this (<c>IdiotProof_Research_GAP</c>, <c>_T375</c>, ...).</summary>
    public const string ResearchPrefix = "IdiotProof_Research_";

    /// <summary>The database the research template names; a run replaces it with its study's own.</summary>
    public const string ResearchTemplateName = "IdiotProof_Research";

    private const string Head = "Server=tcp:" + Server + ",1433;Database=";
    private const string Tail = ";Authentication=Active Directory Default;Encrypt=True;Connect Timeout=60;MultipleActiveResultSets=True;";

    /// <summary>Last resort of <see cref="CoreServiceRegistration.ResolveConnectionString"/> and of <c>dotnet ef</c>.</summary>
    public const string Main = Head + MainName + Tail;

    /// <summary>Last resort of <see cref="IdiotProof.Core.History.HistoryDatabase.FromEnvironment"/>.</summary>
    public const string History = Head + HistoryName + Tail + "Command Timeout=600;";

    /// <summary>Last resort of <see cref="ResearchDatabase.Template"/>.</summary>
    public const string ResearchTemplate = Head + ResearchTemplateName + Tail + "Command Timeout=600;";

    /// <summary>
    /// True for a database name that holds real data: the trading database, the history database, the research
    /// template name and any research study's database. Test helpers refuse these whatever server they are on.
    /// </summary>
    public static bool IsRealDatabaseName(string? name)
    {
        var n = name?.Trim() ?? "";
        return n.Equals(MainName, StringComparison.OrdinalIgnoreCase)
            || n.Equals(HistoryName, StringComparison.OrdinalIgnoreCase)
            || n.Equals(ResearchTemplateName, StringComparison.OrdinalIgnoreCase)
            || n.StartsWith(ResearchPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
