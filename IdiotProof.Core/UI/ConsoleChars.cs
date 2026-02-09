// ============================================================================
// ConsoleChars - ASCII-safe character collection for console rendering
// ============================================================================
//
// Windows Console (CMD, PowerShell) has limited Unicode support.
// This class provides a consistent set of ASCII characters that render
// reliably across all Windows console environments.
//
// USAGE:
//   using static IdiotProof.UI.ConsoleChars;
//   Console.WriteLine(Box.TopLeft + Line.Horizontal(40) + Box.TopRight);
//   Console.WriteLine($"Status: {Symbol.Success} OK");
//
// ============================================================================

namespace IdiotProof.UI;

/// <summary>
/// Collection of ASCII-safe characters for console rendering.
/// All characters are guaranteed to display correctly in Windows CMD and PowerShell.
/// </summary>
public static class ConsoleChars
{
    // ========================================================================
    // BOX DRAWING CHARACTERS
    // ========================================================================

    /// <summary>
    /// Box drawing characters for creating bordered UI elements.
    /// </summary>
    public static class Box
    {
        // Corners
        public const char TopLeft = '+';
        public const char TopRight = '+';
        public const char BottomLeft = '+';
        public const char BottomRight = '+';

        // Edges
        public const char Horizontal = '-';
        public const char Vertical = '|';

        // T-junctions
        public const char TeeDown = '+';      // T pointing down
        public const char TeeUp = '+';        // T pointing up
        public const char TeeRight = '+';     // T pointing right
        public const char TeeLeft = '+';      // T pointing left

        // Cross
        public const char Cross = '+';

        // Double-line (same as single in ASCII)
        public const char DoubleHorizontal = '=';
        public const char DoubleVertical = '|';
        public const char DoubleTopLeft = '+';
        public const char DoubleTopRight = '+';
        public const char DoubleBottomLeft = '+';
        public const char DoubleBottomRight = '+';
    }

    // ========================================================================
    // LINE CHARACTERS AND GENERATORS
    // ========================================================================

    /// <summary>
    /// Line drawing utilities.
    /// </summary>
    public static class Line
    {
        public const char Thin = '-';
        public const char Thick = '=';
        public const char Dotted = '.';
        public const char Dashed = '-';
        public const char Wave = '~';

        /// <summary>Creates a horizontal line of specified length.</summary>
        public static string Horizontal(int length) => new('-', length);

        /// <summary>Creates a thick horizontal line of specified length.</summary>
        public static string HorizontalThick(int length) => new('=', length);

        /// <summary>Creates a dotted line of specified length.</summary>
        public static string HorizontalDotted(int length) => new('.', length);

        /// <summary>Creates a dashed line (alternating).</summary>
        public static string HorizontalDashed(int length)
        {
            var pattern = "- ";
            var sb = new System.Text.StringBuilder();
            while (sb.Length < length) sb.Append(pattern);
            return sb.ToString().Substring(0, length);
        }
    }

    // ========================================================================
    // STATUS SYMBOLS
    // ========================================================================

    /// <summary>
    /// Status indicator symbols for messages and UI.
    /// </summary>
    public static class Symbol
    {
        // Status indicators
        public const char Success = '*';      // Was: ✓
        public const char Enabled = '*';      // Enabled checkbox
        public const char Disabled = 'o';     // Disabled checkbox
        public const char Error = 'X';        // Was: ✗
        public const char Warning = '!';      // Was: ⚠
        public const char Info = 'i';         // Information marker
        public const char Question = '?';     // Question marker
        public const char Pending = '~';      // In-progress/pending

        // Bullets and list markers
        public const char Bullet = '*';       // Bullet point
        public const char BulletHollow = 'o'; // Hollow bullet
        public const char Arrow = '>';        // Arrow marker
        public const char ArrowDouble = '>'; // Double arrow ">>"

