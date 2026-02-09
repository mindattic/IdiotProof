// ============================================================================
// AsciiTableAlignmentHelper - Properly aligned ASCII table output
// ============================================================================
//
// PURPOSE:
// Creates properly aligned ASCII tables where:
// - Column widths are based on the longest string in each column
// - Numeric/cash values are right-aligned
// - Headers and data cells align properly
// - Uses ASCII-only characters (per project guidelines)
//
// ALLOWED CHARACTERS (from ConsoleChars):
//   Line chars:  - = . ~
//   Box chars:   + | [ ] ( ) { } < >
//   Symbols:     * o X ! ? # ^ v
//   Standard:    All printable ASCII (32-126)
//
// USAGE:
//   var table = new AsciiTable();
//   table.AddColumn("#", 3, ColumnAlign.Right);
//   table.AddColumn("Symbol", 8, ColumnAlign.Left);
//   table.AddColumn("Price", 10, ColumnAlign.Right);
//   
//   table.AddRow("1", "NVDA", "1,234.56");
//   table.AddRow("2", "AAPL", "185.00");
//   
//   table.Print();
//
// NOTE: This is the canonical ASCII table solution. The deprecated
//       ConsoleChars.Table class uses Unicode box-drawing characters
//       which violates the "ASCII-Only Console Output" guideline.
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IdiotProof.Helpers;

/// <summary>
/// Allowed ASCII characters for table drawing.
/// These characters are guaranteed to render correctly in Windows Console.
/// </summary>
public static class TableChars
{
    // Line characters for headers and separators
    public const char Dash = '-';        // Standard underline
    public const char Equals = '=';      // Thick underline/title separator
    public const char Dot = '.';         // Dotted line
    public const char Tilde = '~';       // Wave line
    public const char Hash = '#';        // Heavy line
    public const char Underscore = '_';  // Bottom underline
    
    // Column separators
    public const char Pipe = '|';        // Vertical separator
    public const char Space = ' ';       // Space separator
    public const char Colon = ':';       // Colon separator
    
    // Box corners and junctions (all map to +)
    public const char Corner = '+';      // All corners
    public const char Cross = '+';       // Cross junction
}

/// <summary>
/// Column alignment options.
/// </summary>
public enum ColumnAlign
{
    Left,
    Right,
    Center
}

/// <summary>
/// Represents a column definition.
/// </summary>
public sealed class TableColumn
{
    public string Header { get; set; } = "";
    public int MinWidth { get; set; }
    public int ActualWidth { get; set; }
    public ColumnAlign Alignment { get; set; } = ColumnAlign.Left;
}

/// <summary>
/// Helper class for creating properly aligned ASCII tables.
/// This is the canonical ASCII table solution for the project.
/// </summary>
/// <remarks>
/// Uses only allowed ASCII characters from <see cref="TableChars"/>.
/// All characters are guaranteed to render correctly in Windows Console.
/// </remarks>
public sealed class AsciiTable
{
    private readonly List<TableColumn> _columns = [];
    private readonly List<string[]> _rows = [];
    private string? _timestampPrefix;
    private string _columnSeparator = "  "; // Default: 2 spaces between columns
    private char _headerUnderlineChar = TableChars.Dash; // Default: '-'

    /// <summary>
    /// Sets a timestamp prefix for all output lines.
    /// </summary>
    public AsciiTable WithTimestamp(string? prefix = null)
    {
        _timestampPrefix = prefix ?? DateTime.Now.ToString("[HH:mm:ss]  ");
        return this;
    }

    /// <summary>
    /// Sets the column separator (default: 2 spaces).
    /// </summary>
    /// <param name="separator">Use spaces, <see cref="TableChars.Pipe"/>, or <see cref="TableChars.Colon"/>.</param>
    public AsciiTable WithColumnSeparator(string separator)
    {
        _columnSeparator = separator;
        return this;
    }

    /// <summary>
    /// Sets the header underline character.
    /// </summary>
    /// <param name="ch">Use <see cref="TableChars"/> constants: Dash, Equals, Dot, Tilde, Hash, Underscore.</param>
    public AsciiTable WithHeaderUnderline(char ch)
    {
        _headerUnderlineChar = ch;
        return this;
    }

    /// <summary>
    /// Adds a column definition.
    /// </summary>
    /// <param name="header">Column header text.</param>
    /// <param name="minWidth">Minimum column width (0 = auto).</param>
    /// <param name="alignment">Text alignment within column.</param>
    public AsciiTable AddColumn(string header, int minWidth = 0, ColumnAlign alignment = ColumnAlign.Left)
    {
        _columns.Add(new TableColumn
        {
            Header = header,
            MinWidth = minWidth,
            ActualWidth = Math.Max(minWidth, header.Length),
            Alignment = alignment
        });
        return this;
    }

