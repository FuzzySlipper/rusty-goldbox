using System.Buffers.Binary;
using System.IO.Compression;

namespace RustyTales.Cli;

/// <summary>
/// Minimal pure-.NET 8-bit RGBA PNG decoder, encoder, alpha-blitter and
/// premultiplied-alpha box resizer used by <see cref="PuppetBridge"/> to pack
/// baked puppet clips into single-page Rusty Tales sprite sheets with zero
/// native or external package dependencies.
/// </summary>
internal sealed class PngRgba
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public PngRgba(int width, int height, byte[]? pixels = null)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "PNG dimensions must be positive.");
        }

        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[width * height * 4];
        if (Pixels.Length != width * height * 4)
        {
            throw new ArgumentException("Pixel buffer size does not match width * height * 4.", nameof(pixels));
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Straight 8-bit RGBA pixel buffer, row-major top-to-bottom.</summary>
    public byte[] Pixels { get; }

    public static PngRgba Load(string path) => Decode(File.ReadAllBytes(path));

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(path, Encode());
    }

    /// <summary>Decodes an 8-bit RGB or RGBA non-interlaced PNG into an RGBA buffer.</summary>
    public static PngRgba Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 33 || !data[..8].SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a valid PNG file.");
        }

        int width = 0;
        int height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        byte interlace = 0;
        using MemoryStream idat = new();

        int pos = 8;
        while (pos + 12 <= data.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[pos..]));
            ReadOnlySpan<byte> type = data.Slice(pos + 4, 4);
            pos += 8;
            if (length < 0 || pos + length + 4 > data.Length)
            {
                throw new InvalidDataException("Truncated PNG chunk.");
            }

            ReadOnlySpan<byte> chunk = data.Slice(pos, length);
            pos += length + 4;

            if (type.SequenceEqual("IHDR"u8))
            {
                if (length != 13)
                {
                    throw new InvalidDataException("Invalid IHDR chunk length.");
                }

                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk[0..4]));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk[4..8]));
                bitDepth = chunk[8];
                colorType = chunk[9];
                interlace = chunk[12];
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idat.Write(chunk);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (width <= 0 || height <= 0 || bitDepth != 8 || (colorType is not (2 or 6)) || interlace != 0)
        {
            throw new NotSupportedException($"Only 8-bit non-interlaced RGB/RGBA PNGs are supported (got {width}x{height} depth={bitDepth} color={colorType} interlace={interlace}).");
        }

        int bpp = colorType == 6 ? 4 : 3;
        int stride = width * bpp;
        byte[] raw = new byte[(stride + 1) * height];

        idat.Position = 0;
        using (ZLibStream zlib = new(idat, CompressionMode.Decompress, leaveOpen: true))
        {
            int total = 0;
            while (total < raw.Length)
            {
                int read = zlib.Read(raw, total, raw.Length - total);
                if (read == 0)
                {
                    throw new InvalidDataException("Unexpected end of PNG IDAT stream.");
                }

                total += read;
            }
        }

        byte[] prev = new byte[stride];
        byte[] curr = new byte[stride];
        byte[] rgba = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * (stride + 1);
            byte filter = raw[rowStart];
            for (int x = 0; x < stride; x++)
            {
                byte value = raw[rowStart + 1 + x];
                byte a = x >= bpp ? curr[x - bpp] : (byte)0;
                byte b = prev[x];
                byte c = x >= bpp ? prev[x - bpp] : (byte)0;
                curr[x] = filter switch
                {
                    0 => value,
                    1 => unchecked((byte)(value + a)),
                    2 => unchecked((byte)(value + b)),
                    3 => unchecked((byte)(value + ((a + b) >> 1))),
                    4 => unchecked((byte)(value + Paeth(a, b, c))),
                    _ => throw new InvalidDataException($"Unknown PNG scanline filter {filter}."),
                };
            }

            int dstRow = y * width * 4;
            if (bpp == 4)
            {
                Buffer.BlockCopy(curr, 0, rgba, dstRow, stride);
            }
            else
            {
                for (int x = 0; x < width; x++)
                {
                    rgba[dstRow + (x * 4)] = curr[x * 3];
                    rgba[dstRow + (x * 4) + 1] = curr[(x * 3) + 1];
                    rgba[dstRow + (x * 4) + 2] = curr[(x * 3) + 2];
                    rgba[dstRow + (x * 4) + 3] = 255;
                }
            }

            (prev, curr) = (curr, prev);
        }

        return new PngRgba(width, height, rgba);
    }

    /// <summary>Encodes this image as a standard 8-bit RGBA PNG (colour type 6).</summary>
    public byte[] Encode()
    {
        int stride = Width * 4;
        byte[] scanlines = new byte[(stride + 1) * Height];
        for (int y = 0; y < Height; y++)
        {
            int dst = y * (stride + 1);
            scanlines[dst] = 0; // None filter (fast, crisp, deflate handles repeated alpha cleanly)
            Buffer.BlockCopy(Pixels, y * stride, scanlines, dst + 1, stride);
        }

        byte[] compressed;
        using (MemoryStream deflated = new())
        {
            using (ZLibStream zlib = new(deflated, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(scanlines, 0, scanlines.Length);
            }

            compressed = deflated.ToArray();
        }

        using MemoryStream output = new();
        output.Write(Signature);

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)Height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // non-interlaced
        WriteChunk(output, "IHDR"u8, ihdr);
        WriteChunk(output, "IDAT"u8, compressed);
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    /// <summary>Copies a rectangular region from <paramref name="src"/> into this image at (<paramref name="dstX"/>, <paramref name="dstY"/>).</summary>
    public void BlitCopy(PngRgba src, int srcX, int srcY, int copyW, int copyH, int dstX, int dstY)
    {
        for (int y = 0; y < copyH; y++)
        {
            int sy = srcY + y;
            int dy = dstY + y;
            if (sy < 0 || sy >= src.Height || dy < 0 || dy >= Height)
            {
                continue;
            }

            for (int x = 0; x < copyW; x++)
            {
                int sx = srcX + x;
                int dx = dstX + x;
                if (sx < 0 || sx >= src.Width || dx < 0 || dx >= Width)
                {
                    continue;
                }

                int sIdx = ((sy * src.Width) + sx) * 4;
                int dIdx = ((dy * Width) + dx) * 4;
                Pixels[dIdx] = src.Pixels[sIdx];
                Pixels[dIdx + 1] = src.Pixels[sIdx + 1];
                Pixels[dIdx + 2] = src.Pixels[sIdx + 2];
                Pixels[dIdx + 3] = src.Pixels[sIdx + 3];
            }
        }
    }

    /// <summary>
    /// Resizes this image to (<paramref name="targetWidth"/>, <paramref name="targetHeight"/>) using
    /// premultiplied-alpha area-box sampling (downscaling) or bilinear interpolation (upscaling) so
    /// transparent edges stay free of dark fringes.
    /// </summary>
    public PngRgba Resize(int targetWidth, int targetHeight, bool nearest = false)
    {
        if (targetWidth == Width && targetHeight == Height)
        {
            return new PngRgba(Width, Height, (byte[])Pixels.Clone());
        }

        PngRgba dst = new(targetWidth, targetHeight);
        double scaleX = (double)Width / targetWidth;
        double scaleY = (double)Height / targetHeight;

        if (nearest)
        {
            for (int y = 0; y < targetHeight; y++)
            {
                int sy = Math.Clamp((int)Math.Floor((y + 0.5) * scaleY), 0, Height - 1);
                for (int x = 0; x < targetWidth; x++)
                {
                    int sx = Math.Clamp((int)Math.Floor((x + 0.5) * scaleX), 0, Width - 1);
                    int sIdx = ((sy * Width) + sx) * 4;
                    int dIdx = ((y * targetWidth) + x) * 4;
                    dst.Pixels[dIdx] = Pixels[sIdx];
                    dst.Pixels[dIdx + 1] = Pixels[sIdx + 1];
                    dst.Pixels[dIdx + 2] = Pixels[sIdx + 2];
                    dst.Pixels[dIdx + 3] = Pixels[sIdx + 3];
                }
            }

            return dst;
        }

        for (int y = 0; y < targetHeight; y++)
        {
            double y0 = y * scaleY;
            double y1 = (y + 1) * scaleY;
            int syStart = Math.Max(0, (int)Math.Floor(y0));
            int syEnd = Math.Min(Height - 1, (int)Math.Ceiling(y1) - 1);
            if (syEnd < syStart)
            {
                syEnd = syStart;
            }

            for (int x = 0; x < targetWidth; x++)
            {
                double x0 = x * scaleX;
                double x1 = (x + 1) * scaleX;
                int sxStart = Math.Max(0, (int)Math.Floor(x0));
                int sxEnd = Math.Min(Width - 1, (int)Math.Ceiling(x1) - 1);
                if (sxEnd < sxStart)
                {
                    sxEnd = sxStart;
                }

                double sumR = 0;
                double sumG = 0;
                double sumB = 0;
                double sumA = 0;
                double weightTotal = 0;

                for (int sy = syStart; sy <= syEnd; sy++)
                {
                    double wy = Math.Min(sy + 1.0, y1) - Math.Max(sy, y0);
                    if (wy <= 0)
                    {
                        wy = 1.0;
                    }

                    for (int sx = sxStart; sx <= sxEnd; sx++)
                    {
                        double wx = Math.Min(sx + 1.0, x1) - Math.Max(sx, x0);
                        if (wx <= 0)
                        {
                            wx = 1.0;
                        }

                        double w = wx * wy;
                        int sIdx = ((sy * Width) + sx) * 4;
                        double a = Pixels[sIdx + 3] / 255.0;
                        sumR += (Pixels[sIdx] / 255.0) * a * w;
                        sumG += (Pixels[sIdx + 1] / 255.0) * a * w;
                        sumB += (Pixels[sIdx + 2] / 255.0) * a * w;
                        sumA += a * w;
                        weightTotal += w;
                    }
                }

                int dIdx = ((y * targetWidth) + x) * 4;
                if (weightTotal <= 0 || sumA <= 1e-6)
                {
                    dst.Pixels[dIdx] = 0;
                    dst.Pixels[dIdx + 1] = 0;
                    dst.Pixels[dIdx + 2] = 0;
                    dst.Pixels[dIdx + 3] = 0;
                }
                else
                {
                    double outA = Math.Clamp(sumA / weightTotal, 0.0, 1.0);
                    double outR = Math.Clamp(sumR / sumA, 0.0, 1.0);
                    double outG = Math.Clamp(sumG / sumA, 0.0, 1.0);
                    double outB = Math.Clamp(sumB / sumA, 0.0, 1.0);
                    dst.Pixels[dIdx] = (byte)Math.Round(outR * 255.0);
                    dst.Pixels[dIdx + 1] = (byte)Math.Round(outG * 255.0);
                    dst.Pixels[dIdx + 2] = (byte)Math.Round(outB * 255.0);
                    dst.Pixels[dIdx + 3] = (byte)Math.Round(outA * 255.0);
                }
            }
        }

        return dst;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBytes, (uint)data.Length);
        output.Write(lenBytes);
        output.Write(type);
        output.Write(data);

        uint crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        crc ^= 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            crc = CrcTable[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }
}