        // Trading-specific
        public const char Long = '^';         // Long/up position
        public const char Short = 'v';        // Short/down position
        public const char Buy = '+';          // Buy indicator
        public const char Sell = '-';         // Sell indicator
        public const char Profit = '+';       // Profit indicator
        public const char Loss = '-';         // Loss indicator

        // Progress
        public const char ProgressFilled = '#';
        public const char ProgressEmpty = '.';
        public const char ProgressPartial = '=';
    }

    // ========================================================================
    // BRACKET AND DELIMITER CHARACTERS
    // ========================================================================

    /// <summary>
    /// Bracket and delimiter characters for text formatting.
    /// </summary>
    public static class Bracket
    {
        // Square brackets
        public const char SquareLeft = '[';
        public const char SquareRight = ']';

        // Parentheses
        public const char ParenLeft = '(';
        public const char ParenRight = ')';

        // Curly braces
        public const char CurlyLeft = '{';
        public const char CurlyRight = '}';

        // Angle brackets
        public const char AngleLeft = '<';
        public const char AngleRight = '>';

        // Common formats
        public static string Square(string text) => $"[{text}]";
        public static string Paren(string text) => $"({text})";
        public static string Curly(string text) => $"{{{text}}}";
        public static string Angle(string text) => $"<{text}>";
    }

    // ========================================================================
    // ARROW CHARACTERS
    // ========================================================================

    /// <summary>
    /// Arrow characters for directional indicators.
    /// </summary>
    public static class Arrow
    {
        public const char Right = '>';
        public const char Left = '<';
        public const char Up = '^';
        public const char Down = 'v';

        public const string DoubleRight = ">>";
        public const string DoubleLeft = "<<";

        public const string LongRight = "-->";
        public const string LongLeft = "<--";
        public const string BidiArrow = "<->";

        public const string FlowRight = "=>";
        public const string FlowLeft = "<=";
    }

    // ========================================================================
    // SEPARATORS AND DIVIDERS
    // ========================================================================

    /// <summary>
    /// Separator patterns for visual division.
    /// </summary>
    public static class Separator
    {
        public const string Pipe = " | ";
        public const string Colon = ": ";
        public const string Dash = " - ";
        public const string Comma = ", ";
        public const string Dot = " . ";
        public const string Slash = " / ";

        /// <summary>Creates a full-width separator line.</summary>
        public static string FullLine(int width = 60, char ch = '-') => new(ch, width);
    }

    // ========================================================================
    // STATUS BADGES (formatted strings)
    // ========================================================================

    /// <summary>
    /// Pre-formatted status badges for common states.
    /// </summary>
    public static class Badge
    {
        public const string OK = "[OK]";
        public const string ERR = "[ERR]";
        public const string WARN = "[WARN]";
        public const string INFO = "[INFO]";
        public const string PASS = "[PASS]";
        public const string FAIL = "[FAIL]";
        public const string SKIP = "[SKIP]";
        public const string TODO = "[TODO]";
        public const string DONE = "[DONE]";
        public const string WAIT = "[WAIT]";
        public const string RUN = "[RUN]";
        public const string STOP = "[STOP]";

        // Trading badges
        public const string LONG = "[LONG]";
        public const string SHORT = "[SHORT]";
        public const string BUY = "[BUY]";
        public const string SELL = "[SELL]";
        public const string HOLD = "[HOLD]";
        public const string EXIT = "[EXIT]";

        // Connection badges
        public const string CONNECTED = "[CONNECTED]";
        public const string DISCONNECTED = "[DISCONNECTED]";
        public const string PAPER = "[PAPER]";
        public const string LIVE = "[LIVE]";
    }

    // ========================================================================
    // BOX BUILDER HELPER
    // ========================================================================

    /// <summary>
    /// Helper methods for building ASCII boxes and frames.
    /// </summary>
    public static class BoxBuilder
    {
        /// <summary>
        /// Creates a horizontal border line with corners.
        /// </summary>
        /// <param name="width">Total width including corners</param>
        /// <param name="isTop">True for top border, false for bottom</param>
        public static string Border(int width, bool isTop = true)
        {
            if (width < 2) return string.Empty;
            char left = isTop ? Box.TopLeft : Box.BottomLeft;
            char right = isTop ? Box.TopRight : Box.BottomRight;
            return $"{left}{new string(Box.Horizontal, width - 2)}{right}";
        }

