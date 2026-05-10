using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// One page in the Learning Center — the encyclopedia / atlas of IdiotScript.
/// Articles are seeded into SQL on first startup from a fixed catalog
/// (LearningContentSeeder); admins can later edit them in-place from the UI.
/// Body text is Markdown-ish prose containing inline [[script]] wikilinks that
/// render as live strategy flow-charts via WikiContent.
/// </summary>
public sealed class LearningArticle
{
    [Key, MaxLength(128)]
    public string Slug { get; set; } = "";

    [Required, MaxLength(64)]
    public string Category { get; set; } = "";

    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(280)]
    public string? Summary { get; set; }

    /// <summary>
    /// Full Markdown body. May embed live IdiotScript via [[Stock.Ticker(...)]]
    /// wikilinks — WikiContent component renders these as visual flow-charts.
    /// </summary>
    [Required]
    public string BodyMarkdown { get; set; } = "";

    /// <summary>Sort order within Category. Lower = earlier in the sidebar.</summary>
    public int Order { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
