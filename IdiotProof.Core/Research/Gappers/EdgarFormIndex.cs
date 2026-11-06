using System.Globalization;
using System.Text.RegularExpressions;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>One line of EDGAR's quarterly <c>form.idx</c>.</summary>
public sealed record EdgarFormIndexEntry(string FormType, string CompanyName, string Cik, DateOnly FiledDate, string FileName)
{
    /// <summary>"0001234567-24-000012" from "edgar/data/1234567/0001234567-24-000012.txt".</summary>
    public string AccessionNumber => Path.GetFileNameWithoutExtension(FileName);
}

/// <summary>
/// Parses <c>https://www.sec.gov/Archives/edgar/full-index/{YYYY}/QTR{q}/form.idx</c>: a text
/// preamble, a dashed rule, then rows of Form Type, Company Name, CIK, Date Filed, File Name. The
/// header's column edges do not match the body's (the body pads the form type wider), so a row is
/// read from the right: file name, date and CIK are the last three tokens and the form type ends
/// at the first run of two or more spaces. Streams line by line; a 50 MB quarter never sits in
/// memory at once.
/// </summary>
public static partial class EdgarFormIndex
{
    [GeneratedRegex(@"^(?<form>\S+(?: \S+)*?)\s{2,}(?<company>.*?)\s{2,}(?<cik>\d+)\s+(?<date>\d{4}-\d{2}-\d{2})\s+(?<file>\S+)\s*$")]
    private static partial Regex RowPattern();

    public static string Url(int year, int quarter) => $"https://www.sec.gov/Archives/edgar/full-index/{year}/QTR{quarter}/form.idx";

    /// <summary>(year, quarter) for every calendar quarter touching [from, to], ascending.</summary>
    public static List<(int Year, int Quarter)> Quarters(DateOnly from, DateOnly to)
    {
        var result = new List<(int, int)>();
        for (var d = new DateOnly(from.Year, (from.Month - 1) / 3 * 3 + 1, 1); d <= to; d = d.AddMonths(3))
            result.Add((d.Year, (d.Month - 1) / 3 + 1));
        return result;
    }

    public static IEnumerable<EdgarFormIndexEntry> Parse(TextReader reader, ISet<string>? forms = null)
    {
        string? line;
        bool sawHeader = false, inBody = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!inBody)
            {
                if (line.StartsWith("Form Type", StringComparison.Ordinal)) sawHeader = true;
                else if (sawHeader && line.StartsWith("---", StringComparison.Ordinal)) inBody = true;
                continue;
            }
            var m = RowPattern().Match(line);
            if (!m.Success) continue;
            var form = m.Groups["form"].Value;
            if (forms is not null && !forms.Contains(form)) continue;
            if (!DateOnly.TryParseExact(m.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var filed)) continue;
            yield return new EdgarFormIndexEntry(form, m.Groups["company"].Value.Trim(), m.Groups["cik"].Value, filed, m.Groups["file"].Value);
        }
    }
}