        /// <summary>
        /// Creates a thick horizontal border line with corners.
        /// </summary>
        public static string BorderThick(int width, bool isTop = true)
        {
            if (width < 2) return string.Empty;
            char left = isTop ? Box.DoubleTopLeft : Box.DoubleBottomLeft;
            char right = isTop ? Box.DoubleTopRight : Box.DoubleBottomRight;
            return $"{left}{new string(Box.DoubleHorizontal, width - 2)}{right}";
        }

        /// <summary>
        /// Wraps text in a boxed line with side borders.
        /// </summary>
        /// <param name="text">The text content</param>
        /// <param name="width">Total width including borders</param>
        /// <param name="align">Alignment: 'L'eft, 'C'enter, 'R'ight</param>
        public static string ContentLine(string text, int width, char align = 'L')
        {
            if (width < 4) return text;

            int contentWidth = width - 4; // 2 chars for borders + 2 for padding
            string content = text.Length > contentWidth
                ? text.Substring(0, contentWidth - 3) + "..."
                : text;

            string padded = align switch
            {
                'C' => content.PadLeft((contentWidth + content.Length) / 2).PadRight(contentWidth),
                'R' => content.PadLeft(contentWidth),
                _ => content.PadRight(contentWidth)
            };

            return $"{Box.Vertical} {padded} {Box.Vertical}";
        }

        /// <summary>
        /// Creates a simple ASCII box around text lines.
        /// </summary>
        public static string[] CreateBox(string[] lines, int? width = null)
        {
            int maxLen = lines.Max(l => l.Length);
            int boxWidth = width ?? maxLen + 4;

            var result = new List<string>
            {
                Border(boxWidth, true)
            };

            foreach (var line in lines)
            {
                result.Add(ContentLine(line, boxWidth));
            }

            result.Add(Border(boxWidth, false));
            return result.ToArray();
        }

        /// <summary>
        /// Creates a box with a title header.
        /// </summary>
        public static string[] CreateTitledBox(string title, string[] lines, int? width = null)
        {
            int maxLen = Math.Max(title.Length, lines.Max(l => l.Length));
            int boxWidth = width ?? maxLen + 4;

            var result = new List<string>
            {
                Border(boxWidth, true),
                ContentLine(title, boxWidth, 'C'),
                $"{Box.TeeRight}{new string(Box.Horizontal, boxWidth - 2)}{Box.TeeLeft}"
            };

            foreach (var line in lines)
            {
                result.Add(ContentLine(line, boxWidth));
            }

            result.Add(Border(boxWidth, false));
            return result.ToArray();
        }
    }

    // ========================================================================
    // PROGRESS BAR BUILDER
    // ========================================================================

    /// <summary>
    /// Progress bar utilities.
    /// </summary>
    public static class Progress
    {
        /// <summary>
        /// Creates a progress bar string.
        /// </summary>
        /// <param name="percent">Completion percentage (0-100)</param>
        /// <param name="width">Total bar width including brackets</param>
        /// <param name="showPercent">Whether to append percentage</param>
        public static string Bar(double percent, int width = 20, bool showPercent = true)
        {
            percent = Math.Clamp(percent, 0, 100);
            int innerWidth = width - 2; // For brackets
            int filled = (int)(innerWidth * percent / 100);
            int empty = innerWidth - filled;

            string bar = $"[{new string(Symbol.ProgressFilled, filled)}{new string(Symbol.ProgressEmpty, empty)}]";

            if (showPercent)
                bar += $" {percent:F0}%";

            return bar;
        }

        /// <summary>
        /// Creates a spinner frame for animation.
        /// </summary>
        public static char Spinner(int frame)
        {
            char[] frames = { '|', '/', '-', '\\' };
            return frames[frame % frames.Length];
        }
    }

