using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

internal sealed class ColorResponse
{
    public const int Minimum = 180, Maximum = 255, Stride = 80;
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double DpiX { get; init; }
    public required double DpiY { get; init; }
    public required string SourceHash { get; init; }
    public required uint[] Indices { get; init; }
    public required byte[] Table { get; init; }
    public int Classes => Table.Length / Stride;

    public static byte[] Pixels(BitmapSource source)
    {
        var bytes = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(bytes, source.PixelWidth * 4, 0);
        return bytes;
    }

    public static string Hash(BitmapSource source) => Convert.ToHexString(SHA256.HashData(Pixels(source)));

    public byte[] Evaluate(Color color)
    {
        if (color.R < Minimum || color.G < Minimum || color.B < Minimum)
            throw new InvalidOperationException("Color lies outside the pinned production channel range.");
        var result = new byte[Indices.Length * 4];
        for (var i = 0; i < Indices.Length; i++)
        {
            var packed = Indices[i];
            var alpha = (byte)packed;
            if (alpha == 0) continue;
            var offset = (int)(packed >> 8) * Stride;
            result[4 * i] = Table[offset + color.B - Minimum];
            result[4 * i + 1] = Table[offset + color.G - Minimum];
            result[4 * i + 2] = Table[offset + color.R - Minimum];
            result[4 * i + 3] = alpha;
        }
        return result;
    }

    public static object Difference(byte[] expected, byte[] actual)
    {
        var max = 0; long different = 0, alphaDifferent = 0, absolute = 0, opaqueDifferent = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var pixelDifferent = false;
            for (var c = 0; c < 4; c++)
            {
                var delta = Math.Abs(expected[i + c] - actual[i + c]);
                absolute += delta;
                max = Math.Max(max, delta);
                pixelDifferent |= delta != 0;
            }
            if (pixelDifferent) { different++; if (expected[i + 3] == 255) opaqueDifferent++; }
            if (expected[i + 3] != actual[i + 3]) alphaDifferent++;
        }
        return new { max, different, alphaDifferent, opaqueDifferent, meanAbsolute = (double)absolute / expected.Length };
    }

    public static byte[] LinearBasis(byte[] black, byte[] white, Color color)
    {
        var result = new byte[white.Length];
        byte[] channels = [color.B, color.G, color.R];
        for (var i = 0; i < result.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
                result[i + c] = (byte)Math.Clamp(Math.Round(black[i + c] +
                    (white[i + c] - black[i + c]) * channels[c] / 255.0), 0, 255);
            result[i + 3] = white[i + 3];
        }
        return result;
    }

    public static ColorResponse Build(BitmapSource baseline, Func<Color, BitmapSource> renderGray, string logPath)
    {
        var stopwatch = Stopwatch.StartNew();
        var white = Pixels(renderGray(Colors.White));
        var ids = new int[white.Length / 4];
        var parents = new List<int[]>();
        var values = new List<byte[]>();
        var rows = new List<object>();
        var classes = 0;
        for (var color = Minimum; color <= Maximum; color++)
        {
            var start = stopwatch.Elapsed.TotalMilliseconds;
            var pixels = color == 255 ? white : Pixels(renderGray(Color.FromRgb((byte)color, (byte)color, (byte)color)));
            var rasterEnd = stopwatch.Elapsed.TotalMilliseconds;
            var partitions = new Dictionary<long, int>();
            var previous = new List<int>();
            var nextValues = new List<byte>();
            for (var i = 0; i < ids.Length; i++)
            {
                var offset = i * 4;
                if (pixels[offset + 3] != white[offset + 3] ||
                    pixels[offset] != pixels[offset + 1] || pixels[offset] != pixels[offset + 2])
                    throw new InvalidOperationException($"Production gray raster has color-dependent alpha or channel-dependent output at channel={color}, pixel={i}.");
                if (white[offset + 3] == 0)
                {
                    if (pixels[offset] != 0) throw new InvalidOperationException("Nonzero RGB at zero alpha.");
                    continue;
                }
                var key = ((long)ids[i] << 8) | pixels[offset];
                if (!partitions.TryGetValue(key, out var next))
                {
                    next = partitions.Count;
                    partitions.Add(key, next);
                    previous.Add(ids[i]);
                    nextValues.Add(pixels[offset]);
                }
                ids[i] = next;
            }
            classes = partitions.Count;
            if (classes > 262144) throw new InvalidOperationException($"Exact response requires {classes} classes at channel={color}, beyond the bounded 20 MiB lookup budget.");
            parents.Add(previous.ToArray());
            values.Add(nextValues.ToArray());
            rows.Add(new { color, classes, rasterMs = rasterEnd - start,
                partitionMs = stopwatch.Elapsed.TotalMilliseconds - rasterEnd });
            Console.WriteLine($"COLOR-CACHE channel={color} classes={classes} elapsed={stopwatch.Elapsed.TotalSeconds:F1}s");
        }
        var table = new byte[classes * Stride];
        for (var id = 0; id < classes; id++)
        {
            var cursor = id;
            for (var column = Maximum - Minimum; column >= 0; column--)
            {
                table[id * Stride + column] = values[column][cursor];
                cursor = parents[column][cursor];
            }
        }
        var indices = new uint[ids.Length];
        for (var i = 0; i < indices.Length; i++) indices[i] = ((uint)ids[i] << 8) | white[i * 4 + 3];
        File.WriteAllText(logPath, JsonSerializer.Serialize(new
        {
            elapsedMs = stopwatch.Elapsed.TotalMilliseconds, classes, mapBytes = indices.Length * 4,
            tableBytes = table.Length, rows,
            constraint = "Exact finite-channel response lookup, not an approximate tint. Cache creation cost is measured, not hidden."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return new ColorResponse { Width = baseline.PixelWidth, Height = baseline.PixelHeight,
            DpiX = baseline.DpiX, DpiY = baseline.DpiY, SourceHash = Hash(baseline), Indices = indices, Table = table };
    }

    public void Save(string path)
    {
        using var writer = new BinaryWriter(File.Open(path, FileMode.CreateNew));
        writer.Write("WEL-COLOR-RESPONSE-1");
        writer.Write(Width); writer.Write(Height); writer.Write(DpiX); writer.Write(DpiY); writer.Write(SourceHash);
        writer.Write(Table.Length);
        writer.Write(Table);
        foreach (var value in Indices) writer.Write(value);
    }

    public static ColorResponse Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadString() != "WEL-COLOR-RESPONSE-1") throw new InvalidDataException("Unknown color response cache.");
        var width = reader.ReadInt32(); var height = reader.ReadInt32();
        var dpiX = reader.ReadDouble(); var dpiY = reader.ReadDouble(); var hash = reader.ReadString();
        var tableLength = reader.ReadInt32();
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 ||
            !double.IsFinite(dpiX) || !double.IsFinite(dpiY) || dpiX <= 0 || dpiY <= 0 ||
            hash.Length != 64 || tableLength <= 0 || tableLength % Stride != 0 || tableLength > 262144 * Stride ||
            reader.BaseStream.Length - reader.BaseStream.Position != tableLength + (long)width * height * 4)
            throw new InvalidDataException("Invalid or truncated color response cache.");
        var table = reader.ReadBytes(tableLength);
        var indices = new uint[checked(width * height)];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = reader.ReadUInt32();
            if ((indices[i] >> 8) >= tableLength / Stride)
                throw new InvalidDataException("Color response index exceeds the lookup table.");
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing cache data.");
        return new ColorResponse { Width = width, Height = height, DpiX = dpiX, DpiY = dpiY,
            SourceHash = hash, Indices = indices, Table = table };
    }
}
