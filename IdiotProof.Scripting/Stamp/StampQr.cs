using System.Text;
using QRCoder;

namespace IdiotProof.Scripting.Stamp;

/// <summary>
/// Terminal QR code of the stamp's compact payload (<c>IPS1:</c> + base45),
/// drawn with Unicode half-blocks so each text row carries two module rows
/// and the code comes out roughly square. Base45 keeps QRCoder in
/// alphanumeric mode, the densest mode a phone scanner still returns as text.
///
/// The QR has to fit the same 72 columns as the rest of the stamp: with a
/// 2-module quiet zone that caps it at version 12 (65 modules). A strategy
/// whose payload needs more gets a clear one-line note instead of a QR — the
/// armored text block above it always works.
///
/// Light modules are drawn as blocks (and dark ones as blanks), which scans
/// correctly on the usual dark-background terminal.
/// </summary>
internal static class StampQr
{
    private const int QuietZone = 2;

    public static List<string> Render(string payload, int width)
    {
        QRCodeData data;
        try
        {
            using var gen = new QRCodeGenerator();
            data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);
        }
        catch (QRCoder.Exceptions.DataTooLongException)
        {
            return TooLarge(payload, "over QR capacity");
        }

        // QRCoder's matrix includes a 4-module quiet zone; keep only QuietZone of it.
        var matrix = data.ModuleMatrix;
        int trim = 4 - QuietZone, size = matrix.Count - 2 * trim;
        if (size > width)
            return TooLarge(payload, $"v{data.Version} needs {size} cols");

        bool Light(int r, int c) => r >= size || !matrix[r + trim][c + trim];

        var lines = new List<string>();
        var pad = new string(' ', (width - size) / 2);
        for (int r = 0; r < size; r += 2)
        {
            var sb = new StringBuilder(pad);
            for (int c = 0; c < size; c++)
                sb.Append((Light(r, c), Light(r + 1, c)) switch
                {
                    (true, true) => '█',
                    (true, false) => '▀',
                    (false, true) => '▄',
                    _ => ' ',
                });
            lines.Add(sb.ToString().TrimEnd());
        }
        lines.Add($"{pad}QR v{data.Version}-L, {payload.Length} chars: scan it, then paste the text to import");
        return lines;
    }

    private static List<string> TooLarge(string payload, string why) =>
    [
        $"QR: too large for a single QR ({payload.Length} chars; {why}).",
        "    Copy the BEGIN/END block above instead: same strategy, no QR.",
    ];
}