    // ========================================================================
    // TITLE FORMATTERS
    // ========================================================================

    /// <summary>
    /// Title and header formatting utilities.
    /// </summary>
    public static class Title
    {
        /// <summary>
        /// Creates a centered title with decorations.
        /// Example: "=== My Title ==="
        /// </summary>
        public static string Centered(string text, int width = 60, char decorator = '=')
        {
            int textLen = text.Length + 2; // Add spacing
            int sideLen = (width - textLen) / 2;
            string side = new(decorator, Math.Max(1, sideLen));
            return $"{side} {text} {side}";
        }

        /// <summary>
        /// Creates a section header.
        /// Example: "--- Section Name ---"
        /// </summary>
        public static string Section(string text, int width = 40, char decorator = '-')
        {
            return Centered(text, width, decorator);
        }

        /// <summary>
        /// Creates a boxed title line.
        /// Example: "=== [ TITLE ] ==="
        /// </summary>
        public static string Boxed(string text, int width = 60)
        {
            string content = $"[ {text} ]";
            return Centered(content, width, '=');
        }

        /// <summary>
        /// Creates a banner with multiple decoration lines.
        /// </summary>
        public static string[] Banner(string text, int width = 60, char decorator = '=')
        {
            string line = new(decorator, width);
            return new[]
            {
                line,
                Centered(text, width, decorator),
                line
            };
        }
    }

    // ========================================================================
    // TABLE BUILDER
    // ========================================================================

    /// <summary>
    /// Box-drawing table builder (uses Unicode characters).
    /// </summary>
    /// <remarks>
    /// DEPRECATED: Use <see cref="IdiotProof.Helpers.AsciiTable"/> instead.
    /// This class uses Unicode box-drawing characters which violates the
    /// "ASCII-Only Console Output" project guideline. Kept for backwards
    /// compatibility but new code should use AsciiTable.
    /// </remarks>
    [Obsolete("Use IdiotProof.Helpers.AsciiTable instead (ASCII-only output).")]
    public static class Table
    {
        /// <summary>
        /// Creates a table row separator.
        /// </summary>
        public static string RowSeparator(int[] columnWidths)
        {
            var parts = columnWidths.Select(w => new string(Box.Horizontal, w + 2));
            return Box.TeeRight + string.Join(Box.Cross.ToString(), parts) + Box.TeeLeft;
        }

        /// <summary>
        /// Creates a table row with cell content.
        /// </summary>
        public static string Row(string[] cells, int[] columnWidths)
        {
            var paddedCells = new List<string>();
            for (int i = 0; i < cells.Length && i < columnWidths.Length; i++)
            {
                string cell = cells[i] ?? "";
                if (cell.Length > columnWidths[i])
                    cell = cell.Substring(0, columnWidths[i] - 2) + "..";
                paddedCells.Add($" {cell.PadRight(columnWidths[i])} ");
            }
            return Box.Vertical + string.Join(Box.Vertical.ToString(), paddedCells) + Box.Vertical;
        }

        /// <summary>
        /// Creates a simple table from data.
        /// </summary>
        public static string[] Create(string[] headers, string[][] rows)
        {
            // Calculate column widths
            int[] widths = headers.Select(h => h.Length).ToArray();
            foreach (var row in rows)
            {
                for (int i = 0; i < row.Length && i < widths.Length; i++)
                {
                    widths[i] = Math.Max(widths[i], (row[i] ?? "").Length);
                }
            }

            var result = new List<string>();
            int totalWidth = widths.Sum() + (widths.Length * 3) + 1;

            // Top border
            result.Add(BoxBuilder.Border(totalWidth, true));

            // Header row
            result.Add(Row(headers, widths));
            result.Add(RowSeparator(widths));

            // Data rows
            foreach (var row in rows)
            {
                result.Add(Row(row, widths));
            }

            // Bottom border
            result.Add(BoxBuilder.Border(totalWidth, false));

            return result.ToArray();
        }
    }
}
