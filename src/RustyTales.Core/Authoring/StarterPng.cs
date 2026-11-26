using System.Buffers.Binary;
using System.IO.Compression;

namespace RustyTales.Core.Authoring;

/// <summary>
/// Writes small, valid 8-bit RGBA PNG starter art (wall sets, backdrops,
/// figure sprites, icons) with zero external dependencies so every scaffolded
/// genre kit validates and renders out of the box.
/// </summary>
internal static class StarterPng
{
    public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255);

    public static byte[] WallSet(Rgba wall, Rgba mortar, Rgba door, Rgba trim, Rgba floor, Rgba ceiling)
    {
        const int size = 64;
        byte[] pixels = new byte[size * size * 4];

        // Top-left (0..31, 0..31): stone/brick wall.
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                bool seam = (y % 8 == 0) || (((x + ((y / 8) % 2) * 8) % 16) == 0);
                Set(pixels, size, x, y, seam ? mortar : wall);
            }
        }

        // Top-right (32..63, 0..31): framed paneled door with handle.
        for (int y = 0; y < 32; y++)
        {
            for (int x = 32; x < 64; x++)
            {
                int lx = x - 32;
                bool frame = lx < 3 || lx >= 29 || y < 3;
                bool plank = lx == 15 || lx == 16;
                bool handle = lx >= 23 && lx <= 25 && y >= 15 && y <= 18;
                Rgba color = handle ? trim : frame ? mortar : plank ? trim : door;
                Set(pixels, size, x, y, color);
            }
        }

        // Bottom-left (0..31, 32..63): flagged floor.
        for (int y = 32; y < 64; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                int ly = y - 32;
                bool grid = (x % 16 == 0) || (ly % 16 == 0);
                Set(pixels, size, x, y, grid ? mortar : floor);
            }
        }

        // Bottom-right (32..63, 32..63): ceiling.
        for (int y = 32; y < 64; y++)
        {
            for (int x = 32; x < 64; x++)
            {
                int lx = x - 32;
                int ly = y - 32;
                bool beam = (lx % 16 == 0) || (ly % 16 == 0);
                Set(pixels, size, x, y, beam ? mortar : ceiling);
            }
        }

        return Encode(size, size, pixels);
    }

    public static byte[] Backdrop(Rgba top, Rgba bottom, Rgba accent)
    {
        const int width = 64;
        const int height = 48;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            double t = y / (double)(height - 1);
            Rgba row = new(
                (byte)(top.R + (bottom.R - top.R) * t),
                (byte)(top.G + (bottom.G - top.G) * t),
                (byte)(top.B + (bottom.B - top.B) * t));
            for (int x = 0; x < width; x++)
            {
                bool pillar = x < 6 || x >= width - 6 || y < 4 || y >= height - 4;
                bool horizon = y == 28 || y == 29;
                Set(pixels, width, x, y, pillar ? accent : horizon ? accent : row);
            }
        }

        return Encode(width, height, pixels);
    }

    public static byte[] Sprite(Rgba primary, Rgba secondary, Rgba accent, bool humanoid)
    {
        const int width = 32;
        const int height = 48;
        byte[] pixels = new byte[width * height * 4];

        if (humanoid)
        {
            FillRect(pixels, width, 12, 8, 8, 8, accent);       // head
            FillRect(pixels, width, 10, 16, 12, 16, primary);   // torso
            FillRect(pixels, width, 6, 17, 4, 12, secondary);   // left arm
            FillRect(pixels, width, 22, 17, 4, 12, secondary);  // right arm
            FillRect(pixels, width, 11, 32, 4, 14, secondary);  // left leg
            FillRect(pixels, width, 17, 32, 4, 14, secondary);  // right leg
            FillRect(pixels, width, 24, 10, 2, 18, accent);     // weapon
        }
        else
        {
            FillRect(pixels, width, 8, 18, 16, 18, primary);    // body
            FillRect(pixels, width, 6, 24, 20, 14, primary);    // wide lower silhouette
            FillRect(pixels, width, 10, 14, 12, 6, secondary);  // crest/crown
            FillRect(pixels, width, 11, 22, 3, 3, accent);      // left eye
            FillRect(pixels, width, 18, 22, 3, 3, accent);      // right eye
            FillRect(pixels, width, 9, 36, 5, 10, secondary);   // left base
            FillRect(pixels, width, 18, 36, 5, 10, secondary);  // right base
        }

        return Encode(width, height, pixels);
    }

    public static byte[] Icon(Rgba fill, Rgba border)
    {
        const int size = 16;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                Set(pixels, size, x, y, edge ? border : fill);
            }
        }

        return Encode(size, size, pixels);
    }

    private static void FillRect(byte[] pixels, int stride, int x, int y, int width, int height, Rgba color)
    {
        for (int py = y; py < y + height; py++)
        {
            for (int px = x; px < x + width; px++)
            {
                Set(pixels, stride, px, py, color);
            }
        }
    }

    private static void Set(byte[] pixels, int stride, int x, int y, Rgba color)
    {
        int offset = (y * stride + x) * 4;
        pixels[offset] = color.R;
        pixels[offset + 1] = color.G;
        pixels[offset + 2] = color.B;
        pixels[offset + 3] = color.A;
    }

    private static byte[] Encode(int width, int height, byte[] rgba)
    {
        using MemoryStream output = new();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        ihdr[10] = 0; // deflate
        ihdr[11] = 0; // adaptive filter
        ihdr[12] = 0; // no interlace
        WriteChunk(output, "IHDR"u8, ihdr);

        byte[] compressed;
        using (MemoryStream deflate = new())
        {
            using (ZLibStream zlib = new(deflate, CompressionLevel.Fastest, leaveOpen: true))
            {
                int rowBytes = width * 4;
                for (int y = 0; y < height; y++)
                {
                    zlib.WriteByte(0); // filter type None
                    zlib.Write(rgba, y * rowBytes, rowBytes);
                }
            }

            compressed = deflate.ToArray();
        }

        WriteChunk(output, "IDAT"u8, compressed);
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        output.Write(header);
        output.Write(type);
        output.Write(data);

        uint crc = Crc32(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(header, crc);
        output.Write(header);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc;
    }
}
