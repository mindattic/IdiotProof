namespace IdiotProof.Operations.Docs;

public sealed partial class KnowledgeIndexBuilder
{
    /// <summary>The generated story and ticket log (<c>tickets db render</c>).</summary>
    public const string StoriesFile = "docs/USER_STORIES.md";

    /// <summary>IP-T261: every <c>IP-US-*</c> story bullet and every row of the ticket table.</summary>
    internal void IndexStories(KnowledgeIndex index)
    {
        if (ReadText(index, StoriesFile) is not { } text) return;

        foreach (var story in MarkdownSections.ParseStories(text))
        {
            index.Add(new KnowledgeEntry(KnowledgeKind.Story, story.Id, Headline(story.Text), story.Text, StoriesFile, story.Line)
            {
                Status = story.Status,
                Parent = story.Epic,
            });
        }

        foreach (var row in MarkdownSections.ParseTicketRows(text))
        {
            var body = $"{row.Summary}\nType: {row.Type} · Opened: {row.Date} · Status: {row.Status}" +
                       (row.Ref is "" or "—" ? "" : $" · Ref: {row.Ref}");
            index.Add(new KnowledgeEntry(KnowledgeKind.Ticket, row.Id, Headline(row.Summary), body, StoriesFile, row.Line)
            {
                Status = row.Status,
            });
        }
    }

    /// <summary>The first sentence (or 100 characters) of a long text, for result lists.</summary>
    internal static string Headline(string text)
    {
        var t = text.Trim();
        var stop = t.IndexOf(". ", StringComparison.Ordinal);
        if (stop is > 0 and < 100) return t[..(stop + 1)];
        return t.Length <= 100 ? t : t[..99].TrimEnd() + "…";
    }
}