    /// <summary>
    /// Adds a data row.
    /// </summary>
    public AsciiTable AddRow(params string[] values)
    {
        // Pad with empty strings if not enough values
        var row = new string[_columns.Count];
        for (int i = 0; i < _columns.Count; i++)
        {
            row[i] = i < values.Length ? (values[i] ?? "") : "";
            
            // Update actual width if this value is longer
            if (row[i].Length > _columns[i].ActualWidth)
            {
                _columns[i].ActualWidth = row[i].Length;
            }
        }
        _rows.Add(row);
        return this;
    }

    /// <summary>
    /// Formats a cell value with proper alignment.
    /// </summary>
    private string FormatCell(string value, TableColumn column)
    {
        var width = column.ActualWidth;
        
        return column.Alignment switch
        {
            ColumnAlign.Right => value.PadLeft(width),
            ColumnAlign.Center => value.PadLeft((width + value.Length) / 2).PadRight(width),
            _ => value.PadRight(width)
        };
    }

    /// <summary>
    /// Builds the header line.
    /// </summary>
    public string BuildHeaderLine()
    {
        var sb = new StringBuilder();
        sb.Append(_timestampPrefix ?? "");
        
        for (int i = 0; i < _columns.Count; i++)
        {
            if (i > 0) sb.Append(_columnSeparator);
            sb.Append(FormatCell(_columns[i].Header, _columns[i]));
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Builds the separator line (dashes under each header).
    /// </summary>
    public string BuildSeparatorLine()
    {
        var sb = new StringBuilder();
        sb.Append(_timestampPrefix ?? "");
        
        for (int i = 0; i < _columns.Count; i++)
        {
            if (i > 0) sb.Append(_columnSeparator);
            sb.Append(new string(_headerUnderlineChar, _columns[i].ActualWidth));
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Builds a data row line.
    /// </summary>
    public string BuildRowLine(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Count)
            return "";

        var row = _rows[rowIndex];
        var sb = new StringBuilder();
        sb.Append(_timestampPrefix ?? "");
        
        for (int i = 0; i < _columns.Count; i++)
        {
            if (i > 0) sb.Append(_columnSeparator);
            sb.Append(FormatCell(row[i], _columns[i]));
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Builds a footer separator line.
    /// </summary>
    public string BuildFooterSeparator()
    {
        var sb = new StringBuilder();
        sb.Append(_timestampPrefix ?? "");
        sb.Append(new string('-', TotalWidth));
        return sb.ToString();
    }

    /// <summary>
    /// Builds a footer line with a label and value.
    /// </summary>
    public string BuildFooterLine(string label, string value)
    {
        var sb = new StringBuilder();
        sb.Append(_timestampPrefix ?? "");
        sb.Append(label);
        sb.Append(value);
        return sb.ToString();
    }

    /// <summary>
    /// Prints the entire table to console.
    /// </summary>
    public void Print()
    {
        Console.WriteLine(BuildHeaderLine());
        Console.WriteLine(BuildSeparatorLine());
        
        for (int i = 0; i < _rows.Count; i++)
        {
            Console.WriteLine(BuildRowLine(i));
        }
    }

    /// <summary>
    /// Prints the table with a footer.
    /// </summary>
    public void Print(string footerLabel, string footerValue)
    {
        Print();
        Console.WriteLine(BuildFooterSeparator());
        Console.WriteLine(BuildFooterLine(footerLabel, footerValue));
    }

    /// <summary>
    /// Prints the table with a centered title above it.
    /// </summary>
    public void PrintWithTitle(string title)
    {
        var centered = CenterText(title, TotalWidth);
        Console.WriteLine((_timestampPrefix ?? "") + centered);
        Console.WriteLine((_timestampPrefix ?? "") + new string('=', TotalWidth));
        Print();
    }

    /// <summary>
    /// Centers text within a given width.
    /// </summary>
    private static string CenterText(string text, int width)
    {
        if (text.Length >= width) return text;
        var padding = (width - text.Length) / 2;
        return text.PadLeft(padding + text.Length).PadRight(width);
    }

    /// <summary>
    /// Gets all lines as a list.
    /// </summary>
    public List<string> ToLines()
    {
        var lines = new List<string>
        {
            BuildHeaderLine(),
            BuildSeparatorLine()
        };
        
        for (int i = 0; i < _rows.Count; i++)
        {
            lines.Add(BuildRowLine(i));
        }
        
        return lines;
    }

    /// <summary>
    /// Gets the total table width (columns + separators).
    /// </summary>
    public int TotalWidth => _columns.Count == 0 
        ? 0 
        : _columns.Sum(c => c.ActualWidth) + (_columns.Count - 1) * _columnSeparator.Length;

    /// <summary>
    /// Gets the row count.
    /// </summary>
    public int RowCount => _rows.Count;
}
