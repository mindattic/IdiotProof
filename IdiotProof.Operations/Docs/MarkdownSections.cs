using System.Text;
using System.Text.RegularExpressions;

namespace IdiotProof.Operations.Docs;

/// <summary>One markdown heading and everything under it up to the next heading of the same or a higher level.</summary>
/// <param name="Level">1 for <c>#</c>, 2 for <c>##</c>, …</param>
/// <param name="Title">The heading text without the <c>{#anchor}</c>.</param>
/// <param name="Anchor">The explicit <c>{#anchor}</c> when present, else GitHub's slug of the title.</param>
/// <param name="ExplicitAnchor">True when the heading carried <c>{#...}</c>.</param>
/// <param name="Line">1-based line of the heading.</param>
/// <param name="Body">The text under the heading, subsections included, trimmed.</param>
public sealed record MarkdownSection(int Level, string Title, string Anchor, bool ExplicitAnchor, int Line, string Body);

/// <summary>A user story bullet: <c>- **IP-US-K1 ✅** As a trader, …</c> and its continuation lines.</summary>
public sealed record MarkdownStory(string Id, string Status, string Text, int Line, string? Epic);

/// <summary>A row of the ticket table: <c>| IP-T1 | 2026-10-12 | docs | closed | Summary | Ref |</c>.</summary>
public sealed record MarkdownTicketRow(string Id, string Date, string Type, string Status, string Summary, string Ref, int Line);

/// <summary>
/// The markdown parser behind the knowledge index (IP-T259): headings with their <c>{#IP-§n}</c> /
/// <c>{#IP-LAW-n}</c> anchors and bodies, <c>IP-US-*</c> story bullets and <c>IP-T*</c> ticket-table
/// rows. Front matter and fenced code blocks are skipped when looking for headings. Line numbers are
/// 1-based against the original text so <c>path:line</c> links land on the heading.
/// </summary>
public static partial class MarkdownSections
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\{#([^}\s]+)\}")]
    private static partial Regex AnchorPattern();

    [GeneratedRegex(@"^\s*[-*]\s+\*\*(IP-US-[A-Za-z0-9]+)\s*([^*]*)\*\*\s*(.*)$")]
    private static partial Regex StoryPattern();

    [GeneratedRegex(@"^\|\s*(IP-T\d+)\s*\|([^|]*)\|([^|]*)\|([^|]*)\|(.*)\|([^|]*)\|\s*$")]
    private static partial Regex TicketRowPattern();

    [GeneratedRegex(@"^(\d+(?:\.\d+)*)\.?\s")]
    private static partial Regex SectionNumberPattern();

    /// <summary>Splits text into lines (any line ending), keeping 1-based numbering stable.</summary>
    public static string[] Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>Every heading outside front matter and code fences, in document order.</summary>
    public static IReadOnlyList<MarkdownSection> Parse(string text)
    {
        var lines = Lines(text);
        var heads = new List<(int Index, int Level, string Title, string Anchor, bool Explicit)>();
        var inFence = false;
        var start = FrontMatterEnd(lines);
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;
            var m = HeadingPattern().Match(line);
            if (!m.Success) continue;

            var raw = m.Groups[2].Value;
            var anchorMatch = AnchorPattern().Match(raw);
            var title = AnchorPattern().Replace(raw, "").Trim();
            var anchor = anchorMatch.Success ? anchorMatch.Groups[1].Value : Slug(title);
            heads.Add((i, m.Groups[1].Value.Length, title, anchor, anchorMatch.Success));
        }

        var sections = new List<MarkdownSection>(heads.Count);
        for (var h = 0; h < heads.Count; h++)
        {
            var (index, level, title, anchor, isExplicit) = heads[h];
            var end = lines.Length;
            for (var n = h + 1; n < heads.Count; n++)
            {
                if (heads[n].Level <= level)
                {
                    end = heads[n].Index;
                    break;
                }
            }
            var body = string.Join('\n', lines[(index + 1)..end]).Trim();
            sections.Add(new MarkdownSection(level, title, anchor, isExplicit, index + 1, body));
        }
        return sections;
    }

    /// <summary>
    /// The section number a heading carries in its anchor (<c>IP-§4</c> → <c>4</c>) or title
    /// (<c>4.4 Subsystems</c> → <c>4.4</c>), else null.
    /// </summary>
    public static string? SectionNumber(MarkdownSection section)
    {
        if (section.ExplicitAnchor && section.Anchor.StartsWith("IP-§", StringComparison.Ordinal))
            return section.Anchor["IP-§".Length..];
        var m = SectionNumberPattern().Match(section.Title);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Story bullets, each with the nearest preceding <c>##</c> heading as its epic.</summary>
    public static IReadOnlyList<MarkdownStory> ParseStories(string text)
    {
        var lines = Lines(text);
        var stories = new List<MarkdownStory>();
        string? epic = null;
        var inFence = false;
        for (var i = FrontMatterEnd(lines); i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            if (inFence) continue;
            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                if (heading.Groups[1].Value.Length <= 2) epic = AnchorPattern().Replace(heading.Groups[2].Value, "").Trim();
                continue;
            }
            var m = StoryPattern().Match(line);
            if (!m.Success) continue;

            var text0 = new StringBuilder(m.Groups[3].Value.Trim());
            var j = i + 1;
            for (; j < lines.Length; j++)
            {
                var next = lines[j];
                if (next.Trim().Length == 0 || HeadingPattern().IsMatch(next) || next.StartsWith('|')) break;
                if (StoryPattern().IsMatch(next) || IsTopLevelBullet(next)) break;
                text0.Append(' ').Append(next.Trim());
            }
            stories.Add(new MarkdownStory(m.Groups[1].Value, StatusWord(m.Groups[2].Value), text0.ToString(), i + 1, epic));
            i = j - 1;
        }
        return stories;
    }

    /// <summary>Every <c>| IP-Tn | … |</c> row of the ticket table.</summary>
    public static IReadOnlyList<MarkdownTicketRow> ParseTicketRows(string text)
    {
        var lines = Lines(text);
        var rows = new List<MarkdownTicketRow>();
        for (var i = 0; i < lines.Length; i++)
        {
            var m = TicketRowPattern().Match(lines[i]);
            if (!m.Success) continue;
            rows.Add(new MarkdownTicketRow(
                m.Groups[1].Value,
                m.Groups[2].Value.Trim(),
                m.Groups[3].Value.Trim(),
                m.Groups[4].Value.Trim(),
                m.Groups[5].Value.Trim(),
                m.Groups[6].Value.Trim(),
                i + 1));
        }
        return rows;
    }

    /// <summary>The status glyph of a story as a word: done, partial, planned (or the raw text).</summary>
    public static string StatusWord(string glyph)
    {
        var g = glyph.Trim();
        if (g.Contains('✅')) return "done";
        if (g.Contains("🟡", StringComparison.Ordinal)) return "partial";
        if (g.Contains('⬜')) return "planned";
        return g.Length == 0 ? "unknown" : g;
    }

    /// <summary>GitHub's heading slug: lower case, punctuation dropped, spaces to hyphens.</summary>
    public static string Slug(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var c in title.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_') sb.Append(c);
            else if (c == ' ') sb.Append('-');
        }
        return sb.ToString();
    }

    private static bool IsTopLevelBullet(string line) => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal);

    private static int FrontMatterEnd(string[] lines)
    {
        if (lines.Length == 0 || lines[0].Trim() != "---") return 0;
        for (var i = 1; i < lines.Length; i++)
            if (lines[i].Trim() == "---") return i + 1;
        return 0;
    }
}
