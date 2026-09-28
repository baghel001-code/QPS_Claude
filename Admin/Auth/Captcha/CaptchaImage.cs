using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Admin.Auth.Captcha;

/// <summary>
/// Draws a CAPTCHA code as a PNG with no external libraries: 5×7 pixel glyphs, each scaled and
/// rotated a little, a gentle wave distortion, 3×3 supersampling for smooth edges, speckle noise
/// and two thin lines through the text.
/// </summary>
internal static class CaptchaImage
{
    public const int Width = 180;
    public const int Height = 56;

    /// <summary>No look-alikes: O/0, I/1, S/5, Z/2, B/8, G/6 and Q are left out.</summary>
    public const string Alphabet = "ACDEFHJKLMNPRTUVWXY23456789";

    // 5 columns × 7 rows, row by row; '#' is ink.
    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['A'] = ".###.#...##...#######...##...##...#",
        ['C'] = ".###.#...##....#....#....#...#.###.",
        ['D'] = "####.#...##...##...##...##...#####.",
        ['E'] = "######....#....####.#....#....#####",
        ['F'] = "######....#....####.#....#....#....",
        ['H'] = "#...##...##...#######...##...##...#",
        ['J'] = "..###...#....#....#....#.#..#..##..",
        ['K'] = "#...##..#.#.#..##...#.#..#..#.#...#",
        ['L'] = "#....#....#....#....#....#....#####",
        ['M'] = "#...###.###.#.##.#.##...##...##...#",
        ['N'] = "#...###..##.#.##..###...##...##...#",
        ['P'] = "####.#...##...#####.#....#....#....",
        ['R'] = "####.#...##...#####.#.#..#..#.#...#",
        ['T'] = "#####..#....#....#....#....#....#..",
        ['U'] = "#...##...##...##...##...##...#.###.",
        ['V'] = "#...##...##...##...##...#.#.#...#..",
        ['W'] = "#...##...##...##.#.##.#.###.###...#",
        ['X'] = "#...##...#.#.#...#...#.#.#...##...#",
        ['Y'] = "#...##...#.#.#...#....#....#....#..",
        ['2'] = ".###.#...#....#...#...#...#...#####",
        ['3'] = "####.....#....#.###.....#....#####.",
        ['4'] = "...#...##..#.#.#..#.#####...#....#.",
        ['5'] = "######....####.....#....##...#.###.",
        ['6'] = "..##..#...#....####.#...##...#.###.",
        ['7'] = "#####....#...#...#...#....#....#...",
        ['8'] = ".###.#...##...#.###.#...##...#.###.",
        ['9'] = ".###.#...##...#.####....#...#..##..",
    };

    private static readonly byte[][] Inks =
    [
        [15, 75, 140], [170, 0, 95], [30, 40, 60], [10, 95, 150], [140, 20, 90],
    ];

    private readonly record struct Placed(string Bits, double Scale, double Cos, double Sin, double Cx, double Cy, byte[] Ink);

    public static byte[] RenderPng(string code)
    {
        // Random.Shared only varies the look; the code itself comes from RandomNumberGenerator.
        var rnd = Random.Shared;
        var placed = new Placed[code.Length];
        var slot = (Width - 24.0) / code.Length;
        for (var i = 0; i < code.Length; i++)
        {
            var angle = (rnd.NextDouble() * 30 - 15) * Math.PI / 180;
            placed[i] = new Placed(
                Glyphs[code[i]],
                Scale: 4.3 + rnd.NextDouble() * 0.6,
                Cos: Math.Cos(angle),
                Sin: Math.Sin(angle),
                Cx: 12 + slot * (i + 0.5) + (rnd.NextDouble() * 4 - 2),
                Cy: Height / 2.0 + (rnd.NextDouble() * 8 - 4),
                Ink: Inks[rnd.Next(Inks.Length)]);
        }

        var phaseX = rnd.NextDouble() * 2 * Math.PI;
        var phaseY = rnd.NextDouble() * 2 * Math.PI;
        var ampX = 1.2 + rnd.NextDouble() * 0.8;
        var ampY = 0.8 + rnd.NextDouble() * 0.7;
        ReadOnlySpan<double> sub = [-1.0 / 3, 0, 1.0 / 3];

        var rgb = new byte[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        {
            var t = (double)y / Height;
            for (var x = 0; x < Width; x++)
            {
                int r = 248 - (int)(8 * t), g = 245 - (int)(5 * t), b = 251 - (int)(3 * t);
                if (rnd.NextDouble() < 0.035)
                {
                    var d = rnd.Next(25, 61);
                    r -= d; g -= d; b -= d;
                }

                // 3×3 supersampling of the warped text gives anti-aliased edges.
                var hits = 0;
                byte[]? ink = null;
                foreach (var oy in sub)
                {
                    foreach (var ox in sub)
                    {
                        double xx = x + ox, yy = y + oy;
                        var c = InkAt(placed, xx + ampX * Math.Sin(yy / 8.0 + phaseX), yy + ampY * Math.Sin(xx / 12.0 + phaseY));
                        if (c is not null)
                        {
                            hits++;
                            ink = c;
                        }
                    }
                }
                if (ink is not null)
                {
                    var k = hits / 9.0;
                    r = (int)(r * (1 - k) + ink[0] * k);
                    g = (int)(g * (1 - k) + ink[1] * k);
                    b = (int)(b * (1 - k) + ink[2] * k);
                }

                var p = (y * Width + x) * 3;
                rgb[p] = (byte)r;
                rgb[p + 1] = (byte)g;
                rgb[p + 2] = (byte)b;
            }
        }

        // Two thin interference curves, blended over the text.
        for (var n = 0; n < 2; n++)
        {
            var ink = Inks[rnd.Next(Inks.Length)];
            var y0 = 14 + rnd.NextDouble() * (Height - 28);
            var amp = 5 + rnd.NextDouble() * 5;
            var freq = 0.025 + rnd.NextDouble() * 0.025;
            var phase = rnd.NextDouble() * 6;
            for (var x = 0; x < Width; x++)
            {
                var yi = (int)(y0 + amp * Math.Sin(x * freq + phase));
                if (yi < 0 || yi >= Height)
                    continue;
                var p = (yi * Width + x) * 3;
                for (var c = 0; c < 3; c++)
                    rgb[p + c] = (byte)(rgb[p + c] * 0.45 + ink[c] * 0.55);
            }
        }

        return EncodePng(rgb, Width, Height);
    }

    private static byte[]? InkAt(Placed[] placed, double sx, double sy)
    {
        foreach (var ch in placed)
        {
            double dx = sx - ch.Cx, dy = sy - ch.Cy;
            // Undo the rotation and scale to find the glyph cell under this point.
            var gx = (dx * ch.Cos + dy * ch.Sin) / ch.Scale + 2.5;
            var gy = (-dx * ch.Sin + dy * ch.Cos) / ch.Scale + 3.5;
            if (gx >= 0 && gx < 5 && gy >= 0 && gy < 7 && ch.Bits[(int)gy * 5 + (int)gx] == '#')
                return ch.Ink;
        }
        return null;
    }

    // ---- minimal PNG writer: 8-bit RGB, no filtering, zlib-compressed ----

    private static byte[] EncodePng(byte[] rgb, int width, int height)
    {
        var stride = width * 3;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(rgb, y * stride, raw, y * (stride + 1) + 1, stride);   // leading 0 = filter "None"

        byte[] idat;
        using (var compressed = new MemoryStream())
        {
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw, 0, raw.Length);
            idat = compressed.ToArray();
        }

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // colour type: RGB

        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", idat);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        stream.Write(buf);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(buf, Crc32(typeBytes, data));
        stream.Write(buf);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
